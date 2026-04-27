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
    private const string StockSection = "stock";
    private const string TransfersSection = "transfers";
    private const string ReservationsSection = "reservations";
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
    private readonly OperationalWarehouseWorkspace _workspace;

    private WarehouseWorkspace _runtimeView;
    private string _activeSection = StockSection;
    private bool _syncingSearch;
    private bool _suppressFilterEvents;
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
        _workspace = _store.LoadOrCreate(
            string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator) ? Environment.UserName : salesWorkspace.CurrentOperator,
            salesWorkspace);
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
        Dispatcher.BeginInvoke(RefreshAll, System.Windows.Threading.DispatcherPriority.Background);
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

        RefreshMeta();
        RefreshMetrics();
        RefreshWarehouseFilters();
        RefreshStockItems();
        RefreshDocumentsItems();
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
                !EqualsUi(item.Status, "Проведена")))
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
            InventorySection => "Инвентаризация",
            WriteOffsSection => "Списания",
            _ => "Документы склада"
        };

        DocumentsSectionSubtitleText.Text = _activeSection switch
        {
            TransfersSection => "Маршруты между складами и текущий статус выполнения.",
            ReservationsSection => "Документы резерва под продажи и отгрузку.",
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
            _ => "Обновить"
        };
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

    private void SwitchSection(string section)
    {
        _activeSection = section;
        var isStockSection = string.Equals(section, StockSection, StringComparison.OrdinalIgnoreCase);
        StockTabContent.Visibility = isStockSection ? Visibility.Visible : Visibility.Collapsed;
        DocumentsTabContent.Visibility = isStockSection ? Visibility.Collapsed : Visibility.Visible;

        UpdateSectionButtons();

        if (!isStockSection)
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

    private static FrameworkElement CreatePagerLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Margin = new Thickness(6, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TextSecondaryBrush,
            FontSize = 13
        };
    }

    private static WpfButton CreatePagerButton(string text, int? targetPage, bool active, Action<int> setPage)
    {
        var button = new WpfButton
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = active ? PrimarySoftBrush : WpfBrushes.Transparent,
            Background = active ? PrimarySoftBrush : WpfBrushes.Transparent,
            Foreground = active ? PrimaryBrush : TextSecondaryBrush,
            FontSize = 13,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            Content = text,
            Cursor = targetPage.HasValue ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            IsEnabled = targetPage.HasValue
        };

        if (targetPage.HasValue)
        {
            button.Click += (_, _) => setPage(targetPage.Value);
        }
        else
        {
            button.Opacity = 0.45;
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

        if (ContainsUi(status, "контрол")
            || ContainsUi(status, "резерв"))
        {
            return (BrushFromHex("#FFF8ED"), WarningBrush);
        }

        if (ContainsUi(status, "норм")
            || ContainsUi(status, "провед")
            || ContainsUi(status, "списан")
            || ContainsUi(status, "перемещ"))
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

        var directory = Path.Combine(AppContext.BaseDirectory, "print");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"warehouse-labels-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        File.WriteAllText(path, BuildLabelsHtml(rows), Encoding.UTF8);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string BuildLabelsHtml(IEnumerable<WarehouseStockItemViewModel> items)
    {
        var generatedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm", RuCulture);
        var cards = items.Select(item =>
        {
            var barcode = ResolvePseudoBarcode(item.Record);
            var payload = string.Join(
                Environment.NewLine,
                $"Код: {item.Code}",
                $"Товар: {item.Item}",
                $"Склад: {item.Warehouse}",
                $"Остаток: {item.BalanceText}",
                $"Статус: {item.Status}",
                $"Маркер: {barcode}");

            return new LabelPrintHtmlBuilder.LabelCard(
                "Этикетка склада",
                item.Item,
                item.Status,
                new (string Label, string Value)[]
                {
                    ("Код", item.Code),
                    ("Склад", item.Warehouse),
                    ("Остаток", item.BalanceText),
                    ("Мин. остаток", item.MinimumDisplay),
                    ("Обновлено", item.UpdatedDisplay),
                    ("Статус", item.Status)
                },
                barcode,
                payload,
                string.IsNullOrWhiteSpace(item.Record.SourceLabel) ? "Источник: склад" : $"Источник: {item.Record.SourceLabel}",
                $"Сформировано: {generatedAt}");
        });

        return LabelPrintHtmlBuilder.Build("Этикетки склада", cards);
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
        order.Status = _salesWorkspace.OrderStatuses.FirstOrDefault(status => EqualsUi(status, "Готов к отгрузке"))
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

    private void TryPersistWorkspace()
    {
        try
        {
            _store.Save(_workspace);
        }
        catch
        {
        }
    }

    private void TryPersistSalesWorkspace()
    {
        try
        {
            SalesWorkspaceStore.CreateDefault().Save(_salesWorkspace);
        }
        catch
        {
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

