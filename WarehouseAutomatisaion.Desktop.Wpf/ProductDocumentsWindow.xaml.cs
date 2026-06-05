using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

/// <summary>
/// Окно «Документы товара» — список продаж и закупок, в которых участвует товар.
/// JOIN: app_sales_documents + lines × app_purchasing_documents + lines, фильтр по item_code.
/// </summary>
public partial class ProductDocumentsWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string AllValue = "Все";

    private readonly string _itemCode;
    private readonly string _itemName;
    private readonly ObservableCollection<DocRow> _rows = new();
    private List<ProductDocumentRecord> _allRecords = new();

    public ProductDocumentsWindow(string itemCode, string itemName)
    {
        InitializeComponent();
        _itemCode = (itemCode ?? string.Empty).Trim();
        _itemName = (itemName ?? string.Empty).Trim();
        DocumentsGrid.ItemsSource = _rows;
        HeaderSubtitleText.Text = string.IsNullOrEmpty(_itemCode)
            ? "Код товара не определён"
            : $"{_itemCode} • {_itemName}";
        Loaded += HandleLoaded;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        ReloadDocuments();
    }

    private void HandleRefreshClick(object sender, RoutedEventArgs e) => ReloadDocuments();

    private void HandleCloseClick(object sender, RoutedEventArgs e) => Close();

    private void HandleFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ReloadDocuments()
    {
        _allRecords.Clear();
        _rows.Clear();

        if (string.IsNullOrWhiteSpace(_itemCode))
        {
            StatusText.Text = "Код товара не определён.";
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

            _allRecords = backplane.TryLoadProductDocuments(_itemCode).ToList();

            // Заполнить SourceFilter (Все / Продажа / Закупка)
            var sources = _allRecords
                .Select(r => TextMojibakeFixer.NormalizeText(r.Source))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
            var prevSource = SourceFilter.SelectedItem as string;
            SourceFilter.Items.Clear();
            SourceFilter.Items.Add(AllValue);
            foreach (var s in sources) SourceFilter.Items.Add(s);
            SourceFilter.SelectedItem = (prevSource != null && SourceFilter.Items.Contains(prevSource)) ? prevSource : AllValue;

            // Заполнить KindFilter
            var kinds = _allRecords
                .Select(r => HumanizeKind(r.DocumentKind))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
            var prevKind = KindFilter.SelectedItem as string;
            KindFilter.Items.Clear();
            KindFilter.Items.Add(AllValue);
            foreach (var k in kinds) KindFilter.Items.Add(k);
            KindFilter.SelectedItem = (prevKind != null && KindFilter.Items.Contains(prevKind)) ? prevKind : AllValue;

            ApplyFilter();
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, "ProductDocumentsWindow.ReloadDocuments");
            StatusText.Text = $"Ошибка загрузки: {exception.Message}";
        }
    }

    private void ApplyFilter()
    {
        _rows.Clear();

        var selectedSource = SourceFilter.SelectedItem as string ?? AllValue;
        var selectedKind = KindFilter.SelectedItem as string ?? AllValue;

        IEnumerable<ProductDocumentRecord> filtered = _allRecords;
        if (!string.Equals(selectedSource, AllValue, StringComparison.Ordinal))
        {
            filtered = filtered.Where(r => string.Equals(
                TextMojibakeFixer.NormalizeText(r.Source),
                selectedSource,
                StringComparison.OrdinalIgnoreCase));
        }
        if (!string.Equals(selectedKind, AllValue, StringComparison.Ordinal))
        {
            filtered = filtered.Where(r => string.Equals(
                HumanizeKind(r.DocumentKind),
                selectedKind,
                StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        foreach (var record in filteredList)
        {
            _rows.Add(new DocRow
            {
                Source = TextMojibakeFixer.NormalizeText(record.Source),
                DateDisplay = record.DocumentDate == DateTime.MinValue
                    ? string.Empty
                    : record.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
                KindDisplay = HumanizeKind(record.DocumentKind),
                Number = TextMojibakeFixer.NormalizeText(record.Number),
                CounterpartyName = TextMojibakeFixer.NormalizeText(record.CounterpartyName),
                QuantityDisplay = record.Quantity.ToString("N2", RuCulture),
                UnitName = TextMojibakeFixer.NormalizeText(record.UnitName),
                AmountDisplay = record.Amount.ToString("N2", RuCulture) + " " + CurrencySymbol(record.CurrencyCode),
                Status = TextMojibakeFixer.NormalizeText(record.Status)
            });
        }

        // KPI: считаем по полным данным (без учёта фильтра — пользователь хочет видеть итоги по товару)
        var salesCount = _allRecords.Count(r => string.Equals(r.Source, "Продажа", StringComparison.OrdinalIgnoreCase));
        var purchaseCount = _allRecords.Count(r => string.Equals(r.Source, "Закупка", StringComparison.OrdinalIgnoreCase));
        var soldQuantity = _allRecords.Where(r => string.Equals(r.Source, "Продажа", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Quantity);
        var soldAmount = _allRecords.Where(r => string.Equals(r.Source, "Продажа", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount);

        SalesCountText.Text = salesCount.ToString("N0", RuCulture);
        PurchaseCountText.Text = purchaseCount.ToString("N0", RuCulture);
        TotalQuantityText.Text = soldQuantity.ToString("N0", RuCulture);
        TotalAmountText.Text = soldAmount.ToString("N2", RuCulture) + " ₽";

        StatusText.Text = _allRecords.Count switch
        {
            0 => "Документов не найдено.",
            _ => $"Показано {_rows.Count:N0} из {_allRecords.Count:N0} документов."
        };

        UpdateEmptyState();
    }

    private static string HumanizeKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return string.Empty;
        }

        return kind.Trim().ToLowerInvariant() switch
        {
            "order" => "Заказ покупателя",
            "invoice" => "Счёт на оплату",
            "shipment" => "Расходная накладная",
            "return" => "Возврат от покупателя",
            "salesreturn" => "Возврат от покупателя",
            "purchaseorder" => "Заказ поставщику",
            "receipt" => "Приходная накладная",
            "supplierinvoice" => "Счёт поставщика",
            "supplierreturn" => "Возврат поставщику",
            "transferorder" => "Заказ на перемещение",
            "writeoff" => "Списание",
            "write_off" => "Списание",
            "inventory" => "Инвентаризация",
            "reservation" => "Резервирование",
            "discrepancy" => "Расхождение",
            _ => kind.Trim()
        };
    }

    private static string CurrencySymbol(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode)) return "₽";
        return currencyCode.Trim().ToUpperInvariant() switch
        {
            "RUB" => "₽",
            "USD" => "$",
            "EUR" => "€",
            _ => currencyCode.Trim()
        };
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class DocRow
    {
        public string Source { get; set; } = string.Empty;

        public string DateDisplay { get; set; } = string.Empty;

        public string KindDisplay { get; set; } = string.Empty;

        public string Number { get; set; } = string.Empty;

        public string CounterpartyName { get; set; } = string.Empty;

        public string QuantityDisplay { get; set; } = string.Empty;

        public string UnitName { get; set; } = string.Empty;

        public string AmountDisplay { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
    }
}
