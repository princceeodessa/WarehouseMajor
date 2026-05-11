using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseCellGroupPlacementWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly WarehouseCellBalanceRecord _balance;
    private readonly IReadOnlyList<WarehouseStorageCellRecord> _cells;
    private readonly IReadOnlyList<WarehouseCellBalanceRecord> _currentBalances;
    private readonly WarehouseCellPlacementRuleRecord? _rule;
    private readonly ObservableCollection<PlacementRowViewModel> _rows = [];

    public WarehouseCellGroupPlacementWindow(
        WarehouseCellBalanceRecord balance,
        IReadOnlyList<WarehouseStorageCellRecord> cells,
        IReadOnlyList<WarehouseCellBalanceRecord> currentBalances,
        WarehouseCellPlacementRuleRecord? rule)
    {
        _balance = balance;
        _cells = cells
            .Where(item => item.IsActive)
            .Where(item => WarehouseMatches(item.Warehouse, balance.Warehouse))
            .OrderBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _currentBalances = currentBalances;
        _rule = rule?.Clone();

        CellOptions = _cells
            .Select(item => item.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);
        DataContext = this;

        SubtitleText.Text = "Разложите один товар по нескольким адресам. Сумма строк не должна превышать свободный остаток без ячейки.";
        ItemText.Text = $"{Ui(balance.ItemName)} [{Ui(balance.ItemCode)}]";
        AvailableText.Text = FormatQuantity(balance.Quantity, balance.Unit);
        RuleText.Text = BuildRuleText(rule);
        RowsDataGrid.ItemsSource = _rows;
        AutoDistribute();
    }

    public IReadOnlyList<string> CellOptions { get; }

    public IReadOnlyList<WarehouseCellPlacementResult> ResultPlacements { get; private set; } = Array.Empty<WarehouseCellPlacementResult>();

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void HandleAutoDistributeClick(object sender, RoutedEventArgs e)
    {
        AutoDistribute();
    }

    private void HandleAddRowClick(object sender, RoutedEventArgs e)
    {
        _rows.Add(new PlacementRowViewModel(CellOptions.FirstOrDefault() ?? string.Empty, string.Empty, "Укажите количество."));
    }

    private void HandleRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if (RowsDataGrid.SelectedItem is PlacementRowViewModel row)
        {
            _rows.Remove(row);
        }
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        RowsDataGrid.CommitEdit();

        var placements = new List<WarehouseCellPlacementResult>();
        var usedCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0m;

        foreach (var row in _rows)
        {
            var cellCode = Ui(row.CellCode).Trim();
            if (string.IsNullOrWhiteSpace(cellCode))
            {
                ValidationText.Text = "В каждой строке должна быть ячейка.";
                return;
            }

            if (!usedCells.Add(cellCode))
            {
                ValidationText.Text = $"Ячейка {cellCode} указана несколько раз.";
                return;
            }

            var cell = _cells.FirstOrDefault(item => item.Code.Equals(cellCode, StringComparison.OrdinalIgnoreCase));
            if (cell is null)
            {
                ValidationText.Text = $"Ячейка {cellCode} не найдена или закрыта.";
                return;
            }

            if (!TryParseQuantity(row.QuantityText, out var quantity) || quantity <= 0m)
            {
                ValidationText.Text = $"Проверьте количество для ячейки {cellCode}.";
                return;
            }

            if (!ValidateCapacity(cell, quantity, out var capacityError))
            {
                ValidationText.Text = capacityError;
                return;
            }

            total += quantity;
            placements.Add(new WarehouseCellPlacementResult(cell, quantity));
        }

        if (placements.Count == 0)
        {
            ValidationText.Text = "Добавьте хотя бы одну строку размещения.";
            return;
        }

        if (total > _balance.Quantity)
        {
            ValidationText.Text = $"Сумма размещения {FormatQuantity(total, _balance.Unit)} больше доступного остатка {FormatQuantity(_balance.Quantity, _balance.Unit)}.";
            return;
        }

        ResultPlacements = placements;
        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AutoDistribute()
    {
        _rows.Clear();
        var remaining = _balance.Quantity;
        foreach (var cell in GetPreferredCells())
        {
            if (remaining <= 0m)
            {
                break;
            }

            var capacity = GetFreeCapacity(cell);
            var quantity = capacity is null ? remaining : Math.Min(remaining, Math.Max(0m, capacity.Value));
            if (quantity <= 0m)
            {
                continue;
            }

            _rows.Add(new PlacementRowViewModel(
                cell.Code,
                quantity.ToString("N2", RuCulture),
                BuildCellHint(cell, quantity)));
            remaining -= quantity;
        }

        if (_rows.Count == 0 && CellOptions.Count > 0)
        {
            _rows.Add(new PlacementRowViewModel(CellOptions[0], _balance.Quantity.ToString("N2", RuCulture), "Проверьте лимит ячейки."));
        }
    }

    private IEnumerable<WarehouseStorageCellRecord> GetPreferredCells()
    {
        var ordered = new List<WarehouseStorageCellRecord>();

        void AddCell(string code)
        {
            var cell = _cells.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (cell is not null && ordered.All(item => item.Id != cell.Id))
            {
                ordered.Add(cell);
            }
        }

        if (_rule is not null)
        {
            AddCell(_rule.PrimaryCellCode);
            AddCell(_rule.ReserveCellCode);

            foreach (var zone in SplitZones(_rule.ZonePriority))
            {
                foreach (var cell in _cells.Where(item => ZoneMatches(item, zone)))
                {
                    AddCell(cell.Code);
                }
            }
        }

        foreach (var cell in _cells)
        {
            AddCell(cell.Code);
        }

        return ordered;
    }

    private decimal? GetFreeCapacity(WarehouseStorageCellRecord cell)
    {
        if (cell.Capacity <= 0m)
        {
            return null;
        }

        var current = _currentBalances
            .Where(item => item.IsAddressed)
            .Where(item => WarehouseMatches(item.Warehouse, cell.Warehouse))
            .Where(item => Ui(item.Cell).Equals(Ui(cell.Code), StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Quantity);
        return cell.Capacity - current;
    }

    private bool ValidateCapacity(WarehouseStorageCellRecord cell, decimal quantity, out string error)
    {
        var free = GetFreeCapacity(cell);
        if (free is null || quantity <= free.Value)
        {
            error = string.Empty;
            return true;
        }

        error = $"В ячейке {Ui(cell.Code)} свободно по лимиту {free.Value:N2}, указано {quantity:N2}.";
        return false;
    }

    private string BuildCellHint(WarehouseStorageCellRecord cell, decimal quantity)
    {
        var capacity = GetFreeCapacity(cell);
        var zone = string.IsNullOrWhiteSpace(cell.ZoneName) ? cell.ZoneCode : cell.ZoneName;
        return capacity is null
            ? $"{Ui(zone)} / без лимита / {FormatQuantity(quantity, _balance.Unit)}"
            : $"{Ui(zone)} / свободно {capacity.Value:N2} / {FormatQuantity(quantity, _balance.Unit)}";
    }

    private static bool TryParseQuantity(string value, out decimal quantity)
    {
        var raw = Ui(value).Replace('\u00A0', ' ').Replace(" ", string.Empty);
        return decimal.TryParse(raw, NumberStyles.Number, RuCulture, out quantity)
               || decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out quantity);
    }

    private static string BuildRuleText(WarehouseCellPlacementRuleRecord? rule)
    {
        if (rule is null)
        {
            return "Нет активного правила";
        }

        var cells = string.Join(" / ", new[] { rule.PrimaryCellCode, rule.ReserveCellCode }.Where(item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(cells)
            ? $"Зоны: {Ui(rule.ZonePriority)}"
            : $"Ячейки: {Ui(cells)}";
    }

    private static bool WarehouseMatches(string left, string right)
    {
        var cleanLeft = Ui(left);
        var cleanRight = Ui(right);
        return string.IsNullOrWhiteSpace(cleanLeft)
               || string.IsNullOrWhiteSpace(cleanRight)
               || cleanLeft.Equals(cleanRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ZoneMatches(WarehouseStorageCellRecord cell, string zone)
    {
        return Ui(cell.ZoneCode).Equals(Ui(zone), StringComparison.OrdinalIgnoreCase)
               || Ui(cell.ZoneName).Equals(Ui(zone), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitZones(string zones)
    {
        return Ui(zones)
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private static string FormatQuantity(decimal quantity, string unit)
    {
        return $"{quantity:N2} {(string.IsNullOrWhiteSpace(unit) ? "шт" : Ui(unit))}";
    }

    private sealed class PlacementRowViewModel : INotifyPropertyChanged
    {
        private string _cellCode;
        private string _quantityText;

        public PlacementRowViewModel(string cellCode, string quantityText, string hint)
        {
            _cellCode = cellCode;
            _quantityText = quantityText;
            Hint = hint;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string CellCode
        {
            get => _cellCode;
            set
            {
                if (_cellCode == value)
                {
                    return;
                }

                _cellCode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CellCode)));
            }
        }

        public string QuantityText
        {
            get => _quantityText;
            set
            {
                if (_quantityText == value)
                {
                    return;
                }

                _quantityText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantityText)));
            }
        }

        public string Hint { get; }
    }
}

public sealed record WarehouseCellPlacementResult(WarehouseStorageCellRecord Cell, decimal Quantity);
