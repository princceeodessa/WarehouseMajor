using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseAutomatisaion.Application.Contracts.Receiving;
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
                StatusText.Text = "Черновиков нет. Распознайте накладную через 🤖 кнопку наверху или из раздела «Остатки».";
            }
            else
            {
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
    }
}
