using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Фаза D (Sprint 7): окно инвентаризации содержимого ячейки.
// Поток: выбрать ячейку → загрузить её содержимое (StockLocation rows) →
// оператор вписывает фактическое qty в редактируемую колонку «Факт» →
// «Применить факт» делает UpsertAsync с фактическим значением для каждой строки
// где факт отличается от системы.
//
// Дельта подсвечивается красным (недостача) / зелёным (излишек).
// «Факт = система» — быстрая кнопка «всё сошлось».
//
// Out-of-scope сейчас: добавление позиций которых в системе нет
// (для них есть приёмка). Out-of-scope: история факт-актов / документы.
public partial class StockTakeWindow : Window
{
    private readonly IStorageCellCatalog _cellCatalog;
    private readonly IStockLocationRepository _stockLocations;
    private IReadOnlyList<StorageCell>? _cellCache;
    private StorageCell? _selectedCell;
    private readonly ObservableCollection<StockTakeRow> _rows = new();

    public StockTakeWindow(
        IStorageCellCatalog cellCatalog,
        IStockLocationRepository stockLocations)
    {
        InitializeComponent();
        _cellCatalog = cellCatalog ?? throw new ArgumentNullException(nameof(cellCatalog));
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));

        InventoryGrid.ItemsSource = _rows;
        Loaded += (_, _) => StatusText.Text = "Выберите ячейку, чтобы загрузить её содержимое для инвентаризации.";
    }

    private async void OnPickCellClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            PickCellButton.IsEnabled = false;
            PickCellButton.Content = "⏳ Загрузка ячеек...";

            _cellCache ??= await _cellCatalog.GetAllAsync();

            if (_cellCache.Count == 0)
            {
                StatusText.Text = "❌ Нет созданных ячеек. Откройте раздел «Ячейки» и добавьте.";
                return;
            }

            var picker = new CellPickerWindow(_cellCache, initialSearch: _selectedCell?.Code) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedCell is not null)
            {
                _selectedCell = picker.SelectedCell;
                SelectedCellText.Text = $"{_selectedCell.Code}   ·   {_selectedCell.WarehouseName}";
                SelectedCellText.Foreground = System.Windows.Media.Brushes.Black;
                await LoadInventoryAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось открыть выбор ячейки: {ex.Message}";
        }
        finally
        {
            PickCellButton.IsEnabled = true;
            PickCellButton.Content = "🔍 Выбрать ячейку";
        }
    }

    private async Task LoadInventoryAsync()
    {
        if (_selectedCell is null)
        {
            return;
        }

        try
        {
            StatusText.Text = $"⏳ Загрузка инвентаря «{_selectedCell.Code}»...";
            var locations = await _stockLocations.GetByCellAsync(_selectedCell.Id);

            // Отписываемся от событий старых строк (если перезагружаем).
            foreach (var oldRow in _rows)
            {
                oldRow.PropertyChanged -= OnRowChanged;
            }
            _rows.Clear();

            foreach (var loc in locations.OrderBy(l => l.ItemCode))
            {
                var row = new StockTakeRow(loc);
                row.PropertyChanged += OnRowChanged;
                _rows.Add(row);
            }

            UpdateActionButtons();

            StatusText.Text = locations.Count == 0
                ? $"Ячейка «{_selectedCell.Code}» пуста — нечего инвентаризировать."
                : $"Загружено: {locations.Count} позиций. Впишите факт в колонку «Факт».";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось загрузить инвентарь: {ex.Message}";
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StockTakeRow.ActualQuantityText))
        {
            UpdateActionButtons();
        }
    }

    private void OnMatchAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.SetActualEqualSystem();
        }
        StatusText.Text = "✓ Фактические значения подставлены равными системным. Нажмите «Применить факт» чтобы подтвердить.";
        UpdateActionButtons();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedCell is null)
        {
            return;
        }

        var changes = _rows.Where(r => r.HasValidActual && r.HasDifference).ToList();
        if (changes.Count == 0)
        {
            StatusText.Text = "Изменений нет — ничего не применяем.";
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Будет применено корректировок: {changes.Count}\n\n" +
            string.Join("\n", changes.Take(8).Select(r =>
                $"• {r.ItemCode}  {r.ItemName}:  {r.SystemQuantity:N3} → {r.ActualQuantityValue:N3}  ({r.DiffLabel})"))
            + (changes.Count > 8 ? $"\n…и ещё {changes.Count - 8}" : "")
            + "\n\nПрименить?",
            "Подтверждение инвентаризации",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ApplyButton.IsEnabled = false;
            MatchAllButton.IsEnabled = false;
            StatusText.Text = $"⏳ Применяем {changes.Count} корректировок...";

            var ok = 0;
            var failed = 0;

            foreach (var row in changes)
            {
                try
                {
                    await _stockLocations.UpsertAsync(new StockLocationUpsert(
                        ItemId: row.Source.ItemId,
                        ItemCode: row.Source.ItemCode,
                        ItemName: row.Source.ItemName,
                        WarehouseNodeId: row.Source.WarehouseNodeId,
                        WarehouseName: row.Source.WarehouseName,
                        StorageCellId: row.Source.StorageCellId,
                        StorageCellCode: row.Source.StorageCellCode,
                        Quantity: row.ActualQuantityValue,
                        ReservedQuantity: row.Source.ReservedQuantity));
                    ok++;
                }
                catch
                {
                    failed++;
                }
            }

            StatusText.Text = failed == 0
                ? $"✅ Применено {ok} корректировок. Ячейка обновлена."
                : $"⚠ Применено {ok} из {changes.Count}. Ошибок: {failed}.";

            // Перезагружаем — синхронизация с БД.
            await LoadInventoryAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось применить: {ex.Message}";
        }
        finally
        {
            UpdateActionButtons();
        }
    }

    private void UpdateActionButtons()
    {
        var hasRows = _rows.Count > 0;
        var hasValidChanges = _rows.Any(r => r.HasValidActual && r.HasDifference);
        ApplyButton.IsEnabled = hasValidChanges;
        MatchAllButton.IsEnabled = hasRows;
    }

    // Row VM — INotifyPropertyChanged нужен чтобы DataGrid обновлял колонки «Δ» при правке «Факт».
    public sealed class StockTakeRow : INotifyPropertyChanged
    {
        public StockLocation Source { get; }
        public string ItemCode => Source.ItemCode;
        public string ItemName => Source.ItemName;
        public decimal SystemQuantity => Source.Quantity;

        private string _actualQuantityText = string.Empty;
        public string ActualQuantityText
        {
            get => _actualQuantityText;
            set
            {
                if (_actualQuantityText == value)
                {
                    return;
                }
                _actualQuantityText = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DiffLabel));
                OnPropertyChanged(nameof(DiffSign));
            }
        }

        public StockTakeRow(StockLocation source)
        {
            Source = source;
        }

        public bool HasValidActual =>
            !string.IsNullOrWhiteSpace(_actualQuantityText)
            && decimal.TryParse(
                _actualQuantityText.Trim().Replace(',', '.'),
                NumberStyles.Number, CultureInfo.InvariantCulture, out _);

        public decimal ActualQuantityValue =>
            decimal.Parse(_actualQuantityText.Trim().Replace(',', '.'),
                NumberStyles.Number, CultureInfo.InvariantCulture);

        public bool HasDifference =>
            HasValidActual && ActualQuantityValue != SystemQuantity;

        public string DiffLabel
        {
            get
            {
                if (!HasValidActual)
                {
                    return string.Empty;
                }
                var diff = ActualQuantityValue - SystemQuantity;
                if (diff == 0)
                {
                    return "= 0";
                }
                var sign = diff > 0 ? "+" : "−";
                return $"{sign}{Math.Abs(diff):N3}";
            }
        }

        public string DiffSign
        {
            get
            {
                if (!HasValidActual)
                {
                    return string.Empty;
                }
                var diff = ActualQuantityValue - SystemQuantity;
                if (diff > 0)
                {
                    return "positive";
                }
                if (diff < 0)
                {
                    return "negative";
                }
                return "match";
            }
        }

        public void SetActualEqualSystem()
        {
            ActualQuantityText = SystemQuantity.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
