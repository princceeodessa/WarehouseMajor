using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 1 (WMS остатки) Task 6: workspace для просмотра остатков по складам.
// Источник данных: app_warehouse_stock_balances через DesktopMySqlBackplaneService.
// Паттерн: code-behind без MVVM-фреймворка, как ProductsWorkspaceView / WarehouseWorkspaceView.
public partial class StockBalancesWorkspaceView : UserControl
{
    private DesktopMySqlBackplaneService? _backplane;
    private MySqlStockLocationRepository? _stockLocations;
    private IStockLocationBootstrapper? _stockLocationBootstrapper;
    private IWmsReadinessReader? _readinessReader;
    private DispatcherTimer? _searchDebounceTimer;
    private bool _isInitialized;

    public StockBalancesWorkspaceView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (_backplane is null)
        {
            StatusText.Text = "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.";
            return;
        }

        _stockLocations = new MySqlStockLocationRepository(_backplane);
        _stockLocationBootstrapper = DesktopScanLookupFactory.TryCreate()?.StockLocationBootstrapper;
        _readinessReader = WmsReadinessFactory.TryCreate();
        LoadWarehouses();
        _isInitialized = true;
        ReloadStock();
        await RefreshReadinessAsync();
    }

    private async void OnStockRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_stockLocations is null || StockDataGrid.SelectedItem is not WarehouseStockRow selected)
        {
            return;
        }

        if (!Guid.TryParse(selected.ItemId, out var itemGuid))
        {
            StatusText.Text = "❌ Не удалось распарсить ItemId для поиска позиций.";
            return;
        }

        try
        {
            StatusText.Text = $"⏳ Поиск ячеек для «{selected.ItemName}»...";
            var locations = await _stockLocations.GetByItemAsync(itemGuid);

            var popup = new StockLocationsPopupWindow(
                header: $"Где лежит: {selected.ItemName}",
                subheader: $"Код {selected.ItemCode}   ·   общее количество (по складу): {selected.Quantity:N3}",
                locations: locations)
            {
                Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
            };
            popup.ShowDialog();

            StatusText.Text = $"Товар «{selected.ItemName}»: размещено в {locations.Count} ячейках";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Ошибка: {exception.Message}";
        }
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
            new(WarehouseNodeId: null, DisplayName: $"Все склады ({summaries.Count})"),
        };
        foreach (var summary in summaries)
        {
            var name = string.IsNullOrWhiteSpace(summary.WarehouseName) ? "(без имени)" : summary.WarehouseName!;
            items.Add(new WarehouseFilterOption(
                summary.WarehouseNodeId,
                $"{name}   ·  {summary.ItemsCount} поз. · {summary.TotalQuantity:N0}"));
        }

        WarehouseFilterCombo.ItemsSource = items;
        WarehouseFilterCombo.SelectedIndex = 0;
    }

    private void ReloadStock()
    {
        if (_backplane is null)
        {
            return;
        }

        var warehouseFilter = (WarehouseFilterCombo.SelectedItem as WarehouseFilterOption)?.WarehouseNodeId;
        var search = SearchTextBox.Text;
        var onlyPositive = OnlyPositiveCheck.IsChecked == true;

        var rows = _backplane.LoadStockBalances(warehouseFilter, search, onlyPositive, limit: 10000);
        StockDataGrid.ItemsSource = rows;

        var projectedAt = rows.Count > 0 ? rows.Max(r => r.ProjectedAtUtc) : (DateTime?)null;
        var totalQuantity = rows.Sum(r => r.Quantity);
        var totalAvailable = rows.Sum(r => r.AvailableQuantity);

        StatusText.Text = string.Format(
            "Строк: {0:N0}   ·   сумма кол-ва: {1:N0}   ·   доступно: {2:N0}   ·   проекция: {3}",
            rows.Count,
            totalQuantity,
            totalAvailable,
            projectedAt.HasValue ? projectedAt.Value.ToString("dd.MM.yyyy HH:mm") : "—");
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        ReloadStock();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        _searchDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= OnSearchDebounceTick;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        ReloadStock();
    }

    private void OnRecognizeInvoiceClicked(object sender, RoutedEventArgs e)
    {
        var window = new InvoiceRecognitionWindow
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();

        // После закрытия окна — обновим остатки (вдруг создан черновик).
        // В будущем, когда черновик будет проводиться в реальное движение —
        // это автоматически отразится после Refresh.
        if (_isInitialized)
        {
            ReloadStock();
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (_backplane is null)
        {
            return;
        }

        var originalContent = RefreshButton.Content;
        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "Обновление...";

        try
        {
            var affected = await Task.Run(() => _backplane.RefreshStockBalancesProjection());
            LoadWarehouses();
            ReloadStock();
            await RefreshReadinessAsync();
            StatusText.Text = $"Проекция обновлена (затронуто {affected:N0} строк)   ·   " + StatusText.Text;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка обновления: {ex.Message}";
        }
        finally
        {
            RefreshButton.Content = originalContent;
            RefreshButton.IsEnabled = true;
        }
    }

    private async void OnBootstrapLocationsClicked(object sender, RoutedEventArgs e)
    {
        if (_stockLocationBootstrapper is null)
        {
            StatusText.Text = "Нет подключения к MySQL для инициализации адресных остатков.";
            return;
        }

        var owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow;
        var confirmation = MessageBox.Show(
            owner,
            "Создать системную ячейку UNPLACED по каждому складу и положить туда текущие общие остатки?\n\n" +
            "Это стартовый шаг WMS: после него товар можно будет перемещать из UNPLACED в реальные ячейки.",
            "Старт WMS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var originalContent = BootstrapLocationsButton.Content;
        BootstrapLocationsButton.IsEnabled = false;
        BootstrapLocationsButton.Content = "Инициализация...";

        try
        {
            var result = await _stockLocationBootstrapper.BootstrapUnplacedAsync(Environment.UserName);
            ReloadStock();
            await RefreshReadinessAsync();
            StatusText.Text =
                $"WMS старт выполнен: источников {result.SourceRows:N0}, кол-во {result.SourceQuantity:N0}, " +
                $"создано ячеек {result.CellsCreated:N0}, строк размещения затронуто {result.LocationsAffected:N0}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Ошибка инициализации WMS: {exception.Message}";
        }
        finally
        {
            BootstrapLocationsButton.Content = originalContent;
            BootstrapLocationsButton.IsEnabled = true;
        }
    }

    private async void OnRefreshReadinessClicked(object sender, RoutedEventArgs e)
    {
        await RefreshReadinessAsync();
    }

    private async Task RefreshReadinessAsync()
    {
        if (_readinessReader is null)
        {
            ApplyReadinessUnavailable("Нет подключения к MySQL.");
            return;
        }

        try
        {
            ReadinessSubtitleText.Text = "Проверяю остатки и адресное хранение...";
            var snapshot = await _readinessReader.ReadAsync();
            ApplyReadiness(snapshot);
        }
        catch (Exception exception)
        {
            ApplyReadinessUnavailable(exception.Message);
        }
    }

    private void ApplyReadiness(WmsReadinessSnapshot snapshot)
    {
        ReadinessCellsText.Text = $"{snapshot.RealCellCount:N0}";
        ReadinessUnplacedText.Text = $"{snapshot.UnplacedQuantity:N0}";
        ReadinessPlacedText.Text = $"{snapshot.RealLocationQuantity:N0}";
        ReadinessMismatchText.Text = snapshot.MismatchedPairs == 0
            ? "0"
            : $"{snapshot.MismatchedPairs:N0} / {snapshot.AbsoluteDifference:N1}";

        var projectionUtc = NormalizeUtc(snapshot.LatestBalanceProjectionUtc);
        var projectionIsStale = !projectionUtc.HasValue
                                || DateTime.UtcNow - projectionUtc.Value > TimeSpan.FromHours(24);
        var projectionLabel = projectionUtc.HasValue
            ? projectionUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : "нет данных";

        if (!snapshot.HasRealCells || !snapshot.HasUnplacedStart)
        {
            SetReadinessPalette("#FFF0F2", "#F2A8B1");
            ReadinessTitleText.Text = "WMS не готов к пилоту";
            ReadinessSubtitleText.Text =
                $"Нужны реальные ячейки и стартовый остаток UNPLACED. Проекция 1С: {projectionLabel}.";
            return;
        }

        if (projectionIsStale)
        {
            SetReadinessPalette("#FFF8E8", "#F0D58A");
            ReadinessTitleText.Text = "Структура готова, данные 1С устарели";
            ReadinessSubtitleText.Text =
                $"Перед пилотом обновите остатки. Последняя проекция: {projectionLabel}.";
            return;
        }

        if (snapshot.MismatchedPairs > 0)
        {
            SetReadinessPalette("#FFF8E8", "#F0D58A");
            ReadinessTitleText.Text = snapshot.HasOnlyExpectedNegativeMismatch
                ? "Готово к пилоту с оговоркой"
                : "Нужно сверить адресные остатки";
            ReadinessSubtitleText.Text = snapshot.HasOnlyExpectedNegativeMismatch
                ? $"Расхождение создают {snapshot.NegativeBalanceRows:N0} отрицательных остатков из 1С ({snapshot.NegativeBalanceQuantity:N1})."
                : $"Не совпадает {snapshot.MismatchedPairs:N0} товарных позиций; абсолютная разница {snapshot.AbsoluteDifference:N1}.";
            return;
        }

        SetReadinessPalette("#EAF8F0", "#A8D9BB");
        ReadinessTitleText.Text = snapshot.IsReadyForFullCutover
            ? "WMS готов к полному переходу"
            : "WMS готов к пилотному размещению";
        ReadinessSubtitleText.Text = snapshot.HasPlacedInRealCells
            ? $"Адресные остатки совпадают с 1С. Проекция: {projectionLabel}."
            : $"Начните перенос из UNPLACED в реальные ячейки. Проекция: {projectionLabel}.";
    }

    private void ApplyReadinessUnavailable(string message)
    {
        SetReadinessPalette("#FFF0F2", "#F2A8B1");
        ReadinessTitleText.Text = "Готовность WMS не проверена";
        ReadinessSubtitleText.Text = message;
        ReadinessCellsText.Text = "—";
        ReadinessUnplacedText.Text = "—";
        ReadinessPlacedText.Text = "—";
        ReadinessMismatchText.Text = "—";
    }

    private void SetReadinessPalette(string background, string border)
    {
        ReadinessBanner.Background = BrushFromHex(background);
        ReadinessBanner.BorderBrush = BrushFromHex(border);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static Brush BrushFromHex(string value)
    {
        return (Brush)new BrushConverter().ConvertFromString(value)!;
    }

    private sealed record WarehouseFilterOption(string? WarehouseNodeId, string DisplayName);
}
