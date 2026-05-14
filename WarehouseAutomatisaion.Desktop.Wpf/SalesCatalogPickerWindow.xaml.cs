using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesCatalogPickerWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly IReadOnlyList<SalesCatalogItemOption> _catalogItems;
    private readonly ObservableCollection<CatalogPickerRow> _rows = [];

    public SalesCatalogPickerWindow(IReadOnlyList<SalesCatalogItemOption> catalogItems)
    {
        _catalogItems = catalogItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Code))
            .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.Code), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        CatalogGrid.ItemsSource = _rows;
        Loaded += HandleLoaded;
        ApplyFilter();
    }

    public SalesOrderLineRecord? ResultLine { get; private set; }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
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
        SelectedItemText.Text = $"{row.Code} | {row.Name}";
    }

    private void HandleCatalogGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HandleSaveClick(sender, e);
    }

    private void HandleCatalogGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        HandleSaveClick(sender, e);
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (CatalogGrid.SelectedItem is not CatalogPickerRow row)
        {
            ValidationText.Text = "Выберите номенклатуру.";
            return;
        }

        if (!TryParseDecimal(QuantityTextBox.Text, out var quantity) || quantity <= 0m)
        {
            ValidationText.Text = "Укажите количество больше нуля.";
            return;
        }

        if (!TryParseDecimal(PriceTextBox.Text, out var price) || price < 0m)
        {
            ValidationText.Text = "Укажите корректную цену.";
            return;
        }

        ResultLine = new SalesOrderLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = row.Code,
            ItemName = row.Name,
            Unit = row.Unit,
            Quantity = quantity,
            Price = price
        };
        DialogResult = true;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        value = Ui(value)
            .Replace("₽", string.Empty, StringComparison.Ordinal)
            .Replace('\u00A0', ' ')
            .Replace(" ", string.Empty);
        return decimal.TryParse(value, NumberStyles.Number, RuCulture, out result)
               || decimal.TryParse(
                   value.Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out result);
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed record CatalogPickerRow(string Code, string Name, string Unit, decimal Price)
    {
        public string PriceDisplay => $"{Price:N2} ₽";

        public static CatalogPickerRow Create(SalesCatalogItemOption item)
        {
            return new CatalogPickerRow(
                Ui(item.Code),
                Ui(item.Name),
                SalesDocumentDisplayFormatter.NormalizeUnit(item.Unit, item.Name),
                item.DefaultPrice);
        }
    }
}
