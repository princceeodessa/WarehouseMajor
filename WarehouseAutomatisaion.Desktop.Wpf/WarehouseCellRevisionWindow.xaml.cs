using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseCellRevisionWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Brush PositiveBrush = new SolidColorBrush(Color.FromRgb(31, 164, 95));
    private static readonly Brush NegativeBrush = new SolidColorBrush(Color.FromRgb(217, 45, 32));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(104, 118, 147));

    private readonly WarehouseStorageCellRecord _cell;
    private readonly OperationalWarehouseDocumentRecord _draft;
    private readonly IReadOnlyList<SalesCatalogItemOption> _catalogItems;
    private readonly ObservableCollection<RevisionLineViewModel> _lines;

    public WarehouseCellRevisionWindow(
        WarehouseStorageCellRecord cell,
        OperationalWarehouseDocumentRecord draft,
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        IReadOnlyList<WarehouseCellBalanceRecord> currentBalances)
    {
        _cell = cell.Clone();
        _draft = draft.Clone();
        _catalogItems = catalogItems
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _lines = new ObservableCollection<RevisionLineViewModel>(
            currentBalances
                .Where(item => item.IsAddressed && item.Quantity > 0m)
                .OrderBy(item => Ui(item.ItemName), StringComparer.CurrentCultureIgnoreCase)
                .Select(RevisionLineViewModel.FromBalance));

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = $"Ревизия ячейки {_cell.Code}";
        HeaderTitleText.Text = "Ревизия ячейки";
        HeaderSubtitleText.Text = "Укажите фактическое количество товаров в выбранной ячейке. Система создаст инвентаризацию только на разницы.";
        CellCodeText.Text = Ui(_cell.Code);
        WarehouseText.Text = Ui(_cell.Warehouse);
        DocumentNumberText.Text = Ui(_draft.Number);
        LinesDataGrid.ItemsSource = _lines;
    }

    public OperationalWarehouseDocumentRecord? ResultDocument { get; private set; }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void HandleAddLineClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WarehouseLineEditorWindow(
            "Товар в ячейку",
            $"Фактическое количество товара в ячейке {Ui(_cell.Code)}.",
            _catalogItems,
            new OperationalWarehouseLineRecord
            {
                Id = Guid.NewGuid(),
                SourceLocation = _cell.Code,
                TargetLocation = _cell.Code
            },
            allowNegativeQuantity: false,
            allowTargetLocation: false,
            storageCellOptions: new[] { _cell.Code })
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.ResultLine is null)
        {
            return;
        }

        var line = dialog.ResultLine;
        var existing = _lines.FirstOrDefault(item => MatchesItem(item.Code, item.Item, line.ItemCode, line.ItemName));
        if (existing is not null)
        {
            existing.FactQuantity = line.Quantity;
            return;
        }

        _lines.Add(new RevisionLineViewModel(
            Ui(line.ItemCode),
            Ui(line.ItemName),
            string.IsNullOrWhiteSpace(line.Unit) ? "шт" : Ui(line.Unit),
            currentQuantity: 0m,
            factQuantity: line.Quantity,
            source: "Добавлено вручную"));
    }

    private void HandleClearLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesDataGrid.SelectedItem is not RevisionLineViewModel line)
        {
            ValidationText.Text = "Выберите строку для обнуления.";
            return;
        }

        if (line.CurrentQuantity == 0m)
        {
            _lines.Remove(line);
            return;
        }

        line.FactQuantity = 0m;
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        LinesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var adjustmentLines = new List<OperationalWarehouseLineRecord>();
        foreach (var line in _lines)
        {
            if (!line.TryGetFactQuantity(out var factQuantity))
            {
                ValidationText.Text = $"Проверьте фактическое количество по товару {line.Item}.";
                return;
            }

            if (factQuantity < 0m)
            {
                ValidationText.Text = $"Фактическое количество по товару {line.Item} не может быть отрицательным.";
                return;
            }

            var delta = factQuantity - line.CurrentQuantity;
            if (delta == 0m)
            {
                continue;
            }

            adjustmentLines.Add(new OperationalWarehouseLineRecord
            {
                Id = Guid.NewGuid(),
                ItemCode = line.Code,
                ItemName = line.Item,
                Unit = line.Unit,
                Quantity = delta,
                SourceLocation = _cell.Code,
                TargetLocation = _cell.Code,
                RelatedDocument = _cell.Code
            });
        }

        if (adjustmentLines.Count == 0)
        {
            ValidationText.Text = "Нет изменений для проведения ревизии.";
            return;
        }

        ResultDocument = _draft.Clone();
        ResultDocument.Status = "Проведена";
        ResultDocument.SourceWarehouse = _cell.Warehouse;
        ResultDocument.TargetWarehouse = _cell.Code;
        ResultDocument.RelatedDocument = _cell.Code;
        ResultDocument.Comment = $"Ревизия ячейки {_cell.Code}: создана корректировка фактических остатков.";
        ResultDocument.Lines = new BindingList<OperationalWarehouseLineRecord>(adjustmentLines);
        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static bool MatchesItem(string leftCode, string leftName, string rightCode, string rightName)
    {
        return !string.IsNullOrWhiteSpace(leftCode)
               && !string.IsNullOrWhiteSpace(rightCode)
               && leftCode.Equals(rightCode, StringComparison.OrdinalIgnoreCase)
               || !string.IsNullOrWhiteSpace(leftName)
               && !string.IsNullOrWhiteSpace(rightName)
               && leftName.Equals(rightName, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RevisionLineViewModel : INotifyPropertyChanged
    {
        private decimal _factQuantity;
        private string _factText;

        public RevisionLineViewModel(
            string code,
            string item,
            string unit,
            decimal currentQuantity,
            decimal factQuantity,
            string source)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "-" : code;
            Item = string.IsNullOrWhiteSpace(item) ? "Без названия" : item;
            Unit = string.IsNullOrWhiteSpace(unit) ? "шт" : unit;
            CurrentQuantity = currentQuantity;
            _factQuantity = factQuantity;
            _factText = factQuantity.ToString("N2", RuCulture);
            Source = string.IsNullOrWhiteSpace(source) ? "-" : source;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Code { get; }

        public string Item { get; }

        public string Unit { get; }

        public decimal CurrentQuantity { get; }

        public string CurrentDisplay => $"{CurrentQuantity:N2} {Unit}";

        public decimal FactQuantity
        {
            get => _factQuantity;
            set
            {
                _factQuantity = value;
                _factText = value.ToString("N2", RuCulture);
                RaiseQuantityChanged();
            }
        }

        public string FactText
        {
            get => _factText;
            set
            {
                if (_factText == value)
                {
                    return;
                }

                _factText = value;
                if (TryGetFactQuantity(out var parsed))
                {
                    _factQuantity = parsed;
                }

                RaiseQuantityChanged();
            }
        }

        public decimal Difference => TryGetFactQuantity(out var fact) ? fact - CurrentQuantity : 0m;

        public string DifferenceDisplay
        {
            get
            {
                if (!TryGetFactQuantity(out var fact))
                {
                    return "?";
                }

                var difference = fact - CurrentQuantity;
                return difference == 0m ? "0" : $"{difference:+0.##;-0.##} {Unit}";
            }
        }

        public Brush DifferenceBrush => Difference switch
        {
            > 0m => PositiveBrush,
            < 0m => NegativeBrush,
            _ => NeutralBrush
        };

        public string Source { get; }

        public bool TryGetFactQuantity(out decimal quantity)
        {
            var raw = FactText
                .Replace('\u00A0', ' ')
                .Replace(" ", string.Empty);
            return decimal.TryParse(raw, NumberStyles.Number, RuCulture, out quantity)
                   || decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out quantity);
        }

        public static RevisionLineViewModel FromBalance(WarehouseCellBalanceRecord balance)
        {
            return new RevisionLineViewModel(
                Ui(balance.ItemCode),
                Ui(balance.ItemName),
                string.IsNullOrWhiteSpace(balance.Unit) ? "шт" : Ui(balance.Unit),
                balance.Quantity,
                balance.Quantity,
                Ui(balance.SourceLabel));
        }

        private void RaiseQuantityChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FactText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Difference)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DifferenceDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DifferenceBrush)));
        }
    }
}
