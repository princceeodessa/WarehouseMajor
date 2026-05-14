using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private readonly ObservableCollection<CartRow> _cart = [];

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
        CartGrid.ItemsSource = _cart;
        _cart.CollectionChanged += HandleCartCollectionChanged;
        Loaded += HandleLoaded;
        ApplyFilter();
        RefreshCartSummary();
    }

    /// <summary>
    /// Возвращает первую строку из корзины (или текущий выбор, если корзина пуста)
    /// — для обратной совместимости со старым кодом, который ждёт одну позицию.
    /// </summary>
    public SalesOrderLineRecord? ResultLine => ResultLines.FirstOrDefault();

    public IReadOnlyList<SalesOrderLineRecord> ResultLines { get; private set; } = Array.Empty<SalesOrderLineRecord>();

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
        // Двойной клик теперь добавляет товар в корзину, а не закрывает окно —
        // чтобы можно было набрать несколько позиций подряд.
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

    private void HandleAddToCartClick(object sender, RoutedEventArgs e)
    {
        AddCurrentSelectionToCart();
    }

    private void AddCurrentSelectionToCart()
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
            QuantityTextBox.Focus();
            QuantityTextBox.SelectAll();
            return;
        }

        if (!TryParseDecimal(PriceTextBox.Text, out var price) || price < 0m)
        {
            ValidationText.Text = "Укажите корректную цену.";
            PriceTextBox.Focus();
            PriceTextBox.SelectAll();
            return;
        }

        _cart.Add(new CartRow(row.Code, row.Name, row.Unit, quantity, price));

        // Сбрасываем форму, чтобы пользователь сразу мог искать следующий товар.
        SearchTextBox.Text = string.Empty;
        QuantityTextBox.Text = "1";
        PriceTextBox.Text = "0,00";
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void HandleCartSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveFromCartButton.IsEnabled = CartGrid.SelectedItem is CartRow;
    }

    private void HandleCartGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (CartGrid.SelectedItem is CartRow row)
        {
            _cart.Remove(row);
        }
    }

    private void HandleRemoveFromCartClick(object sender, RoutedEventArgs e)
    {
        if (CartGrid.SelectedItem is CartRow row)
        {
            _cart.Remove(row);
        }
    }

    private void HandleClearCartClick(object sender, RoutedEventArgs e)
    {
        _cart.Clear();
    }

    private void HandleCartCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCartSummary();
    }

    private void RefreshCartSummary()
    {
        var count = _cart.Count;
        var total = _cart.Sum(item => item.Amount);
        CartTitleText.Text = count == 0
            ? "Корзина"
            : $"Корзина · {count:N0} позиций";
        CartEmptyHintText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CartTotalText.Text = count == 0
            ? string.Empty
            : $"Итого: {total:N2} ₽";
        ClearCartButton.IsEnabled = count > 0;
        RemoveFromCartButton.IsEnabled = count > 0 && CartGrid.SelectedItem is CartRow;
        SaveButton.Content = count > 0
            ? $"Перенести в документ ({count:N0})"
            : "Перенести в документ";
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        // Если в корзине есть позиции — забираем их все и закрываем окно.
        if (_cart.Count > 0)
        {
            ResultLines = _cart
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
            DialogResult = true;
            return;
        }

        // Корзина пуста — но в каталоге может быть выбран товар: добавим его «на лету»
        // и сразу закроем диалог (старое одно-click поведение для скорости).
        if (CatalogGrid.SelectedItem is not CatalogPickerRow row)
        {
            ValidationText.Text = "Корзина пуста. Выберите номенклатуру или нажмите «+ В корзину».";
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

        ResultLines = new[]
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
        DialogResult = true;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        value = Ui(value)
            .Replace("₽", string.Empty, StringComparison.Ordinal)
            .Replace(' ', ' ')
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

    private sealed record CartRow(string ItemCode, string ItemName, string Unit, decimal Quantity, decimal Price)
    {
        public decimal Amount => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);

        public string QuantityDisplay => Quantity.ToString("N2", RuCulture);

        public string PriceDisplay => $"{Price:N2} ₽";

        public string AmountDisplay => $"{Amount:N2} ₽";
    }
}
