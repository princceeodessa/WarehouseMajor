using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 3 Task 20: workspace «Ячейки склада».
// CRUD-список ячеек из app_warehouse_storage_cells.
// Открытие редактора по двойному клику или кнопке «Редактировать».
public partial class StorageCellsWorkspaceView : UserControl
{
    private DesktopMySqlBackplaneService? _backplane;
    private MySqlStorageCellCatalog? _catalog;
    private bool _isInitialized;
    private string? _selectedWarehouseFilter;

    public StorageCellsWorkspaceView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (_backplane is null)
        {
            StatusText.Text = "❌ Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.";
            DisableActions();
            return;
        }

        _catalog = new MySqlStorageCellCatalog(_backplane);
        LoadWarehouses();
        _isInitialized = true;
        ReloadCells();
    }

    private void LoadWarehouses()
    {
        if (_backplane is null)
        {
            return;
        }

        var summaries = _backplane.LoadStockWarehouses();
        var items = new List<WarehouseFilterOption>
        {
            new(null, "Все склады"),
        };
        foreach (var s in summaries)
        {
            var name = string.IsNullOrWhiteSpace(s.WarehouseName) ? "(без имени)" : s.WarehouseName!;
            items.Add(new WarehouseFilterOption(name, name));
        }

        WarehouseFilterCombo.ItemsSource = items;
        WarehouseFilterCombo.DisplayMemberPath = nameof(WarehouseFilterOption.DisplayName);
        WarehouseFilterCombo.SelectedValuePath = nameof(WarehouseFilterOption.Value);
        WarehouseFilterCombo.SelectedIndex = 0;
    }

    private async void ReloadCells()
    {
        if (_catalog is null)
        {
            return;
        }

        try
        {
            var cells = await _catalog.GetAllAsync(_selectedWarehouseFilter);
            var rows = cells.Select(c => new CellRowViewModel
            {
                Source = c,
                Code = c.Code,
                WarehouseName = c.WarehouseName,
                ZoneLabel = string.IsNullOrWhiteSpace(c.ZoneName)
                    ? (c.ZoneCode ?? string.Empty)
                    : $"{c.ZoneCode} · {c.ZoneName}",
                AddressLabel = $"R{c.RowNo:D2}-К{c.RackNo:D2}-П{c.ShelfNo:D2}-Я{c.CellNo:D2}",
                CellType = c.CellType ?? string.Empty,
                Capacity = c.Capacity,
                StatusText = c.StatusText ?? string.Empty,
                CommentText = c.CommentText ?? string.Empty,
            }).ToList();

            CellsDataGrid.ItemsSource = rows;

            if (rows.Count == 0)
            {
                StatusText.Text = "Ячеек ещё нет. Добавьте через «+ Новая ячейка» или импорт CSV (Task 21).";
            }
            else
            {
                var distinctWarehouses = rows.Select(r => r.WarehouseName).Distinct().Count();
                var distinctZones = rows.Where(r => !string.IsNullOrEmpty(r.ZoneLabel))
                    .Select(r => r.ZoneLabel).Distinct().Count();
                StatusText.Text = $"Ячеек: {rows.Count}   ·   складов: {distinctWarehouses}   ·   зон: {distinctZones}";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Ошибка загрузки: {exception.Message}";
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        _selectedWarehouseFilter = (WarehouseFilterCombo.SelectedItem as WarehouseFilterOption)?.Value;
        ReloadCells();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            ReloadCells();
        }
    }

    private void OnCellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = CellsDataGrid.SelectedItem is not null;
        EditCellButton.IsEnabled = hasSelection;
        DeleteCellButton.IsEnabled = hasSelection;
    }

    private void OnCellDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CellsDataGrid.SelectedItem is CellRowViewModel selected)
        {
            OpenEditor(selected.Source);
        }
    }

    private void OnNewCellClicked(object sender, RoutedEventArgs e)
    {
        OpenEditor(source: null);
    }

    private void OnEditCellClicked(object sender, RoutedEventArgs e)
    {
        if (CellsDataGrid.SelectedItem is CellRowViewModel selected)
        {
            OpenEditor(selected.Source);
        }
    }

    private async void OnDeleteCellClicked(object sender, RoutedEventArgs e)
    {
        if (_catalog is null || CellsDataGrid.SelectedItem is not CellRowViewModel selected)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
            $"Удалить ячейку «{selected.Code}» на складе «{selected.WarehouseName}»?\n\nДействие нельзя отменить.",
            "Удаление ячейки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _catalog.DeleteAsync(selected.Source.Id);
            ReloadCells();
            StatusText.Text = $"✅ Ячейка «{selected.Code}» удалена.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Не удалось удалить: {exception.Message}";
        }
    }

    private void OpenEditor(StorageCell? source)
    {
        if (_catalog is null)
        {
            return;
        }

        var editor = new StorageCellEditorWindow(_catalog, source)
        {
            Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
        };

        if (editor.ShowDialog() == true)
        {
            ReloadCells();
        }
    }

    private void DisableActions()
    {
        NewCellButton.IsEnabled = false;
        EditCellButton.IsEnabled = false;
        DeleteCellButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
    }

    private sealed class CellRowViewModel
    {
        public StorageCell Source { get; init; } = null!;
        public string Code { get; init; } = string.Empty;
        public string WarehouseName { get; init; } = string.Empty;
        public string ZoneLabel { get; init; } = string.Empty;
        public string AddressLabel { get; init; } = string.Empty;
        public string CellType { get; init; } = string.Empty;
        public decimal Capacity { get; init; }
        public string StatusText { get; init; } = string.Empty;
        public string CommentText { get; init; } = string.Empty;
    }

    private sealed record WarehouseFilterOption(string? Value, string DisplayName);
}
