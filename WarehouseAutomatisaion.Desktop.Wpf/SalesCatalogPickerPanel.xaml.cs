using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesCatalogPickerPanel : UserControl
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x8B, 0x33));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0x30, 0x30));

    private IReadOnlyList<SalesCatalogItemOption> _catalogItems = Array.Empty<SalesCatalogItemOption>();
    private readonly ObservableCollection<CatalogPickerRow> _rows = [];
    private readonly ObservableCollection<CartRow> _cart = [];

    public event EventHandler<SalesPickerLinesEventArgs>? LinesTransferred;
    public event EventHandler? CloseRequested;

    public SalesCatalogPickerPanel()
    {
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);
        CatalogGrid.ItemsSource = _rows;
        CartGrid.ItemsSource = _cart;
        _cart.CollectionChanged += HandleCartCollectionChanged;
        RefreshCartSummary();
    }

    public void LoadCatalog(IReadOnlyList<SalesCatalogItemOption> catalogItems)
    {
        _catalogItems = (catalogItems ?? Array.Empty<SalesCatalogItemOption>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Code))
            .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.Code), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        ApplyFilter();
    }

    public void FocusSearch()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public bool HasCartItems => _cart.Count > 0;

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void HandleFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void HandleFiltersToggleChanged(object sender, RoutedEventArgs e)
    {
        FiltersPanel.Visibility = FiltersToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        var selectedCode = (CatalogGrid.SelectedItem as CatalogPickerRow)?.Code;
        var query = Ui(SearchTextBox.Text).Trim();
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var filtered = _catalogItems
            .Where(item => tokens.Length == 0 || tokens.All(token => MatchesToken(item, token)))
            .Take(500)
            .Select(CatalogPickerRow.Create)
            .ToArray();

        _rows.Clear();
        foreach (var row in filtered)
        {
            _rows.Add(row);
        }

        ResultCountText.Text = query.Length == 0
            ? $"Показано {_rows.Count:N0} из {_catalogItems.Count:N0}"
            : $"Найдено {_rows.Count:N0} из {_catalogItems.Count:N0}";

        var selected = !string.IsNullOrWhiteSpace(selectedCode)
            ? _rows.FirstOrDefault(row => row.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase))
            : null;
        CatalogGrid.SelectedItem = selected ?? _rows.FirstOrDefault();
    }

    private static bool MatchesToken(SalesCatalogItemOption item, string token)
    {
        return Ui(item.Code).Contains(token, StringComparison.CurrentCultureIgnoreCase)
               || Ui(item.Name).Contains(token, StringComparison.CurrentCultureIgnoreCase)
               || Ui(item.Unit).Contains(token, StringComparison.CurrentCultureIgnoreCase);
    }

    private void HandleCatalogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CatalogGrid.SelectedItem is not CatalogPickerRow row)
        {
            SelectedItemText.Text = string.Empty;
            return;
        }

        QuantityLabelText.Text = $"Количество ({row.Unit})";
        PriceTextBox.Text = row.Price.ToString("N2", RuCulture);
        SelectedItemText.Text = $"{row.Code} • {row.Name}";
    }

    private void HandleCatalogGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AddCurrentSelectionToCart();
    }

    private void HandleCatalogGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        AddCurrentSelectionToCart();
    }

    private void AddCurrentSelectionToCart()
    {
        if (CatalogGrid.SelectedItem is not CatalogPickerRow row)
        {
            return;
        }

        if (!TryParseDecimal(QuantityTextBox.Text, out var quantity) || quantity <= 0m)
        {
            QuantityTextBox.Focus();
            QuantityTextBox.SelectAll();
            return;
        }

        if (!TryParseDecimal(PriceTextBox.Text, out var price) || price < 0m)
        {
            PriceTextBox.Focus();
            PriceTextBox.SelectAll();
            return;
        }

        _cart.Add(new CartRow(row.Code, row.Name, row.Unit, quantity, price));

        SearchTextBox.Text = string.Empty;
        QuantityTextBox.Text = "1";
        PriceTextBox.Text = "0,00";
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void HandleCartSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // зарезервировано на будущее (удаление через клавишу Delete и т. п.)
    }

    private void HandleCartGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (CartGrid.SelectedItem is CartRow row)
        {
            _cart.Remove(row);
        }
    }

    private void HandleClearCartClick(object sender, RoutedEventArgs e) => _cart.Clear();

    private void HandleCartCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCartSummary();

    private void RefreshCartSummary()
    {
        var count = _cart.Count;
        var total = _cart.Sum(item => item.Amount);
        CartSummaryText.Text = count == 0
            ? "Корзина пуста"
            : $"Ваша корзина: {count:N0} на {total:N2} ₽";
        ClearButton.IsEnabled = count > 0;
        TransferButton.Content = count > 0
            ? $"Перенести в документ ({count:N0})"
            : "Перенести в документ";
    }

    private void HandleTransferClick(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0)
        {
            // если в корзине пусто — попробуем перенести текущий выбор (быстрый сценарий)
            if (CatalogGrid.SelectedItem is not CatalogPickerRow row)
            {
                return;
            }

            if (!TryParseDecimal(QuantityTextBox.Text, out var quantity) || quantity <= 0m)
            {
                return;
            }

            if (!TryParseDecimal(PriceTextBox.Text, out var price) || price < 0m)
            {
                return;
            }

            var oneLine = new[]
            {
                new SalesOrderLineRecord
                {
                    Id = Guid.NewGuid(),
                    ItemCode = row.Code,
                    ItemName = row.Name,
                    Unit = row.Unit,
                    Quantity = quantity,
                    Price = price
                }
            };
            LinesTransferred?.Invoke(this, new SalesPickerLinesEventArgs(oneLine));
            return;
        }

        var lines = _cart
            .Select(row => new SalesOrderLineRecord
            {
                Id = Guid.NewGuid(),
                ItemCode = row.ItemCode,
                ItemName = row.ItemName,
                Unit = row.Unit,
                Quantity = row.Quantity,
                Price = row.Price
            })
            .ToArray();
        _cart.Clear();
        LinesTransferred?.Invoke(this, new SalesPickerLinesEventArgs(lines));
    }

    private void HandleCloseClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        value = Ui(value)
            .Replace("₽", string.Empty, StringComparison.Ordinal)
            .Replace(' ', ' ')
            .Replace(" ", string.Empty);
        return decimal.TryParse(value, NumberStyles.Number, RuCulture, out result)
               || decimal.TryParse(
                   value.Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out result);
    }

    private sealed record CatalogPickerRow(string Code, string Name, string Unit, decimal Price)
    {
        public string PriceDisplay => Price > 0m ? Price.ToString("N2", RuCulture) : string.Empty;
        public Brush NameBrush => Price > 0m ? ActiveBrush : InactiveBrush;

        public static CatalogPickerRow Create(SalesCatalogItemOption item)
        {
            return new CatalogPickerRow(
                Ui(item.Code),
                Ui(item.Name),
                SalesDocumentDisplayFormatter.NormalizeUnit(item.Unit, item.Name),
                item.DefaultPrice);
        }
    }

    private sealed record CartRow(string ItemCode, string ItemName, string Unit, decimal Quantity, decimal Price)
    {
        public decimal Amount => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);
        public string QuantityDisplay => Quantity.ToString("N3", RuCulture);
        public string PriceDisplay => Price.ToString("N2", RuCulture);
        public string AmountDisplay => Amount.ToString("N2", RuCulture);
    }
}

public sealed class SalesPickerLinesEventArgs : EventArgs
{
    public SalesPickerLinesEventArgs(IReadOnlyList<SalesOrderLineRecord> lines)
    {
        Lines = lines;
    }

    public IReadOnlyList<SalesOrderLineRecord> Lines { get; }
}
