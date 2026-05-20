using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

/// <summary>
/// Модальное окно «Штрихкоды товара» — открывается из карточки товара (ProductEditorWindow)
/// при клике на вкладку «Штрихкоды». Источник данных — таблица app_product_barcodes,
/// заполненная скриптом scripts/Import-UnfBarcodesToMySql.ps1 из 1С УНФ.
/// </summary>
public partial class ProductBarcodesWindow : Window
{
    private readonly string _itemCode;
    private readonly string _itemName;
    private readonly ObservableCollection<BarcodeRow> _rows = new();

    public ProductBarcodesWindow(string itemCode, string itemName)
    {
        InitializeComponent();
        _itemCode = (itemCode ?? string.Empty).Trim();
        _itemName = (itemName ?? string.Empty).Trim();
        BarcodesGrid.ItemsSource = _rows;
        HeaderSubtitleText.Text = string.IsNullOrEmpty(_itemCode)
            ? "Код товара не определён"
            : $"{_itemCode} • {_itemName}";
        Loaded += HandleLoaded;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        ReloadBarcodes();
    }

    private void HandleRefreshClick(object sender, RoutedEventArgs e) => ReloadBarcodes();

    private void HandleCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ReloadBarcodes()
    {
        _rows.Clear();
        if (string.IsNullOrWhiteSpace(_itemCode))
        {
            UpdateEmptyState();
            return;
        }

        try
        {
            var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
            if (backplane is null)
            {
                StatusText.Text = "Нет подключения к серверу.";
                UpdateEmptyState();
                return;
            }

            var barcodes = backplane.TryLoadProductBarcodes(_itemCode);
            var lineNo = 1;
            foreach (var record in barcodes)
            {
                _rows.Add(new BarcodeRow
                {
                    LineNo = lineNo++,
                    BarcodeValue = TextMojibakeFixer.NormalizeText(record.BarcodeValue),
                    BarcodeKind = TextMojibakeFixer.NormalizeText(record.BarcodeKind),
                    BarcodeSource = TextMojibakeFixer.NormalizeText(record.BarcodeSource)
                });
            }

            StatusText.Text = _rows.Count switch
            {
                0 => "Штрихкодов не найдено.",
                1 => "Найден 1 штрихкод.",
                _ when _rows.Count < 5 => $"Найдено {_rows.Count} штрихкода.",
                _ => $"Найдено {_rows.Count} штрихкодов."
            };
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, "ProductBarcodesWindow.ReloadBarcodes");
            StatusText.Text = $"Ошибка загрузки: {exception.Message}";
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class BarcodeRow
    {
        public int LineNo { get; set; }

        public string BarcodeValue { get; set; } = string.Empty;

        public string BarcodeKind { get; set; } = string.Empty;

        public string BarcodeSource { get; set; } = string.Empty;
    }
}
