using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ProductEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly CatalogWorkspace _workspace;
    private readonly CatalogItemRecord _draft;

    public ProductEditorWindow(CatalogWorkspace workspace, CatalogItemRecord? item = null)
    {
        _workspace = workspace;
        _draft = item?.Clone() ?? workspace.CreateItemDraft();

        InitializeComponent();

        Title = item is null ? "Новый товар" : $"Товар {_draft.Code}";
        HeaderTitleText.Text = item is null ? "Новый товар" : "Карточка товара";

        WarehouseComboBox.ItemsSource = workspace.Warehouses
            .Select(Ui)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        StatusComboBox.ItemsSource = workspace.ItemStatuses.Select(Ui).ToArray();
        CurrencyComboBox.ItemsSource = workspace.Currencies.Select(Ui).ToArray();

        LoadDraft();
    }

    public CatalogItemRecord? ResultItem { get; private set; }

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private void LoadDraft()
    {
        CodeTextBox.Text = Ui(_draft.Code);
        NameTextBox.Text = Ui(_draft.Name);
        CategoryTextBox.Text = Ui(_draft.Category);
        SupplierTextBox.Text = Ui(_draft.Supplier);
        WarehouseComboBox.Text = Ui(_draft.DefaultWarehouse);
        UnitTextBox.Text = string.IsNullOrWhiteSpace(_draft.Unit) ? "шт" : Ui(_draft.Unit);
        PriceTextBox.Text = _draft.DefaultPrice.ToString("N2", RuCulture);
        BarcodeTextBox.Text = Ui(_draft.BarcodeValue);
        NotesTextBox.Text = Ui(_draft.Notes);
        SelectComboValue(StatusComboBox, Ui(_draft.Status));
        SelectComboValue(CurrencyComboBox, Ui(_draft.CurrencyCode));
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(CodeTextBox.Text))
        {
            ValidationText.Text = "Укажите код товара.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            ValidationText.Text = "Укажите наименование товара.";
            return;
        }

        if (!TryParseDecimal(PriceTextBox.Text, out var price))
        {
            ValidationText.Text = "Цена должна быть числом.";
            return;
        }

        ResultItem = new CatalogItemRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Code = CodeTextBox.Text.Trim(),
            Name = NameTextBox.Text.Trim(),
            Unit = UnitTextBox.Text.Trim(),
            Category = CategoryTextBox.Text.Trim(),
            Supplier = SupplierTextBox.Text.Trim(),
            DefaultWarehouse = WarehouseComboBox.Text.Trim(),
            Status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text.Trim(),
            CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim(),
            DefaultPrice = price,
            BarcodeValue = BarcodeTextBox.Text.Trim(),
            BarcodeFormat = string.IsNullOrWhiteSpace(_draft.BarcodeFormat) ? "Code128" : _draft.BarcodeFormat,
            QrPayload = _draft.QrPayload,
            Notes = NotesTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный каталог" : _draft.SourceLabel
        };

        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static void SelectComboValue(System.Windows.Controls.ComboBox comboBox, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var selected = comboBox.Items
                .Cast<object>()
                .Select(item => item?.ToString() ?? string.Empty)
                .FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                comboBox.SelectedItem = selected;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
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
