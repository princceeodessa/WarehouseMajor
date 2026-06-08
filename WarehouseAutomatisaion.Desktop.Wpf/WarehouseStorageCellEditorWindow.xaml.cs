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
    private bool _isLoading;

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
            .Concat([_draft.Warehouse])
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
        _isLoading = true;
        SelectComboValue(WarehouseComboBox, Ui(_draft.Warehouse));
        CodeTextBox.Text = Ui(_draft.Code);
        ZoneCodeTextBox.Text = string.IsNullOrWhiteSpace(_draft.ZoneCode) ? "STG" : Ui(_draft.ZoneCode);
        ZoneNameTextBox.Text = string.IsNullOrWhiteSpace(_draft.ZoneName) ? "Хранение" : Ui(_draft.ZoneName);
        SelectComboValue(CellTypeComboBox, string.IsNullOrWhiteSpace(_draft.CellType) ? "Штучная" : Ui(_draft.CellType));
        SelectComboValue(StatusComboBox, string.IsNullOrWhiteSpace(_draft.Status) ? "Активна" : Ui(_draft.Status));
        RackTextBox.Text = Math.Max(1, _draft.Rack).ToString("N0", RuCulture);
        ShelfTextBox.Text = Math.Max(1, _draft.Shelf).ToString("N0", RuCulture);
        RowTextBox.Text = Math.Max(1, _draft.Row).ToString("N0", RuCulture);
        CapacityTextBox.Text = _draft.Capacity > 0m ? _draft.Capacity.ToString("N0", RuCulture) : "40";
        QrPayloadTextBox.Text = Ui(_draft.QrPayload);
        CommentTextBox.Text = Ui(_draft.Comment);
        _isLoading = false;
        if (string.IsNullOrWhiteSpace(_draft.Code))
        {
            UpdateGeneratedCode();
        }
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        var warehouse = (WarehouseComboBox.SelectedItem as string ?? WarehouseComboBox.Text).Trim();
        if (string.IsNullOrWhiteSpace(warehouse))
        {
            ValidationText.Text = "Укажите склад.";
            return;
        }

        if (!TryParseRack(RackTextBox.Text, out var rack)
            || !TryParsePositiveInt(ShelfTextBox.Text, out var shelf)
            || !TryParsePositiveInt(RowTextBox.Text, out var row))
        {
            ValidationText.Text = "Стеллаж должен быть числом или одной буквой, этаж и ряд — положительными числами.";
            return;
        }

        if (!TryParseDecimal(CapacityTextBox.Text, out var capacity) || capacity < 0m)
        {
            ValidationText.Text = "Лимит вместимости должен быть числом не меньше нуля.";
            return;
        }

        UpdateGeneratedCode();
        var code = CodeTextBox.Text.Trim();
        var zoneCode = string.IsNullOrWhiteSpace(ZoneCodeTextBox.Text) ? "STG" : ZoneCodeTextBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            ValidationText.Text = "Заполните зону, стеллаж, этаж и ряд — код сформируется автоматически.";
            return;
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
            Cell = 0,
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

    private void HandleAddressPartChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        UpdateGeneratedCode();
    }

    private void UpdateGeneratedCode()
    {
        if (CodeTextBox is null
            || ZoneCodeTextBox is null
            || ZoneNameTextBox is null
            || RackTextBox is null
            || ShelfTextBox is null
            || RowTextBox is null)
        {
            return;
        }

        var zone = NormalizeCodePart(!string.IsNullOrWhiteSpace(ZoneCodeTextBox.Text)
            ? ZoneCodeTextBox.Text
            : ZoneNameTextBox.Text);
        var rack = NormalizeCodePart(RackTextBox.Text);
        var floor = NormalizeCodePart(ShelfTextBox.Text);
        var row = NormalizeCodePart(RowTextBox.Text);

        CodeTextBox.Text = string.IsNullOrWhiteSpace(zone)
            || string.IsNullOrWhiteSpace(rack)
            || string.IsNullOrWhiteSpace(floor)
            || string.IsNullOrWhiteSpace(row)
                ? string.Empty
                : string.Join("-", new[] { zone, rack, floor, row });
    }

    private static string NormalizeCodePart(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "0")
        {
            return string.Empty;
        }

        var result = new List<char>(trimmed.Length);
        var lastWasSeparator = false;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                result.Add(char.ToUpperInvariant(ch));
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
            {
                continue;
            }

            result.Add('-');
            lastWasSeparator = true;
        }

        return new string(result.ToArray()).Trim('-');
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

    private static bool TryParseRack(string value, out int result)
    {
        value = value.Replace('\u00A0', ' ').Replace(" ", string.Empty);
        if ((int.TryParse(value, NumberStyles.Integer, RuCulture, out result)
             || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            && result > 0)
        {
            return true;
        }

        if (value.Length == 1 && char.IsLetter(value[0]))
        {
            var letter = char.ToUpperInvariant(value[0]);
            if (letter is >= 'A' and <= 'Z')
            {
                result = letter - 'A' + 1;
                return true;
            }

            const string russianRackLetters = "АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩЭЮЯ";
            var index = russianRackLetters.IndexOf(letter);
            if (index >= 0)
            {
                result = index + 1;
                return true;
            }
        }

        result = 0;
        return false;
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
