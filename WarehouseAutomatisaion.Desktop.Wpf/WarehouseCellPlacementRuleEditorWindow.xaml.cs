using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseCellPlacementRuleEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IReadOnlyList<SalesCatalogItemOption> _catalogItems;
    private readonly IReadOnlyList<WarehouseStorageCellRecord> _cells;
    private readonly WarehouseCellPlacementRuleRecord _draft;

    public WarehouseCellPlacementRuleEditorWindow(
        IReadOnlyList<string> warehouses,
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        IReadOnlyList<WarehouseStorageCellRecord> cells,
        WarehouseCellPlacementRuleRecord? rule = null)
    {
        _catalogItems = catalogItems
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _cells = cells
            .OrderBy(item => item.Warehouse, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _draft = rule?.Clone() ?? new WarehouseCellPlacementRuleRecord
        {
            Id = Guid.NewGuid(),
            Warehouse = warehouses.FirstOrDefault() ?? string.Empty,
            IsActive = true,
            ForbidMixedCategories = true
        };

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = rule is null ? "Новое правило размещения" : "Правило размещения";
        HeaderTitleText.Text = Title;
        WarehouseComboBox.ItemsSource = warehouses
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        ItemComboBox.ItemsSource = _catalogItems;
        LoadDraft();
    }

    public WarehouseCellPlacementRuleRecord? ResultRule { get; private set; }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void LoadDraft()
    {
        WarehouseComboBox.SelectedItem = WarehouseComboBox.Items
            .Cast<string>()
            .FirstOrDefault(item => item.Equals(_draft.Warehouse, StringComparison.OrdinalIgnoreCase))
            ?? WarehouseComboBox.Items.Cast<string>().FirstOrDefault();
        RefreshCellOptions();

        if (!string.IsNullOrWhiteSpace(_draft.ItemCode))
        {
            ItemComboBox.SelectedItem = _catalogItems.FirstOrDefault(item => item.Code.Equals(_draft.ItemCode, StringComparison.OrdinalIgnoreCase));
        }

        if (ItemComboBox.SelectedItem is null && !string.IsNullOrWhiteSpace(_draft.ItemName))
        {
            ItemComboBox.Text = Ui(_draft.ItemName);
        }

        ItemCodeTextBox.Text = Ui(_draft.ItemCode);
        CategoryTextBox.Text = Ui(_draft.Category);
        PrimaryCellComboBox.Text = Ui(_draft.PrimaryCellCode);
        ReserveCellComboBox.Text = Ui(_draft.ReserveCellCode);
        ZonePriorityTextBox.Text = Ui(_draft.ZonePriority);
        ForbidMixedCategoriesCheckBox.IsChecked = _draft.ForbidMixedCategories;
        IsActiveCheckBox.IsChecked = _draft.IsActive;
        CommentTextBox.Text = Ui(_draft.Comment);
    }

    private void HandleWarehouseChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshCellOptions();
    }

    private void RefreshCellOptions()
    {
        var warehouse = WarehouseComboBox.SelectedItem as string ?? WarehouseComboBox.Text;
        var cellCodes = _cells
            .Where(item => string.IsNullOrWhiteSpace(warehouse) || item.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.IsActive)
            .Select(item => item.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        PrimaryCellComboBox.ItemsSource = cellCodes;
        ReserveCellComboBox.ItemsSource = cellCodes;
    }

    private void HandleItemChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySelectedItem();
    }

    private void HandleItemLostFocus(object sender, RoutedEventArgs e)
    {
        ResolveItemFromText();
        ApplySelectedItem();
    }

    private void ResolveItemFromText()
    {
        var text = ItemComboBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var selected = _catalogItems.FirstOrDefault(item =>
            item.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
            || item.Code.Equals(text, StringComparison.OrdinalIgnoreCase)
            || $"{item.Name} [{item.Code}]".Equals(text, StringComparison.OrdinalIgnoreCase))
            ?? _catalogItems.FirstOrDefault(item =>
                item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.Code.Contains(text, StringComparison.OrdinalIgnoreCase));

        if (selected is not null)
        {
            ItemComboBox.SelectedItem = selected;
        }
    }

    private void ApplySelectedItem()
    {
        if (ItemComboBox.SelectedItem is not SalesCatalogItemOption selected)
        {
            return;
        }

        ItemCodeTextBox.Text = selected.Code;
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        ResolveItemFromText();

        var item = ItemComboBox.SelectedItem as SalesCatalogItemOption;
        var itemName = item?.Name ?? ItemComboBox.Text.Trim();
        var itemCode = FirstNonEmpty(ItemCodeTextBox.Text, item?.Code ?? string.Empty);
        var category = CategoryTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(itemCode) && string.IsNullOrWhiteSpace(itemName) && string.IsNullOrWhiteSpace(category))
        {
            ValidationText.Text = "Укажите товар или категорию.";
            return;
        }

        var primary = PrimaryCellComboBox.Text.Trim();
        var reserve = ReserveCellComboBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(primary)
            && !string.IsNullOrWhiteSpace(reserve)
            && primary.Equals(reserve, StringComparison.OrdinalIgnoreCase))
        {
            ValidationText.Text = "Основная и резервная ячейки должны отличаться.";
            return;
        }

        ResultRule = new WarehouseCellPlacementRuleRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Warehouse = WarehouseComboBox.SelectedItem as string ?? WarehouseComboBox.Text.Trim(),
            ItemCode = itemCode,
            ItemName = itemName,
            Category = category,
            PrimaryCellCode = primary,
            ReserveCellCode = reserve,
            ZonePriority = ZonePriorityTextBox.Text.Trim(),
            ForbidMixedCategories = ForbidMixedCategoriesCheckBox.IsChecked == true,
            IsActive = IsActiveCheckBox.IsChecked == true,
            Comment = CommentTextBox.Text.Trim()
        };
        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.Select(Ui).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
