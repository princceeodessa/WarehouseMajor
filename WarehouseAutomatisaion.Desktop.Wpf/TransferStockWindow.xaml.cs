using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Фаза C (Sprint 6): окно перемещения товара между ячейками.
// Поток: source cell → выбрать позицию → target cell → qty → «Переместить».
//
// Атомарность: API IStockLocationRepository.UpsertAsync — это два отдельных UPSERT.
// Между ними возможна гонка, но в нашем сценарии (одиночный оператор склада)
// это приемлемо. Полноценные транзакции придут в Sprint 7+ если будет нужно
// (например, при многопользовательских инвентаризациях).
//
// Если qty == source.Quantity (полное перемещение), source-позиция остаётся
// с quantity=0 (не удаляем — оставляем для аудита кто-куда-сколько перемещал).
public partial class TransferStockWindow : Window
{
    private readonly IStorageCellCatalog _cellCatalog;
    private readonly IStockLocationRepository _stockLocations;

    private IReadOnlyList<StorageCell>? _cellCache;

    private StorageCell? _sourceCell;
    private StorageCell? _targetCell;
    private StockLocation? _selectedSourceLocation;

    private readonly List<HistoryEntry> _history = new();

    public TransferStockWindow(
        IStorageCellCatalog cellCatalog,
        IStockLocationRepository stockLocations)
    {
        InitializeComponent();
        _cellCatalog = cellCatalog ?? throw new ArgumentNullException(nameof(cellCatalog));
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));

        Loaded += (_, _) => StatusText.Text = "Выберите ячейку-источник, позицию из неё и ячейку-приёмник.";
    }

    private async void OnPickSourceClicked(object sender, RoutedEventArgs e)
    {
        var picked = await PickCellAsync("source");
        if (picked is null)
        {
            return;
        }

        _sourceCell = picked;
        SourceCellText.Text = $"{picked.Code}   ·   {picked.WarehouseName}";
        SourceCellText.Foreground = System.Windows.Media.Brushes.Black;
        await LoadSourceContentAsync();
        UpdateActionButtons();
    }

    private async void OnPickTargetClicked(object sender, RoutedEventArgs e)
    {
        var picked = await PickCellAsync("target");
        if (picked is null)
        {
            return;
        }

        if (_sourceCell is not null && picked.Id == _sourceCell.Id)
        {
            StatusText.Text = "❌ Ячейка-приёмник не может совпадать с источником.";
            return;
        }

        _targetCell = picked;
        TargetCellText.Text = $"{picked.Code}   ·   {picked.WarehouseName}";
        TargetCellText.Foreground = System.Windows.Media.Brushes.Black;
        await RefreshTargetHintAsync();
        UpdateActionButtons();
    }

    private async Task<StorageCell?> PickCellAsync(string role)
    {
        var button = role == "source" ? PickSourceButton : PickTargetButton;
        var originalContent = button.Content;

        try
        {
            button.IsEnabled = false;
            button.Content = "⏳";

            _cellCache ??= await _cellCatalog.GetAllAsync();

            if (_cellCache.Count == 0)
            {
                StatusText.Text = "❌ Нет созданных ячеек. Откройте раздел «Ячейки» и добавьте.";
                return null;
            }

            var initial = role == "source" ? _sourceCell?.Code : _targetCell?.Code;
            var picker = new CellPickerWindow(_cellCache, initialSearch: initial) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedCell is not null)
            {
                return picker.SelectedCell;
            }
            return null;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось открыть выбор ячейки: {ex.Message}";
            return null;
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = originalContent;
        }
    }

    private async Task LoadSourceContentAsync()
    {
        if (_sourceCell is null)
        {
            return;
        }

        try
        {
            StatusText.Text = $"⏳ Загрузка содержимого «{_sourceCell.Code}»...";
            var locations = await _stockLocations.GetByCellAsync(_sourceCell.Id);

            // Прячем позиции с quantity == 0 — они уже всё переместили, нечего двигать.
            var rows = locations.Where(loc => loc.Quantity > 0).OrderBy(loc => loc.ItemCode).ToList();
            SourceContentGrid.ItemsSource = rows;

            _selectedSourceLocation = null;
            SelectedItemText.Text = rows.Count == 0
                ? "В ячейке-источнике нет позиций для перемещения."
                : "Выберите позицию в ячейке-источнике слева.";
            SourceQtyHint.Text = string.Empty;

            StatusText.Text = $"Ячейка «{_sourceCell.Code}»: позиций к перемещению {rows.Count}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось загрузить содержимое: {ex.Message}";
        }
    }

    private async void OnSourceContentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceContentGrid.SelectedItem is StockLocation loc)
        {
            _selectedSourceLocation = loc;
            SelectedItemText.Text = $"{loc.ItemCode}  ·  {loc.ItemName}";
            SourceQtyHint.Text = $"Доступно в источнике: {loc.Quantity:N3} (резерв {loc.ReservedQuantity:N3}).";
            MoveAllButton.IsEnabled = loc.Quantity > 0;
            await RefreshTargetHintAsync();
        }
        else
        {
            _selectedSourceLocation = null;
            SelectedItemText.Text = "Выберите позицию в ячейке-источнике слева.";
            SourceQtyHint.Text = string.Empty;
            TargetQtyHint.Text = string.Empty;
            MoveAllButton.IsEnabled = false;
        }
        UpdateActionButtons();
    }

    private void OnQuantityChanged(object sender, TextChangedEventArgs e)
    {
        UpdateActionButtons();
    }

    private void OnMoveAllClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedSourceLocation is null)
        {
            return;
        }
        QuantityBox.Text = _selectedSourceLocation.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private async Task RefreshTargetHintAsync()
    {
        TargetQtyHint.Text = string.Empty;

        if (_targetCell is null || _selectedSourceLocation is null)
        {
            return;
        }

        try
        {
            var targetLocations = await _stockLocations.GetByCellAsync(_targetCell.Id);
            var sameItem = targetLocations.FirstOrDefault(loc => loc.ItemId == _selectedSourceLocation.ItemId);
            TargetQtyHint.Text = sameItem is null
                ? $"В «{_targetCell.Code}» этого товара пока нет — будет создана позиция."
                : $"В «{_targetCell.Code}» уже есть: {sameItem.Quantity:N3} (резерв {sameItem.ReservedQuantity:N3}). Прибавится.";
        }
        catch (Exception ex)
        {
            TargetQtyHint.Text = $"⚠ Не удалось прочитать целевую ячейку: {ex.Message}";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private async void OnTransferClicked(object sender, RoutedEventArgs e)
    {
        if (_sourceCell is null || _targetCell is null || _selectedSourceLocation is null)
        {
            StatusText.Text = "❌ Заполните источник, позицию и приёмник.";
            return;
        }

        if (!TryParseQuantity(QuantityBox.Text, out var qty) || qty <= 0)
        {
            StatusText.Text = "❌ Введите положительное число для количества.";
            QuantityBox.Focus();
            return;
        }

        if (qty > _selectedSourceLocation.Quantity)
        {
            StatusText.Text = $"❌ В источнике только {_selectedSourceLocation.Quantity:N3} — больше переместить нельзя.";
            return;
        }

        if (_sourceCell.Id == _targetCell.Id)
        {
            StatusText.Text = "❌ Ячейка-приёмник не может совпадать с источником.";
            return;
        }

        Guid? targetWarehouseNodeId = TryParseGuid(_targetCell.WarehouseNodeId);

        try
        {
            TransferButton.IsEnabled = false;
            StatusText.Text = $"⏳ Перемещение {qty:N3} ед. товара...";

            // 1) Уменьшаем source
            var newSourceQty = _selectedSourceLocation.Quantity - qty;
            await _stockLocations.UpsertAsync(new StockLocationUpsert(
                ItemId: _selectedSourceLocation.ItemId,
                ItemCode: _selectedSourceLocation.ItemCode,
                ItemName: _selectedSourceLocation.ItemName,
                WarehouseNodeId: _selectedSourceLocation.WarehouseNodeId,
                WarehouseName: _selectedSourceLocation.WarehouseName,
                StorageCellId: _sourceCell.Id,
                StorageCellCode: _sourceCell.Code,
                Quantity: newSourceQty,
                ReservedQuantity: _selectedSourceLocation.ReservedQuantity));

            // 2) Прибавляем к target (читаем существующую если есть)
            var targetLocations = await _stockLocations.GetByCellAsync(_targetCell.Id);
            var existingTarget = targetLocations.FirstOrDefault(loc => loc.ItemId == _selectedSourceLocation.ItemId);
            var newTargetQty = (existingTarget?.Quantity ?? 0m) + qty;
            var preservedReserved = existingTarget?.ReservedQuantity ?? 0m;

            await _stockLocations.UpsertAsync(new StockLocationUpsert(
                ItemId: _selectedSourceLocation.ItemId,
                ItemCode: _selectedSourceLocation.ItemCode,
                ItemName: _selectedSourceLocation.ItemName,
                WarehouseNodeId: targetWarehouseNodeId,
                WarehouseName: _targetCell.WarehouseName,
                StorageCellId: _targetCell.Id,
                StorageCellCode: _targetCell.Code,
                Quantity: newTargetQty,
                ReservedQuantity: preservedReserved));

            AddHistoryEntry(_selectedSourceLocation, _sourceCell, _targetCell, qty, newSourceQty, newTargetQty);

            StatusText.Text = $"✅ Перемещено {qty:N3} ед. «{_selectedSourceLocation.ItemName}»: " +
                              $"«{_sourceCell.Code}» (стало {newSourceQty:N3}) → «{_targetCell.Code}» (стало {newTargetQty:N3})";

            // Обновляем грид и подсказки.
            await LoadSourceContentAsync();
            await RefreshTargetHintAsync();
            QuantityBox.Clear();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось переместить: {ex.Message}";
        }
        finally
        {
            UpdateActionButtons();
        }
    }

    private void UpdateActionButtons()
    {
        var hasSource = _sourceCell is not null;
        var hasTarget = _targetCell is not null;
        var hasItem = _selectedSourceLocation is not null;
        var hasQty = TryParseQuantity(QuantityBox.Text, out var q) && q > 0;
        var withinAvailable = !hasItem || q <= (_selectedSourceLocation?.Quantity ?? 0m);
        var differentCells = !hasSource || !hasTarget || _sourceCell!.Id != _targetCell!.Id;

        TransferButton.IsEnabled = hasSource && hasTarget && hasItem && hasQty && withinAvailable && differentCells;
        MoveAllButton.IsEnabled = hasItem && (_selectedSourceLocation?.Quantity ?? 0m) > 0;
    }

    private static bool TryParseQuantity(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        var normalized = text.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static Guid? TryParseGuid(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        return Guid.TryParse(text, out var g) ? g : (Guid?)null;
    }

    private void AddHistoryEntry(
        StockLocation source,
        StorageCell sourceCell,
        StorageCell targetCell,
        decimal moved,
        decimal newSourceQty,
        decimal newTargetQty)
    {
        _history.Insert(0, new HistoryEntry(
            TimeStamp: DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Description: $"{source.ItemCode} · {source.ItemName}   ·   " +
                         $"{sourceCell.Code} ({newSourceQty:N3}) → {targetCell.Code} ({newTargetQty:N3})   ·   {moved:N3} ед."));

        if (_history.Count > 20)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        HistoryListBox.ItemsSource = null;
        HistoryListBox.ItemsSource = _history;
        HistoryListBox.Visibility = Visibility.Visible;
        HistoryEmptyHint.Visibility = Visibility.Collapsed;
    }

    private sealed record HistoryEntry(string TimeStamp, string Description);
}
