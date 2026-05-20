using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

/// <summary>
/// Окно «История цен товара» — открывается из карточки товара по вкладке «Цены».
/// Источник данных — таблица app_product_price_history, заполненная скриптом
/// scripts/Import-UnfPriceHistoryToMySql.ps1 (унаследовано из 1С УНФ, ~81k записей).
/// </summary>
public partial class ProductPriceHistoryWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string AllPriceTypes = "Все виды цен";

    private readonly string _itemCode;
    private readonly string _itemName;
    private readonly ObservableCollection<HistoryRow> _rows = new();
    private List<ProductPriceHistoryRecord> _allRecords = new();

    public ProductPriceHistoryWindow(string itemCode, string itemName)
    {
        InitializeComponent();
        _itemCode = (itemCode ?? string.Empty).Trim();
        _itemName = (itemName ?? string.Empty).Trim();
        HistoryGrid.ItemsSource = _rows;
        HeaderSubtitleText.Text = string.IsNullOrEmpty(_itemCode)
            ? "Код товара не определён"
            : $"{_itemCode} • {_itemName}";
        Loaded += HandleLoaded;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        ReloadHistory();
    }

    private void HandleRefreshClick(object sender, RoutedEventArgs e) => ReloadHistory();

    private void HandleCloseClick(object sender, RoutedEventArgs e) => Close();

    private void HandlePriceTypeFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ReloadHistory()
    {
        _allRecords.Clear();
        _rows.Clear();

        if (string.IsNullOrWhiteSpace(_itemCode))
        {
            UpdateEmptyState();
            StatusText.Text = "Код товара не определён.";
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

            _allRecords = backplane.TryLoadPriceHistory(_itemCode).ToList();

            // Заполняем фильтр уникальными видами цен.
            var priceTypes = _allRecords
                .Select(r => TextMojibakeFixer.NormalizeText(r.PriceType))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            var previouslySelected = PriceTypeFilter.SelectedItem as string;
            PriceTypeFilter.Items.Clear();
            PriceTypeFilter.Items.Add(AllPriceTypes);
            foreach (var pt in priceTypes)
            {
                PriceTypeFilter.Items.Add(pt);
            }

            if (!string.IsNullOrEmpty(previouslySelected) && PriceTypeFilter.Items.Contains(previouslySelected))
            {
                PriceTypeFilter.SelectedItem = previouslySelected;
            }
            else
            {
                PriceTypeFilter.SelectedIndex = 0;
            }

            // ApplyFilter будет вызван SelectionChanged'ом, но на всякий случай.
            ApplyFilter();
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, "ProductPriceHistoryWindow.ReloadHistory");
            StatusText.Text = $"Ошибка загрузки: {exception.Message}";
        }
    }

    private void ApplyFilter()
    {
        _rows.Clear();
        var selected = PriceTypeFilter.SelectedItem as string ?? AllPriceTypes;

        var filtered = _allRecords.AsEnumerable();
        if (!string.Equals(selected, AllPriceTypes, StringComparison.Ordinal))
        {
            filtered = filtered.Where(r => string.Equals(
                TextMojibakeFixer.NormalizeText(r.PriceType),
                selected,
                StringComparison.OrdinalIgnoreCase));
        }

        foreach (var record in filtered)
        {
            _rows.Add(new HistoryRow
            {
                PeriodDisplay = record.Period == DateTime.MinValue
                    ? string.Empty
                    : record.Period.ToString("dd.MM.yyyy HH:mm", RuCulture),
                PriceType = TextMojibakeFixer.NormalizeText(record.PriceType),
                PriceDisplay = record.PriceValue.ToString("N2", RuCulture),
                CurrencyCode = string.IsNullOrWhiteSpace(record.CurrencyCode) ? "RUB" : record.CurrencyCode,
                UnitName = TextMojibakeFixer.NormalizeText(record.UnitName)
            });
        }

        StatusText.Text = _allRecords.Count switch
        {
            0 => "История цен пуста.",
            _ => $"Показано {_rows.Count:N0} из {_allRecords.Count:N0} записей."
        };

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class HistoryRow
    {
        public string PeriodDisplay { get; set; } = string.Empty;

        public string PriceType { get; set; } = string.Empty;

        public string PriceDisplay { get; set; } = string.Empty;

        public string CurrencyCode { get; set; } = string.Empty;

        public string UnitName { get; set; } = string.Empty;
    }
}
