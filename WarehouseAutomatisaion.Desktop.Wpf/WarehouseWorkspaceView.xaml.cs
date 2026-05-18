using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseWorkspaceView : WpfUserControl, IDisposable
{
    private const string AllWarehousesFilter = "Все склады";
    private const string AllTypesFilter = "Все категории";
    private const string AllStatusesFilter = "Все статусы";
    private const string AllStorageCellWarehousesFilter = "Все склады";
    private const string StockSection = "stock";
    private const string TransfersSection = "transfers";
    private const string ReservationsSection = "reservations";
    private const string CellStorageSection = "cellstorage";
    private const string ExpenseInvoicesSection = "expenseinvoices";
    private const string InventorySection = "inventory";
    private const string WriteOffsSection = "writeoffs";
    private const int StockPageSize = 8;
    private const int DocumentsPageSize = 10;

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly SolidColorBrush PrimaryBrush = BrushFromHex("#4F5BFF");
    private static readonly SolidColorBrush PrimarySoftBrush = BrushFromHex("#EEF2FF");
    private static readonly SolidColorBrush SuccessBrush = BrushFromHex("#26A85B");
    private static readonly SolidColorBrush WarningBrush = BrushFromHex("#FF9F1A");
    private static readonly SolidColorBrush DangerBrush = BrushFromHex("#FF5B5B");
    private static readonly SolidColorBrush TextSecondaryBrush = BrushFromHex("#6E7B98");
    private static readonly SolidColorBrush TextMutedBrush = BrushFromHex("#98A3BC");

    private readonly SalesWorkspace _salesWorkspace;
    private readonly WarehouseOperationalWorkspaceStore _store;
    private readonly PurchasingOperationalWorkspaceStore _purchasingStore;

    private OperationalWarehouseWorkspace _workspace;
    private WarehouseWorkspace _runtimeView;
    private OperationalPurchasingWorkspace _purchasingWorkspace;
    private CatalogWorkspace? _catalogWorkspaceForRules;
    private WarehouseCellStorageSnapshot _cellStorageSnapshot = WarehouseCellStorageSnapshot.Empty;
    private string _activeSection = StockSection;
    private bool _syncingSearch;
    private bool _suppressFilterEvents;
    private bool _suppressStorageCellFilterEvents;
    private bool _operationalWorkspacesLoaded;
    private bool _operationalWorkspacesLoading;
    private int _stockPage = 1;
    private int _documentsPage = 1;
    private WarehouseStockItemViewModel[] _filteredStockItems = Array.Empty<WarehouseStockItemViewModel>();
    private WarehouseDocumentItemViewModel[] _filteredDocumentItems = Array.Empty<WarehouseDocumentItemViewModel>();
    private string? _selectedStockKey;

    public event EventHandler<string>? NavigationRequested;

    public WarehouseWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        _store = WarehouseOperationalWorkspaceStore.CreateDefault();
        _workspace = OperationalWarehouseWorkspace.Create(GetCurrentOperator(), salesWorkspace);
        _purchasingStore = PurchasingOperationalWorkspaceStore.CreateDefault();
        _purchasingWorkspace = OperationalPurchasingWorkspace.Create(GetCurrentOperator(), salesWorkspace);
        _runtimeView = WarehouseWorkspace.Create(salesWorkspace);

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);
        InitializeFilters();
        InitializeCreateMenu();
        HookEvents();
        Loaded += HandleLoaded;
    }

    public void Dispose()
    {
        UnhookEvents();
        TryPersistWorkspace();
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private static bool EqualsUi(string? source, string expected)
    {
        return string.Equals(Ui(source), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsUi(string? source, string search)
    {
        return Ui(source).Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void HookEvents()
    {
        _salesWorkspace.Changed += HandleSalesWorkspaceChanged;
        _workspace.Changed += HandleWorkspaceChanged;
        SizeChanged += HandleSizeChanged;
        Unloaded += HandleUnloaded;
    }

    private void UnhookEvents()
    {
        _salesWorkspace.Changed -= HandleSalesWorkspaceChanged;
        _workspace.Changed -= HandleWorkspaceChanged;
        SizeChanged -= HandleSizeChanged;
        Unloaded -= HandleUnloaded;
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        TryPersistWorkspace();
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        Dispatcher.BeginInvoke(() =>
        {
            RefreshAll();
            _ = LoadOperationalWorkspacesAsync();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task LoadOperationalWorkspacesAsync()
    {
        if (_operationalWorkspacesLoaded || _operationalWorkspacesLoading)
        {
            return;
        }

        _operationalWorkspacesLoading = true;
        var currentOperator = GetCurrentOperator();

        try
        {
            var warehouseTask = Task.Run(() => _store.LoadOrCreate(currentOperator, _salesWorkspace));
            var purchasingTask = Task.Run(() => _purchasingStore.LoadOrCreate(currentOperator, _salesWorkspace));
            await Task.WhenAll(warehouseTask, purchasingTask);

            _workspace.Changed -= HandleWorkspaceChanged;
            _workspace = warehouseTask.Result;
            _workspace.Changed += HandleWorkspaceChanged;
            _purchasingWorkspace = purchasingTask.Result;
            _operationalWorkspacesLoaded = true;

            RefreshAll();
        }
        catch (Exception exception)
        {
            ShowTransientWarning($"Не удалось загрузить складские данные из БД: {exception.Message}");
        }
        finally
        {
            _operationalWorkspacesLoading = false;
        }
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshAll, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TryPersistWorkspace();
            RefreshAll();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void InitializeFilters()
    {
        WarehouseFilterCombo.ItemsSource = new[] { AllWarehousesFilter };
        TypeFilterCombo.ItemsSource = new[] { AllTypesFilter, "С резервом", "Свободный остаток" };
        StatusFilterCombo.ItemsSource = new[] { AllStatusesFilter, "Критично", "Под контролем", "Норма" };
        WarehouseFilterCombo.SelectedIndex = 0;
        TypeFilterCombo.SelectedIndex = 0;
        StatusFilterCombo.SelectedIndex = 0;
    }

    private void InitializeCreateMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Перемещение", (_, _) => CreateTransfer()));
        menu.Items.Add(CreateMenuItem("Инвентаризация", (_, _) => CreateInventory()));
        menu.Items.Add(CreateMenuItem("Списание", (_, _) => CreateWriteOff()));
        menu.Items.Add(CreateMenuItem("Резервы", (_, _) => SwitchSection(ReservationsSection)));
        menu.Items.Add(CreateMenuItem("Ячейки", (_, _) => SwitchSection(CellStorageSection)));
        menu.Items.Add(CreateMenuItem("Расходные накладные", (_, _) => SwitchSection(ExpenseInvoicesSection)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Сбросить фильтры", (_, _) => ResetStockFilters(clearSearch: true)));
        menu.Items.Add(CreateMenuItem("Экспорт текущего вида", (_, _) => HandleExportClick(this, new RoutedEventArgs())));
        ActionsButton.ContextMenu = menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void RefreshAll()
    {
        _workspace.RefreshReferenceData(_salesWorkspace);
        _runtimeView = WarehouseWorkspace.Create(_salesWorkspace);
        _cellStorageSnapshot = WarehouseCellStorageOperations.Build(
            _salesWorkspace,
            _runtimeView,
            _workspace,
            _purchasingWorkspace,
            DateTime.Today);

        RefreshMeta();
        RefreshMetrics();
        RefreshWarehouseFilters();
        RefreshStockItems();
        RefreshDocumentsItems();
        RefreshCellStorageItems();
        SwitchSection(_activeSection);
        UpdateSearchPlaceholders();
        UpdateResponsiveLayout();
    }

    private void RefreshMeta()
    {
        PrimaryWarehouseText.Text = ResolvePrimaryWarehouseLabel();
        OperatorText.Text = $"Оператор: {Ui(_workspace.CurrentOperator)}";
        UpdatedAtText.Text = $"Обновлено: {DateTime.Now:HH:mm}";
    }

    private void RefreshMetrics()
    {
        CriticalMetricText.Text = _runtimeView.StockBalances.Count(item => EqualsUi(item.Status, "Критично")).ToString("N0", RuCulture);

        TransfersMetricText.Text = _workspace.TransferOrders.Count(item =>
            !EqualsUi(item.Status, "Перемещен")).ToString("N0", RuCulture);

        ReservationsMetricText.Text = _runtimeView.Reservations.Count.ToString("N0", RuCulture);

        InventoryMetricText.Text = (_workspace.InventoryCounts.Count(item =>
                !EqualsUi(item.Status, "Проведена"))
            + _workspace.WriteOffs.Count(item =>
                !EqualsUi(item.Status, "Списано")))
            .ToString("N0", RuCulture);
    }

    private void RefreshWarehouseFilters()
    {
        var selectedWarehouse = WarehouseFilterCombo.SelectedItem as string ?? AllWarehousesFilter;
        var selectedType = TypeFilterCombo.SelectedItem as string ?? AllTypesFilter;
        var selectedStatus = StatusFilterCombo.SelectedItem as string ?? AllStatusesFilter;

        _suppressFilterEvents = true;
        try
        {
            WarehouseFilterCombo.ItemsSource = new[] { AllWarehousesFilter }
                .Concat(_runtimeView.StockBalances
                    .Select(item => item.Warehouse)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            WarehouseFilterCombo.SelectedItem = WarehouseFilterCombo.Items.Cast<string>().Contains(selectedWarehouse)
                ? selectedWarehouse
                : AllWarehousesFilter;
            TypeFilterCombo.SelectedItem = TypeFilterCombo.Items.Cast<string>().Contains(selectedType)
                ? selectedType
                : AllTypesFilter;
            StatusFilterCombo.SelectedItem = StatusFilterCombo.Items.Cast<string>().Contains(selectedStatus)
                ? selectedStatus
                : AllStatusesFilter;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void RefreshStockItems()
    {
        var search = StockSearchBox.Text.Trim();
        var selectedWarehouse = WarehouseFilterCombo.SelectedItem as string ?? AllWarehousesFilter;
        var selectedType = TypeFilterCombo.SelectedItem as string ?? AllTypesFilter;
        var selectedStatus = StatusFilterCombo.SelectedItem as string ?? AllStatusesFilter;
        var onlyProblems = ProblemsOnlyCheckBox.IsChecked == true;

        _filteredStockItems = _runtimeView.StockBalances
            .Where(item =>
                string.IsNullOrWhiteSpace(search)
                || Contains(item.ItemCode, search)
                || Contains(item.ItemName, search)
                || Contains(item.Warehouse, search)
                || Contains(item.Status, search))
            .Where(item =>
                string.Equals(selectedWarehouse, AllWarehousesFilter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Warehouse, selectedWarehouse, StringComparison.OrdinalIgnoreCase))
            .Where(item => selectedType switch
            {
                "С резервом" => item.ReservedQuantity > 0m,
                "Свободный остаток" => item.FreeQuantity > 0m,
                _ => true
            })
            .Where(item =>
                string.Equals(selectedStatus, AllStatusesFilter, StringComparison.OrdinalIgnoreCase)
                || EqualsUi(item.Status, selectedStatus))
            .Where(item =>
                !onlyProblems
                || EqualsUi(item.Status, "Критично")
                || EqualsUi(item.Status, "Под контроль")
                || EqualsUi(item.Status, "Под контролем"))
            .OrderBy(ResolveStockPriority)
            .ThenBy(item => item.FreeQuantity)
            .ThenBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .Select(WarehouseStockItemViewModel.Create)
            .ToArray();

        var pageCount = Math.Max(1, (int)Math.Ceiling(_filteredStockItems.Length / (double)StockPageSize));
        _stockPage = Math.Clamp(_stockPage, 1, pageCount);
        var pageItems = _filteredStockItems
            .Skip((_stockPage - 1) * StockPageSize)
            .Take(StockPageSize)
            .ToArray();

        StockDataGrid.ItemsSource = pageItems;
        ShownCountText.Text = BuildShownText(_filteredStockItems.Length, _stockPage, StockPageSize);
        BuildPager(PagerPanel, _stockPage, pageCount, page =>
        {
            _stockPage = page;
            RefreshStockItems();
        });

        var selectedItem = pageItems.FirstOrDefault(item => item.SelectionKey == _selectedStockKey) ?? pageItems.FirstOrDefault();
        StockDataGrid.SelectedItem = selectedItem;
        RefreshSelectedStockDetails(selectedItem);
        UpdateBulkActions();
    }

    private void RefreshDocumentsItems()
    {
        var search = DocumentsSearchBox.Text.Trim();

        _filteredDocumentItems = GetActiveDocuments()
            .Where(item => string.IsNullOrWhiteSpace(search) || Contains(item.SearchText, search))
            .OrderByDescending(item => item.SortDate)
            .ToArray();

        var pageCount = Math.Max(1, (int)Math.Ceiling(_filteredDocumentItems.Length / (double)DocumentsPageSize));
        _documentsPage = Math.Clamp(_documentsPage, 1, pageCount);

        DocumentsDataGrid.ItemsSource = _filteredDocumentItems
            .Skip((_documentsPage - 1) * DocumentsPageSize)
            .Take(DocumentsPageSize)
            .ToArray();

        DocumentsShownCountText.Text = BuildShownText(_filteredDocumentItems.Length, _documentsPage, DocumentsPageSize);
        BuildPager(DocumentsPagerPanel, _documentsPage, pageCount, page =>
        {
            _documentsPage = page;
            RefreshDocumentsItems();
        });

        DocumentsSectionTitleText.Text = _activeSection switch
        {
            TransfersSection => "Перемещения",
            ReservationsSection => "Резервы",
            ExpenseInvoicesSection => "Расходные накладные",
            InventorySection => "Инвентаризация",
            WriteOffsSection => "Списания",
            _ => "Документы склада"
        };

        DocumentsSectionSubtitleText.Text = _activeSection switch
        {
            TransfersSection => "Маршруты между складами и текущий статус выполнения.",
            ReservationsSection => "Документы резерва под продажи и отгрузку.",
            ExpenseInvoicesSection => "Расходные документы 1С по отгрузкам клиентам.",
            InventorySection => "Фиксация пересчета и расхождений по складу.",
            WriteOffsSection => "Потери, брак и внутренние корректировки остатков.",
            _ => "Складские документы текущего раздела."
        };

        DocumentsPrimaryButton.Content = _activeSection switch
        {
            TransfersSection => "Новое перемещение",
            InventorySection => "Новая инвентаризация",
            WriteOffsSection => "Новое списание",
            ReservationsSection => "Открыть остатки",
            ExpenseInvoicesSection => "Обновить",
            _ => "Обновить"
        };
    }

    private void RefreshCellStorageItems()
    {
        if (!IsInitialized)
        {
            return;
        }

        CellTodayMetricText.Text = _cellStorageSnapshot.TodayShipmentCount.ToString("N0", RuCulture);
        CellReadyMetricText.Text = _cellStorageSnapshot.ReadyShipmentCount.ToString("N0", RuCulture);
        CellShortMetricText.Text = _cellStorageSnapshot.ShortShipmentCount.ToString("N0", RuCulture);
        CellMissingMetricText.Text = _cellStorageSnapshot.MissingCellLineCount.ToString("N0", RuCulture);

        var shipmentRecords = GetFilteredCellShipments().ToArray();
        CellVisibleQueueText.Text = shipmentRecords.Length == _cellStorageSnapshot.TodayShipments.Count
            ? $"Показано: {shipmentRecords.Length:N0}"
            : $"Показано: {shipmentRecords.Length:N0} из {_cellStorageSnapshot.TodayShipments.Count:N0}";

        var shipments = shipmentRecords
            .Select(WarehouseCellShipmentViewModel.Create)
            .ToArray();
        var selectedId = (CellShipmentsDataGrid.SelectedItem as WarehouseCellShipmentViewModel)?.ShipmentId;
        CellShipmentsDataGrid.ItemsSource = shipments;

        var selected = shipments.FirstOrDefault(item => item.ShipmentId == selectedId) ?? shipments.FirstOrDefault();
        CellShipmentsDataGrid.SelectedItem = selected;
        RefreshSelectedCellShipment(selected);

        var unassignedRows = GetUnassignedCellBalances()
            .Select(WarehouseCellBalanceViewModel.Create)
            .ToArray();
        UnassignedCellItemsDataGrid.ItemsSource = unassignedRows;
        UnassignedCellItemsHintText.Text = unassignedRows.Length == 0
            ? "Все свободные остатки привязаны к адресам хранения."
            : $"Товары есть на свободном остатке, но не привязаны к адресу хранения. Строк: {unassignedRows.Length:N0}.";

        CellBalancesDataGrid.ItemsSource = _cellStorageSnapshot.CellBalances
            .Select(WarehouseCellBalanceViewModel.Create)
            .ToArray();

        RefreshPlacementRuleItems();
        RefreshCellStorageIssueItems();
        RefreshStorageCellFilters();
        RefreshStorageCellItems();
    }

    private void RefreshPlacementRuleItems()
    {
        PlacementRulesDataGrid.ItemsSource = _workspace.PlacementRules
            .OrderBy(item => item.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayKey, StringComparer.CurrentCultureIgnoreCase)
            .Select(WarehouseCellPlacementRuleViewModel.Create)
            .ToArray();
    }

    private void RefreshCellStorageIssueItems()
    {
        CellStorageIssuesDataGrid.ItemsSource = _workspace.CellStorageIssues
            .OrderByDescending(item => item.CreatedAt)
            .Select(WarehouseCellIssueViewModel.Create)
            .ToArray();
    }

    private IEnumerable<WarehouseTodayShipmentRecord> GetFilteredCellShipments()
    {
        var search = CellShipmentSearchBox.Text.Trim();
        var onlyProblems = CellOnlyProblemShipmentsCheckBox.IsChecked == true;

        return _cellStorageSnapshot.TodayShipments
            .Where(item => !onlyProblems || !item.IsStockCovered || !item.IsCellCovered)
            .Where(item => string.IsNullOrWhiteSpace(search)
                           || Contains(item.SearchText, search)
                           || _cellStorageSnapshot.PickLines.Any(line =>
                               line.ShipmentId == item.ShipmentId
                               && Contains(line.SearchText, search)))
            .OrderBy(item => item.ReadinessWeight)
            .ThenBy(item => item.ShipmentDate)
            .ThenBy(item => item.ShipmentNumber, StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshStorageCellFilters()
    {
        var selected = StorageCellWarehouseFilterCombo.SelectedItem as string ?? AllStorageCellWarehousesFilter;
        var warehouses = new[] { AllStorageCellWarehousesFilter }
            .Concat(_workspace.StorageCells
                .Select(item => item.Warehouse)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        _suppressStorageCellFilterEvents = true;
        try
        {
            StorageCellWarehouseFilterCombo.ItemsSource = warehouses;
            StorageCellWarehouseFilterCombo.SelectedItem = warehouses.Contains(selected)
                ? selected
                : AllStorageCellWarehousesFilter;
        }
        finally
        {
            _suppressStorageCellFilterEvents = false;
        }
    }

    private void RefreshStorageCellItems()
    {
        var selectedId = (StorageCellsDataGrid.SelectedItem as WarehouseStorageCellViewModel)?.Record.Id;
        var rows = GetFilteredStorageCells()
            .Select(WarehouseStorageCellViewModel.Create)
            .ToArray();

        StorageCellsDataGrid.ItemsSource = rows;
        StorageCellsDataGrid.SelectedItem = rows.FirstOrDefault(item => item.Record.Id == selectedId);
        RefreshStorageCellActions();
    }

    private IEnumerable<WarehouseStorageCellRecord> GetFilteredStorageCells()
    {
        var warehouse = StorageCellWarehouseFilterCombo.SelectedItem as string ?? AllStorageCellWarehousesFilter;
        var search = StorageCellSearchBox.Text.Trim();

        return _workspace.StorageCells
            .Where(item => warehouse.Equals(AllStorageCellWarehousesFilter, StringComparison.OrdinalIgnoreCase)
                           || item.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(search)
                           || Contains(item.Code, search)
                           || Contains(item.Warehouse, search)
                           || Contains(item.ZoneName, search)
                           || Contains(item.ZoneCode, search)
                           || Contains(item.CellType, search)
                           || Contains(item.Status, search)
                           || Contains(item.Comment, search))
            .OrderBy(item => item.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.IsActive)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshSelectedCellShipment(WarehouseCellShipmentViewModel? shipment)
    {
        if (shipment is null)
        {
            CellSelectedShipmentTitleText.Text = "Отгрузка не выбрана";
            CellSelectedShipmentSubtitleText.Text = "На сегодня нет активных отгрузок или они уже закрыты.";
            CellPickLinesDataGrid.ItemsSource = Array.Empty<WarehouseCellPickLineViewModel>();
            CellPickShipmentButton.IsEnabled = false;
            CellOpenShipmentButton.IsEnabled = false;
            return;
        }

        CellSelectedShipmentTitleText.Text = $"{shipment.Number} / заказ {shipment.SalesOrderNumber}";
        CellSelectedShipmentSubtitleText.Text = $"{shipment.Customer} / {shipment.Warehouse} / {shipment.Readiness} / нужно {shipment.RequiredDisplay}, дефицит {shipment.ShortageDisplay}";
        CellPickShipmentButton.IsEnabled = true;
        CellOpenShipmentButton.IsEnabled = true;
        CellPickLinesDataGrid.ItemsSource = _cellStorageSnapshot.PickLines
            .Where(item => item.ShipmentId == shipment.ShipmentId)
            .Select(WarehouseCellPickLineViewModel.Create)
            .ToArray();
    }

    private WarehouseDocumentItemViewModel[] GetActiveDocuments()
    {
        return _activeSection switch
            {
            TransfersSection => _workspace.TransferOrders
                .Select(item => WarehouseDocumentItemViewModel.Create(
                    TransfersSection,
                    item.Number,
                    item.DocumentDate,
                    BuildRoute(item.SourceWarehouse, item.TargetWarehouse),
                    item.Status,
                    item.RelatedDocument,
                    item.PositionCount,
                    BuildDocumentSearchText(
                        item.Number,
                        item.SourceWarehouse,
                        item.TargetWarehouse,
                        item.RelatedDocument,
                        item.Status,
                        item.Comment,
                        string.Join(' ', item.Lines.Select(line => $"{line.ItemCode} {line.ItemName}"))),
                    item.Id,
                    true))
                .ToArray(),
            ReservationsSection => _runtimeView.Reservations
                .Select(item => WarehouseDocumentItemViewModel.Create(
                    ReservationsSection,
                    item.Number,
                    item.Date ?? DateTime.MinValue,
                    BuildRoute(item.SourceWarehouse, item.TargetWarehouse),
                    item.Status,
                    item.RelatedDocument,
                    item.Lines.Count,
                    BuildDocumentSearchText(
                        item.Number,
                        item.SourceWarehouse,
                        item.TargetWarehouse,
                        item.RelatedDocument,
                        item.Status,
                        item.Comment,
                        item.Title,
                        item.Subtitle,
                        string.Join(' ', item.Lines.Select(line => line.Item)))))
                .ToArray(),
            ExpenseInvoicesSection => _salesWorkspace.Shipments
                .Select(item => WarehouseDocumentItemViewModel.Create(
                    ExpenseInvoicesSection,
                    item.Number,
                    item.ShipmentDate,
                    BuildRoute(item.Warehouse, item.CustomerName),
                    item.Status,
                    item.SalesOrderNumber,
                    item.PositionCount,
                    BuildDocumentSearchText(
                        item.Number,
                        item.SalesOrderNumber,
                        item.CustomerCode,
                        item.CustomerName,
                        item.ContractNumber,
                        item.Warehouse,
                        item.Status,
                        item.Carrier,
                        item.Manager,
                        item.Comment,
                        string.Join(' ', item.Lines.Select(line => $"{line.ItemCode} {line.ItemName}"))),
                    item.Id,
                    true))
                .ToArray(),
            InventorySection => _workspace.InventoryCounts
                .Select(item => WarehouseDocumentItemViewModel.Create(
                    InventorySection,
                    item.Number,
                    item.DocumentDate,
                    BuildRoute(item.SourceWarehouse, item.TargetWarehouse),
                    item.Status,
                    item.RelatedDocument,
                    item.PositionCount,
                    BuildDocumentSearchText(
                        item.Number,
                        item.SourceWarehouse,
                        item.TargetWarehouse,
                        item.RelatedDocument,
                        item.Status,
                        item.Comment,
                        string.Join(' ', item.Lines.Select(line => $"{line.ItemCode} {line.ItemName}"))),
                    item.Id,
                    true))
                .ToArray(),
            WriteOffsSection => _workspace.WriteOffs
                .Select(item => WarehouseDocumentItemViewModel.Create(
                    WriteOffsSection,
                    item.Number,
                    item.DocumentDate,
                    BuildRoute(item.SourceWarehouse, item.TargetWarehouse),
                    item.Status,
                    item.RelatedDocument,
                    item.PositionCount,
                    BuildDocumentSearchText(
                        item.Number,
                        item.SourceWarehouse,
                        item.TargetWarehouse,
                        item.RelatedDocument,
                        item.Status,
                        item.Comment,
                        string.Join(' ', item.Lines.Select(line => $"{line.ItemCode} {line.ItemName}"))),
                    item.Id,
                    true))
                .ToArray(),
            _ => Array.Empty<WarehouseDocumentItemViewModel>()
        };
    }

    private void RefreshSelectedStockDetails(WarehouseStockItemViewModel? selectedItem)
    {
        if (selectedItem is null)
        {
            SelectedItemTitleText.Text = "Позиция не выбрана";
            SelectedWarehouseText.Text = "Выберите строку в таблице остатков.";
            SelectedCodeText.Text = "—";
            SelectedNameText.Text = "—";
            SelectedStockWarehouseText.Text = "—";
            SelectedUnitText.Text = "—";
            SelectedBarcodeText.Text = "—";
            FreeQuantityText.Text = "0";
            ReservedQuantityText.Text = "0";
            TransitQuantityText.Text = "0";
            MinimumStockText.Text = "—";
            DeficitText.Text = "—";
            SelectedStockTimestampText.Text = "На текущее время";
            SelectedStatusText.Text = "—";
            SelectedStatusBadge.Background = PrimarySoftBrush;
            SelectedStatusText.Foreground = PrimaryBrush;
            MovementsItemsControl.ItemsSource = new[]
            {
                new WarehouseMovementItemViewModel(
                    "Нет связанных движений.",
                    "Позиция пока не участвовала в складских операциях.",
                    string.Empty,
                    TextMutedBrush,
                    DateTime.MinValue)
            };
            DocumentsItemsControl.ItemsSource = new[]
            {
                new WarehouseLinkItemViewModel(
                    "Нет связанных документов.",
                    "Выберите позицию или создайте складский документ.")
            };
            return;
        }

        var record = selectedItem.Record;
        _selectedStockKey = selectedItem.SelectionKey;
        SelectedItemTitleText.Text = Ui(record.ItemName);
        SelectedWarehouseText.Text = Ui(record.Warehouse);
        SelectedCodeText.Text = Ui(record.ItemCode);
        SelectedNameText.Text = Ui(record.ItemName);
        SelectedStockWarehouseText.Text = Ui(record.Warehouse);
        SelectedUnitText.Text = string.IsNullOrWhiteSpace(record.Unit) ? "шт" : Ui(record.Unit);
        SelectedBarcodeText.Text = ResolvePseudoBarcode(record);
        FreeQuantityText.Text = record.FreeQuantity.ToString("N0", RuCulture);
        ReservedQuantityText.Text = record.ReservedQuantity.ToString("N0", RuCulture);
        TransitQuantityText.Text = record.ShippedQuantity.ToString("N0", RuCulture);
        MinimumStockText.Text = $"{ResolveMinimumStock(record):N0} {SelectedUnitText.Text}";
        DeficitText.Text = $"{Math.Max(0m, ResolveMinimumStock(record) - record.FreeQuantity):N0} {SelectedUnitText.Text}";
        SelectedStatusText.Text = Ui(record.Status);
        SelectedStockTimestampText.Text = $"На {DateTime.Now:dd.MM.yyyy HH:mm}";

        var palette = ResolveStatusPalette(record.Status);
        SelectedStatusBadge.Background = palette.Back;
        SelectedStatusText.Foreground = palette.Fore;

        MovementsItemsControl.ItemsSource = BuildMovementItems(record);
        DocumentsItemsControl.ItemsSource = BuildRelatedDocumentItems(record);
    }

    public void ActivateSubSection(string subSectionKey)
    {
        if (!string.IsNullOrWhiteSpace(subSectionKey))
        {
            SwitchSection(subSectionKey);
        }
    }

    private void SwitchSection(string section)
    {
        _activeSection = section;
        var isStockSection = string.Equals(section, StockSection, StringComparison.OrdinalIgnoreCase);
        var isCellStorageSection = string.Equals(section, CellStorageSection, StringComparison.OrdinalIgnoreCase);
        StockTabContent.Visibility = isStockSection ? Visibility.Visible : Visibility.Collapsed;
        CellStorageTabContent.Visibility = isCellStorageSection ? Visibility.Visible : Visibility.Collapsed;
        DocumentsTabContent.Visibility = isStockSection || isCellStorageSection ? Visibility.Collapsed : Visibility.Visible;

        UpdateSectionButtons();

        if (isCellStorageSection)
        {
            RefreshCellStorageItems();
        }
        else if (!isStockSection)
        {
            RefreshDocumentsItems();
        }

        UpdateSearchPlaceholders();
    }

    private void UpdateSectionButtons()
    {
        ApplySectionButtonStyle(StockTabButton, _activeSection == StockSection);
        ApplySectionButtonStyle(TransfersTabButton, _activeSection == TransfersSection);
        ApplySectionButtonStyle(ReservationsTabButton, _activeSection == ReservationsSection);
        ApplySectionButtonStyle(CellStorageTabButton, _activeSection == CellStorageSection);
        ApplySectionButtonStyle(ExpenseInvoicesTabButton, _activeSection == ExpenseInvoicesSection);
        ApplySectionButtonStyle(InventoryTabButton, _activeSection == InventorySection);
        ApplySectionButtonStyle(WriteOffsTabButton, _activeSection == WriteOffsSection);
    }

    private static void ApplySectionButtonStyle(WpfButton button, bool active)
    {
        button.Foreground = active ? PrimaryBrush : TextSecondaryBrush;
        button.BorderBrush = active ? PrimaryBrush : WpfBrushes.Transparent;
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void UpdateSearchPlaceholders()
    {
        HeroSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(HeroSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        StockSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(StockSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        DocumentsSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(DocumentsSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateResponsiveLayout()
    {
        var width = ActualWidth;
        var narrowHero = width < 1500;
        var stackDetails = width < 1460;

        HeroGrid.ColumnDefinitions[1].Width = narrowHero ? new GridLength(1, GridUnitType.Star) : new GridLength(760);
        Grid.SetColumn(HeroActionsGrid, narrowHero ? 0 : 1);
        Grid.SetRow(HeroActionsGrid, narrowHero ? 1 : 0);
        Grid.SetColumnSpan(HeroActionsGrid, narrowHero ? 2 : 1);
        HeroActionsGrid.Margin = narrowHero ? new Thickness(0, 18, 0, 0) : new Thickness(0);
        HeroActionsGrid.HorizontalAlignment = narrowHero
            ? System.Windows.HorizontalAlignment.Stretch
            : System.Windows.HorizontalAlignment.Right;

        if (stackDetails)
        {
            StockLayoutGrid.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(DetailsCard, 0);
            Grid.SetRow(DetailsCard, 1);
            StockTableCard.Margin = new Thickness(0);
            DetailsCard.Margin = new Thickness(0, 18, 0, 0);
        }
        else
        {
            StockLayoutGrid.ColumnDefinitions[1].Width = new GridLength(360);
            Grid.SetColumn(DetailsCard, 1);
            Grid.SetRow(DetailsCard, 0);
            StockTableCard.Margin = new Thickness(0, 0, 18, 0);
            DetailsCard.Margin = new Thickness(0);
        }
    }

    private static string BuildShownText(int totalItems, int currentPage, int pageSize)
    {
        if (totalItems <= 0)
        {
            return "Показано 0 из 0";
        }

        var start = (currentPage - 1) * pageSize + 1;
        var end = Math.Min(totalItems, currentPage * pageSize);
        return $"Показано {start}-{end} из {totalItems:N0}";
    }

    private void BuildPager(WpfPanel host, int currentPage, int pageCount, Action<int> setPage)
    {
        host.Children.Clear();
        if (pageCount <= 1)
        {
            return;
        }

        host.Children.Add(CreatePagerButton("<", currentPage > 1 ? currentPage - 1 : null, false, setPage));

        foreach (var token in BuildPagerTokens(currentPage, pageCount))
        {
            if (token is null)
            {
                host.Children.Add(CreatePagerLabel("..."));
                continue;
            }

            var page = token.Value;
            host.Children.Add(CreatePagerButton(page.ToString(RuCulture), page, page == currentPage, setPage));
        }

        host.Children.Add(CreatePagerButton(">", currentPage < pageCount ? currentPage + 1 : null, false, setPage));
    }

    private static IEnumerable<int?> BuildPagerTokens(int currentPage, int pageCount)
    {
        if (pageCount <= 5)
        {
            for (var page = 1; page <= pageCount; page++)
            {
                yield return page;
            }

            yield break;
        }

        yield return 1;
        if (currentPage > 3)
        {
            yield return null;
        }

        var start = Math.Max(2, currentPage - 1);
        var end = Math.Min(pageCount - 1, currentPage + 1);
        for (var page = start; page <= end; page++)
        {
            yield return page;
        }

        if (currentPage < pageCount - 2)
        {
            yield return null;
        }

        yield return pageCount;
    }

    private FrameworkElement CreatePagerLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = TryFindResource("TablePagerEllipsisTextStyle") as Style
        };
    }

    private WpfButton CreatePagerButton(string text, int? targetPage, bool active, Action<int> setPage)
    {
        var button = new WpfButton
        {
            Content = text,
            Style = TryFindResource(active ? "TablePagerActiveButtonStyle" : "TablePagerButtonStyle") as Style,
            Cursor = targetPage.HasValue ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            IsEnabled = targetPage.HasValue,
            Opacity = targetPage.HasValue ? 1d : 0.45d
        };

        if (targetPage.HasValue)
        {
            button.Click += (_, _) => setPage(targetPage.Value);
        }
        return button;
    }

    private static void MarkSelection(IEnumerable<WarehouseStockItemViewModel> items, WarehouseStockItemViewModel? selectedItem)
    {
        foreach (var item in items)
        {
            item.IsSelected = ReferenceEquals(item, selectedItem);
        }
    }

    private static int ResolveStockPriority(WarehouseStockBalanceRecord record)
    {
        if (EqualsUi(record.Status, "Критично"))
        {
            return 0;
        }

        if (ContainsUi(record.Status, "контрол"))
        {
            return 1;
        }

        return 2;
    }

    private static decimal ResolveMinimumStock(WarehouseStockBalanceRecord record)
    {
        var basis = Math.Max(record.ReservedQuantity + record.ShippedQuantity, 10m);
        return Math.Ceiling(basis / 10m) * 10m;
    }

    private static string ResolvePseudoBarcode(WarehouseStockBalanceRecord record)
    {
        return LabelPrintHtmlBuilder.BuildStableNumericCode(record.ItemCode, record.ItemName, record.Warehouse);
    }

    private string ResolvePrimaryWarehouseLabel()
    {
        return _runtimeView.StockBalances
            .Where(item => !string.IsNullOrWhiteSpace(item.Warehouse))
            .GroupBy(item => item.Warehouse)
            .OrderByDescending(group => group.Count())
            .Select(group => Ui(group.Key))
            .FirstOrDefault() ?? "Главный склад";
    }

    private static bool Contains(string? source, string value)
    {
        return ContainsUi(source, value);
    }

    private static string BuildRoute(string sourceWarehouse, string targetWarehouse)
    {
        if (string.IsNullOrWhiteSpace(sourceWarehouse) && string.IsNullOrWhiteSpace(targetWarehouse))
        {
            return "—";
        }

        if (string.IsNullOrWhiteSpace(targetWarehouse))
        {
            return Ui(sourceWarehouse);
        }

        if (string.IsNullOrWhiteSpace(sourceWarehouse))
        {
            return Ui(targetWarehouse);
        }

        return $"{Ui(sourceWarehouse)} → {Ui(targetWarehouse)}";
    }

    private static string BuildDocumentSearchText(params string?[] parts)
    {
        return string.Join(
            ' ',
            parts.Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(Ui));
    }

    private static (WpfBrush Back, WpfBrush Fore) ResolveStatusPalette(string status)
    {
        if (ContainsUi(status, "критич"))
        {
            return (BrushFromHex("#FFF1F1"), DangerBrush);
        }

        if (ContainsUi(status, "не хватает"))
        {
            return (BrushFromHex("#FFF1F1"), DangerBrush);
        }

        if (ContainsUi(status, "контрол")
            || ContainsUi(status, "резерв"))
        {
            return (BrushFromHex("#FFF8ED"), WarningBrush);
        }

        if (ContainsUi(status, "ячейк"))
        {
            return (BrushFromHex("#FFF8ED"), WarningBrush);
        }

        if (ContainsUi(status, "норм")
            || ContainsUi(status, "провед")
            || ContainsUi(status, "списан")
            || ContainsUi(status, "перемещ")
            || ContainsUi(status, "готов")
            || ContainsUi(status, "адресовано"))
        {
            return (BrushFromHex("#F0FAF4"), SuccessBrush);
        }

        return (PrimarySoftBrush, PrimaryBrush);
    }

    private WarehouseMovementItemViewModel[] BuildMovementItems(WarehouseStockBalanceRecord record)
    {
        var items = new List<WarehouseMovementItemViewModel>();

        foreach (var item in _workspace.TransferOrders)
        {
            var quantity = item.Lines
                .Where(line => MatchesStockLine(record, line.ItemCode, line.ItemName))
                .Sum(line => line.Quantity);
            if (quantity <= 0m)
            {
                continue;
            }

            var incoming = string.Equals(item.TargetWarehouse, record.Warehouse, StringComparison.OrdinalIgnoreCase);
            items.Add(new WarehouseMovementItemViewModel(
                $"Перемещение {Ui(item.Number)}",
                $"{item.DocumentDate:dd.MM.yyyy HH:mm} ? {BuildRoute(item.SourceWarehouse, item.TargetWarehouse)}",
                $"{(incoming ? "+" : "-")}{quantity:N0} {Ui(record.Unit)}",
                incoming ? SuccessBrush : DangerBrush,
                item.DocumentDate));
        }

        foreach (var item in _runtimeView.Reservations)
        {
            var quantity = item.Lines
                .Where(line => MatchesStockLine(record, string.Empty, line.Item))
                .Sum(line => line.Quantity);
            if (quantity <= 0m)
            {
                continue;
            }

            items.Add(new WarehouseMovementItemViewModel(
                $"Резерв {Ui(item.Number)}",
                $"{(item.Date ?? DateTime.MinValue):dd.MM.yyyy HH:mm} ? {Ui(item.RelatedDocument)}",
                $"-{quantity:N0} {Ui(record.Unit)}",
                WarningBrush,
                item.Date ?? DateTime.MinValue));
        }

        foreach (var item in _workspace.InventoryCounts)
        {
            var quantity = item.Lines
                .Where(line => MatchesStockLine(record, line.ItemCode, line.ItemName))
                .Sum(line => line.Quantity);
            if (quantity == 0m)
            {
                continue;
            }

            items.Add(new WarehouseMovementItemViewModel(
                $"Инвентаризация {Ui(item.Number)}",
                $"{item.DocumentDate:dd.MM.yyyy HH:mm} ? {Ui(item.SourceWarehouse)}",
                $"{(quantity > 0m ? "+" : string.Empty)}{quantity:N0} {Ui(record.Unit)}",
                quantity > 0m ? SuccessBrush : DangerBrush,
                item.DocumentDate));
        }

        foreach (var item in _workspace.WriteOffs)
        {
            var quantity = item.Lines
                .Where(line => MatchesStockLine(record, line.ItemCode, line.ItemName))
                .Sum(line => line.Quantity);
            if (quantity <= 0m)
            {
                continue;
            }

            items.Add(new WarehouseMovementItemViewModel(
                $"Списание {Ui(item.Number)}",
                $"{item.DocumentDate:dd.MM.yyyy HH:mm} ? {Ui(item.Comment)}",
                $"-{quantity:N0} {Ui(record.Unit)}",
                DangerBrush,
                item.DocumentDate));
        }

        var result = items
            .OrderByDescending(item => item.OccurredAt)
            .Take(5)
            .ToArray();

        return result.Length > 0
            ? result
            : new[]
            {
                new WarehouseMovementItemViewModel(
                    "Нет связанных движений.",
                    "Позиция пока не участвовала в складских операциях.",
                    string.Empty,
                    TextMutedBrush,
                    DateTime.MinValue)
            };
    }

    private WarehouseLinkItemViewModel[] BuildRelatedDocumentItems(WarehouseStockBalanceRecord record)
    {
        var items = new List<WarehouseLinkItemViewModel>();

        items.AddRange(_workspace.TransferOrders
            .Where(item => item.Lines.Any(line => MatchesStockLine(record, line.ItemCode, line.ItemName)))
            .OrderByDescending(item => item.DocumentDate)
            .Take(3)
            .Select(item => new WarehouseLinkItemViewModel(
                Ui(item.Number),
                $"{item.DocumentDate:dd.MM.yyyy} ? {Ui(item.Status)}",
                TransfersSection,
                item.Number,
                true)));

        items.AddRange(_runtimeView.Reservations
            .Where(item => item.Lines.Any(line => MatchesStockLine(record, string.Empty, line.Item)))
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .Take(3)
            .Select(item => new WarehouseLinkItemViewModel(
                Ui(item.Number),
                $"{(item.Date ?? DateTime.MinValue):dd.MM.yyyy} ? {Ui(item.Status)}",
                ReservationsSection,
                item.Number,
                true)));

        items.AddRange(_workspace.InventoryCounts
            .Where(item => item.Lines.Any(line => MatchesStockLine(record, line.ItemCode, line.ItemName)))
            .OrderByDescending(item => item.DocumentDate)
            .Take(2)
            .Select(item => new WarehouseLinkItemViewModel(
                Ui(item.Number),
                $"{item.DocumentDate:dd.MM.yyyy} ? {Ui(item.Status)}",
                InventorySection,
                item.Number,
                true)));

        items.AddRange(_workspace.WriteOffs
            .Where(item => item.Lines.Any(line => MatchesStockLine(record, line.ItemCode, line.ItemName)))
            .OrderByDescending(item => item.DocumentDate)
            .Take(2)
            .Select(item => new WarehouseLinkItemViewModel(
                Ui(item.Number),
                $"{item.DocumentDate:dd.MM.yyyy} ? {Ui(item.Status)}",
                WriteOffsSection,
                item.Number,
                true)));

        var result = items
            .GroupBy(item => item.Caption, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(5)
            .ToArray();

        return result.Length > 0
            ? result
            : new[]
            {
                new WarehouseLinkItemViewModel(
                    "Нет связанных документов.",
                    "Связанные документы появятся после заказов, перемещений или списаний.")
            };
    }

    private static bool MatchesStockLine(WarehouseStockBalanceRecord record, string itemCode, string itemName)
    {
        return (!string.IsNullOrWhiteSpace(itemCode)
                && string.Equals(record.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(itemName)
                   && string.Equals(record.ItemName, itemName, StringComparison.OrdinalIgnoreCase));
    }

    private void PersistAndRefresh()
    {
        TryPersistWorkspace();
        RefreshAll();
    }

    private WarehouseStorageCellRecord? GetSelectedStorageCell()
    {
        return (StorageCellsDataGrid.SelectedItem as WarehouseStorageCellViewModel)?.Record;
    }

    private IEnumerable<WarehouseCellBalanceRecord> GetStorageCellBalances(WarehouseStorageCellRecord cell)
    {
        return _cellStorageSnapshot.CellBalances
            .Where(item => item.IsAddressed)
            .Where(item => item.Quantity > 0m)
            .Where(item => Ui(item.Warehouse).Equals(Ui(cell.Warehouse), StringComparison.OrdinalIgnoreCase))
            .Where(item => Ui(item.Cell).Equals(Ui(cell.Code), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => Ui(item.ItemName), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.ItemCode), StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<WarehouseCellBalanceRecord> GetUnassignedCellBalances()
    {
        return _cellStorageSnapshot.CellBalances
            .Where(item => !item.IsAddressed)
            .Where(item => item.Quantity > 0m)
            .OrderBy(item => Ui(item.Warehouse), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.ItemName), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.ItemCode), StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshStorageCellActions()
    {
        var selected = GetSelectedStorageCell();
        ToggleStorageCellButton.Content = selected?.IsActive == false ? "Активировать" : "Закрыть";
        ToggleStorageCellButton.IsEnabled = selected is not null;
        ReviseStorageCellButton.Content = "Ревизия";
        ReviseStorageCellButton.IsEnabled = selected is not null && selected.IsActive;
        MoveStorageCellButton.IsEnabled = selected is not null && selected.IsActive;

        if (selected is null)
        {
            SelectedStorageCellTitleText.Text = "Ячейка не выбрана";
            SelectedStorageCellSubtitleText.Text = "Выберите ячейку, чтобы увидеть товары внутри и провести ревизию.";
            SelectedStorageCellBalancesDataGrid.ItemsSource = Array.Empty<WarehouseCellBalanceViewModel>();
            SelectedStorageCellHistoryDataGrid.ItemsSource = Array.Empty<WarehouseCellHistoryViewModel>();
            return;
        }

        var cellBalances = GetStorageCellBalances(selected).ToArray();
        var balanceRows = cellBalances
            .Select(WarehouseCellBalanceViewModel.Create)
            .ToArray();
        var historyRows = BuildStorageCellHistory(selected)
            .Select(WarehouseCellHistoryViewModel.Create)
            .ToArray();

        SelectedStorageCellTitleText.Text = $"{Ui(selected.Code)} / {Ui(selected.Warehouse)}";
        SelectedStorageCellSubtitleText.Text = balanceRows.Length == 0
            ? "В этой ячейке нет адресованных остатков."
            : $"Позиций: {balanceRows.Length:N0}, всего: {cellBalances.Sum(item => item.Quantity):N0}.";
        SelectedStorageCellBalancesDataGrid.Columns[0].Header = "Код";
        SelectedStorageCellBalancesDataGrid.Columns[1].Header = "Товар";
        SelectedStorageCellBalancesDataGrid.Columns[2].Header = "Кол-во";
        SelectedStorageCellBalancesDataGrid.ItemsSource = balanceRows;
        SelectedStorageCellHistoryDataGrid.ItemsSource = historyRows;
    }

    private IReadOnlyList<string> GetActiveStorageCellCodes(string warehouse, string? excludeCode = null)
    {
        return _workspace.StorageCells
            .Where(item => item.IsActive)
            .Where(item => WarehouseMatches(item.Warehouse, warehouse))
            .Where(item => string.IsNullOrWhiteSpace(excludeCode)
                           || !Ui(item.Code).Equals(Ui(excludeCode), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => Ui(item.Code), StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ApplyStorageCellScan()
    {
        var raw = StorageCellScanBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            MessageBox.Show(Window.GetWindow(this), "Отсканируйте QR ячейки или введите код адреса.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var requestedWarehouse = string.Empty;
        var requestedCell = raw;
        if (WarehouseCellStoragePreparationPlan.TryParseQrPayload(raw, out var payload)
            && payload.ObjectType.Equals("cell", StringComparison.OrdinalIgnoreCase))
        {
            if (payload.Values.TryGetValue("warehouse", out var parsedWarehouse))
            {
                requestedWarehouse = parsedWarehouse;
            }

            if (payload.Values.TryGetValue("cell", out var parsedCell))
            {
                requestedCell = parsedCell;
            }
        }

        var match = _workspace.StorageCells.FirstOrDefault(item =>
            (string.IsNullOrWhiteSpace(requestedWarehouse) || WarehouseMatches(item.Warehouse, requestedWarehouse))
            && (Ui(item.Code).Equals(Ui(requestedCell), StringComparison.OrdinalIgnoreCase)
                || Ui(item.QrPayload).Equals(Ui(raw), StringComparison.OrdinalIgnoreCase)));

        if (match is null)
        {
            MessageBox.Show(Window.GetWindow(this), $"Ячейка по скану {Ui(raw)} не найдена.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _suppressStorageCellFilterEvents = true;
        try
        {
            StorageCellWarehouseFilterCombo.SelectedItem = StorageCellWarehouseFilterCombo.Items
                .Cast<string>()
                .FirstOrDefault(item => item.Equals(match.Warehouse, StringComparison.OrdinalIgnoreCase))
                ?? AllStorageCellWarehousesFilter;
            StorageCellSearchBox.Text = match.Code;
        }
        finally
        {
            _suppressStorageCellFilterEvents = false;
        }

        RefreshStorageCellItems();
        var row = StorageCellsDataGrid.Items
            .Cast<WarehouseStorageCellViewModel>()
            .FirstOrDefault(item => item.Record.Id == match.Id);
        if (row is not null)
        {
            StorageCellsDataGrid.SelectedItem = row;
            StorageCellsDataGrid.ScrollIntoView(row);
        }
    }

    private WarehouseStorageCellRecord? FindStorageCell(string warehouse, string code)
    {
        var normalizedCode = Ui(code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return null;
        }

        return _workspace.StorageCells.FirstOrDefault(item =>
            WarehouseMatches(item.Warehouse, warehouse)
            && Ui(item.Code).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SalesCatalogItemOption> BuildCatalogOptions(WarehouseCellBalanceRecord balance)
    {
        return new[]
        {
            new SalesCatalogItemOption(
                Ui(balance.ItemCode),
                string.IsNullOrWhiteSpace(balance.ItemName) ? Ui(balance.ItemCode) : Ui(balance.ItemName),
                string.IsNullOrWhiteSpace(balance.Unit) ? "шт" : Ui(balance.Unit),
                0m)
        };
    }

    private WarehouseCellPlacementRuleRecord? ResolvePlacementRule(WarehouseCellBalanceRecord balance)
    {
        var category = ResolveProductCategory(balance.ItemCode, balance.ItemName);
        return _workspace.PlacementRules
            .Where(item => item.IsActive)
            .Where(item => WarehouseMatches(item.Warehouse, balance.Warehouse))
            .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.ItemCode))
            .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.ItemName))
            .FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(item.ItemCode)
                 && !string.IsNullOrWhiteSpace(balance.ItemCode)
                 && Ui(item.ItemCode).Equals(Ui(balance.ItemCode), StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(item.ItemName)
                    && !string.IsNullOrWhiteSpace(balance.ItemName)
                    && Ui(item.ItemName).Equals(Ui(balance.ItemName), StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(item.Category)
                    && !string.IsNullOrWhiteSpace(category)
                    && Ui(item.Category).Equals(category, StringComparison.OrdinalIgnoreCase)));
    }

    private bool ValidatePlacementRule(
        WarehouseCellBalanceRecord balance,
        WarehouseStorageCellRecord targetCell,
        WarehouseCellPlacementRuleRecord? rule,
        out string error)
    {
        if (rule is null || !rule.IsActive)
        {
            error = string.Empty;
            return true;
        }

        var preferredCells = new[] { rule.PrimaryCellCode, rule.ReserveCellCode }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(Ui)
            .ToArray();
        if (preferredCells.Length > 0
            && preferredCells.All(item => !item.Equals(Ui(targetCell.Code), StringComparison.OrdinalIgnoreCase)))
        {
            error = $"По правилу размещения товар {Ui(balance.ItemName)} можно размещать только в ячейки: {string.Join(", ", preferredCells)}.";
            return false;
        }

        var zonePriority = SplitRuleZones(rule.ZonePriority).ToArray();
        if (preferredCells.Length == 0
            && zonePriority.Length > 0
            && zonePriority.All(zone => !ZoneMatches(targetCell, zone)))
        {
            error = $"Ячейка {Ui(targetCell.Code)} не входит в приоритетные зоны правила: {string.Join(", ", zonePriority)}.";
            return false;
        }

        if (rule.ForbidMixedCategories && !CanMixCategoryInCell(balance, targetCell, rule, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool CanMixCategoryInCell(
        WarehouseCellBalanceRecord balance,
        WarehouseStorageCellRecord targetCell,
        WarehouseCellPlacementRuleRecord rule,
        out string error)
    {
        var targetCategory = FirstNonEmpty(rule.Category, ResolveProductCategory(balance.ItemCode, balance.ItemName));
        if (string.IsNullOrWhiteSpace(targetCategory))
        {
            error = string.Empty;
            return true;
        }

        var existingDifferentCategory = _cellStorageSnapshot.CellBalances
            .Where(item => item.IsAddressed)
            .Where(item => item.Quantity > 0m)
            .Where(item => WarehouseMatches(item.Warehouse, targetCell.Warehouse))
            .Where(item => Ui(item.Cell).Equals(Ui(targetCell.Code), StringComparison.OrdinalIgnoreCase))
            .Where(item => !MatchesCellItem(item, balance.ItemCode, balance.ItemName))
            .Select(item => new
            {
                Balance = item,
                Category = ResolveProductCategory(item.ItemCode, item.ItemName)
            })
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Category)
                                    && !item.Category.Equals(targetCategory, StringComparison.OrdinalIgnoreCase));

        if (existingDifferentCategory is null)
        {
            error = string.Empty;
            return true;
        }

        error = $"В ячейке {Ui(targetCell.Code)} уже лежит категория {existingDifferentCategory.Category}; правило запрещает смешивать с категорией {targetCategory}.";
        return false;
    }

    private string ResolveProductCategory(string itemCode, string itemName)
    {
        try
        {
            _catalogWorkspaceForRules ??= CatalogWorkspaceStore
                .CreateDefault()
                .TryLoadExisting(GetCurrentOperator(), warehouses: _workspace.Warehouses);
        }
        catch
        {
            _catalogWorkspaceForRules = null;
        }

        var catalogItem = _catalogWorkspaceForRules?.Items.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(itemCode)
             && item.Code.Equals(itemCode, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(itemName)
                && item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)));
        return Ui(catalogItem?.Category).Trim();
    }

    private static IEnumerable<string> SplitRuleZones(string zones)
    {
        return Ui(zones)
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private static bool ZoneMatches(WarehouseStorageCellRecord cell, string zone)
    {
        return Ui(cell.ZoneCode).Equals(Ui(zone), StringComparison.OrdinalIgnoreCase)
               || Ui(cell.ZoneName).Equals(Ui(zone), StringComparison.OrdinalIgnoreCase);
    }

    private void ShowCellStorageWarning(
        string operation,
        string severity,
        string warehouse,
        string cellCode,
        string itemCode,
        string itemName,
        string message,
        string relatedDocument)
    {
        RegisterCellStorageIssue(operation, severity, warehouse, cellCode, itemCode, itemName, message, relatedDocument);
        MessageBox.Show(Window.GetWindow(this), message, "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RegisterCellStorageIssue(
        string operation,
        string severity,
        string warehouse,
        string cellCode,
        string itemCode,
        string itemName,
        string message,
        string relatedDocument)
    {
        var duplicate = _workspace.CellStorageIssues.Any(item =>
            item.Status.Equals("Открыта", StringComparison.OrdinalIgnoreCase)
            && item.Operation.Equals(operation, StringComparison.OrdinalIgnoreCase)
            && item.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase)
            && item.CellCode.Equals(cellCode, StringComparison.OrdinalIgnoreCase)
            && item.ItemCode.Equals(itemCode, StringComparison.OrdinalIgnoreCase)
            && item.Message.Equals(message, StringComparison.OrdinalIgnoreCase));
        if (!duplicate)
        {
            _workspace.AddCellStorageIssue(new WarehouseCellIntegrityIssueRecord
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                Severity = severity,
                Operation = operation,
                Warehouse = warehouse,
                CellCode = cellCode,
                ItemCode = itemCode,
                ItemName = itemName,
                Message = message,
                RelatedDocument = relatedDocument,
                Status = "Открыта"
            });
            TryPersistWorkspace();
        }

        RefreshCellStorageIssueItems();
    }

    private static bool ValidateTargetCell(WarehouseStorageCellRecord? cell, string requestedCode, out string error)
    {
        if (cell is null)
        {
            error = $"Ячейка {Ui(requestedCode)} не найдена.";
            return false;
        }

        if (!cell.IsActive)
        {
            error = $"Ячейка {Ui(cell.Code)} закрыта для операций.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool ValidateCellCapacity(WarehouseStorageCellRecord cell, decimal addingQuantity, out string error)
    {
        if (cell.Capacity <= 0m)
        {
            error = string.Empty;
            return true;
        }

        var currentQuantity = _cellStorageSnapshot.CellBalances
            .Where(item => item.IsAddressed)
            .Where(item => WarehouseMatches(item.Warehouse, cell.Warehouse))
            .Where(item => Ui(item.Cell).Equals(Ui(cell.Code), StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Quantity);

        if (currentQuantity + addingQuantity <= cell.Capacity)
        {
            error = string.Empty;
            return true;
        }

        error = $"Лимит ячейки {Ui(cell.Code)}: {cell.Capacity:N0}. Сейчас внутри {currentQuantity:N0}, операция добавляет {addingQuantity:N0}.";
        return false;
    }

    private static bool MatchesCellItem(WarehouseCellBalanceRecord balance, string itemCode, string itemName)
    {
        return (!string.IsNullOrWhiteSpace(balance.ItemCode)
                && !string.IsNullOrWhiteSpace(itemCode)
                && Ui(balance.ItemCode).Equals(Ui(itemCode), StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(balance.ItemName)
                   && !string.IsNullOrWhiteSpace(itemName)
                   && Ui(balance.ItemName).Equals(Ui(itemName), StringComparison.OrdinalIgnoreCase));
    }

    private static bool WarehouseMatches(string left, string right)
    {
        var cleanLeft = Ui(left);
        var cleanRight = Ui(right);
        return string.IsNullOrWhiteSpace(cleanLeft)
               || string.IsNullOrWhiteSpace(cleanRight)
               || cleanLeft.Equals(cleanRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatQuantity(decimal quantity, string unit)
    {
        return $"{quantity:N2} {(string.IsNullOrWhiteSpace(unit) ? "шт" : Ui(unit))}";
    }

    private bool HasPostedPickingDocument(SalesShipmentRecord shipment)
    {
        var shipmentNumber = Ui(shipment.Number);
        if (string.IsNullOrWhiteSpace(shipmentNumber))
        {
            return false;
        }

        return _workspace.WriteOffs.Any(document =>
            !IsDraftLikeStatus(document.Status)
            && (Ui(document.RelatedDocument).Equals(shipmentNumber, StringComparison.OrdinalIgnoreCase)
                || Ui(document.Comment).Contains(shipmentNumber, StringComparison.OrdinalIgnoreCase))
            && Ui(document.Comment).Contains("Отбор", StringComparison.OrdinalIgnoreCase));
    }

    private CellPickAllocationResult BuildShipmentCellPickLines(SalesShipmentRecord shipment)
    {
        var buckets = _cellStorageSnapshot.CellBalances
            .Where(item => item.IsAddressed)
            .Where(item => item.Quantity > 0m)
            .Where(item => WarehouseMatches(item.Warehouse, shipment.Warehouse))
            .OrderBy(item => Ui(item.Cell), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.ItemName), StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new CellAllocationBucket(item))
            .ToArray();

        var result = new CellPickAllocationResult();
        foreach (var shipmentLine in shipment.Lines.Where(item => item.Quantity > 0m))
        {
            var remaining = shipmentLine.Quantity;
            foreach (var bucket in buckets.Where(item => item.RemainingQuantity > 0m && MatchesCellItem(item.Record, shipmentLine.ItemCode, shipmentLine.ItemName)))
            {
                var quantity = Math.Min(remaining, bucket.RemainingQuantity);
                if (quantity <= 0m)
                {
                    continue;
                }

                result.Lines.Add(new OperationalWarehouseLineRecord
                {
                    Id = Guid.NewGuid(),
                    ItemCode = shipmentLine.ItemCode,
                    ItemName = shipmentLine.ItemName,
                    Unit = string.IsNullOrWhiteSpace(shipmentLine.Unit) ? bucket.Record.Unit : shipmentLine.Unit,
                    Quantity = quantity,
                    SourceLocation = bucket.Record.Cell,
                    TargetLocation = string.Empty,
                    RelatedDocument = shipment.Number
                });

                bucket.RemainingQuantity -= quantity;
                remaining -= quantity;
                if (remaining <= 0m)
                {
                    break;
                }
            }

            if (remaining > 0m)
            {
                result.Errors.Add($"Не хватает адресного остатка по товару {Ui(shipmentLine.ItemName)}: нужно {FormatQuantity(shipmentLine.Quantity, shipmentLine.Unit)}, не покрыто {FormatQuantity(remaining, shipmentLine.Unit)}.");
            }
        }

        return result;
    }

    private static bool IsDraftLikeStatus(string status)
    {
        var value = Ui(status).ToLowerInvariant();
        return value.Contains("чернов", StringComparison.OrdinalIgnoreCase)
               || value.Contains("план", StringComparison.OrdinalIgnoreCase)
               || value.Contains("отмен", StringComparison.OrdinalIgnoreCase)
               || value.Contains("архив", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClosedShipmentStatus(string status)
    {
        var value = Ui(status).ToLowerInvariant();
        return value.Contains("отгруж", StringComparison.OrdinalIgnoreCase)
               || value.Contains("закры", StringComparison.OrdinalIgnoreCase)
               || value.Contains("отмен", StringComparison.OrdinalIgnoreCase)
               || value.Contains("архив", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<StorageCellHistoryRecord> BuildStorageCellHistory(WarehouseStorageCellRecord cell)
    {
        var rows = new List<StorageCellHistoryRecord>();

        foreach (var receipt in _purchasingWorkspace.PurchaseReceipts
                     .Where(item => !IsDraftLikeStatus(item.Status))
                     .Where(item => WarehouseMatches(item.Warehouse, cell.Warehouse)))
        {
            foreach (var line in receipt.Lines.Where(line => LocationMatchesCell(line.TargetLocation, cell.Code)))
            {
                rows.Add(new StorageCellHistoryRecord(
                    receipt.DocumentDate,
                    "Приемка",
                    receipt.Number,
                    line.ItemCode,
                    line.ItemName,
                    line.Unit,
                    line.Quantity,
                    receipt.Comment));
            }
        }

        foreach (var transfer in _workspace.TransferOrders
                     .Where(item => !IsDraftLikeStatus(item.Status))
                     .Where(item => WarehouseMatches(item.SourceWarehouse, cell.Warehouse) || WarehouseMatches(item.TargetWarehouse, cell.Warehouse)))
        {
            foreach (var line in transfer.Lines)
            {
                if (LocationMatchesCell(line.SourceLocation, cell.Code))
                {
                    rows.Add(new StorageCellHistoryRecord(
                        transfer.DocumentDate,
                        "Перемещение",
                        transfer.Number,
                        line.ItemCode,
                        line.ItemName,
                        line.Unit,
                        -line.Quantity,
                        transfer.Comment));
                }

                if (LocationMatchesCell(line.TargetLocation, cell.Code))
                {
                    rows.Add(new StorageCellHistoryRecord(
                        transfer.DocumentDate,
                        "Перемещение",
                        transfer.Number,
                        line.ItemCode,
                        line.ItemName,
                        line.Unit,
                        line.Quantity,
                        transfer.Comment));
                }
            }
        }

        foreach (var inventory in _workspace.InventoryCounts
                     .Where(item => !IsDraftLikeStatus(item.Status))
                     .Where(item => WarehouseMatches(item.SourceWarehouse, cell.Warehouse)))
        {
            foreach (var line in inventory.Lines.Where(line => LineTouchesCell(line, cell.Code)))
            {
                rows.Add(new StorageCellHistoryRecord(
                    inventory.DocumentDate,
                    "Ревизия",
                    inventory.Number,
                    line.ItemCode,
                    line.ItemName,
                    line.Unit,
                    line.Quantity,
                    inventory.Comment));
            }
        }

        foreach (var writeOff in _workspace.WriteOffs
                     .Where(item => !IsDraftLikeStatus(item.Status))
                     .Where(item => WarehouseMatches(item.SourceWarehouse, cell.Warehouse)))
        {
            foreach (var line in writeOff.Lines.Where(line => LocationMatchesCell(line.SourceLocation, cell.Code)))
            {
                rows.Add(new StorageCellHistoryRecord(
                    writeOff.DocumentDate,
                    "Списание",
                    writeOff.Number,
                    line.ItemCode,
                    line.ItemName,
                    line.Unit,
                    -line.Quantity,
                    writeOff.Comment));
            }
        }

        return rows
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.DocumentNumber, StringComparer.OrdinalIgnoreCase)
            .Take(100);
    }

    private OperationalWarehouseDocumentRecord? FindLatestCellInventoryDocument(WarehouseStorageCellRecord cell)
    {
        return _workspace.InventoryCounts
            .Where(item => !IsDraftLikeStatus(item.Status))
            .Where(item => WarehouseMatches(item.SourceWarehouse, cell.Warehouse))
            .Where(item => DocumentTouchesCell(item, cell))
            .OrderByDescending(item => item.DocumentDate)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool DocumentTouchesCell(OperationalWarehouseDocumentRecord document, WarehouseStorageCellRecord cell)
    {
        return Ui(document.RelatedDocument).Equals(Ui(cell.Code), StringComparison.OrdinalIgnoreCase)
               || Ui(document.TargetWarehouse).Equals(Ui(cell.Code), StringComparison.OrdinalIgnoreCase)
               || document.Lines.Any(line => LineTouchesCell(line, cell.Code));
    }

    private static bool LineTouchesCell(OperationalWarehouseLineRecord line, string cellCode)
    {
        return LocationMatchesCell(line.SourceLocation, cellCode)
               || LocationMatchesCell(line.TargetLocation, cellCode)
               || LocationMatchesCell(line.RelatedDocument, cellCode);
    }

    private static bool LocationMatchesCell(string value, string cellCode)
    {
        return !string.IsNullOrWhiteSpace(value)
               && Ui(value).Equals(Ui(cellCode), StringComparison.OrdinalIgnoreCase);
    }

    private static PrintableLabelDefinition BuildPrintableCellLabel(WarehouseStorageCellRecord cell)
    {
        var generatedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm", RuCulture);
        var payload = string.IsNullOrWhiteSpace(cell.QrPayload)
            ? WarehouseCellStoragePreparationPlan.BuildCellQrPayload(cell.Warehouse, cell.Code)
            : cell.QrPayload;
        var marker = string.IsNullOrWhiteSpace(payload) ? cell.Code : payload;

        return new PrintableLabelDefinition(
            "Ячейка хранения",
            Ui(cell.Code),
            cell.IsActive ? "Активна" : Ui(cell.Status),
            new[]
            {
                new PrintableField("Склад", Ui(cell.Warehouse)),
                new PrintableField("Зона", string.IsNullOrWhiteSpace(cell.ZoneName) ? Ui(cell.ZoneCode) : Ui(cell.ZoneName)),
                new PrintableField("Тип", Ui(cell.CellType)),
                new PrintableField("Лимит", cell.Capacity <= 0m ? "Не задан" : cell.Capacity.ToString("N0", RuCulture)),
                new PrintableField("Адрес", $"{cell.Row}-{cell.Rack}-{cell.Shelf}-{cell.Cell}")
            },
            marker,
            payload,
            $"Сформировано: {generatedAt}");
    }

    private static IReadOnlyDictionary<string, int> BuildHeaderMap(IReadOnlyList<string> cells)
    {
        var knownHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ячейка", "код", "адрес", "cell", "code", "склад", "warehouse", "кодзоны", "зонакод", "zonecode",
            "зона", "zonename", "zone", "тип", "типячейки", "celltype", "type", "статус", "status",
            "комментарий", "comment", "примечание", "qr", "payload", "qrpayload", "ряд", "row",
            "стеллаж", "rack", "полка", "shelf", "место", "cellnumber", "place", "лимит", "вместимость", "capacity"
        };

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < cells.Count; index++)
        {
            var key = NormalizeHeader(cells[index]);
            if (!knownHeaders.Contains(key) || result.ContainsKey(key))
            {
                continue;
            }

            result[key] = index;
        }

        return result;
    }

    private static string Field(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> headerMap, int fallbackIndex, params string[] aliases)
    {
        foreach (var alias in aliases.Select(NormalizeHeader))
        {
            if (headerMap.TryGetValue(alias, out var index) && index >= 0 && index < cells.Count)
            {
                return Ui(cells[index]);
            }
        }

        return fallbackIndex >= 0 && fallbackIndex < cells.Count ? Ui(cells[fallbackIndex]) : string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.Select(Ui).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string NormalizeHeader(string value)
    {
        return string.Concat(Ui(value).ToLowerInvariant().Where(char.IsLetterOrDigit));
    }

    private static bool TryParseIntFlexible(string value, out int result)
    {
        if (int.TryParse(Ui(value), NumberStyles.Integer, RuCulture, out result)
            || int.TryParse(Ui(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        if (TryParseDecimalFlexible(value, out var decimalValue))
        {
            result = (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
            return true;
        }

        result = 0;
        return false;
    }

    private void HandleAddStorageCellClick(object sender, RoutedEventArgs e)
    {
        var selectedWarehouse = StorageCellWarehouseFilterCombo.SelectedItem as string;
        var draft = _workspace.CreateStorageCellDraft(
            string.IsNullOrWhiteSpace(selectedWarehouse) || selectedWarehouse.Equals(AllStorageCellWarehousesFilter, StringComparison.OrdinalIgnoreCase)
                ? ResolvePrimaryWarehouseLabel()
                : selectedWarehouse);

        OpenStorageCellEditor(draft, isNew: true);
    }

    private void HandleEditStorageCellClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedStorageCell();
        if (selected is null)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Выберите ячейку для изменения.",
                "Ячеечное хранение",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenStorageCellEditor(selected, isNew: false);
    }

    private void HandleToggleStorageCellClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedStorageCell();
        if (selected is null)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Выберите ячейку.",
                "Ячеечное хранение",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var targetActive = !selected.IsActive;
        var action = targetActive ? "активировать" : "закрыть";
        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"Вы действительно хотите {action} ячейку {Ui(selected.Code)}?",
            "Ячеечное хранение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _workspace.SetStorageCellActive(selected.Id, targetActive);
            PersistAndRefresh();
        }
        catch (Exception exception)
        {
            ShowStorageCellError(exception);
        }
    }

    private void HandleReviseStorageCellClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedStorageCell();
        if (selected is null)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Выберите ячейку для ревизии.",
                "Ячеечное хранение",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var draft = _workspace.CreateInventoryDraft(selected.Warehouse);
        draft.TargetWarehouse = selected.Code;
        draft.RelatedDocument = selected.Code;
        draft.Comment = $"Ревизия ячейки {selected.Code}.";

        var dialog = new WarehouseCellRevisionWindow(
            selected,
            draft,
            _workspace.CatalogItems,
            GetStorageCellBalances(selected).ToArray())
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        _workspace.AddInventoryCount(dialog.ResultDocument);
        PersistAndRefresh();
        MessageBox.Show(
            Window.GetWindow(this),
            $"Ревизия ячейки {Ui(selected.Code)} проведена. Создан документ {Ui(dialog.ResultDocument.Number)}.",
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandlePlaceUnassignedCellClick(object sender, RoutedEventArgs e)
    {
        if (UnassignedCellItemsDataGrid.SelectedItem is not WarehouseCellBalanceViewModel selectedBalance)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Выберите товар из очереди без ячейки.",
                "Ячеечное хранение",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var balance = selectedBalance.Record;
        var activeCells = _workspace.StorageCells
            .Where(item => item.IsActive)
            .Where(item => WarehouseMatches(item.Warehouse, balance.Warehouse))
            .OrderBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (activeCells.Length == 0)
        {
            ShowCellStorageWarning(
                "Размещение",
                "Ошибка",
                balance.Warehouse,
                string.Empty,
                balance.ItemCode,
                balance.ItemName,
                $"На складе {Ui(balance.Warehouse)} нет активных ячеек для размещения.",
                string.Empty);
            return;
        }

        var placementRule = ResolvePlacementRule(balance);
        var dialog = new WarehouseCellGroupPlacementWindow(
            balance,
            activeCells,
            _cellStorageSnapshot.CellBalances,
            placementRule)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultPlacements.Count == 0)
        {
            return;
        }

        var totalQuantity = dialog.ResultPlacements.Sum(item => item.Quantity);
        if (totalQuantity <= 0m || totalQuantity > balance.Quantity)
        {
            ShowCellStorageWarning(
                "Размещение",
                "Ошибка",
                balance.Warehouse,
                string.Empty,
                balance.ItemCode,
                balance.ItemName,
                $"Сумма размещения должна быть больше нуля и не больше свободного остатка {FormatQuantity(balance.Quantity, balance.Unit)}.",
                string.Empty);
            return;
        }

        foreach (var placement in dialog.ResultPlacements)
        {
            if (!ValidatePlacementRule(balance, placement.Cell, placementRule, out var ruleError))
            {
                ShowCellStorageWarning(
                    "Размещение",
                    "Ошибка",
                    balance.Warehouse,
                    placement.Cell.Code,
                    balance.ItemCode,
                    balance.ItemName,
                    ruleError,
                    string.Empty);
                return;
            }
        }

        var document = _workspace.CreateInventoryDraft(balance.Warehouse);
        document.Status = "Проведена";
        document.SourceWarehouse = balance.Warehouse;
        document.TargetWarehouse = dialog.ResultPlacements.Count == 1 ? dialog.ResultPlacements[0].Cell.Code : balance.Warehouse;
        document.RelatedDocument = "Групповое размещение";
        document.Comment = $"Размещение товара {Ui(balance.ItemName)} по ячейкам: {string.Join(", ", dialog.ResultPlacements.Select(item => $"{item.Cell.Code}={item.Quantity:N2}"))}.";
        foreach (var placement in dialog.ResultPlacements)
        {
            document.Lines.Add(new OperationalWarehouseLineRecord
            {
                Id = Guid.NewGuid(),
                ItemCode = balance.ItemCode,
                ItemName = balance.ItemName,
                Unit = string.IsNullOrWhiteSpace(balance.Unit) ? "шт" : balance.Unit,
                Quantity = placement.Quantity,
                SourceLocation = string.Empty,
                TargetLocation = placement.Cell.Code,
                RelatedDocument = placement.Cell.Code
            });
        }

        _workspace.AddInventoryCount(document);
        PersistAndRefresh();
        MessageBox.Show(
            Window.GetWindow(this),
            $"Товар размещен по ячейкам. Строк: {dialog.ResultPlacements.Count:N0}. Создан документ {Ui(document.Number)}.",
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandleMoveStorageCellClick(object sender, RoutedEventArgs e)
    {
        var sourceCell = GetSelectedStorageCell();
        if (sourceCell is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите ячейку-источник.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!sourceCell.IsActive)
        {
            MessageBox.Show(Window.GetWindow(this), "Закрытая ячейка не может быть источником перемещения.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedStorageCellBalancesDataGrid.SelectedItem is not WarehouseCellBalanceViewModel selectedBalance)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите товар внутри ячейки для перемещения.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var balance = selectedBalance.Record;
        var cellOptions = GetActiveStorageCellCodes(sourceCell.Warehouse, sourceCell.Code).ToArray();
        if (cellOptions.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Нет другой активной ячейки для перемещения.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var draftLine = new OperationalWarehouseLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = balance.ItemCode,
            ItemName = balance.ItemName,
            Unit = string.IsNullOrWhiteSpace(balance.Unit) ? "шт" : balance.Unit,
            Quantity = balance.Quantity,
            SourceLocation = sourceCell.Code,
            TargetLocation = cellOptions.FirstOrDefault() ?? string.Empty,
            RelatedDocument = sourceCell.Code
        };

        var dialog = new WarehouseLineEditorWindow(
            "Перемещение между ячейками",
            $"Источник: {Ui(sourceCell.Code)}. Доступно: {FormatQuantity(balance.Quantity, balance.Unit)}.",
            BuildCatalogOptions(balance),
            draftLine,
            allowNegativeQuantity: false,
            allowTargetLocation: true,
            storageCellOptions: GetActiveStorageCellCodes(sourceCell.Warehouse).ToArray())
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultLine is null)
        {
            return;
        }

        var line = dialog.ResultLine;
        if (!MatchesCellItem(balance, line.ItemCode, line.ItemName))
        {
            MessageBox.Show(Window.GetWindow(this), "Перемещать можно только выбранный товар.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Ui(line.SourceLocation).Equals(Ui(sourceCell.Code), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Window.GetWindow(this), $"Источник должен быть выбранной ячейкой {Ui(sourceCell.Code)}.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (line.Quantity <= 0m || line.Quantity > balance.Quantity)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Количество для перемещения должно быть больше нуля и не больше остатка {FormatQuantity(balance.Quantity, balance.Unit)}.",
                "Ячеечное хранение",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (Ui(line.TargetLocation).Equals(Ui(sourceCell.Code), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Window.GetWindow(this), "Ячейка назначения должна отличаться от источника.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetCell = FindStorageCell(sourceCell.Warehouse, line.TargetLocation);
        if (!ValidateTargetCell(targetCell, line.TargetLocation, out var targetError))
        {
            ShowCellStorageWarning("Перемещение", "Ошибка", sourceCell.Warehouse, line.TargetLocation, balance.ItemCode, balance.ItemName, targetError, string.Empty);
            return;
        }

        if (!ValidateCellCapacity(targetCell!, line.Quantity, out var capacityError))
        {
            ShowCellStorageWarning("Перемещение", "Ошибка", sourceCell.Warehouse, targetCell!.Code, balance.ItemCode, balance.ItemName, capacityError, string.Empty);
            return;
        }

        var placementRule = ResolvePlacementRule(balance);
        if (!ValidatePlacementRule(balance, targetCell!, placementRule, out var ruleError))
        {
            ShowCellStorageWarning("Перемещение", "Ошибка", sourceCell.Warehouse, targetCell!.Code, balance.ItemCode, balance.ItemName, ruleError, string.Empty);
            return;
        }

        var document = _workspace.CreateTransferDraft(sourceCell.Warehouse);
        document.Status = "Перемещен";
        document.SourceWarehouse = sourceCell.Warehouse;
        document.TargetWarehouse = sourceCell.Warehouse;
        document.RelatedDocument = $"{sourceCell.Code} -> {targetCell!.Code}";
        document.Comment = $"Перемещение товара {Ui(balance.ItemName)} из {sourceCell.Code} в {targetCell.Code}.";
        document.Lines.Add(new OperationalWarehouseLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = line.ItemCode,
            ItemName = line.ItemName,
            Unit = line.Unit,
            Quantity = line.Quantity,
            SourceLocation = sourceCell.Code,
            TargetLocation = targetCell.Code,
            RelatedDocument = document.RelatedDocument
        });

        _workspace.AddTransferOrder(document);
        PersistAndRefresh();
        MessageBox.Show(
            Window.GetWindow(this),
            $"Перемещение проведено. Создан документ {Ui(document.Number)}.",
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandlePrintStorageCellLabelClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedStorageCell();
        if (selected is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите ячейку для печати QR-этикетки.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var labels = new[] { BuildPrintableCellLabel(selected) };
        PrintDocumentComposer.Print(
            Window.GetWindow(this),
            "QR-этикетка ячейки",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildLabelsDocument("QR-этикетка ячейки", labels, pageWidth, pageHeight));
    }

    private void HandleExportStorageCellActClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedStorageCell();
        if (selected is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите ячейку для выгрузки акта.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var document = FindLatestCellInventoryDocument(selected);
        if (document is null)
        {
            MessageBox.Show(Window.GetWindow(this), $"По ячейке {Ui(selected.Code)} еще нет проведенной ревизии.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"cell-act-{selected.Code}-{document.DocumentDate:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string>
        {
            "Акт ревизии ячейки",
            $"Ячейка;{EscapeCsv(selected.Code)}",
            $"Склад;{EscapeCsv(selected.Warehouse)}",
            $"Документ;{EscapeCsv(document.Number)}",
            $"Дата;{document.DocumentDate:dd.MM.yyyy}",
            string.Empty,
            "Код;Товар;Количество изменения;Ед.;Комментарий"
        };

        lines.AddRange(document.Lines
            .Where(line => LineTouchesCell(line, selected.Code))
            .Select(line => string.Join(";",
                EscapeCsv(Ui(line.ItemCode)),
                EscapeCsv(Ui(line.ItemName)),
                line.Quantity.ToString("N2", RuCulture),
                EscapeCsv(string.IsNullOrWhiteSpace(line.Unit) ? "шт" : Ui(line.Unit)),
                EscapeCsv(Ui(document.Comment)))));

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
        MessageBox.Show(Window.GetWindow(this), "Акт ревизии выгружен.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HandleImportStorageCellsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV/TSV (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Все файлы (*.*)|*.*",
            Title = "Импорт ячеек хранения"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var rows = File.ReadAllLines(dialog.FileName, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(SplitDelimitedLine)
            .Where(cells => cells.Length > 0)
            .ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "В файле нет строк для импорта.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var headerMap = BuildHeaderMap(rows[0]);
        var startIndex = headerMap.Count > 0 ? 1 : 0;
        var created = 0;
        var updated = 0;
        var skipped = 0;

        for (var index = startIndex; index < rows.Length; index++)
        {
            var cells = rows[index];
            var warehouse = FirstNonEmpty(
                Field(cells, headerMap, 1, "склад", "warehouse"),
                ResolvePrimaryWarehouseLabel());
            var code = FirstNonEmpty(
                Field(cells, headerMap, 0, "ячейка", "код", "адрес", "cell", "code"),
                string.Empty);

            if (string.IsNullOrWhiteSpace(code))
            {
                skipped++;
                continue;
            }

            var existing = FindStorageCell(warehouse, code);
            var record = existing?.Clone() ?? _workspace.CreateStorageCellDraft(warehouse);
            record.Warehouse = warehouse;
            record.Code = code;
            record.ZoneCode = FirstNonEmpty(Field(cells, headerMap, 2, "кодзоны", "зонакод", "zonecode"), record.ZoneCode);
            record.ZoneName = FirstNonEmpty(Field(cells, headerMap, 3, "зона", "zonename", "zone"), record.ZoneName);
            record.CellType = FirstNonEmpty(Field(cells, headerMap, 4, "тип", "типячейки", "celltype", "type"), record.CellType);
            record.Status = FirstNonEmpty(Field(cells, headerMap, 5, "статус", "status"), record.Status);
            record.Comment = FirstNonEmpty(Field(cells, headerMap, 6, "комментарий", "comment", "примечание"), record.Comment);
            record.QrPayload = FirstNonEmpty(Field(cells, headerMap, 7, "qr", "payload", "qrpayload"), record.QrPayload);

            if (TryParseIntFlexible(Field(cells, headerMap, 8, "ряд", "row"), out var row))
            {
                record.Row = row;
            }

            if (TryParseIntFlexible(Field(cells, headerMap, 9, "стеллаж", "rack"), out var rack))
            {
                record.Rack = rack;
            }

            if (TryParseIntFlexible(Field(cells, headerMap, 10, "полка", "shelf"), out var shelf))
            {
                record.Shelf = shelf;
            }

            if (TryParseIntFlexible(Field(cells, headerMap, 11, "место", "cellnumber", "place"), out var cellNumber))
            {
                record.Cell = cellNumber;
            }

            if (TryParseDecimalFlexible(Field(cells, headerMap, 12, "лимит", "вместимость", "capacity"), out var capacity))
            {
                record.Capacity = capacity;
            }

            try
            {
                if (existing is null)
                {
                    _workspace.AddStorageCell(record);
                    created++;
                }
                else
                {
                    _workspace.UpdateStorageCell(record);
                    updated++;
                }
            }
            catch
            {
                skipped++;
            }
        }

        PersistAndRefresh();
        MessageBox.Show(
            Window.GetWindow(this),
            $"Импорт ячеек завершен. Создано: {created:N0}, обновлено: {updated:N0}, пропущено: {skipped:N0}.",
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandleAddPlacementRuleClick(object sender, RoutedEventArgs e)
    {
        OpenPlacementRuleEditor(null);
    }

    private void HandleEditPlacementRuleClick(object sender, RoutedEventArgs e)
    {
        if (PlacementRulesDataGrid.SelectedItem is not WarehouseCellPlacementRuleViewModel selected)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите правило размещения.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenPlacementRuleEditor(selected.Record);
    }

    private void HandleDeletePlacementRuleClick(object sender, RoutedEventArgs e)
    {
        if (PlacementRulesDataGrid.SelectedItem is not WarehouseCellPlacementRuleViewModel selected)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите правило размещения.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"Удалить правило {selected.Subject}?",
            "Ячеечное хранение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _workspace.RemovePlacementRule(selected.Record.Id);
        PersistAndRefresh();
    }

    private void OpenPlacementRuleEditor(WarehouseCellPlacementRuleRecord? rule)
    {
        var dialog = new WarehouseCellPlacementRuleEditorWindow(
            _workspace.Warehouses,
            _workspace.CatalogItems,
            _workspace.StorageCells.ToArray(),
            rule)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultRule is null)
        {
            return;
        }

        _workspace.UpsertPlacementRule(dialog.ResultRule);
        PersistAndRefresh();
    }

    private void HandleAuditCellStorageIssuesClick(object sender, RoutedEventArgs e)
    {
        var before = _workspace.CellStorageIssues.Count;
        foreach (var balance in _cellStorageSnapshot.CellBalances.Where(item => item.IsAddressed && item.Quantity > 0m))
        {
            var cell = FindStorageCell(balance.Warehouse, balance.Cell);
            if (cell is null)
            {
                RegisterCellStorageIssue(
                    "Проверка целостности",
                    "Ошибка",
                    balance.Warehouse,
                    balance.Cell,
                    balance.ItemCode,
                    balance.ItemName,
                    $"Остаток находится в ячейке {Ui(balance.Cell)}, но такой ячейки нет в справочнике.",
                    string.Empty);
                continue;
            }

            if (!cell.IsActive)
            {
                RegisterCellStorageIssue(
                    "Проверка целостности",
                    "Предупреждение",
                    balance.Warehouse,
                    cell.Code,
                    balance.ItemCode,
                    balance.ItemName,
                    $"В закрытой ячейке {Ui(cell.Code)} есть остаток товара.",
                    string.Empty);
            }

            var rule = ResolvePlacementRule(balance);
            if (!ValidatePlacementRule(balance, cell, rule, out var ruleError))
            {
                RegisterCellStorageIssue(
                    "Проверка правил",
                    "Ошибка",
                    balance.Warehouse,
                    cell.Code,
                    balance.ItemCode,
                    balance.ItemName,
                    ruleError,
                    string.Empty);
            }
        }

        foreach (var cell in _workspace.StorageCells)
        {
            if (cell.Capacity <= 0m)
            {
                continue;
            }

            var quantity = _cellStorageSnapshot.CellBalances
                .Where(item => item.IsAddressed && item.Quantity > 0m)
                .Where(item => WarehouseMatches(item.Warehouse, cell.Warehouse))
                .Where(item => Ui(item.Cell).Equals(Ui(cell.Code), StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Quantity);
            if (quantity <= cell.Capacity)
            {
                continue;
            }

            RegisterCellStorageIssue(
                "Проверка лимитов",
                "Ошибка",
                cell.Warehouse,
                cell.Code,
                string.Empty,
                string.Empty,
                $"В ячейке {Ui(cell.Code)} превышен лимит: {quantity:N2} из {cell.Capacity:N2}.",
                string.Empty);
        }

        TryPersistWorkspace();
        RefreshCellStorageIssueItems();
        var added = _workspace.CellStorageIssues.Count - before;
        MessageBox.Show(
            Window.GetWindow(this),
            added <= 0 ? "Проверка завершена, новых ошибок нет." : $"Проверка завершена. Новых записей: {added:N0}.",
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandleClearCellStorageIssuesClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            Window.GetWindow(this),
            "Очистить журнал ошибок ячеечного хранения?",
            "Ячеечное хранение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _workspace.ClearCellStorageIssues();
        PersistAndRefresh();
    }

    private void OpenStorageCellEditor(WarehouseStorageCellRecord cell, bool isNew)
    {
        var dialog = new WarehouseStorageCellEditorWindow(_workspace.Warehouses, cell)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultCell is null)
        {
            return;
        }

        try
        {
            if (isNew)
            {
                _workspace.AddStorageCell(dialog.ResultCell);
            }
            else
            {
                _workspace.UpdateStorageCell(dialog.ResultCell);
            }

            PersistAndRefresh();
        }
        catch (Exception exception)
        {
            ShowStorageCellError(exception);
        }
    }

    private void ShowStorageCellError(Exception exception)
    {
        MessageBox.Show(
            Window.GetWindow(this),
            exception.Message,
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void HandleEnsureStorageCellsClick(object sender, RoutedEventArgs e)
    {
        var added = _workspace.EnsureDefaultStorageCells();
        if (added == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Шаблон ячеек уже актуален.",
                "Ячеечное хранение",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RefreshCellStorageItems();
            return;
        }

        PersistAndRefresh();
        MessageBox.Show(
            Window.GetWindow(this),
            $"Добавлено ячеек: {added:N0}.",
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CreateTransfer()
    {
        CreateDocument(WarehouseDocumentEditorMode.Transfer, _workspace.AddTransferOrder);
    }

    private void CreateInventory()
    {
        CreateDocument(WarehouseDocumentEditorMode.Inventory, _workspace.AddInventoryCount);
    }

    private void CreateWriteOff()
    {
        CreateDocument(WarehouseDocumentEditorMode.WriteOff, _workspace.AddWriteOff);
    }

    private void CreateDocument(WarehouseDocumentEditorMode mode, Action<OperationalWarehouseDocumentRecord> persist)
    {
        var dialog = new WarehouseDocumentEditorWindow(_workspace, mode)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        persist(dialog.ResultDocument);
        PersistAndRefresh();
    }

    private void ExportCurrentStockView()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "warehouse-stock.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string>
        {
            "Код;Товар;Склад;Свободно;Резерв;В пути;Мин. остаток;Статус"
        };

        foreach (var item in _filteredStockItems)
        {
            lines.Add(string.Join(";",
                EscapeCsv(item.Code),
                EscapeCsv(item.Item),
                EscapeCsv(item.Warehouse),
                item.Record.FreeQuantity.ToString("N0", RuCulture),
                item.Record.ReservedQuantity.ToString("N0", RuCulture),
                item.Record.ShippedQuantity.ToString("N0", RuCulture),
                item.MinimumDisplay,
                EscapeCsv(item.Status)));
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
    }

    private void ExportCurrentDocumentsView()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"warehouse-{_activeSection}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string>
        {
            "Номер;Дата;Маршрут;Статус;Основание;Позиций"
        };

        foreach (var item in _filteredDocumentItems)
        {
            lines.Add(string.Join(";",
                EscapeCsv(item.Number),
                EscapeCsv(item.DateText),
                EscapeCsv(item.Route),
                EscapeCsv(item.Status),
                EscapeCsv(item.RelatedDocument),
                item.Positions.ToString(RuCulture)));
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
    }

    private void ExportCellStorageView()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "warehouse-cell-picking.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string>
        {
            "Отгрузка;Заказ;Клиент;Склад;Статус;Готовность;Товар;Нужно;Доступно;Не хватает;Ячейки"
        };

        foreach (var shipment in _cellStorageSnapshot.TodayShipments)
        {
            var pickLines = _cellStorageSnapshot.PickLines.Where(item => item.ShipmentId == shipment.ShipmentId).ToArray();
            if (pickLines.Length == 0)
            {
                lines.Add(string.Join(";",
                    EscapeCsv(shipment.ShipmentNumber),
                    EscapeCsv(shipment.SalesOrderNumber),
                    EscapeCsv(shipment.CustomerName),
                    EscapeCsv(shipment.Warehouse),
                    EscapeCsv(shipment.Status),
                    EscapeCsv(shipment.Readiness),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty));
                continue;
            }

            foreach (var line in pickLines)
            {
                lines.Add(string.Join(";",
                    EscapeCsv(shipment.ShipmentNumber),
                    EscapeCsv(shipment.SalesOrderNumber),
                    EscapeCsv(shipment.CustomerName),
                    EscapeCsv(shipment.Warehouse),
                    EscapeCsv(shipment.Status),
                    EscapeCsv(shipment.Readiness),
                    EscapeCsv(line.ItemName),
                    line.RequiredQuantity.ToString("N2", RuCulture),
                    line.AvailableQuantity.ToString("N2", RuCulture),
                    line.ShortageQuantity.ToString("N2", RuCulture),
                    EscapeCsv(line.CellSummary)));
            }
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
    }

    private void ExportSelectedStockItems()
    {
        var selected = GetCheckedOrSelectedStockItems();
        if (selected.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите позиции для экспорта.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "warehouse-selected-stock.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string>
        {
            "Код;Товар;Склад;Свободно;Резерв;В пути;Мин. остаток;Статус"
        };
        lines.AddRange(selected.Select(item => string.Join(";",
            EscapeCsv(item.Code),
            EscapeCsv(item.Item),
            EscapeCsv(item.Warehouse),
            item.Record.FreeQuantity.ToString("N0", RuCulture),
            item.Record.ReservedQuantity.ToString("N0", RuCulture),
            item.Record.ShippedQuantity.ToString("N0", RuCulture),
            item.MinimumDisplay,
            EscapeCsv(item.Status))));

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
    }

    private void ImportInventoryDocument()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV/TSV (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Все файлы (*.*)|*.*",
            Title = "Импорт складских остатков"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var rows = File.ReadAllLines(dialog.FileName, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(SplitDelimitedLine)
            .Where(cells => cells.Length > 0)
            .ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "В файле нет строк для импорта.", "Склад", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var startIndex = LooksLikeHeader(rows[0]) ? 1 : 0;
        var imported = 0;
        var draft = _workspace.CreateInventoryDraft(ResolvePrimaryWarehouseLabel());
        draft.Comment = $"Импорт из файла {Path.GetFileName(dialog.FileName)}.";
        draft.Lines.Clear();

        for (var index = startIndex; index < rows.Length; index++)
        {
            var cells = rows[index];
            var code = Cell(cells, 0);
            var name = Cell(cells, 1);
            var warehouse = Cell(cells, 2);
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var quantity = TryParseDecimalFlexible(Cell(cells, 3), out var parsedQuantity) ? parsedQuantity : 1m;
            draft.Lines.Add(new OperationalWarehouseLineRecord
            {
                Id = Guid.NewGuid(),
                ItemCode = code,
                ItemName = string.IsNullOrWhiteSpace(name) ? code : name,
                Quantity = quantity,
                Unit = string.IsNullOrWhiteSpace(Cell(cells, 4)) ? "шт" : Cell(cells, 4),
                SourceLocation = string.IsNullOrWhiteSpace(warehouse) ? ResolvePrimaryWarehouseLabel() : warehouse,
                TargetLocation = string.Empty,
                RelatedDocument = $"Импорт {DateTime.Now:dd.MM.yyyy}"
            });
            imported++;
        }

        if (imported == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Не удалось распознать позиции в файле.", "Склад", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _workspace.AddInventoryCount(draft);
        PersistAndRefresh();
        SwitchSection(InventorySection);
        MessageBox.Show(Window.GetWindow(this), $"Импорт создан как документ инвентаризации. Позиций: {imported:N0}.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PrintLabels(IReadOnlyCollection<WarehouseStockItemViewModel> items)
    {
        var rows = items.Count == 0 ? GetCheckedOrSelectedStockItems() : items.ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите позиции для печати этикеток.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var labels = rows.Select(BuildPrintableLabel).ToArray();
        PrintDocumentComposer.Print(
            Window.GetWindow(this),
            "Этикетки склада",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildLabelsDocument("Этикетки склада", labels, pageWidth, pageHeight));
    }

    private static PrintableLabelDefinition BuildPrintableLabel(WarehouseStockItemViewModel item)
    {
        var generatedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm", RuCulture);
        var barcode = ResolvePseudoBarcode(item.Record);
        var payload = string.Join(
            Environment.NewLine,
            $"Код: {item.Code}",
            $"Товар: {item.Item}",
            $"Склад: {item.Warehouse}",
            $"Остаток: {item.BalanceText}",
            $"Статус: {item.Status}",
            $"Маркер: {barcode}");

        return new PrintableLabelDefinition(
            "Этикетка склада",
            item.Item,
            item.Status,
            new[]
            {
                new PrintableField("Код", item.Code),
                new PrintableField("Склад", item.Warehouse),
                new PrintableField("Остаток", item.BalanceText),
                new PrintableField("Мин. остаток", item.MinimumDisplay),
                new PrintableField("Обновлено", item.UpdatedDisplay),
                new PrintableField("Статус", item.Status)
            },
            barcode,
            payload,
            $"Сформировано: {generatedAt}");
    }

    private void EditStockCatalogItem(WarehouseStockItemViewModel? item)
    {
        if (item is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите позицию для редактирования.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var store = CatalogWorkspaceStore.CreateDefault();
        var catalog = store.LoadOrCreate(
            string.IsNullOrWhiteSpace(_salesWorkspace.CurrentOperator) ? Environment.UserName : _salesWorkspace.CurrentOperator,
            _salesWorkspace);
        var record = catalog.Items.FirstOrDefault(entry => Ui(entry.Code).Equals(item.Code, StringComparison.OrdinalIgnoreCase))?.Clone()
                     ?? catalog.CreateItemDraft();

        if (string.IsNullOrWhiteSpace(record.Code) || record.Code.StartsWith("ITEM-", StringComparison.OrdinalIgnoreCase))
        {
            record.Code = item.Code;
        }

        if (string.IsNullOrWhiteSpace(record.Name))
        {
            record.Name = item.Item;
        }

        record.DefaultWarehouse = string.IsNullOrWhiteSpace(record.DefaultWarehouse) ? item.Warehouse : record.DefaultWarehouse;
        record.Unit = string.IsNullOrWhiteSpace(record.Unit) ? (string.IsNullOrWhiteSpace(item.Record.Unit) ? "шт" : item.Record.Unit) : record.Unit;

        var editor = new ProductEditorWindow(catalog, record)
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() != true || editor.ResultItem is null)
        {
            return;
        }

        catalog.UpsertItem(editor.ResultItem);
        store.Save(catalog);
        NavigationRequested?.Invoke(this, "catalog");
    }

    private void CreatePurchaseOrderForStockItems(IReadOnlyCollection<WarehouseStockItemViewModel> items)
    {
        var rows = items.Count == 0 ? GetCheckedOrSelectedStockItems() : items.ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите позиции для закупки.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var store = PurchasingOperationalWorkspaceStore.CreateDefault();
        var workspace = store.LoadOrCreate(
            string.IsNullOrWhiteSpace(_salesWorkspace.CurrentOperator) ? Environment.UserName : _salesWorkspace.CurrentOperator,
            _salesWorkspace);
        var document = workspace.CreatePurchaseOrderDraft(null);
        document.Comment = $"Создано из склада: {string.Join(", ", rows.Select(item => item.Item).Take(3))}.";
        document.Lines.Clear();

        foreach (var item in rows)
        {
            document.Lines.Add(new OperationalPurchasingLineRecord
            {
                Id = Guid.NewGuid(),
                SectionName = "Склад",
                ItemCode = item.Code,
                ItemName = item.Item,
                Quantity = Math.Max(1m, ResolveMinimumStock(item.Record) - item.Record.FreeQuantity),
                Unit = string.IsNullOrWhiteSpace(item.Record.Unit) ? "шт" : item.Record.Unit,
                Price = 0m,
                PlannedDate = DateTime.Today.AddDays(3),
                RelatedDocument = item.Warehouse
            });
        }

        workspace.AddPurchaseOrder(document);
        store.Save(workspace);
        NavigationRequested?.Invoke(this, "purchasing");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static string Html(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static string[] SplitDelimitedLine(string line)
    {
        var delimiter = line.Contains('\t')
            ? '\t'
            : line.Count(ch => ch == ';') >= line.Count(ch => ch == ',')
                ? ';'
                : ',';

        return line.Split(delimiter)
            .Select(cell => cell.Trim().Trim('"'))
            .ToArray();
    }

    private static bool LooksLikeHeader(string[] cells)
    {
        var joined = string.Join(" ", cells).ToLowerInvariant();
        return joined.Contains("код")
               || joined.Contains("артикул")
               || joined.Contains("товар")
               || joined.Contains("номенклатура")
               || joined.Contains("warehouse");
    }

    private static string Cell(string[] cells, int index)
    {
        return index >= 0 && index < cells.Length ? Ui(cells[index]) : string.Empty;
    }

    private static bool TryParseDecimalFlexible(string value, out decimal result)
    {
        value = value.Replace('\u00A0', ' ').Replace(" ", string.Empty);
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

    private void SyncSearchBoxes(WpfTextBox source, params WpfTextBox[] targets)
    {
        if (_syncingSearch)
        {
            return;
        }

        _syncingSearch = true;
        try
        {
            foreach (var target in targets)
            {
                if (!string.Equals(target.Text, source.Text, StringComparison.Ordinal))
                {
                    target.Text = source.Text;
                }
            }
        }
        finally
        {
            _syncingSearch = false;
        }

        UpdateSearchPlaceholders();
    }

    private WarehouseStockItemViewModel? GetSelectedStockItem()
    {
        return StockDataGrid.SelectedItem as WarehouseStockItemViewModel
            ?? _filteredStockItems.FirstOrDefault(item => item.SelectionKey == _selectedStockKey);
    }

    private WarehouseStockItemViewModel[] GetCheckedStockItems()
    {
        return (StockDataGrid.ItemsSource?.Cast<WarehouseStockItemViewModel>() ?? Array.Empty<WarehouseStockItemViewModel>())
            .Where(item => item.IsSelected)
            .ToArray();
    }

    private WarehouseStockItemViewModel[] GetCheckedOrSelectedStockItems()
    {
        var checkedItems = GetCheckedStockItems();
        if (checkedItems.Length > 0)
        {
            return checkedItems;
        }

        var selected = GetSelectedStockItem();
        return selected is null ? Array.Empty<WarehouseStockItemViewModel>() : new[] { selected };
    }

    private void UpdateBulkActions()
    {
        var selected = GetCheckedStockItems().Length;
        BulkActionsPanel.Visibility = selected > 1 ? Visibility.Visible : Visibility.Collapsed;
        BulkSelectedCountText.Text = $"Выбрано {selected:N0} позиции";
    }

    private void ClearCheckedStockItems()
    {
        foreach (var item in StockDataGrid.ItemsSource?.Cast<WarehouseStockItemViewModel>() ?? Array.Empty<WarehouseStockItemViewModel>())
        {
            item.IsSelected = false;
        }

        UpdateBulkActions();
    }

    private void HandleMetricCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string target)
        {
            return;
        }

        switch (target)
        {
            case "critical":
                SwitchSection(StockSection);
                ApplyStockPreset(status: "Критично", onlyProblems: true);
                break;
            case "transfers":
                _documentsPage = 1;
                SwitchSection(TransfersSection);
                break;
            case "reservations":
                _documentsPage = 1;
                SwitchSection(ReservationsSection);
                break;
            case "inventory":
                _documentsPage = 1;
                SwitchSection(InventorySection);
                break;
        }
    }

    private void HandleFiltersButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Сбросить фильтры", (_, _) => ResetStockFilters(clearSearch: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Только критичные", (_, _) => ApplyStockPreset(status: "Критично", onlyProblems: true)));
        menu.Items.Add(CreateMenuItem("С остатком в резерве", (_, _) => ApplyStockPreset(type: "С резервом")));
        menu.Items.Add(CreateMenuItem("Свободный остаток", (_, _) => ApplyStockPreset(type: "Свободный остаток")));
        menu.Items.Add(CreateMenuItem("Под контролем", (_, _) => ApplyStockPreset(status: "Под контролем", onlyProblems: true)));
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void HandleStockRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target || target.Tag is not WarehouseStockItemViewModel item)
        {
            return;
        }

        OpenStockActionsMenu(target, item);
        e.Handled = true;
    }

    private void HandleStockRowCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WarehouseStockItemViewModel item })
        {
            StockDataGrid.SelectedItem = item;
            RefreshSelectedStockDetails(item);
        }

        UpdateBulkActions();
        e.Handled = true;
    }

    private void HandleBulkClearClick(object sender, RoutedEventArgs e)
    {
        ClearCheckedStockItems();
    }

    private void HandleBulkCloseClick(object sender, RoutedEventArgs e)
    {
        ClearCheckedStockItems();
    }

    private void HandleBulkTransferClick(object sender, RoutedEventArgs e)
    {
        CreateTransferForStockItems(GetCheckedOrSelectedStockItems());
    }

    private void HandleBulkReserveClick(object sender, RoutedEventArgs e)
    {
        CreateReserveForStockItems(GetCheckedOrSelectedStockItems());
    }

    private void HandleBulkShipClick(object sender, RoutedEventArgs e)
    {
        CreateShipmentForStockItems(GetCheckedOrSelectedStockItems());
    }

    private void HandleBulkWriteOffClick(object sender, RoutedEventArgs e)
    {
        CreateWriteOffForStockItems(GetCheckedOrSelectedStockItems());
    }

    private void HandleBulkInventoryClick(object sender, RoutedEventArgs e)
    {
        CreateInventoryForStockItems(GetCheckedOrSelectedStockItems());
    }

    private void HandleBulkMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Экспорт выбранных", (_, _) => ExportSelectedStockItems()));
        menu.Items.Add(CreateMenuItem("Печать этикеток", (_, _) => PrintLabels(GetCheckedOrSelectedStockItems())));
        menu.Items.Add(CreateMenuItem("Создать закупку", (_, _) => CreatePurchaseOrderForStockItems(GetCheckedOrSelectedStockItems())));
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void HandleDetailsActionsClick(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedStockItem();
        if (item is null)
        {
            MessageBox.Show(Window.GetWindow(this)!, "Сначала выберите позицию в таблице остатков.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenStockActionsMenu(sender as FrameworkElement ?? DetailsActionsButton, item);
    }

    private void HandleLinkedDocumentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target || target.Tag is not WarehouseLinkItemViewModel item)
        {
            return;
        }

        OpenLinkedDocument(item);
        e.Handled = true;
    }

    private void HandleDocumentsGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DocumentsDataGrid.SelectedItem is not WarehouseDocumentItemViewModel item)
        {
            return;
        }

        if (item.IsEditable)
        {
            OpenDocumentEditor(item);
            return;
        }

        MessageBox.Show(
            Window.GetWindow(this)!,
            $"Документ {item.Number} доступен только для просмотра.\n\nСтатус: {item.Status}\nМаршрут: {item.Route}\nОснование: {item.RelatedDocument}",
            "Документ склада",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandleHeroSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SyncSearchBoxes(HeroSearchBox, StockSearchBox, DocumentsSearchBox);
        _stockPage = 1;
        _documentsPage = 1;
        RefreshStockItems();
        RefreshDocumentsItems();
    }

    private void HandleStockSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SyncSearchBoxes(StockSearchBox, HeroSearchBox, DocumentsSearchBox);
        _stockPage = 1;
        RefreshStockItems();
    }

    private void HandleDocumentsSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SyncSearchBoxes(DocumentsSearchBox, HeroSearchBox, StockSearchBox);
        _documentsPage = 1;
        RefreshDocumentsItems();
    }

    private void HandleFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents)
        {
            return;
        }

        _stockPage = 1;
        RefreshStockItems();
    }

    private void HandleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents)
        {
            return;
        }

        _stockPage = 1;
        RefreshStockItems();
    }

    private void HandleResetFiltersClick(object sender, RoutedEventArgs e)
    {
        ResetStockFilters(clearSearch: true);
    }

    private void HandleStockSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = StockDataGrid.SelectedItem as WarehouseStockItemViewModel;
        RefreshSelectedStockDetails(selectedItem);
    }

    private void HandleCellShipmentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelectedCellShipment(CellShipmentsDataGrid.SelectedItem as WarehouseCellShipmentViewModel);
    }

    private void HandleCellShipmentSearchChanged(object sender, TextChangedEventArgs e)
    {
        RefreshCellStorageItems();
    }

    private void HandleCellShipmentFilterChanged(object sender, RoutedEventArgs e)
    {
        RefreshCellStorageItems();
    }

    private void HandleOpenSelectedCellShipmentClick(object sender, RoutedEventArgs e)
    {
        if (CellShipmentsDataGrid.SelectedItem is not WarehouseCellShipmentViewModel shipment)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите отгрузку в очереди кладовщика.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SwitchSection(ExpenseInvoicesSection);
        DocumentsSearchBox.Text = string.IsNullOrWhiteSpace(shipment.Record.ShipmentNumber)
            ? shipment.Record.SalesOrderNumber
            : shipment.Record.ShipmentNumber;
        RefreshDocumentsItems();

        var match = DocumentsDataGrid.Items
            .Cast<WarehouseDocumentItemViewModel>()
            .FirstOrDefault(item => string.Equals(item.Number, shipment.Record.ShipmentNumber, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            DocumentsDataGrid.SelectedItem = match;
            DocumentsDataGrid.ScrollIntoView(match);
        }
    }

    private void HandlePickSelectedCellShipmentClick(object sender, RoutedEventArgs e)
    {
        if (CellShipmentsDataGrid.SelectedItem is not WarehouseCellShipmentViewModel selected)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите отгрузку для сборки.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var shipment = _salesWorkspace.Shipments.FirstOrDefault(item => item.Id == selected.ShipmentId);
        if (shipment is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Отгрузка не найдена в рабочей области продаж.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (HasPostedPickingDocument(shipment))
        {
            MessageBox.Show(Window.GetWindow(this), $"По отгрузке {Ui(shipment.Number)} уже есть проведенный отбор.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var allocationResult = BuildShipmentCellPickLines(shipment);
        if (allocationResult.Errors.Count > 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                string.Join(Environment.NewLine, allocationResult.Errors.Take(8)),
                "Отбор не проведен",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (allocationResult.Lines.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "В отгрузке нет строк для отбора.", "Ячеечное хранение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var document = _workspace.CreateWriteOffDraft(shipment.Warehouse);
        document.Status = "Списано";
        document.SourceWarehouse = shipment.Warehouse;
        document.RelatedDocument = shipment.Number;
        document.Comment = $"Отбор по отгрузке {shipment.Number}.";
        document.Lines = new BindingList<OperationalWarehouseLineRecord>(allocationResult.Lines);

        _workspace.AddWriteOff(document);

        var updatedShipment = shipment.Clone();
        if (!IsClosedShipmentStatus(updatedShipment.Status))
        {
            updatedShipment.Status = "Готова к отгрузке";
        }

        var pickComment = $"Отбор из ячеек проведен документом {document.Number}.";
        updatedShipment.Comment = string.IsNullOrWhiteSpace(updatedShipment.Comment)
            ? pickComment
            : updatedShipment.Comment.Contains(pickComment, StringComparison.OrdinalIgnoreCase)
                ? updatedShipment.Comment
                : $"{updatedShipment.Comment}{Environment.NewLine}{pickComment}";

        _salesWorkspace.UpdateShipment(updatedShipment);
        TryPersistSalesWorkspace();
        PersistAndRefresh();

        MessageBox.Show(
            Window.GetWindow(this),
            $"Отбор по отгрузке {Ui(shipment.Number)} проведен. Создано строк: {allocationResult.Lines.Count:N0}.",
            "Ячеечное хранение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandleStorageCellFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStorageCellFilterEvents)
        {
            return;
        }

        RefreshStorageCellItems();
    }

    private void HandleStorageCellSearchChanged(object sender, TextChangedEventArgs e)
    {
        RefreshStorageCellItems();
    }

    private void HandleStorageCellScanKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ApplyStorageCellScan();
    }

    private void HandleStorageCellScanClick(object sender, RoutedEventArgs e)
    {
        ApplyStorageCellScan();
    }

    private void HandleStorageCellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshStorageCellActions();
    }

    private void HandleStorageCellDoubleClick(object sender, MouseButtonEventArgs e)
    {
        HandleEditStorageCellClick(sender, e);
    }

    private void HandleSectionTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string section)
        {
            return;
        }

        _documentsPage = 1;
        SwitchSection(section);
    }

    private void HandleExportClick(object sender, RoutedEventArgs e)
    {
        if (_activeSection == StockSection)
        {
            ExportCurrentStockView();
            return;
        }

        if (_activeSection == CellStorageSection)
        {
            ExportCellStorageView();
            return;
        }

        ExportCurrentDocumentsView();
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        CreateTransfer();
    }

    private void HandleActionsClick(object sender, RoutedEventArgs e)
    {
        if (ActionsButton.ContextMenu is null)
        {
            return;
        }

        ActionsButton.ContextMenu.PlacementTarget = ActionsButton;
        ActionsButton.ContextMenu.IsOpen = true;
    }

    private void HandleImportClick(object sender, RoutedEventArgs e)
    {
        ImportInventoryDocument();
    }

    private void HandleCreateTransferClick(object sender, RoutedEventArgs e)
    {
        CreateTransferForSelectedStock(GetSelectedStockItem());
    }

    private void HandleCreateInventoryClick(object sender, RoutedEventArgs e)
    {
        CreateInventoryForSelectedStock(GetSelectedStockItem());
    }

    private void HandleCreateWriteOffClick(object sender, RoutedEventArgs e)
    {
        CreateWriteOffForSelectedStock(GetSelectedStockItem());
    }

    private void HandleEditSelectedStockClick(object sender, RoutedEventArgs e)
    {
        EditStockCatalogItem(GetSelectedStockItem());
    }

    private void HandlePrintLabelsClick(object sender, RoutedEventArgs e)
    {
        PrintLabels(GetCheckedOrSelectedStockItems());
    }

    private void HandlePurchaseSelectedClick(object sender, RoutedEventArgs e)
    {
        CreatePurchaseOrderForStockItems(GetCheckedOrSelectedStockItems());
    }

    private void HandleUpdatePriceClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, "catalog");
    }

    private void HandleOpenReservationsClick(object sender, RoutedEventArgs e)
    {
        CreateReserveForStockItems(GetCheckedOrSelectedStockItems());
    }

    private void HandleDocumentsPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        switch (_activeSection)
        {
            case TransfersSection:
                CreateTransfer();
                break;
            case InventorySection:
                CreateInventory();
                break;
            case WriteOffsSection:
                CreateWriteOff();
                break;
            case ReservationsSection:
                SwitchSection(StockSection);
                break;
            default:
                RefreshDocumentsItems();
                break;
        }
    }

    private void ResetStockFilters(bool clearSearch)
    {
        _suppressFilterEvents = true;
        try
        {
            if (clearSearch)
            {
                HeroSearchBox.Text = string.Empty;
                StockSearchBox.Text = string.Empty;
                DocumentsSearchBox.Text = string.Empty;
            }

            WarehouseFilterCombo.SelectedIndex = 0;
            TypeFilterCombo.SelectedIndex = 0;
            StatusFilterCombo.SelectedIndex = 0;
            ProblemsOnlyCheckBox.IsChecked = false;
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        _stockPage = 1;
        _documentsPage = 1;
        RefreshStockItems();
        RefreshDocumentsItems();
        UpdateSearchPlaceholders();
    }

    private void ApplyStockPreset(string? status = null, string? type = null, bool onlyProblems = false)
    {
        _suppressFilterEvents = true;
        try
        {
            WarehouseFilterCombo.SelectedIndex = 0;
            TypeFilterCombo.SelectedItem = string.IsNullOrWhiteSpace(type)
                ? AllTypesFilter
                : TypeFilterCombo.Items.Cast<string>().FirstOrDefault(item => EqualsUi(item, type)) ?? AllTypesFilter;
            StatusFilterCombo.SelectedItem = string.IsNullOrWhiteSpace(status)
                ? AllStatusesFilter
                : StatusFilterCombo.Items.Cast<string>().FirstOrDefault(item => EqualsUi(item, status)) ?? AllStatusesFilter;
            ProblemsOnlyCheckBox.IsChecked = onlyProblems;
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        _stockPage = 1;
        RefreshStockItems();
    }

    private void OpenStockActionsMenu(FrameworkElement placementTarget, WarehouseStockItemViewModel item)
    {
        StockDataGrid.SelectedItem = item;
        RefreshSelectedStockDetails(item);

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Открыть карточку", (_, _) => EditStockCatalogItem(item)));
        menu.Items.Add(CreateMenuItem("Печать этикетки", (_, _) => PrintLabels(new[] { item })));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Переместить", (_, _) => CreateTransferForSelectedStock(item)));
        menu.Items.Add(CreateMenuItem("Инвентаризация", (_, _) => CreateInventoryForSelectedStock(item)));
        menu.Items.Add(CreateMenuItem("Списать", (_, _) => CreateWriteOffForSelectedStock(item)));
        menu.Items.Add(CreateMenuItem("Отгрузить", (_, _) => CreateShipmentForStockItems(new[] { item })));
        menu.Items.Add(CreateMenuItem("Создать закупку", (_, _) => CreatePurchaseOrderForStockItems(new[] { item })));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Зарезервировать", (_, _) => CreateReserveForStockItems(new[] { item })));
        menu.Items.Add(CreateMenuItem("Показать резервы", (_, _) => OpenReservationsForSelectedStock(item)));
        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private void OpenReservationsForSelectedStock(WarehouseStockItemViewModel? item)
    {
        _documentsPage = 1;
        SwitchSection(ReservationsSection);

        if (item is null)
        {
            return;
        }

        DocumentsSearchBox.Text = string.IsNullOrWhiteSpace(item.Record.ItemCode)
            ? item.Item
            : $"{item.Record.ItemCode} {item.Item}";
        RefreshDocumentsItems();
    }

    private void CreateTransferForSelectedStock(WarehouseStockItemViewModel? item)
    {
        if (item is null)
        {
            CreateTransfer();
            return;
        }

        var draft = _workspace.CreateTransferDraft(item.Record.Warehouse);
        PrefillDraftWithSelectedStock(draft, item);
        OpenDocumentEditorWindow(WarehouseDocumentEditorMode.Transfer, draft, _workspace.AddTransferOrder);
    }

    private void CreateInventoryForSelectedStock(WarehouseStockItemViewModel? item)
    {
        if (item is null)
        {
            CreateInventory();
            return;
        }

        var draft = _workspace.CreateInventoryDraft(item.Record.Warehouse);
        PrefillDraftWithSelectedStock(draft, item);
        OpenDocumentEditorWindow(WarehouseDocumentEditorMode.Inventory, draft, _workspace.AddInventoryCount);
    }

    private void CreateWriteOffForSelectedStock(WarehouseStockItemViewModel? item)
    {
        if (item is null)
        {
            CreateWriteOff();
            return;
        }

        var draft = _workspace.CreateWriteOffDraft(item.Record.Warehouse);
        PrefillDraftWithSelectedStock(draft, item);
        OpenDocumentEditorWindow(WarehouseDocumentEditorMode.WriteOff, draft, _workspace.AddWriteOff);
    }

    private void CreateTransferForStockItems(IReadOnlyCollection<WarehouseStockItemViewModel> items)
    {
        CreateDocumentForStockItems(WarehouseDocumentEditorMode.Transfer, items, _workspace.CreateTransferDraft, _workspace.AddTransferOrder);
    }

    private void CreateInventoryForStockItems(IReadOnlyCollection<WarehouseStockItemViewModel> items)
    {
        CreateDocumentForStockItems(WarehouseDocumentEditorMode.Inventory, items, _workspace.CreateInventoryDraft, _workspace.AddInventoryCount);
    }

    private void CreateWriteOffForStockItems(IReadOnlyCollection<WarehouseStockItemViewModel> items)
    {
        CreateDocumentForStockItems(WarehouseDocumentEditorMode.WriteOff, items, _workspace.CreateWriteOffDraft, _workspace.AddWriteOff);
    }

    private void CreateShipmentForStockItems(IReadOnlyCollection<WarehouseStockItemViewModel> items)
    {
        var rows = items.Count == 0 ? GetCheckedOrSelectedStockItems() : items.ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите позиции для отгрузки.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_salesWorkspace.Customers.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Для создания отгрузки нужен хотя бы один клиент.", "Склад", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new WarehouseShipmentDraftWindow(_salesWorkspace, rows.Select(BuildShipmentDraftLine))
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || dialog.ResultCustomer is null || dialog.ResultLines.Count == 0)
        {
            return;
        }

        var order = _salesWorkspace.CreateOrderDraft(dialog.ResultCustomer.Id);
        order.Warehouse = dialog.ResultLines.Select(item => Ui(item.Warehouse))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? _salesWorkspace.Warehouses.Select(Ui).FirstOrDefault()
            ?? "Главный склад";
        order.Status = _salesWorkspace.OrderStatuses.FirstOrDefault(status => EqualsUi(status, "На выполнении"))
            ?? _salesWorkspace.OrderStatuses.FirstOrDefault()
            ?? order.Status;
        order.Comment = $"Создано со склада для отгрузки: {string.Join(", ", dialog.ResultLines.Select(item => item.Name).Take(3))}.";
        order.Lines.Clear();

        foreach (var item in dialog.ResultLines)
        {
            order.Lines.Add(BuildSalesLine(item));
        }

        _salesWorkspace.AddOrder(order);

        var shipment = _salesWorkspace.CreateShipmentDraftFromOrder(order.Id);
        shipment.Status = _salesWorkspace.ShipmentStatuses.FirstOrDefault(status => EqualsUi(status, "К сборке"))
            ?? shipment.Status;
        shipment.Comment = $"Создано со склада по заказу {order.Number}.";
        _salesWorkspace.AddShipment(shipment);
        TryPersistSalesWorkspace();

        NavigationRequested?.Invoke(this, "shipments");
        ClearCheckedStockItems();
        MessageBox.Show(
            Window.GetWindow(this),
            $"Созданы заказ {order.Number} и отгрузка {shipment.Number}.",
            "Склад",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CreateReserveForStockItems(IReadOnlyCollection<WarehouseStockItemViewModel> items)
    {
        var rows = items.Count == 0 ? GetCheckedOrSelectedStockItems() : items.ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите позиции для резервирования.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_salesWorkspace.Customers.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Для резервирования нужен хотя бы один клиент.", "Склад", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new WarehouseShipmentDraftWindow(
            _salesWorkspace,
            rows.Select(BuildShipmentDraftLine),
            "Резервирование товара",
            "Выберите клиента и количество, которое нужно поставить в резерв.",
            "Создать резерв")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || dialog.ResultCustomer is null || dialog.ResultLines.Count == 0)
        {
            return;
        }

        var order = _salesWorkspace.CreateOrderDraft(dialog.ResultCustomer.Id);
        order.Warehouse = dialog.ResultLines.Select(item => Ui(item.Warehouse))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? _salesWorkspace.Warehouses.Select(Ui).FirstOrDefault()
            ?? "Главный склад";
        order.Comment = $"Резерв создан со склада: {string.Join(", ", dialog.ResultLines.Select(item => item.Name).Take(3))}.";
        order.Lines.Clear();

        foreach (var item in dialog.ResultLines)
        {
            order.Lines.Add(BuildSalesLine(item));
        }

        _salesWorkspace.AddOrder(order);
        var result = _salesWorkspace.ReserveOrder(order.Id);
        TryPersistSalesWorkspace();

        NavigationRequested?.Invoke(this, "sales");
        ClearCheckedStockItems();
        MessageBox.Show(
            Window.GetWindow(this),
            $"{result.Message}\n{result.Detail}\n\nЗаказ: {order.Number}",
            "Склад",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private WarehouseShipmentDraftLine BuildShipmentDraftLine(WarehouseStockItemViewModel item)
    {
        var catalogItem = _salesWorkspace.CatalogItems
            .FirstOrDefault(entry => EqualsUi(entry.Code, item.Record.ItemCode) || EqualsUi(entry.Name, item.Record.ItemName));
        var quantity = item.Record.FreeQuantity > 0m ? Math.Min(item.Record.FreeQuantity, 1m) : 0m;

        return new WarehouseShipmentDraftLine
        {
            Code = string.IsNullOrWhiteSpace(item.Record.ItemCode) ? item.Code : item.Record.ItemCode,
            Name = string.IsNullOrWhiteSpace(item.Record.ItemName) ? item.Item : item.Record.ItemName,
            Warehouse = item.Record.Warehouse,
            Unit = string.IsNullOrWhiteSpace(item.Record.Unit) ? (catalogItem?.Unit ?? "шт") : item.Record.Unit,
            AvailableQuantity = item.Record.FreeQuantity,
            Quantity = quantity,
            Price = catalogItem?.DefaultPrice ?? 0m
        };
    }

    private static SalesOrderLineRecord BuildSalesLine(WarehouseShipmentDraftLine item)
    {
        return new SalesOrderLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = item.Code,
            ItemName = item.Name,
            Unit = item.Unit,
            Quantity = item.Quantity,
            Price = item.Price
        };
    }

    private void CreateDocumentForStockItems(
        WarehouseDocumentEditorMode mode,
        IReadOnlyCollection<WarehouseStockItemViewModel> items,
        Func<string, OperationalWarehouseDocumentRecord> createDraft,
        Action<OperationalWarehouseDocumentRecord> persist)
    {
        var rows = items.Count == 0 ? GetCheckedOrSelectedStockItems() : items.ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите позиции склада.", "Склад", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var draft = createDraft(rows[0].Record.Warehouse);
        draft.Comment = $"Создано из склада: {string.Join(", ", rows.Select(item => item.Item).Take(3))}.";
        draft.Lines.Clear();

        foreach (var item in rows)
        {
            draft.Lines.Add(BuildWarehouseLine(draft, item));
        }

        OpenDocumentEditorWindow(mode, draft, persist);
    }

    private static OperationalWarehouseLineRecord BuildWarehouseLine(OperationalWarehouseDocumentRecord draft, WarehouseStockItemViewModel item)
    {
        return new OperationalWarehouseLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = item.Record.ItemCode,
            ItemName = item.Record.ItemName,
            Quantity = 1m,
            Unit = string.IsNullOrWhiteSpace(item.Record.Unit) ? "шт" : item.Record.Unit,
            SourceLocation = item.Record.Warehouse,
            TargetLocation = draft.TargetWarehouse,
            RelatedDocument = draft.RelatedDocument
        };
    }

    private void PrefillDraftWithSelectedStock(OperationalWarehouseDocumentRecord draft, WarehouseStockItemViewModel item)
    {
        draft.RelatedDocument = string.IsNullOrWhiteSpace(draft.RelatedDocument) ? item.Code : draft.RelatedDocument;
        draft.Comment = string.IsNullOrWhiteSpace(draft.Comment)
            ? $"Создано по позиции {item.Item}"
            : draft.Comment;

        if (draft.Lines.Count > 0)
        {
            return;
        }

        draft.Lines.Add(BuildWarehouseLine(draft, item));
    }

    private void OpenLinkedDocument(WarehouseLinkItemViewModel item)
    {
        if (!item.CanOpen || string.IsNullOrWhiteSpace(item.TargetSection))
        {
            return;
        }

        _documentsPage = 1;
        SwitchSection(item.TargetSection);
        DocumentsSearchBox.Text = string.IsNullOrWhiteSpace(item.DocumentNumber)
            ? item.Caption
            : item.DocumentNumber;
        RefreshDocumentsItems();

        var match = DocumentsDataGrid.Items
            .Cast<WarehouseDocumentItemViewModel>()
            .FirstOrDefault(document => string.Equals(document.Number, Ui(item.DocumentNumber), StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            DocumentsDataGrid.SelectedItem = match;
            DocumentsDataGrid.ScrollIntoView(match);
        }
    }

    private void OpenDocumentEditor(WarehouseDocumentItemViewModel item)
    {
        if (!item.IsEditable || item.DocumentId is null)
        {
            return;
        }

        if (string.Equals(item.SectionKey, ExpenseInvoicesSection, StringComparison.OrdinalIgnoreCase))
        {
            OpenSalesShipmentEditor(item.DocumentId.Value);
            return;
        }

        var document = item.SectionKey switch
        {
            TransfersSection => _workspace.TransferOrders.FirstOrDefault(entry => entry.Id == item.DocumentId.Value),
            InventorySection => _workspace.InventoryCounts.FirstOrDefault(entry => entry.Id == item.DocumentId.Value),
            WriteOffsSection => _workspace.WriteOffs.FirstOrDefault(entry => entry.Id == item.DocumentId.Value),
            _ => null
        };
        if (document is null)
        {
            MessageBox.Show(Window.GetWindow(this)!, "Документ не найден в локальном контуре склада.", "Склад", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = item.SectionKey switch
        {
            TransfersSection => WarehouseDocumentEditorMode.Transfer,
            InventorySection => WarehouseDocumentEditorMode.Inventory,
            WriteOffsSection => WarehouseDocumentEditorMode.WriteOff,
            _ => WarehouseDocumentEditorMode.Transfer
        };

        Action<OperationalWarehouseDocumentRecord> persist = item.SectionKey switch
        {
            TransfersSection => _workspace.UpdateTransferOrder,
            InventorySection => _workspace.UpdateInventoryCount,
            WriteOffsSection => _workspace.UpdateWriteOff,
            _ => _workspace.UpdateTransferOrder
        };

        OpenDocumentEditorWindow(mode, document, persist);
    }

    private void OpenSalesShipmentEditor(Guid shipmentId)
    {
        var shipment = _salesWorkspace.Shipments.FirstOrDefault(entry => entry.Id == shipmentId);
        if (shipment is null)
        {
            MessageBox.Show(Window.GetWindow(this)!, "Расходная накладная не найдена в продажах.", "Склад", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SalesDocumentEditorWindow(_salesWorkspace, shipment)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultShipment is null)
        {
            return;
        }

        _salesWorkspace.UpdateShipment(dialog.ResultShipment);
        TryPersistSalesWorkspace();
        RefreshDocumentsItems();
        RefreshCellStorageItems();
    }

    private void OpenDocumentEditorWindow(
        WarehouseDocumentEditorMode mode,
        OperationalWarehouseDocumentRecord document,
        Action<OperationalWarehouseDocumentRecord> persist)
    {
        var dialog = new WarehouseDocumentEditorWindow(_workspace, mode, document)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        persist(dialog.ResultDocument);
        PersistAndRefresh();
    }

    private string GetCurrentOperator()
    {
        return string.IsNullOrWhiteSpace(_salesWorkspace.CurrentOperator)
            ? Environment.UserName
            : _salesWorkspace.CurrentOperator;
    }

    private void ShowTransientWarning(string message)
    {
        MessageBox.Show(
            Window.GetWindow(this),
            Ui(message),
            "Склад",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void TryPersistWorkspace()
    {
        try
        {
            _store.Save(_workspace);
        }
        catch (Exception exception)
        {
            // Release 1.0.123: popup убран — спамил пользователя при каждой
            // неудачной авто-попытке save (срабатывало при загрузке вкладки).
            try { System.Diagnostics.Debug.WriteLine($"[TryPersistWorkspace] warehouse save failed: {exception}"); } catch { }
        }
    }

    private async void TryPersistSalesWorkspace()
    {
        var store = SalesWorkspaceStore.CreateDefault();
        try
        {
            var snapshot = SalesWorkspaceSnapshot.FromWorkspace(_salesWorkspace);
            var currentOperator = _salesWorkspace.CurrentOperator;
            await Task.Run(() => store.SaveSnapshot(snapshot, currentOperator));
        }
        catch (Exception exception)
        {
            // Release 1.0.123: popup убран — то же что для warehouse.
            try { System.Diagnostics.Debug.WriteLine($"[TryPersistSalesWorkspace] sales save failed: {exception}"); } catch { }
        }
    }

    private sealed class WarehouseStockItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        private WarehouseStockItemViewModel(
            WarehouseStockBalanceRecord record,
            string code,
            string item,
            string warehouse,
            string freeDisplay,
            string reservedDisplay,
            string inTransitDisplay,
            string minimumDisplay,
            string balanceText,
            double freeBarWidth,
            double reservedBarWidth,
            double inTransitBarWidth,
            string status,
            string updatedDisplay,
            WpfBrush statusBackground,
            WpfBrush statusForeground)
        {
            Record = record;
            Code = code;
            Item = item;
            Warehouse = warehouse;
            FreeDisplay = freeDisplay;
            ReservedDisplay = reservedDisplay;
            InTransitDisplay = inTransitDisplay;
            MinimumDisplay = minimumDisplay;
            BalanceText = balanceText;
            FreeBarWidth = freeBarWidth;
            ReservedBarWidth = reservedBarWidth;
            InTransitBarWidth = inTransitBarWidth;
            Status = status;
            UpdatedDisplay = updatedDisplay;
            StatusBackground = statusBackground;
            StatusForeground = statusForeground;
            SelectionKey = $"{code}|{warehouse}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public WarehouseStockBalanceRecord Record { get; }

        public string Code { get; }

        public string Item { get; }

        public string Warehouse { get; }

        public string FreeDisplay { get; }

        public string ReservedDisplay { get; }

        public string InTransitDisplay { get; }

        public string MinimumDisplay { get; }

        public string BalanceText { get; }

        public double FreeBarWidth { get; }

        public double ReservedBarWidth { get; }

        public double InTransitBarWidth { get; }

        public string Status { get; }

        public string UpdatedDisplay { get; }

        public WpfBrush StatusBackground { get; }

        public WpfBrush StatusForeground { get; }

        public string SelectionKey { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public static WarehouseStockItemViewModel Create(WarehouseStockBalanceRecord record)
        {
            var total = Math.Max(1m, record.FreeQuantity + record.ReservedQuantity + record.ShippedQuantity);
            const double totalWidth = 120d;

            double Scale(decimal value)
            {
                if (value <= 0m)
                {
                    return 0d;
                }

                return Math.Round((double)(value / total) * totalWidth, 1);
            }

            var palette = ResolveStatusPalette(record.Status);

            return new WarehouseStockItemViewModel(
                record,
                string.IsNullOrWhiteSpace(record.ItemCode) ? "—" : Ui(record.ItemCode),
                string.IsNullOrWhiteSpace(record.ItemName) ? "Без названия" : Ui(record.ItemName),
                string.IsNullOrWhiteSpace(record.Warehouse) ? "Главный склад" : Ui(record.Warehouse),
                record.FreeQuantity.ToString("N0", RuCulture),
                record.ReservedQuantity.ToString("N0", RuCulture),
                record.ShippedQuantity.ToString("N0", RuCulture),
                ResolveMinimumStock(record).ToString("N0", RuCulture),
                $"{record.FreeQuantity:N0} / {record.ReservedQuantity:N0} / {record.ShippedQuantity:N0}",
                Scale(record.FreeQuantity),
                Scale(record.ReservedQuantity),
                Scale(record.ShippedQuantity),
                Ui(record.Status),
                DateTime.Now.ToString("dd.MM.yyyy HH:mm", RuCulture),
                palette.Back,
                palette.Fore);
        }
    }

    private sealed class WarehouseDocumentItemViewModel
    {
        private WarehouseDocumentItemViewModel(
            string sectionKey,
            string number,
            string dateText,
            string route,
            string status,
            string relatedDocument,
            int positions,
            DateTime sortDate,
            WpfBrush statusBackground,
            WpfBrush statusForeground,
            string searchText,
            Guid? documentId,
            bool isEditable)
        {
            SectionKey = sectionKey;
            Number = string.IsNullOrWhiteSpace(number) ? "—" : Ui(number);
            DateText = dateText;
            Route = Ui(route);
            Status = string.IsNullOrWhiteSpace(status) ? "Черновик" : Ui(status);
            RelatedDocument = string.IsNullOrWhiteSpace(relatedDocument) ? "—" : Ui(relatedDocument);
            Positions = positions;
            SortDate = sortDate;
            StatusBackground = statusBackground;
            StatusForeground = statusForeground;
            SearchText = Ui(searchText);
            DocumentId = documentId;
            IsEditable = isEditable;
        }

        public string SectionKey { get; }

        public string Number { get; }

        public string DateText { get; }

        public string Route { get; }

        public string Status { get; }

        public string RelatedDocument { get; }

        public int Positions { get; }

        public DateTime SortDate { get; }

        public WpfBrush StatusBackground { get; }

        public WpfBrush StatusForeground { get; }

        public string SearchText { get; }

        public Guid? DocumentId { get; }

        public bool IsEditable { get; }

        public static WarehouseDocumentItemViewModel Create(
            string sectionKey,
            string number,
            DateTime date,
            string route,
            string status,
            string relatedDocument,
            int positions,
            string searchText,
            Guid? documentId = null,
            bool isEditable = false)
        {
            var palette = ResolveStatusPalette(status);
            return new WarehouseDocumentItemViewModel(
                sectionKey,
                number,
                date == DateTime.MinValue ? "—" : date.ToString("dd.MM.yyyy", RuCulture),
                route,
                status,
                relatedDocument,
                positions,
                date,
                palette.Back,
                palette.Fore,
                searchText,
                documentId,
                isEditable);
        }
    }

    private sealed class WarehouseCellShipmentViewModel
    {
        private WarehouseCellShipmentViewModel(
            WarehouseTodayShipmentRecord record,
            string number,
            string dateText,
            string salesOrderNumber,
            string customer,
            string warehouse,
            string status,
            string readiness,
            string requiredDisplay,
            string shortageDisplay,
            WpfBrush statusBackground,
            WpfBrush statusForeground)
        {
            Record = record;
            ShipmentId = record.ShipmentId;
            Number = number;
            DateText = dateText;
            SalesOrderNumber = salesOrderNumber;
            Customer = customer;
            Warehouse = warehouse;
            Status = status;
            Readiness = readiness;
            RequiredDisplay = requiredDisplay;
            ShortageDisplay = shortageDisplay;
            StatusBackground = statusBackground;
            StatusForeground = statusForeground;
        }

        public WarehouseTodayShipmentRecord Record { get; }

        public Guid ShipmentId { get; }

        public string Number { get; }

        public string DateText { get; }

        public string SalesOrderNumber { get; }

        public string Customer { get; }

        public string Warehouse { get; }

        public string Status { get; }

        public string Readiness { get; }

        public string RequiredDisplay { get; }

        public string ShortageDisplay { get; }

        public WpfBrush StatusBackground { get; }

        public WpfBrush StatusForeground { get; }

        public static WarehouseCellShipmentViewModel Create(WarehouseTodayShipmentRecord record)
        {
            var palette = ResolveStatusPalette(record.Readiness);
            return new WarehouseCellShipmentViewModel(
                record,
                string.IsNullOrWhiteSpace(record.ShipmentNumber) ? "—" : Ui(record.ShipmentNumber),
                record.ShipmentDate == DateTime.MinValue ? "—" : record.ShipmentDate.ToString("dd.MM.yyyy", RuCulture),
                string.IsNullOrWhiteSpace(record.SalesOrderNumber) ? "—" : Ui(record.SalesOrderNumber),
                string.IsNullOrWhiteSpace(record.CustomerName) ? "Клиент не указан" : Ui(record.CustomerName),
                string.IsNullOrWhiteSpace(record.Warehouse) ? "Склад не указан" : Ui(record.Warehouse),
                string.IsNullOrWhiteSpace(record.Status) ? "Черновик" : Ui(record.Status),
                Ui(record.Readiness),
                record.RequiredQuantity.ToString("N0", RuCulture),
                record.ShortageQuantity <= 0m ? "0" : record.ShortageQuantity.ToString("N0", RuCulture),
                palette.Back,
                palette.Fore);
        }
    }

    private sealed class WarehouseCellPickLineViewModel
    {
        private WarehouseCellPickLineViewModel(
            WarehouseShipmentPickLineRecord record,
            string code,
            string item,
            string requiredDisplay,
            string availableDisplay,
            string shortageDisplay,
            string cellDisplay,
            string status,
            WpfBrush statusBackground,
            WpfBrush statusForeground)
        {
            Record = record;
            Code = code;
            Item = item;
            RequiredDisplay = requiredDisplay;
            AvailableDisplay = availableDisplay;
            ShortageDisplay = shortageDisplay;
            CellDisplay = cellDisplay;
            Status = status;
            StatusBackground = statusBackground;
            StatusForeground = statusForeground;
        }

        public WarehouseShipmentPickLineRecord Record { get; }

        public string Code { get; }

        public string Item { get; }

        public string RequiredDisplay { get; }

        public string AvailableDisplay { get; }

        public string ShortageDisplay { get; }

        public string CellDisplay { get; }

        public string Status { get; }

        public WpfBrush StatusBackground { get; }

        public WpfBrush StatusForeground { get; }

        public static WarehouseCellPickLineViewModel Create(WarehouseShipmentPickLineRecord record)
        {
            var palette = ResolveStatusPalette(record.PickStatus);
            var unit = string.IsNullOrWhiteSpace(record.Unit) ? "шт" : Ui(record.Unit);
            return new WarehouseCellPickLineViewModel(
                record,
                string.IsNullOrWhiteSpace(record.ItemCode) ? "—" : Ui(record.ItemCode),
                string.IsNullOrWhiteSpace(record.ItemName) ? "Без названия" : Ui(record.ItemName),
                $"{record.RequiredQuantity:N0} {unit}",
                $"{record.AvailableQuantity:N0} {unit}",
                record.ShortageQuantity <= 0m ? "0" : $"{record.ShortageQuantity:N0} {unit}",
                Ui(record.CellSummary),
                Ui(record.PickStatus),
                palette.Back,
                palette.Fore);
        }
    }

    private sealed class WarehouseCellBalanceViewModel
    {
        private WarehouseCellBalanceViewModel(
            WarehouseCellBalanceRecord record,
            string cell,
            string warehouse,
            string code,
            string item,
            string quantityDisplay,
            string source,
            string status,
            WpfBrush statusBackground,
            WpfBrush statusForeground)
        {
            Record = record;
            Cell = cell;
            Warehouse = warehouse;
            Code = code;
            Item = item;
            QuantityDisplay = quantityDisplay;
            Source = source;
            Status = status;
            StatusBackground = statusBackground;
            StatusForeground = statusForeground;
        }

        public WarehouseCellBalanceRecord Record { get; }

        public string Cell { get; }

        public string Warehouse { get; }

        public string Code { get; }

        public string Item { get; }

        public string QuantityDisplay { get; }

        public string Source { get; }

        public string Status { get; }

        public WpfBrush StatusBackground { get; }

        public WpfBrush StatusForeground { get; }

        public static WarehouseCellBalanceViewModel Create(WarehouseCellBalanceRecord record)
        {
            var status = record.IsAddressed ? "Адресовано" : "Без ячейки";
            var palette = ResolveStatusPalette(status);
            var unit = string.IsNullOrWhiteSpace(record.Unit) ? "шт" : Ui(record.Unit);
            return new WarehouseCellBalanceViewModel(
                record,
                string.IsNullOrWhiteSpace(record.Cell) ? WarehouseCellStorageOperations.UnassignedCellName : Ui(record.Cell),
                string.IsNullOrWhiteSpace(record.Warehouse) ? "Склад не указан" : Ui(record.Warehouse),
                string.IsNullOrWhiteSpace(record.ItemCode) ? "—" : Ui(record.ItemCode),
                string.IsNullOrWhiteSpace(record.ItemName) ? "Без названия" : Ui(record.ItemName),
                $"{record.Quantity:N0} {unit}",
                string.IsNullOrWhiteSpace(record.SourceLabel) ? "—" : Ui(record.SourceLabel),
                status,
                palette.Back,
                palette.Fore);
        }
    }

    private sealed class WarehouseCellHistoryViewModel
    {
        private WarehouseCellHistoryViewModel(
            string dateText,
            string operation,
            string documentNumber,
            string item,
            string quantityDisplay)
        {
            DateText = dateText;
            Operation = operation;
            DocumentNumber = documentNumber;
            Item = item;
            QuantityDisplay = quantityDisplay;
        }

        public string DateText { get; }

        public string Operation { get; }

        public string DocumentNumber { get; }

        public string Item { get; }

        public string QuantityDisplay { get; }

        public static WarehouseCellHistoryViewModel Create(StorageCellHistoryRecord record)
        {
            var sign = record.Quantity > 0m ? "+" : string.Empty;
            var unit = string.IsNullOrWhiteSpace(record.Unit) ? "шт" : Ui(record.Unit);
            return new WarehouseCellHistoryViewModel(
                record.Date == DateTime.MinValue ? "—" : record.Date.ToString("dd.MM.yy", RuCulture),
                Ui(record.Operation),
                string.IsNullOrWhiteSpace(record.DocumentNumber) ? "—" : Ui(record.DocumentNumber),
                string.IsNullOrWhiteSpace(record.ItemName) ? Ui(record.ItemCode) : Ui(record.ItemName),
                $"{sign}{record.Quantity:N0} {unit}");
        }
    }

    private sealed record StorageCellHistoryRecord(
        DateTime Date,
        string Operation,
        string DocumentNumber,
        string ItemCode,
        string ItemName,
        string Unit,
        decimal Quantity,
        string Comment);

    private sealed class WarehouseCellPlacementRuleViewModel
    {
        private WarehouseCellPlacementRuleViewModel(
            WarehouseCellPlacementRuleRecord record,
            string subject,
            string warehouse,
            string primaryCell,
            string reserveCell,
            string zonePriority,
            string mixingRule,
            string status)
        {
            Record = record;
            Subject = subject;
            Warehouse = warehouse;
            PrimaryCell = primaryCell;
            ReserveCell = reserveCell;
            ZonePriority = zonePriority;
            MixingRule = mixingRule;
            Status = status;
        }

        public WarehouseCellPlacementRuleRecord Record { get; }

        public string Subject { get; }

        public string Warehouse { get; }

        public string PrimaryCell { get; }

        public string ReserveCell { get; }

        public string ZonePriority { get; }

        public string MixingRule { get; }

        public string Status { get; }

        public static WarehouseCellPlacementRuleViewModel Create(WarehouseCellPlacementRuleRecord record)
        {
            var subject = !string.IsNullOrWhiteSpace(record.ItemCode) || !string.IsNullOrWhiteSpace(record.ItemName)
                ? $"{FirstNonEmpty(record.ItemName, record.ItemCode)} [{Ui(record.ItemCode)}]"
                : $"Категория: {Ui(record.Category)}";

            return new WarehouseCellPlacementRuleViewModel(
                record,
                Ui(subject),
                Ui(record.Warehouse),
                string.IsNullOrWhiteSpace(record.PrimaryCellCode) ? "—" : Ui(record.PrimaryCellCode),
                string.IsNullOrWhiteSpace(record.ReserveCellCode) ? "—" : Ui(record.ReserveCellCode),
                string.IsNullOrWhiteSpace(record.ZonePriority) ? "—" : Ui(record.ZonePriority),
                record.ForbidMixedCategories ? "Запрещено" : "Разрешено",
                record.IsActive ? "Активно" : "Отключено");
        }
    }

    private sealed class WarehouseCellIssueViewModel
    {
        private WarehouseCellIssueViewModel(
            string dateText,
            string severity,
            string operation,
            string cell,
            string item,
            string message)
        {
            DateText = dateText;
            Severity = severity;
            Operation = operation;
            Cell = cell;
            Item = item;
            Message = message;
        }

        public string DateText { get; }

        public string Severity { get; }

        public string Operation { get; }

        public string Cell { get; }

        public string Item { get; }

        public string Message { get; }

        public static WarehouseCellIssueViewModel Create(WarehouseCellIntegrityIssueRecord record)
        {
            return new WarehouseCellIssueViewModel(
                record.CreatedAt == DateTime.MinValue ? "—" : record.CreatedAt.ToString("dd.MM HH:mm", RuCulture),
                Ui(record.Severity),
                Ui(record.Operation),
                string.IsNullOrWhiteSpace(record.CellCode) ? "—" : Ui(record.CellCode),
                string.IsNullOrWhiteSpace(record.ItemName) ? Ui(record.ItemCode) : Ui(record.ItemName),
                Ui(record.Message));
        }
    }

    private sealed class CellAllocationBucket
    {
        public CellAllocationBucket(WarehouseCellBalanceRecord record)
        {
            Record = record;
            RemainingQuantity = record.Quantity;
        }

        public WarehouseCellBalanceRecord Record { get; }

        public decimal RemainingQuantity { get; set; }
    }

    private sealed class CellPickAllocationResult
    {
        public List<OperationalWarehouseLineRecord> Lines { get; } = [];

        public List<string> Errors { get; } = [];
    }

    private sealed class WarehouseStorageCellViewModel
    {
        private WarehouseStorageCellViewModel(
            WarehouseStorageCellRecord record,
            string code,
            string warehouse,
            string zone,
            string cellType,
            string capacityDisplay,
            string status,
            string qrState,
            string comment)
        {
            Record = record;
            Code = code;
            Warehouse = warehouse;
            Zone = zone;
            CellType = cellType;
            CapacityDisplay = capacityDisplay;
            Status = status;
            QrState = qrState;
            Comment = comment;
        }

        public WarehouseStorageCellRecord Record { get; }

        public bool IsActive => Record.IsActive;

        public string Code { get; }

        public string Warehouse { get; }

        public string Zone { get; }

        public string CellType { get; }

        public string CapacityDisplay { get; }

        public string Status { get; }

        public string QrState { get; }

        public string Comment { get; }

        public static WarehouseStorageCellViewModel Create(WarehouseStorageCellRecord record)
        {
            return new WarehouseStorageCellViewModel(
                record,
                string.IsNullOrWhiteSpace(record.Code) ? "—" : Ui(record.Code),
                string.IsNullOrWhiteSpace(record.Warehouse) ? "Склад не указан" : Ui(record.Warehouse),
                string.IsNullOrWhiteSpace(record.ZoneName) ? Ui(record.ZoneCode) : Ui(record.ZoneName),
                string.IsNullOrWhiteSpace(record.CellType) ? "Не задан" : Ui(record.CellType),
                record.Capacity <= 0m ? "—" : record.Capacity.ToString("N0", RuCulture),
                string.IsNullOrWhiteSpace(record.Status) ? "Активна" : Ui(record.Status),
                string.IsNullOrWhiteSpace(record.QrPayload) ? "Не подготовлен" : "Подготовлен",
                string.IsNullOrWhiteSpace(record.Comment) ? "—" : Ui(record.Comment));
        }
    }

    private sealed class WarehouseMovementItemViewModel
    {
        public WarehouseMovementItemViewModel(string caption, string subtitle, string delta, WpfBrush deltaBrush, DateTime occurredAt)
        {
            Caption = caption;
            Subtitle = subtitle;
            Delta = delta;
            DeltaBrush = deltaBrush;
            OccurredAt = occurredAt;
        }

        public string Caption { get; }

        public string Subtitle { get; }

        public string Delta { get; }

        public WpfBrush DeltaBrush { get; }

        public DateTime OccurredAt { get; }
    }

    private sealed class WarehouseLinkItemViewModel
    {
        public WarehouseLinkItemViewModel(
            string caption,
            string subtitle,
            string? targetSection = null,
            string? documentNumber = null,
            bool canOpen = false)
        {
            Caption = caption;
            Subtitle = subtitle;
            TargetSection = targetSection;
            DocumentNumber = documentNumber;
            CanOpen = canOpen;
        }

        public string Caption { get; }

        public string Subtitle { get; }

        public string? TargetSection { get; }

        public string? DocumentNumber { get; }

        public bool CanOpen { get; }
    }
}

