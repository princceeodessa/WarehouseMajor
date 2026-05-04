using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseLineEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly IReadOnlyList<SalesCatalogItemOption> _catalogItems;
    private readonly IReadOnlyList<string> _storageCellOptions;
    private readonly bool _allowNegativeQuantity;
    private readonly bool _allowTargetLocation;
    private readonly OperationalWarehouseLineRecord _draft;

    public WarehouseLineEditorWindow(
        string title,
        string subtitle,
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        OperationalWarehouseLineRecord? line = null,
        bool allowNegativeQuantity = false,
        bool allowTargetLocation = true,
        IReadOnlyList<string>? storageCellOptions = null)
    {
        _catalogItems = catalogItems
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _storageCellOptions = (storageCellOptions ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _allowNegativeQuantity = allowNegativeQuantity;
        _allowTargetLocation = allowTargetLocation;
        _draft = line?.Clone() ?? new OperationalWarehouseLineRecord { Id = Guid.NewGuid() };

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = Ui(title);
        HeaderTitleText.Text = Ui(title);
        HeaderSubtitleText.Text = Ui(subtitle);
        QuantityLabelText.Text = allowNegativeQuantity ? "Корректировка (+/-)" : "Количество";
        TargetLocationTextBox.IsEnabled = allowTargetLocation;
        if (!allowTargetLocation)
        {
            TargetLocationTextBox.Opacity = 0.65;
        }

        ItemComboBox.ItemsSource = _catalogItems;
        SourceLocationTextBox.ItemsSource = _storageCellOptions;
        TargetLocationTextBox.ItemsSource = _storageCellOptions;
        LoadDraft();
    }

    public OperationalWarehouseLineRecord? ResultLine { get; private set; }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void LoadDraft()
    {
        var hasDraftItem = !string.IsNullOrWhiteSpace(_draft.ItemCode) || !string.IsNullOrWhiteSpace(_draft.ItemName);
        if (!string.IsNullOrWhiteSpace(_draft.ItemCode))
        {
            var selected = _catalogItems.FirstOrDefault(item => item.Code.Equals(_draft.ItemCode, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                ItemComboBox.SelectedItem = selected;
            }
        }

        if (ItemComboBox.SelectedItem is null && hasDraftItem)
        {
            ItemComboBox.Text = Ui(string.IsNullOrWhiteSpace(_draft.ItemName) ? _draft.ItemCode : _draft.ItemName);
            CodeTextBox.Text = Ui(_draft.ItemCode);
            UnitTextBox.Text = Ui(_draft.Unit);
        }
        else if (ItemComboBox.SelectedItem is null && _catalogItems.Count > 0)
        {
            ItemComboBox.SelectedIndex = 0;
        }

        QuantityTextBox.Text = _draft.Quantity != 0m
            ? _draft.Quantity.ToString("N2", RuCulture)
            : string.Empty;
        SourceLocationTextBox.Text = Ui(_draft.SourceLocation);
        TargetLocationTextBox.Text = Ui(_draft.TargetLocation);
        ApplySelectedItemDefaults();
    }

    private void HandleItemSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplySelectedItemDefaults();
    }

    private void HandleItemLostFocus(object sender, RoutedEventArgs e)
    {
        ResolveSelectedItem();
    }

    private void ApplySelectedItemDefaults()
    {
        if (ItemComboBox.SelectedItem is not SalesCatalogItemOption selected)
        {
            return;
        }

        CodeTextBox.Text = selected.Code;
        UnitTextBox.Text = selected.Unit;
    }

    private void ResolveSelectedItem()
    {
        var text = ItemComboBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var selected = _catalogItems.FirstOrDefault(item =>
            item.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
            || item.Code.Equals(text, StringComparison.OrdinalIgnoreCase)
            || $"{item.Name} [{item.Code}]".Equals(text, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            ItemComboBox.SelectedItem = selected;
            return;
        }

        selected = _catalogItems.FirstOrDefault(item =>
            item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || item.Code.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            ItemComboBox.SelectedItem = selected;
        }
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        ResolveSelectedItem();

        if (ItemComboBox.SelectedItem is not SalesCatalogItemOption selectedItem)
        {
            ValidationText.Text = "Выберите номенклатуру из каталога.";
            return;
        }

        if (!TryParseQuantity(out var quantity))
        {
            ValidationText.Text = _allowNegativeQuantity
                ? "Укажите корректировку числом."
                : "Укажите количество числом.";
            return;
        }

        if (!_allowNegativeQuantity && quantity <= 0m)
        {
            ValidationText.Text = "Количество должно быть больше нуля.";
            return;
        }

        if (_allowNegativeQuantity && quantity == 0m)
        {
            ValidationText.Text = "Корректировка не может быть нулевой.";
            return;
        }

        ResultLine = new OperationalWarehouseLineRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            ItemCode = selectedItem.Code,
            ItemName = selectedItem.Name,
            Unit = selectedItem.Unit,
            Quantity = quantity,
            SourceLocation = SourceLocationTextBox.Text.Trim(),
            TargetLocation = _allowTargetLocation ? TargetLocationTextBox.Text.Trim() : string.Empty,
            RelatedDocument = _draft.RelatedDocument,
            Fields = _draft.Fields.ToArray()
        };

        DialogResult = true;
    }

    private bool TryParseQuantity(out decimal quantity)
    {
        var raw = QuantityTextBox.Text.Trim();
        return decimal.TryParse(raw, NumberStyles.Number, RuCulture, out quantity)
               || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity);
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
