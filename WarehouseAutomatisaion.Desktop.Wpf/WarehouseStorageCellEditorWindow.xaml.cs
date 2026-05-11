using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseStorageCellEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly WarehouseStorageCellRecord _draft;

    public WarehouseStorageCellEditorWindow(
        IReadOnlyList<string> warehouses,
        WarehouseStorageCellRecord cell)
    {
        _draft = cell.Clone();

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = string.IsNullOrWhiteSpace(_draft.Code) ? "Новая ячейка" : $"Ячейка {_draft.Code}";
        HeaderTitleText.Text = string.IsNullOrWhiteSpace(_draft.Code) ? "Новая ячейка" : "Карточка ячейки";
        WarehouseComboBox.ItemsSource = warehouses
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(Ui)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        CellTypeComboBox.ItemsSource = new[] { "Штучная", "Паллетная", "Длинномер", "Временная", "Карантин" };
        StatusComboBox.ItemsSource = new[] { "Активна", "Закрыта" };
        LoadDraft();
    }

    public WarehouseStorageCellRecord? ResultCell { get; private set; }

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private void LoadDraft()
    {
        SelectComboValue(WarehouseComboBox, Ui(_draft.Warehouse));
        CodeTextBox.Text = Ui(_draft.Code);
        ZoneCodeTextBox.Text = string.IsNullOrWhiteSpace(_draft.ZoneCode) ? "STG" : Ui(_draft.ZoneCode);
        ZoneNameTextBox.Text = string.IsNullOrWhiteSpace(_draft.ZoneName) ? "Хранение" : Ui(_draft.ZoneName);
        SelectComboValue(CellTypeComboBox, string.IsNullOrWhiteSpace(_draft.CellType) ? "Штучная" : Ui(_draft.CellType));
        SelectComboValue(StatusComboBox, string.IsNullOrWhiteSpace(_draft.Status) ? "Активна" : Ui(_draft.Status));
        RowTextBox.Text = Math.Max(1, _draft.Row).ToString("N0", RuCulture);
        RackTextBox.Text = Math.Max(1, _draft.Rack).ToString("N0", RuCulture);
        ShelfTextBox.Text = Math.Max(1, _draft.Shelf).ToString("N0", RuCulture);
        CellTextBox.Text = Math.Max(1, _draft.Cell).ToString("N0", RuCulture);
        CapacityTextBox.Text = _draft.Capacity > 0m ? _draft.Capacity.ToString("N0", RuCulture) : "40";
        QrPayloadTextBox.Text = Ui(_draft.QrPayload);
        CommentTextBox.Text = Ui(_draft.Comment);
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        var warehouse = WarehouseComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(warehouse))
        {
            ValidationText.Text = "Укажите склад.";
            return;
        }

        if (!TryParsePositiveInt(RowTextBox.Text, out var row)
            || !TryParsePositiveInt(RackTextBox.Text, out var rack)
            || !TryParsePositiveInt(ShelfTextBox.Text, out var shelf)
            || !TryParsePositiveInt(CellTextBox.Text, out var cell))
        {
            ValidationText.Text = "Ряд, стеллаж, полка и место должны быть положительными числами.";
            return;
        }

        if (!TryParseDecimal(CapacityTextBox.Text, out var capacity) || capacity < 0m)
        {
            ValidationText.Text = "Лимит вместимости должен быть числом не меньше нуля.";
            return;
        }

        var code = CodeTextBox.Text.Trim();
        var zoneCode = string.IsNullOrWhiteSpace(ZoneCodeTextBox.Text) ? "STG" : ZoneCodeTextBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            code = $"{zoneCode}-{row:00}-{rack:00}-{shelf:00}-{cell:00}";
        }

        var payload = WarehouseCellStoragePreparationPlan.BuildCellQrPayload(warehouse, code.ToUpperInvariant());
        ResultCell = new WarehouseStorageCellRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Warehouse = warehouse,
            Code = code.ToUpperInvariant(),
            ZoneCode = zoneCode,
            ZoneName = string.IsNullOrWhiteSpace(ZoneNameTextBox.Text) ? "Хранение" : ZoneNameTextBox.Text.Trim(),
            Row = row,
            Rack = rack,
            Shelf = shelf,
            Cell = cell,
            CellType = string.IsNullOrWhiteSpace(CellTypeComboBox.Text) ? "Штучная" : CellTypeComboBox.Text.Trim(),
            Capacity = capacity,
            Status = string.IsNullOrWhiteSpace(StatusComboBox.Text) ? "Активна" : StatusComboBox.Text.Trim(),
            QrPayload = payload,
            Comment = CommentTextBox.Text.Trim()
        };

        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
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

            comboBox.Text = value;
            return;
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static bool TryParsePositiveInt(string value, out int result)
    {
        value = value.Replace('\u00A0', ' ').Replace(" ", string.Empty);
        return (int.TryParse(value, NumberStyles.Integer, RuCulture, out result)
                || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
               && result > 0;
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
