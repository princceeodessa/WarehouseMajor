using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseLineEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly IReadOnlyList<SalesCatalogItemOption> _catalogItems;
    private readonly bool _allowNegativeQuantity;
    private readonly bool _allowTargetLocation;
    private readonly OperationalWarehouseLineRecord _draft;

    public WarehouseLineEditorWindow(
        string title,
        string subtitle,
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        OperationalWarehouseLineRecord? line = null,
        bool allowNegativeQuantity = false,
        bool allowTargetLocation = true)
    {
        _catalogItems = catalogItems
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _allowNegativeQuantity = allowNegativeQuantity;
        _allowTargetLocation = allowTargetLocation;
        _draft = line?.Clone() ?? new OperationalWarehouseLineRecord { Id = Guid.NewGuid() };

        InitializeComponent();

        Title = title;
        HeaderTitleText.Text = title;
        HeaderSubtitleText.Text = subtitle;
        QuantityLabelText.Text = allowNegativeQuantity ? "Корректировка (+/-)" : "Количество";
        TargetLocationTextBox.IsEnabled = allowTargetLocation;
        if (!allowTargetLocation)
        {
            TargetLocationTextBox.Opacity = 0.65;
        }

        ItemComboBox.ItemsSource = _catalogItems;
        LoadDraft();
    }

    public OperationalWarehouseLineRecord? ResultLine { get; private set; }

    private void LoadDraft()
    {
        if (!string.IsNullOrWhiteSpace(_draft.ItemCode))
        {
            var selected = _catalogItems.FirstOrDefault(item => item.Code.Equals(_draft.ItemCode, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                ItemComboBox.SelectedItem = selected;
            }
        }

        if (ItemComboBox.SelectedItem is null && _catalogItems.Count > 0)
        {
            ItemComboBox.SelectedIndex = 0;
        }

        QuantityTextBox.Text = _draft.Quantity != 0m
            ? _draft.Quantity.ToString("N2", RuCulture)
            : string.Empty;
        SourceLocationTextBox.Text = _draft.SourceLocation;
        TargetLocationTextBox.Text = _draft.TargetLocation;
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
