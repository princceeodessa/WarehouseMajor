using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseAutomatisaion.Application.Contracts.Receiving;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 8 (AI loop closure): workspace «Черновики AI-приёмок».
// Показывает накладные распознанные через InvoiceRecognitionWindow и ещё
// не разнесённые в stock_locations. По двойному клику — модальное окно
// приёмки в выбранную ячейку.
public partial class ReceiptDraftsWorkspaceView : UserControl
{
    private DesktopMySqlBackplaneService? _backplane;
    private MySqlReceiptDraftReader? _draftReader;
    private MySqlStorageCellCatalog? _cellCatalog;
    private MySqlStockLocationRepository? _stockLocations;
    private bool _isInitialized;

    public ReceiptDraftsWorkspaceView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (_backplane is null)
        {
            StatusText.Text = "❌ Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.";
            DisableActions();
            return;
        }

        _draftReader = new MySqlReceiptDraftReader(_backplane);
        _cellCatalog = new MySqlStorageCellCatalog(_backplane);
        _stockLocations = new MySqlStockLocationRepository(_backplane);

        _isInitialized = true;
        ReloadDrafts();
    }

    private async void ReloadDrafts()
    {
        if (_draftReader is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "⏳ Загрузка черновиков...";
            var drafts = await _draftReader.LoadAllAsync();
            DraftsGrid.ItemsSource = drafts;

            if (drafts.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                DraftsGrid.Visibility = Visibility.Collapsed;
                StatusText.Text = "Черновиков нет — самое время распознать первую накладную.";
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                DraftsGrid.Visibility = Visibility.Visible;
                var totalQty = drafts.Sum(d => d.TotalQuantity);
                var totalAmt = drafts.Sum(d => d.TotalAmount ?? 0m);
                StatusText.Text = $"Черновиков: {drafts.Count}   ·   суммарно строк: {drafts.Sum(d => d.LinesCount):N0}   ·   " +
                                  $"кол-во: {totalQty:N0}   ·   сумма: {totalAmt:N2} ₽";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Ошибка загрузки: {ex.Message}";
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            ReloadDrafts();
        }
    }

    private void OnRecognizeClicked(object sender, RoutedEventArgs e)
    {
        var window = new InvoiceRecognitionWindow
        {
            Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();

        // После закрытия — обновим список (вдруг создан новый draft).
        if (_isInitialized)
        {
            ReloadDrafts();
        }
    }

    private async void OnDraftDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DraftsGrid.SelectedItem is not ReceiptDraftSummary selected
            || _draftReader is null || _cellCatalog is null || _stockLocations is null)
        {
            return;
        }

        try
        {
            StatusText.Text = $"⏳ Загрузка черновика «{selected.InvoiceNumber}»...";
            var detail = await _draftReader.GetByIdAsync(selected.Id);
            if (detail is null)
            {
                StatusText.Text = "❌ Черновик не найден (возможно был удалён).";
                return;
            }

            var window = new ReceiptDraftReceiveWindow(detail, _cellCatalog, _stockLocations, _draftReader)
            {
                Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
            };

            if (window.ShowDialog() == true)
            {
                StatusText.Text = $"✅ Черновик «{selected.InvoiceNumber}» принят.";
                ReloadDrafts();
            }
            else
            {
                StatusText.Text = $"Открыт черновик «{selected.InvoiceNumber}» (без изменений).";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось открыть: {ex.Message}";
        }
    }

    private void DisableActions()
    {
        RefreshButton.IsEnabled = false;
        RecognizeButton.IsEnabled = false;
        RefreshEmbeddingsButton.IsEnabled = false;
    }

    // Sprint 11: refresh embeddings — пересчитать только недостающие. Для full rebuild
    // (например при смене модели) пользователь может явно нажать Shift+Click — но пока
    // оставляем missing-only для дешевизны.
    private async void OnRefreshEmbeddingsClicked(object sender, RoutedEventArgs e)
    {
        var pipeline = InvoiceRecognitionPipelineFactory.TryCreate();
        if (pipeline is null)
        {
            StatusText.Text = "❌ OpenAI / MySQL не настроены — embeddings не пересчитать.";
            return;
        }

        var refresher = new NomenclatureEmbeddingRefresher(
            pipeline.EmbeddingService,
            pipeline.CatalogReader,
            pipeline.EmbeddingStore);

        try
        {
            RefreshEmbeddingsButton.IsEnabled = false;
            var originalContent = RefreshEmbeddingsButton.Content;
            RefreshEmbeddingsButton.Content = "⏳ AI считает...";

            var progress = new Progress<RefreshProgress>(p =>
            {
                StatusText.Text = $"⏳ {p.Message}  ({p.Done}/{p.Total})";
            });

            var result = await Task.Run(() =>
                refresher.RefreshMissingAsync(progress));

            StatusText.Text = result.Failed == 0
                ? $"✅ AI каталог обновлён. Embeddings посчитаны для {result.Processed} новых товаров (провайдер {result.Provider})."
                : $"⚠ AI каталог: добавлено {result.Processed}, ошибок {result.Failed} (из {result.Total}).";

            RefreshEmbeddingsButton.Content = originalContent;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Ошибка обновления AI каталога: {ex.Message}";
            RefreshEmbeddingsButton.Content = "🧠 Обновить AI каталог";
        }
        finally
        {
            RefreshEmbeddingsButton.IsEnabled = true;
        }
    }
}
