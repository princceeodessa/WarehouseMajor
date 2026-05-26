using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Фаза B (Sprint 4): окно приёмки товара в ячейку склада.
// Поток: выбрать товар (NomenclaturePicker) → выбрать ячейку (CellPicker) → ввести qty → «Принять».
//
// При приёмке ARITHMETIC ДОБАВЛЕНИЯ: читаем текущую позицию по (item_id, cell_id) если есть,
// прибавляем введённое qty и делаем UpsertAsync. Это согласуется с контрактом
// StockLocationUpsert.Quantity — он REPLACES, поэтому caller должен сам сложить.
//
// Кнопка «Принять ещё» — для серийной приёмки одной ячейки (склад грузит партию):
// после успешной операции ячейка остаётся выбранной, товар и qty очищаются.
public partial class ReceiveStockWindow : Window
{
    private readonly INomenclatureCatalogReader _catalogReader;
    private readonly IStorageCellCatalog _cellCatalog;
    private readonly IStockLocationRepository _stockLocations;
    private readonly CellRecommendationService _recommender;

    private IReadOnlyList<NomenclatureRef>? _itemCatalog;
    private IReadOnlyList<StorageCell>? _cellCache;

    private NomenclatureRef? _selectedItem;
    private StorageCell? _selectedCell;

    private readonly List<HistoryEntry> _history = new();

    public ReceiveStockWindow(
        INomenclatureCatalogReader catalogReader,
        IStorageCellCatalog cellCatalog,
        IStockLocationRepository stockLocations)
    {
        InitializeComponent();
        _catalogReader = catalogReader ?? throw new ArgumentNullException(nameof(catalogReader));
        _cellCatalog = cellCatalog ?? throw new ArgumentNullException(nameof(cellCatalog));
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));
        _recommender = new CellRecommendationService(stockLocations, cellCatalog);

        Loaded += (_, _) => StatusText.Text = "Выберите товар и ячейку, затем введите количество.";
    }

    private async void OnPickItemClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            PickItemButton.IsEnabled = false;
            PickItemButton.Content = "⏳ Загрузка каталога...";

            // Кешируем каталог в рамках окна — несколько приёмок подряд используют один и тот же список.
            _itemCatalog ??= await _catalogReader.GetAllAsync();

            if (_itemCatalog.Count == 0)
            {
                StatusText.Text = "❌ Каталог номенклатуры пуст. Проверьте подключение к MySQL.";
                return;
            }

            var picker = new NomenclaturePickerWindow(
                _itemCatalog,
                recognizedText: _selectedItem?.Name ?? string.Empty,
                initialSelection: _selectedItem)
            {
                Owner = this
            };

            if (picker.ShowDialog() == true && picker.SelectedItem is not null)
            {
                _selectedItem = picker.SelectedItem;
                SelectedItemText.Text = $"{_selectedItem.Code}   ·   {_selectedItem.Name}";
                SelectedItemText.Foreground = System.Windows.Media.Brushes.Black;
                await RefreshCurrentStockHintAsync();
                await RefreshRecommendationsAsync();
                UpdateButtons();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось открыть каталог: {ex.Message}";
        }
        finally
        {
            PickItemButton.IsEnabled = true;
            PickItemButton.Content = "🔍 Выбрать товар";
        }
    }

    private async Task RefreshRecommendationsAsync()
    {
        if (_selectedItem is null || !Guid.TryParse(_selectedItem.Id, out var itemId))
        {
            RecommendationsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var recs = await _recommender.GetTopRecommendationsAsync(itemId, top: 3);
            if (recs.Count == 0)
            {
                RecommendationsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var chips = recs.Select(r => new RecommendationChip(r)).ToList();
            RecommendationsList.ItemsSource = chips;
            RecommendationsPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            // тихо скрываем — рекомендации это nice-to-have
            RecommendationsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnRecommendationClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not RecommendationChip chip)
        {
            return;
        }

        _selectedCell = chip.Source.Cell;
        SelectedCellText.Text = $"{_selectedCell.Code}   ·   {_selectedCell.WarehouseName}";
        SelectedCellText.Foreground = System.Windows.Media.Brushes.Black;
        StatusText.Text = $"✨ Подставлена ячейка из рекомендации: {chip.Source.Reason}";
        await RefreshCurrentStockHintAsync();
        UpdateButtons();
    }

    private sealed record RecommendationChip(CellRecommendation Source)
    {
        public string ChipIcon => Source.Kind switch
        {
            RecommendationKind.History => "📍",
            RecommendationKind.SameZone => "🏷",
            RecommendationKind.FreeStorage => "📦",
            _ => "•"
        };

        public string ChipLabel => $"{Source.Cell.Code} · {Source.Cell.WarehouseName}";
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

            var picker = new CellPickerWindow(
                _cellCache,
                initialSearch: _selectedCell?.Code)
            {
                Owner = this
            };

            if (picker.ShowDialog() == true && picker.SelectedCell is not null)
            {
                _selectedCell = picker.SelectedCell;
                SelectedCellText.Text = $"{_selectedCell.Code}   ·   {_selectedCell.WarehouseName}";
                SelectedCellText.Foreground = System.Windows.Media.Brushes.Black;
                await RefreshCurrentStockHintAsync();
                UpdateButtons();
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

    private void OnFormChanged(object sender, TextChangedEventArgs e)
    {
        UpdateButtons();
    }

    private async void OnReceiveClicked(object sender, RoutedEventArgs e)
    {
        await ReceiveAsync(continueAfter: false);
    }

    private async void OnReceiveAndContinueClicked(object sender, RoutedEventArgs e)
    {
        await ReceiveAsync(continueAfter: true);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private async Task ReceiveAsync(bool continueAfter)
    {
        if (_selectedItem is null || _selectedCell is null)
        {
            StatusText.Text = "❌ Сначала выберите товар и ячейку.";
            return;
        }

        if (!TryParseQuantity(QuantityBox.Text, out var qty) || qty <= 0)
        {
            StatusText.Text = "❌ Введите положительное число для количества.";
            QuantityBox.Focus();
            return;
        }

        if (!Guid.TryParse(_selectedItem.Id, out var itemId))
        {
            StatusText.Text = "❌ ID товара не распознан как Guid. Каталог повреждён?";
            return;
        }

        Guid? warehouseNodeId = null;
        if (!string.IsNullOrWhiteSpace(_selectedCell.WarehouseNodeId)
            && Guid.TryParse(_selectedCell.WarehouseNodeId, out var parsedNodeId))
        {
            warehouseNodeId = parsedNodeId;
        }

        try
        {
            ReceiveButton.IsEnabled = false;
            ReceiveAndContinueButton.IsEnabled = false;
            StatusText.Text = $"⏳ Запись приёмки {qty:N3} ед. товара «{_selectedItem.Name}»...";

            // Читаем существующую позицию в этой ячейке для этого товара — quantity-арифметика на стороне caller.
            var existingInCell = await _stockLocations.GetByCellAsync(_selectedCell.Id);
            var existingForItem = existingInCell.FirstOrDefault(loc => loc.ItemId == itemId);

            var existingQty = existingForItem?.Quantity ?? 0m;
            var existingReserved = existingForItem?.ReservedQuantity ?? 0m;
            var newQty = existingQty + qty;

            await _stockLocations.UpsertAsync(new StockLocationUpsert(
                ItemId: itemId,
                ItemCode: _selectedItem.Code ?? string.Empty,
                ItemName: _selectedItem.Name ?? string.Empty,
                WarehouseNodeId: warehouseNodeId,
                WarehouseName: _selectedCell.WarehouseName,
                StorageCellId: _selectedCell.Id,
                StorageCellCode: _selectedCell.Code,
                Quantity: newQty,
                ReservedQuantity: existingReserved));

            // Учитываем в локальной истории сессии.
            AddHistoryEntry(_selectedItem, _selectedCell, qty, newQty);

            StatusText.Text = $"✅ Принято {qty:N3} ед. «{_selectedItem.Name}» → «{_selectedCell.Code}». Всего в ячейке: {newQty:N3}";

            if (continueAfter)
            {
                // Серийная приёмка: ячейка остаётся, товар и qty очищаются.
                _selectedItem = null;
                SelectedItemText.Text = "— не выбран —";
                SelectedItemText.Foreground = System.Windows.Media.Brushes.Gray;
                QuantityBox.Clear();
                CurrentStockHint.Text = string.Empty;
                PickItemButton.Focus();
            }
            else
            {
                // Single-shot: остаёмся в окне но всё очищаем.
                ResetForm();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось записать приёмку: {ex.Message}";
        }
        finally
        {
            UpdateButtons();
        }
    }

    private async Task RefreshCurrentStockHintAsync()
    {
        if (_selectedItem is null || _selectedCell is null)
        {
            CurrentStockHint.Text = string.Empty;
            return;
        }

        if (!Guid.TryParse(_selectedItem.Id, out var itemId))
        {
            CurrentStockHint.Text = string.Empty;
            return;
        }

        try
        {
            var existingInCell = await _stockLocations.GetByCellAsync(_selectedCell.Id);
            var existingForItem = existingInCell.FirstOrDefault(loc => loc.ItemId == itemId);

            CurrentStockHint.Text = existingForItem is null
                ? $"В ячейке этого товара ещё нет. После приёмки будет создана новая позиция."
                : $"Сейчас в ячейке: {existingForItem.Quantity:N3} ед. (резерв {existingForItem.ReservedQuantity:N3}). Приёмка прибавляется.";
        }
        catch (Exception ex)
        {
            CurrentStockHint.Text = $"⚠ Не удалось прочитать текущий остаток: {ex.Message}";
        }
    }

    private void ResetForm()
    {
        _selectedItem = null;
        _selectedCell = null;
        SelectedItemText.Text = "— не выбран —";
        SelectedItemText.Foreground = System.Windows.Media.Brushes.Gray;
        SelectedCellText.Text = "— не выбрана —";
        SelectedCellText.Foreground = System.Windows.Media.Brushes.Gray;
        QuantityBox.Clear();
        CurrentStockHint.Text = string.Empty;
    }

    private void UpdateButtons()
    {
        var hasItem = _selectedItem is not null;
        var hasCell = _selectedCell is not null;
        var hasQty = TryParseQuantity(QuantityBox.Text, out var q) && q > 0;
        var ready = hasItem && hasCell && hasQty;
        ReceiveButton.IsEnabled = ready;
        ReceiveAndContinueButton.IsEnabled = ready;
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

    private void AddHistoryEntry(NomenclatureRef item, StorageCell cell, decimal qty, decimal totalAfter)
    {
        _history.Insert(0, new HistoryEntry(
            TimeStamp: DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Description: $"{item.Code}  ·  {item.Name}  →  {cell.Code} ({cell.WarehouseName})",
            QuantityLabel: $"+{qty:N3} (стало {totalAfter:N3})"));

        // Ограничим хранением 20 строк — окно небольшое.
        if (_history.Count > 20)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        HistoryListBox.ItemsSource = null;
        HistoryListBox.ItemsSource = _history;
        HistoryListBox.Visibility = Visibility.Visible;
        HistoryEmptyHint.Visibility = Visibility.Collapsed;
    }

    private sealed record HistoryEntry(string TimeStamp, string Description, string QuantityLabel);
}
