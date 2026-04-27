using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Controls;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ProductPriceUpdateWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly CatalogWorkspace _workspace;
    private readonly IReadOnlyList<ProductsWorkspaceView.ProductRowViewModel> _products;

    public ProductPriceUpdateWindow(
        CatalogWorkspace workspace,
        IReadOnlyList<ProductsWorkspaceView.ProductRowViewModel> products)
    {
        _workspace = workspace;
        _products = products;

        InitializeComponent();

        PriceTypeComboBox.ItemsSource = workspace.PriceTypes
            .Select(item => Ui(item.Name))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .DefaultIfEmpty(workspace.GetDefaultPriceTypeName())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        PriceTypeComboBox.SelectedItem = workspace.GetDefaultPriceTypeName();
        if (PriceTypeComboBox.SelectedItem is null && PriceTypeComboBox.Items.Count > 0)
        {
            PriceTypeComboBox.SelectedIndex = 0;
        }

        ModeComboBox.ItemsSource = new[] { "Установить цену", "Изменить на процент" };
        ModeComboBox.SelectedIndex = 0;
        ValueTextBox.Text = products.Count == 1 ? products[0].Price.ToString("N2", RuCulture) : "0";
        ProductsCountText.Text = $"Позиций к обновлению: {products.Count:N0}";
    }

    public CatalogPriceRegistrationRecord? ResultDocument { get; private set; }

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (_products.Count == 0)
        {
            ValidationText.Text = "Выберите товары для обновления цены.";
            return;
        }

        if (!TryParseDecimal(ValueTextBox.Text, out var value))
        {
            ValidationText.Text = "Введите корректное число.";
            return;
        }

        var isPercent = string.Equals(ModeComboBox.SelectedItem?.ToString(), "Изменить на процент", StringComparison.OrdinalIgnoreCase);
        var document = _workspace.CreatePriceRegistrationDraft();
        document.PriceTypeName = PriceTypeComboBox.SelectedItem?.ToString() ?? _workspace.GetDefaultPriceTypeName();
        document.Status = ApplyImmediatelyCheckBox.IsChecked == true ? "Проведен" : "Подготовлен";
        document.Comment = isPercent
            ? $"Массовое изменение цены на {value:N2}%."
            : "Установка цены из модуля Товары.";
        document.Lines.Clear();

        foreach (var product in _products)
        {
            var newPrice = isPercent
                ? Math.Round(product.Price * (1m + value / 100m), 2, MidpointRounding.AwayFromZero)
                : value;

            if (newPrice < 0m)
            {
                ValidationText.Text = "Цена не может быть отрицательной.";
                return;
            }

            document.Lines.Add(new CatalogPriceRegistrationLineRecord
            {
                Id = Guid.NewGuid(),
                ItemCode = product.Code,
                ItemName = product.Name,
                Unit = product.Unit,
                PreviousPrice = product.Price,
                NewPrice = newPrice
            });
        }

        ResultDocument = document;
        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        value = value
            .Replace('\u00A0', ' ')
            .Replace(" ", string.Empty);
        return decimal.TryParse(
                   value,
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                   RuCulture,
                   out result)
               || decimal.TryParse(
                   value.Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out result);
    }
}
