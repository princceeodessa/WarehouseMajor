using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Фаза B: окно поиска ячейки склада. Аналог NomenclaturePickerWindow для StorageCell.
// Используется в приёмке, перемещении, инвентаризации.
public partial class CellPickerWindow : Window
{
    private readonly IReadOnlyList<StorageCell> _cells;
    private readonly List<CellRowViewModel> _allRows;
    private DispatcherTimer? _searchDebounceTimer;

    public StorageCell? SelectedCell { get; private set; }

    public CellPickerWindow(IReadOnlyList<StorageCell> cells, string? initialSearch = null)
    {
        InitializeComponent();
        _cells = cells;
        _allRows = cells.Select(c => new CellRowViewModel
        {
            Source = c,
            Code = c.Code,
            WarehouseName = c.WarehouseName,
            ZoneLabel = string.IsNullOrWhiteSpace(c.ZoneName)
                ? (c.ZoneCode ?? string.Empty)
                : $"{c.ZoneCode} · {c.ZoneName}",
            CellType = c.CellType ?? string.Empty,
            Capacity = c.Capacity,
            QrPayload = c.QrPayload ?? string.Empty,
        }).ToList();

        SearchBox.Text = initialSearch ?? string.Empty;
        ApplyFilter(SearchBox.Text);

        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= OnDebounceTick;
        _searchDebounceTimer.Tick += OnDebounceTick;
        _searchDebounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        ApplyFilter(SearchBox.Text);
    }

    private void ApplyFilter(string? query)
    {
        var normalized = (query ?? string.Empty).Trim().ToLowerInvariant();

        IEnumerable<CellRowViewModel> filtered;
        if (string.IsNullOrEmpty(normalized))
        {
            filtered = _allRows.Take(300);
        }
        else
        {
            // Поиск по code, warehouse_name, zone, qr_payload (для скана QR).
            filtered = _allRows
                .Where(row =>
                    row.Code.ToLowerInvariant().Contains(normalized)
                    || row.WarehouseName.ToLowerInvariant().Contains(normalized)
                    || row.ZoneLabel.ToLowerInvariant().Contains(normalized)
                    || row.QrPayload.ToLowerInvariant().Contains(normalized))
                .Take(500);
        }

        var rows = filtered.ToList();
        ResultsGrid.ItemsSource = rows;

        StatusText.Text = string.IsNullOrEmpty(normalized)
            ? $"Показаны первые 300 из {_allRows.Count} ячеек. Введите код, склад или отсканируйте QR."
            : $"Найдено: {rows.Count} (из {_allRows.Count})";
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled = ResultsGrid.SelectedItem is CellRowViewModel;
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is CellRowViewModel row)
        {
            Select(row);
        }
    }

    private void OnSelectClicked(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is CellRowViewModel row)
        {
            Select(row);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Select(CellRowViewModel row)
    {
        SelectedCell = row.Source;
        DialogResult = true;
        Close();
    }

    private sealed class CellRowViewModel
    {
        public StorageCell Source { get; init; } = null!;
        public string Code { get; init; } = string.Empty;
        public string WarehouseName { get; init; } = string.Empty;
        public string ZoneLabel { get; init; } = string.Empty;
        public string CellType { get; init; } = string.Empty;
        public decimal Capacity { get; init; }
        public string QrPayload { get; init; } = string.Empty;
    }
}
