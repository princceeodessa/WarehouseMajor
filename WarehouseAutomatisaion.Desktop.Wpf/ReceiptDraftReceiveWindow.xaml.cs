using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Receiving;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 8 (AI loop closure): окно приёмки целого AI-черновика накладной в одну ячейку.
// Стандартный WMS-паттерн: «приёмочная зона» — все строки накладной попадают в одну
// receiving-ячейку, оператор потом раскладывает товар через TransferStockWindow.
//
// Только строки с MatchedItemId записываются в stock_locations.
// Несматченные строки остаются в черновике как unmatched — оператор должен будет
// либо домэтчить через InvoiceRecognitionWindow learning loop, либо добавить вручную.
public partial class ReceiptDraftReceiveWindow : Window
{
    private readonly ReceiptDraftDetail _detail;
    private readonly IStorageCellCatalog _cellCatalog;
    private readonly IStockLocationRepository _stockLocations;
    private readonly IReceiptDraftReader _draftReader;

    private IReadOnlyList<StorageCell>? _cellCache;
    private StorageCell? _selectedCell;

    public ReceiptDraftReceiveWindow(
        ReceiptDraftDetail detail,
        IStorageCellCatalog cellCatalog,
        IStockLocationRepository stockLocations,
        IReceiptDraftReader draftReader)
    {
        InitializeComponent();
        _detail = detail ?? throw new ArgumentNullException(nameof(detail));
        _cellCatalog = cellCatalog ?? throw new ArgumentNullException(nameof(cellCatalog));
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));
        _draftReader = draftReader ?? throw new ArgumentNullException(nameof(draftReader));

        InitializeView();
    }

    private void InitializeView()
    {
        var header = _detail.Header;
        var rows = _detail.Lines.Select(BuildRow).ToList();
        LinesGrid.ItemsSource = rows;

        var matched = rows.Count(r => r.IsMatched);
        var unmatched = rows.Count - matched;

        HeaderSummaryText.Text = $"Поставщик: {header.SupplierName}   ·   Накладная № {header.InvoiceNumber}   ·   " +
                                 $"Дата: {header.DocumentDate:dd.MM.yyyy}   ·   Сумма: {header.TotalAmount:N2} ₽";

        MatchedCountText.Text = $"✓ Сматчено: {matched}";
        UnmatchedCountText.Text = unmatched > 0
            ? $"⚠ Не сматчено: {unmatched} (будут пропущены)"
            : "✓ Все строки сматчены";

        StatusText.Text = "Выберите ячейку для приёмки и нажмите «✓ Принять накладную».";
    }

    private static LineRow BuildRow(ReceiptDraftLineDetail line) => new(line);

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
                SelectedCellText.Text = $"{_selectedCell.Code}   ·   {_selectedCell.WarehouseName}   ·   {_selectedCell.CellType ?? string.Empty}";
                SelectedCellText.Foreground = System.Windows.Media.Brushes.Black;
                ReceiveAllButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось выбрать ячейку: {ex.Message}";
        }
        finally
        {
            PickCellButton.IsEnabled = true;
            PickCellButton.Content = "🔍 Выбрать ячейку";
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void OnReceiveAllClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedCell is null)
        {
            StatusText.Text = "❌ Сначала выберите ячейку приёмки.";
            return;
        }

        var matched = _detail.Lines.Where(l => l.MatchedItemId is not null).ToList();
        var unmatched = _detail.Lines.Count - matched.Count;

        if (matched.Count == 0)
        {
            StatusText.Text = "❌ В черновике нет сматченных строк — нечего записывать.";
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Будет принято {matched.Count} строк в ячейку «{_selectedCell.Code}» на складе «{_selectedCell.WarehouseName}».\n\n" +
            (unmatched > 0
                ? $"Пропущено: {unmatched} строк (не сматчены с каталогом).\n\n"
                : "") +
            "Количество товаров прибавляется к существующим остаткам в ячейке.\n\nПодтвердить?",
            "Подтверждение приёмки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        Guid? warehouseNodeId = TryParseGuid(_selectedCell.WarehouseNodeId);
        var ok = 0;
        var failed = 0;
        var errors = new List<string>();

        try
        {
            ReceiveAllButton.IsEnabled = false;
            PickCellButton.IsEnabled = false;
            StatusText.Text = $"⏳ Применяем {matched.Count} строк...";

            // Прочитаем текущие позиции этой ячейки один раз, дальше работаем с in-memory словарём.
            var currentInCell = await _stockLocations.GetByCellAsync(_selectedCell.Id);
            var byItem = currentInCell.ToDictionary(loc => loc.ItemId, loc => loc);

            foreach (var line in matched)
            {
                try
                {
                    var itemId = line.MatchedItemId!.Value;
                    byItem.TryGetValue(itemId, out var existing);
                    var newQty = (existing?.Quantity ?? 0m) + line.Quantity;
                    var reserved = existing?.ReservedQuantity ?? 0m;

                    await _stockLocations.UpsertAsync(new StockLocationUpsert(
                        ItemId: itemId,
                        ItemCode: line.OriginalSku ?? string.Empty,
                        ItemName: line.OriginalItemName,
                        WarehouseNodeId: warehouseNodeId,
                        WarehouseName: _selectedCell.WarehouseName,
                        StorageCellId: _selectedCell.Id,
                        StorageCellCode: _selectedCell.Code,
                        Quantity: newQty,
                        ReservedQuantity: reserved));

                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"строка {line.LineNumber}: {ex.Message}");
                }
            }

            if (ok > 0)
            {
                // Помечаем черновик как принятый только если хоть что-то записали.
                await _draftReader.MarkReceivedAsync(_detail.Header.Id, _selectedCell.Id, _selectedCell.Code, ok);
            }

            var summary = failed == 0
                ? $"✅ Принято {ok} из {matched.Count} строк в «{_selectedCell.Code}»."
                : $"⚠ Принято {ok} из {matched.Count}. Ошибок: {failed}.\nПервые: {string.Join("; ", errors.Take(3))}";

            MessageBox.Show(this, summary, "Результат приёмки",
                MessageBoxButton.OK,
                failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (ok > 0)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                StatusText.Text = summary;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось принять накладную: {ex.Message}";
        }
        finally
        {
            ReceiveAllButton.IsEnabled = true;
            PickCellButton.IsEnabled = true;
        }
    }

    private static Guid? TryParseGuid(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        return Guid.TryParse(text, out var g) ? g : (Guid?)null;
    }

    // VM строки накладной для биндинга в DataGrid.
    public sealed class LineRow
    {
        public LineRow(ReceiptDraftLineDetail source)
        {
            LineNumber = source.LineNumber;
            OriginalItemName = source.OriginalItemName;
            OriginalSku = source.OriginalSku;
            Unit = source.Unit ?? string.Empty;
            Quantity = source.Quantity;
            UnitPrice = source.UnitPrice;
            Total = source.Total;
            IsMatched = source.MatchedItemId is not null;
            MatchStatus = IsMatched
                ? $"✓ Сматчено: {OriginalSku ?? "—"}"
                : "⚠ Нет в каталоге";
        }

        public int LineNumber { get; }
        public string OriginalItemName { get; }
        public string? OriginalSku { get; }
        public string Unit { get; }
        public decimal Quantity { get; }
        public decimal? UnitPrice { get; }
        public decimal? Total { get; }
        public bool IsMatched { get; }
        public string MatchStatus { get; }
    }
}
