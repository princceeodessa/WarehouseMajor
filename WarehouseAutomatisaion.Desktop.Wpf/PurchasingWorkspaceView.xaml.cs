using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Printing;
using WarehouseAutomatisaion.Desktop.Text;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView : WpfUserControl, IDisposable
{
    private const string OrdersSection = "orders";
    private const string SuppliersSection = "suppliers";
    private const string InvoicesSection = "invoices";
    private const string ReceiptsSection = "receipts";
    private const string PaymentsSection = "payments";
    private const string DiscrepanciesSection = "discrepancies";
    private const string JournalSection = "journal";

    private const string AllStatusesFilter = "Все статусы";
    private const string AllSuppliersFilter = "Все поставщики";
    private const string AllWarehousesFilter = "Все склады";
    private const string AllDocumentTypesFilter = "Все типы";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly SolidColorBrush PrimaryBrush = BrushFromHex("#4F5BFF");
    private static readonly SolidColorBrush PrimarySoftBrush = BrushFromHex("#EEF2FF");
    private static readonly SolidColorBrush SuccessBrush = BrushFromHex("#26A85B");
    private static readonly SolidColorBrush SuccessSoftBrush = BrushFromHex("#EAF8F0");
    private static readonly SolidColorBrush WarningBrush = BrushFromHex("#FF9F1A");
    private static readonly SolidColorBrush WarningSoftBrush = BrushFromHex("#FFF4E4");
    private static readonly SolidColorBrush DangerBrush = BrushFromHex("#FF5B5B");
    private static readonly SolidColorBrush DangerSoftBrush = BrushFromHex("#FFF1F1");
    private static readonly SolidColorBrush NeutralBrush = BrushFromHex("#6E7B98");
    private static readonly SolidColorBrush NeutralSoftBrush = BrushFromHex("#F3F6FB");
    private static readonly SolidColorBrush PurpleBrush = BrushFromHex("#8A63F6");
    private static readonly SolidColorBrush PurpleSoftBrush = BrushFromHex("#F2EEFF");

    private readonly SalesWorkspace _salesWorkspace;
    private readonly PurchasingOperationalWorkspaceStore _store;
    private readonly OperationalPurchasingWorkspace _workspace;
    private readonly ObservableCollection<PurchasingGridRow> _rows = new();
    private readonly ObservableCollection<PurchasingDetailLineRow> _detailLines = new();
    private readonly HashSet<string> _checkedKeys = new(StringComparer.OrdinalIgnoreCase);

    private PurchasingGridRow[] _allRows = Array.Empty<PurchasingGridRow>();
    private PurchasingGridRow[] _filteredRows = Array.Empty<PurchasingGridRow>();
    private string _activeSection = OrdersSection;
    private string? _selectedRowKey;
    private string? _dismissedLockKey;
    private bool _syncingSearch;
    private bool _suppressFilters;
    private bool _initialized;
    private bool _dateRangeInitialized;
    private int _page = 1;
    private int _pageSize = 20;
    private DateTime? _defaultDateFrom;
    private DateTime? _defaultDateTo;
    private PurchasingCardAction _primaryCardAction = PurchasingCardAction.None;

    public PurchasingWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        _store = PurchasingOperationalWorkspaceStore.CreateDefault();
        _workspace = _store.LoadOrCreate(GetCurrentOperator(), salesWorkspace);

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        PurchasingGrid.ItemsSource = _rows;
        DetailLinesGrid.ItemsSource = _detailLines;

        InitializeStaticLabels();
        InitializeFilters();
        InitializeActionsMenu();
        HookEvents();
        Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;
    }

    public void Dispose()
    {
        SizeChanged -= HandleSizeChanged;
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

    private static string BuildSelectionKey(string section, Guid id)
    {
        return $"{section}:{id:D}";
    }

    private void InitializeStaticLabels()
    {
        HeaderSearchPlaceholderText.Text = "Поиск по номеру, поставщику, товару или коду...";
        TableSearchPlaceholderText.Text = "Поиск по номеру, поставщику, товару...";
        EmptyStateTitleText.Text = "Нет закупочных документов";
        EmptyStateHintText.Text = "Создайте первый документ вручную или импортируйте данные.";
        ShowAllPositionsText.Text = "Показать все";
    }

    private void InitializeFilters()
    {
        _suppressFilters = true;
        try
        {
            StatusFilterCombo.ItemsSource = new[] { AllStatusesFilter };
            SupplierFilterCombo.ItemsSource = new[] { AllSuppliersFilter };
            WarehouseFilterCombo.ItemsSource = new[] { AllWarehousesFilter };
            DocumentTypeFilterCombo.ItemsSource = new[] { AllDocumentTypesFilter };

            StatusFilterCombo.SelectedIndex = 0;
            SupplierFilterCombo.SelectedIndex = 0;
            WarehouseFilterCombo.SelectedIndex = 0;
            DocumentTypeFilterCombo.SelectedIndex = 0;

            PageSizeCombo.SelectedIndex = 1;
        }
        finally
        {
            _suppressFilters = false;
        }
    }

    private void InitializeActionsMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Новая закупка", (_, _) => CreateNewPurchase()));
        menu.Items.Add(CreateMenuItem("Новый поставщик", (_, _) => OpenSupplierEditor(null)));
        menu.Items.Add(CreateMenuItem("Новый счет поставщика", (_, _) => OpenDocumentEditor(PurchasingDocumentEditorMode.SupplierInvoice, null, ResolveSelectedSupplierId())));
        menu.Items.Add(CreateMenuItem("Новая приемка", (_, _) => OpenDocumentEditor(PurchasingDocumentEditorMode.PurchaseReceipt, null, ResolveSelectedSupplierId())));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Сбросить фильтры", (_, _) => ResetFilters(clearSearch: true)));
        menu.Items.Add(CreateMenuItem("Экспорт текущего вида", (_, _) => ExportRows(_filteredRows, "Закупки")));
        ActionsButton.ContextMenu = menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void HookEvents()
    {
        _workspace.Changed += HandleWorkspaceChanged;
        _salesWorkspace.Changed += HandleSalesWorkspaceChanged;
        Unloaded += HandleUnloaded;
    }

    private void UnhookEvents()
    {
        _workspace.Changed -= HandleWorkspaceChanged;
        _salesWorkspace.Changed -= HandleSalesWorkspaceChanged;
        Unloaded -= HandleUnloaded;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        Dispatcher.BeginInvoke(() =>
        {
            RefreshAll();
            UpdateResponsiveLayout();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        TryPersistWorkspace();
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TryPersistWorkspace();
            RefreshAll();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RefreshAll();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshAll()
    {
        EnsureDateRangeInitialized();
        RefreshMetrics();
        RefreshIssueChips();
        ApplySection(_activeSection, keepSelection: true, resetFilters: !_initialized);
        UpdateResponsiveLayout();
        _initialized = true;
    }

    private void UpdateResponsiveLayout()
    {
        var width = ActualWidth;
        var compact = width < 1280;
        var stackDetails = width < 1360;

        MetricsGrid.Columns = width < 1120 ? 2 : compact ? 3 : 5;
        IssueChipsGrid.Columns = width < 1120 ? 2 : compact ? 3 : 5;
        CardActionsGrid.Columns = stackDetails ? 3 : 1;

        if (stackDetails)
        {
            WorkspaceLayoutGrid.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(DetailsCard, 0);
            Grid.SetRow(DetailsCard, 1);
            Grid.SetColumnSpan(DetailsCard, 2);
            WorkspaceLeftPanel.Margin = new Thickness(0);
            DetailsCard.Margin = new Thickness(0, 18, 0, 0);
        }
        else
        {
            WorkspaceLayoutGrid.ColumnDefinitions[1].Width = new GridLength(344);
            Grid.SetColumn(DetailsCard, 1);
            Grid.SetRow(DetailsCard, 0);
            Grid.SetColumnSpan(DetailsCard, 1);
            WorkspaceLeftPanel.Margin = new Thickness(0, 0, 24, 0);
            DetailsCard.Margin = new Thickness(0);
        }
    }

    private void EnsureDateRangeInitialized()
    {
        if (_dateRangeInitialized)
        {
            return;
        }

        var allDates = _workspace.PurchaseOrders.Select(item => item.DocumentDate)
            .Concat(_workspace.SupplierInvoices.Select(item => item.DocumentDate))
            .Concat(_workspace.PurchaseReceipts.Select(item => item.DocumentDate))
            .Concat(_workspace.OperationLog.Select(item => item.LoggedAt))
            .ToArray();

        var min = allDates.Length > 0 ? allDates.Min().Date : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var max = allDates.Length > 0 ? allDates.Max().Date : DateTime.Today;
        _defaultDateFrom = min;
        _defaultDateTo = max > DateTime.Today ? max : DateTime.Today;

        _suppressFilters = true;
        try
        {
            DateFromPicker.SelectedDate = _defaultDateFrom;
            DateToPicker.SelectedDate = _defaultDateTo;
        }
        finally
        {
            _suppressFilters = false;
        }

        _dateRangeInitialized = true;
    }

    private void RefreshMetrics()
    {
        ActiveSuppliersMetricText.Text = _workspace.Suppliers.Count(item => !Ui(item.Status).Equals("Пауза", StringComparison.OrdinalIgnoreCase)).ToString("N0", RuCulture);
        OpenOrdersMetricText.Text = _workspace.PurchaseOrders.Count(item => !IsOrderClosed(item)).ToString("N0", RuCulture);
        PendingInvoiceMetricText.Text = _workspace.PurchaseOrders.Count(item => !GetInvoicesForOrder(item.Id).Any()).ToString("N0", RuCulture);
        PendingReceiptMetricText.Text = _workspace.PurchaseOrders.Count(item => !GetReceiptsForOrder(item.Id).Any()).ToString("N0", RuCulture);
        OverdueMetricText.Text = CountOverdueDocuments().ToString("N0", RuCulture);
    }

    private void RefreshIssueChips()
    {
        MissingInvoiceChipText.Text = _workspace.PurchaseOrders.Count(item => !GetInvoicesForOrder(item.Id).Any()).ToString("N0", RuCulture);
        MissingReceiptChipText.Text = _workspace.PurchaseOrders.Count(item => !GetReceiptsForOrder(item.Id).Any()).ToString("N0", RuCulture);
        OverdueChipText.Text = CountOverdueDocuments().ToString("N0", RuCulture);
        UnpaidChipText.Text = _workspace.SupplierInvoices.Count(item => !IsInvoicePaid(item)).ToString("N0", RuCulture);
        DiscrepancyChipText.Text = CountDiscrepancyDocuments().ToString("N0", RuCulture);
    }

    private int CountOverdueDocuments()
    {
        return _workspace.PurchaseOrders.Count(IsOrderOverdue)
               + _workspace.SupplierInvoices.Count(IsInvoiceOverdue)
               + _workspace.PurchaseReceipts.Count(IsReceiptOverdue);
    }

    private int CountDiscrepancyDocuments()
    {
        return _workspace.PurchaseOrders.Count(HasDiscrepancy)
               + _workspace.SupplierInvoices.Count(HasDiscrepancy)
               + _workspace.PurchaseReceipts.Count(HasDiscrepancy);
    }

    private void ApplySection(string section, bool keepSelection = false, bool resetFilters = false)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        var sectionChanged = !string.Equals(_activeSection, section, StringComparison.OrdinalIgnoreCase);
        _activeSection = section;

        if (sectionChanged)
        {
            _page = 1;
            _selectedRowKey = null;
            _dismissedLockKey = null;
            ClearCheckedRows();
        }

        _allRows = BuildRowsForSection(section);
        ConfigureGridForSection(section);
        RefreshFilterOptions(resetFilters || sectionChanged);
        ApplySectionButtons();
        ApplyFilters(keepSelection && !sectionChanged);
        UpdateSearchPlaceholders();
        UpdateEmptyStateCopy();
    }

    private void ApplySectionButtons()
    {
        ApplySectionButton(OrdersTabButton, _activeSection == OrdersSection);
        ApplySectionButton(SuppliersTabButton, _activeSection == SuppliersSection);
        ApplySectionButton(InvoicesTabButton, _activeSection == InvoicesSection);
        ApplySectionButton(ReceiptsTabButton, _activeSection == ReceiptsSection);
        ApplySectionButton(PaymentsTabButton, _activeSection == PaymentsSection);
        ApplySectionButton(DiscrepanciesTabButton, _activeSection == DiscrepanciesSection);
        ApplySectionButton(JournalTabButton, _activeSection == JournalSection);
    }

    private static void ApplySectionButton(WpfButton button, bool isActive)
    {
        button.BorderBrush = isActive ? PrimaryBrush : Brushes.Transparent;
        button.Foreground = isActive ? PrimaryBrush : NeutralBrush;
    }

    private void RefreshFilterOptions(bool resetSelections)
    {
        var status = Ui(StatusFilterCombo.SelectedItem as string);
        var supplier = Ui(SupplierFilterCombo.SelectedItem as string);
        var warehouse = Ui(WarehouseFilterCombo.SelectedItem as string);
        var documentType = Ui(DocumentTypeFilterCombo.SelectedItem as string);

        _suppressFilters = true;
        try
        {
            StatusFilterCombo.ItemsSource = BuildOptions(AllStatusesFilter, _allRows.Select(item => item.RawStatus));
            SupplierFilterCombo.ItemsSource = BuildOptions(AllSuppliersFilter, _allRows.Select(item => item.SupplierName));
            WarehouseFilterCombo.ItemsSource = BuildOptions(AllWarehousesFilter, _allRows.Select(item => item.Warehouse));
            DocumentTypeFilterCombo.ItemsSource = BuildOptions(AllDocumentTypesFilter, _allRows.Select(item => item.DocumentType));

            StatusFilterCombo.SelectedItem = resetSelections ? AllStatusesFilter : SelectOrFallback(StatusFilterCombo, status, AllStatusesFilter);
            SupplierFilterCombo.SelectedItem = resetSelections ? AllSuppliersFilter : SelectOrFallback(SupplierFilterCombo, supplier, AllSuppliersFilter);
            WarehouseFilterCombo.SelectedItem = resetSelections ? AllWarehousesFilter : SelectOrFallback(WarehouseFilterCombo, warehouse, AllWarehousesFilter);
            DocumentTypeFilterCombo.SelectedItem = resetSelections ? AllDocumentTypesFilter : SelectOrFallback(DocumentTypeFilterCombo, documentType, AllDocumentTypesFilter);

            if (resetSelections)
            {
                OverdueOnlyCheckBox.IsChecked = false;
                MissingInvoiceOnlyCheckBox.IsChecked = false;
                MissingReceiptOnlyCheckBox.IsChecked = false;
                UnpaidOnlyCheckBox.IsChecked = false;
            }
        }
        finally
        {
            _suppressFilters = false;
        }
    }

    private static string[] BuildOptions(string allCaption, IEnumerable<string> source)
    {
        return new[] { allCaption }
            .Concat(source
                .Select(Ui)
                .Where(item => !string.IsNullOrWhiteSpace(item) && item != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
    }

    private static object SelectOrFallback(ComboBox comboBox, string current, string fallback)
    {
        return comboBox.Items.Cast<object>().FirstOrDefault(item => Ui(item?.ToString()).Equals(current, StringComparison.OrdinalIgnoreCase))
               ?? fallback;
    }

    private void ApplyFilters(bool keepSelection)
    {
        var search = Ui(TableSearchBox.Text).Trim();
        var status = Ui(StatusFilterCombo.SelectedItem as string);
        var supplier = Ui(SupplierFilterCombo.SelectedItem as string);
        var warehouse = Ui(WarehouseFilterCombo.SelectedItem as string);
        var documentType = Ui(DocumentTypeFilterCombo.SelectedItem as string);
        var from = DateFromPicker.SelectedDate?.Date;
        var to = DateToPicker.SelectedDate?.Date.AddDays(1).AddTicks(-1);
        var onlyOverdue = OverdueOnlyCheckBox.IsChecked == true;
        var onlyMissingInvoice = MissingInvoiceOnlyCheckBox.IsChecked == true;
        var onlyMissingReceipt = MissingReceiptOnlyCheckBox.IsChecked == true;
        var onlyUnpaid = UnpaidOnlyCheckBox.IsChecked == true;

        _filteredRows = _allRows
            .Where(item => string.IsNullOrWhiteSpace(search) || item.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(item => status == AllStatusesFilter || item.RawStatus.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(item => supplier == AllSuppliersFilter || item.SupplierName.Equals(supplier, StringComparison.OrdinalIgnoreCase))
            .Where(item => warehouse == AllWarehousesFilter || item.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase))
            .Where(item => documentType == AllDocumentTypesFilter || item.DocumentType.Equals(documentType, StringComparison.OrdinalIgnoreCase))
            .Where(item => !from.HasValue || item.SortDate.Date >= from.Value)
            .Where(item => !to.HasValue || item.SortDate <= to.Value)
            .Where(item => !onlyOverdue || item.IsOverdue)
            .Where(item => !onlyMissingInvoice || item.MissingInvoice)
            .Where(item => !onlyMissingReceipt || item.MissingReceipt)
            .Where(item => !onlyUnpaid || item.IsUnpaid)
            .OrderByDescending(item => item.SortDate)
            .ThenBy(item => item.Col1, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (keepSelection && !string.IsNullOrWhiteSpace(_selectedRowKey))
        {
            var selectionIndex = Array.FindIndex(_filteredRows, item => item.SelectionKey.Equals(_selectedRowKey, StringComparison.OrdinalIgnoreCase));
            if (selectionIndex >= 0)
            {
                _page = (selectionIndex / Math.Max(1, _pageSize)) + 1;
            }
        }

        RebuildPage(keepSelection);
        UpdateBulkBar();
    }

    private void RebuildPage(bool keepSelection)
    {
        foreach (var row in _rows)
        {
            row.PropertyChanged -= HandleRowPropertyChanged;
        }

        _rows.Clear();

        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRows.Length / (double)Math.Max(1, _pageSize)));
        if (_page > totalPages)
        {
            _page = totalPages;
        }

        if (_page < 1)
        {
            _page = 1;
        }

        var pageRows = _filteredRows.Skip((_page - 1) * _pageSize).Take(_pageSize).ToArray();
        foreach (var row in pageRows)
        {
            row.PropertyChanged += HandleRowPropertyChanged;
            _rows.Add(row);
        }

        EmptyStatePanel.Visibility = _filteredRows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PurchasingGrid.Visibility = _filteredRows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PagerSummaryText.Text = BuildPagerSummary(pageRows.Length);
        PagerIndexText.Text = $"{_page:N0} / {totalPages:N0}";

        PurchasingGrid.SelectedItem = null;
        PurchasingGrid.Items.Refresh();

        if (_filteredRows.Length == 0)
        {
            _selectedRowKey = null;
            RefreshDetails(null);
            return;
        }

        PurchasingGridRow? selectedRow = null;
        if (keepSelection && !string.IsNullOrWhiteSpace(_selectedRowKey))
        {
            selectedRow = pageRows.FirstOrDefault(item => item.SelectionKey.Equals(_selectedRowKey, StringComparison.OrdinalIgnoreCase));
        }

        selectedRow ??= pageRows.FirstOrDefault();
        if (selectedRow is not null)
        {
            _selectedRowKey = selectedRow.SelectionKey;
            PurchasingGrid.SelectedItem = selectedRow;
            PurchasingGrid.ScrollIntoView(selectedRow);
            RefreshDetails(selectedRow);
        }
    }

    private string BuildPagerSummary(int visibleCount)
    {
        if (_filteredRows.Length == 0 || visibleCount == 0)
        {
            return "Показано 0 из 0";
        }

        var from = ((_page - 1) * _pageSize) + 1;
        var to = from + visibleCount - 1;
        return $"Показано {from:N0}–{to:N0} из {_filteredRows.Length:N0}";
    }

    private void UpdateBulkBar()
    {
        var checkedRows = GetCheckedRows().ToArray();
        BulkBarBorder.Visibility = checkedRows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BulkSelectionText.Text = checkedRows.Length switch
        {
            0 => string.Empty,
            1 => "Выбран 1 документ",
            _ => $"Выбрано {checkedRows.Length:N0} записей"
        };
    }

    private IEnumerable<PurchasingGridRow> GetCheckedRows()
    {
        return _allRows.Where(item => _checkedKeys.Contains(item.SelectionKey));
    }

    private PurchasingGridRow[] GetCheckedOrCurrentRows()
    {
        var checkedRows = GetCheckedRows().ToArray();
        if (checkedRows.Length > 0)
        {
            return checkedRows;
        }

        var current = GetCurrentRow();
        return current is null ? Array.Empty<PurchasingGridRow>() : new[] { current };
    }

    private PurchasingGridRow? GetCurrentRow()
    {
        if (PurchasingGrid.SelectedItem is PurchasingGridRow row)
        {
            return row;
        }

        return _filteredRows.FirstOrDefault(item => item.SelectionKey.Equals(_selectedRowKey, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearCheckedRows()
    {
        _checkedKeys.Clear();
        foreach (var row in _allRows)
        {
            row.IsChecked = false;
        }

        foreach (var row in _rows)
        {
            row.IsChecked = false;
        }

        UpdateBulkBar();
    }

    private void ConfigureGridForSection(string section)
    {
        switch (section)
        {
            case SuppliersSection:
                SetGridHeaders("Код", "Поставщик", "ИНН", "Телефон", "E-mail", "Договор", "Заказы", "Активные документы", "Статус", "Источник");
                break;
            case InvoicesSection:
                SetGridHeaders("Номер", "Поставщик", "Дата счета", "Оплатить до", "Сумма", "Склад", "Оплачено", "Основание", "Статус", "Источник");
                break;
            case ReceiptsSection:
                SetGridHeaders("Номер", "Поставщик", "Дата приемки", "Основание", "Склад", "Сумма", "Позиций", "Источник", "Статус", "Ответственный");
                break;
            case PaymentsSection:
                SetGridHeaders("Платеж", "Поставщик", "Дата счета", "Срок оплаты", "Сумма", "Склад", "Оплачено", "Остаток", "Статус", "Основание");
                break;
            case DiscrepanciesSection:
                SetGridHeaders("Документ", "Поставщик", "Дата", "Основание", "Склад", "Сумма", "Комментарий", "Источник", "Статус", "Ответственный");
                break;
            case JournalSection:
                SetGridHeaders("Время", "Объект", "Номер", "Действие", "Результат", "Пользователь", "Комментарий", "Ответственный", "Статус", "Источник");
                break;
            default:
                SetGridHeaders("Номер", "Поставщик", "Дата заказа", "Плановая поставка", "Склад", "Сумма", "Оплачено", "Остаток", "Статус", "Ответственный");
                break;
        }
    }

    private void SetGridHeaders(
        string column1,
        string column2,
        string column3,
        string column4,
        string column5,
        string column6,
        string column7,
        string column8,
        string status,
        string column9)
    {
        Column1.Header = column1;
        Column2.Header = column2;
        Column3.Header = column3;
        Column4.Header = column4;
        Column5.Header = column5;
        Column6.Header = column6;
        Column7.Header = column7;
        Column8.Header = column8;
        StatusColumn.Header = status;
        Column9.Header = column9;
        ActionsColumn.Header = "Действия";
    }

    private void UpdateSearchPlaceholders()
    {
        var placeholder = _activeSection switch
        {
            SuppliersSection => "Поиск по поставщику, ИНН, телефону или договору...",
            InvoicesSection => "Поиск по счету, поставщику или заказу...",
            ReceiptsSection => "Поиск по приемке, поставщику или заказу...",
            PaymentsSection => "Поиск по оплате, счету или поставщику...",
            DiscrepanciesSection => "Поиск по проблемным закупкам и расхождениям...",
            JournalSection => "Поиск по журналу операций и документам...",
            _ => "Поиск по номеру, поставщику, товару или коду..."
        };

        HeaderSearchPlaceholderText.Text = placeholder;
        TableSearchPlaceholderText.Text = placeholder;
        HeaderSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(HeaderSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        TableSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(TableSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEmptyStateCopy()
    {
        switch (_activeSection)
        {
            case SuppliersSection:
                EmptyStateTitleText.Text = "Нет поставщиков";
                EmptyStateHintText.Text = "Добавьте поставщика вручную или импортируйте данные.";
                break;
            case InvoicesSection:
                EmptyStateTitleText.Text = "Нет счетов поставщика";
                EmptyStateHintText.Text = "Счета поставщика появятся после регистрации входящих документов.";
                break;
            case ReceiptsSection:
                EmptyStateTitleText.Text = "Нет приемок";
                EmptyStateHintText.Text = "Создайте приемку на основе заказа поставщику или импортируйте данные.";
                break;
            case PaymentsSection:
                EmptyStateTitleText.Text = "Нет оплат";
                EmptyStateHintText.Text = "Оплаты появятся по зарегистрированным счетам поставщика.";
                break;
            case DiscrepanciesSection:
                EmptyStateTitleText.Text = "Нет проблемных закупок";
                EmptyStateHintText.Text = "Расхождения, недостачи и проблемные документы появятся здесь.";
                break;
            case JournalSection:
                EmptyStateTitleText.Text = "Журнал пуст";
                EmptyStateHintText.Text = "Операции появятся после создания и обработки закупочных документов.";
                break;
            default:
                EmptyStateTitleText.Text = "Нет закупочных документов";
                EmptyStateHintText.Text = "Создайте первый документ вручную или импортируйте данные.";
                break;
        }
    }

    private PurchasingGridRow[] BuildRowsForSection(string section)
    {
        return section switch
        {
            SuppliersSection => _workspace.Suppliers
                .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
                .Select(BuildSupplierRow)
                .ToArray(),
            InvoicesSection => _workspace.SupplierInvoices
                .OrderByDescending(item => item.DocumentDate)
                .Select(BuildInvoiceRow)
                .ToArray(),
            ReceiptsSection => _workspace.PurchaseReceipts
                .OrderByDescending(item => item.DocumentDate)
                .Select(BuildReceiptRow)
                .ToArray(),
            PaymentsSection => _workspace.SupplierInvoices
                .OrderByDescending(item => item.DueDate ?? item.DocumentDate)
                .Select(BuildPaymentRow)
                .ToArray(),
            DiscrepanciesSection => BuildDiscrepancyRows(),
            JournalSection => _workspace.OperationLog
                .OrderByDescending(item => item.LoggedAt)
                .Select(BuildJournalRow)
                .ToArray(),
            _ => _workspace.PurchaseOrders
                .OrderByDescending(item => item.DocumentDate)
                .Select(BuildOrderRow)
                .ToArray()
        };
    }

    private PurchasingGridRow[] BuildDiscrepancyRows()
    {
        return _workspace.PurchaseOrders.Cast<OperationalPurchasingDocumentRecord>()
            .Concat(_workspace.SupplierInvoices)
            .Concat(_workspace.PurchaseReceipts)
            .Where(item => HasDiscrepancy(item) || IsDocumentOverdue(item) || MissingInvoiceForDocument(item) || MissingReceiptForDocument(item) || UnpaidForDocument(item))
            .OrderByDescending(item => item.DocumentDate)
            .Select(BuildDiscrepancyRow)
            .ToArray();
    }

    private PurchasingGridRow BuildOrderRow(OperationalPurchasingDocumentRecord order)
    {
        var invoices = GetInvoicesForOrder(order.Id);
        var receipts = GetReceiptsForOrder(order.Id);
        var plannedDate = ResolvePlannedDate(order);
        var paid = invoices.Where(IsInvoicePaid).Sum(item => item.TotalAmount);
        var balance = Math.Max(order.TotalAmount - paid, 0m);
        var responsible = ResolveResponsible(order.DocumentType, order.Id);

        return CreateRow(
            OrdersSection,
            order.Id,
            order,
            order.DocumentType,
            order.SupplierName,
            order.Warehouse,
            order.Number,
            order.SupplierName,
            order.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            plannedDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-",
            order.Warehouse,
            FormatMoney(order.TotalAmount),
            FormatMoney(paid),
            FormatMoney(balance),
            order.Status,
            responsible,
            order.Status,
            order.DocumentDate,
            IsOrderClosed(order),
            IsOrderOverdue(order),
            !invoices.Any(),
            !receipts.Any(),
            invoices.Any(item => !IsInvoicePaid(item)),
            HasDiscrepancy(order) || invoices.Any(HasDiscrepancy) || receipts.Any(HasDiscrepancy),
            order.Id,
            order.TotalAmount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                order.Number,
                order.SupplierName,
                order.Warehouse,
                order.Status,
                order.Contract,
                order.Comment,
                string.Join(" ", order.Lines.Select(item => $"{item.ItemCode} {item.ItemName}"))
            }));
    }

    private PurchasingGridRow BuildSupplierRow(OperationalPurchasingSupplierRecord supplier)
    {
        var orders = _workspace.PurchaseOrders.Where(item => item.SupplierId == supplier.Id).ToArray();
        var invoices = _workspace.SupplierInvoices.Where(item => item.SupplierId == supplier.Id).ToArray();
        var receipts = _workspace.PurchaseReceipts.Where(item => item.SupplierId == supplier.Id).ToArray();
        var openOrders = orders.Count(item => !IsOrderClosed(item));
        var activeDocuments = invoices.Length + receipts.Length;
        var lastDate = orders.Select(item => item.DocumentDate)
            .Concat(invoices.Select(item => item.DocumentDate))
            .Concat(receipts.Select(item => item.DocumentDate))
            .DefaultIfEmpty(DateTime.Today)
            .Max();
        var paid = invoices.Where(IsInvoicePaid).Sum(item => item.TotalAmount);
        var amount = orders.Sum(item => item.TotalAmount);
        var balance = Math.Max(amount - paid, 0m);

        return CreateRow(
            SuppliersSection,
            supplier.Id,
            supplier,
            "Поставщик",
            supplier.Name,
            ResolveDominantWarehouse(orders, invoices, receipts),
            supplier.Code,
            supplier.Name,
            EmptyAsDash(supplier.TaxId),
            EmptyAsDash(supplier.Phone),
            EmptyAsDash(supplier.Email),
            EmptyAsDash(supplier.Contract),
            openOrders.ToString("N0", RuCulture),
            activeDocuments.ToString("N0", RuCulture),
            supplier.Status,
            EmptyAsDash(supplier.SourceLabel),
            supplier.Status,
            lastDate,
            Ui(supplier.Status).Equals("Пауза", StringComparison.OrdinalIgnoreCase),
            false,
            orders.Any(item => !GetInvoicesForOrder(item.Id).Any()),
            orders.Any(item => !GetReceiptsForOrder(item.Id).Any()),
            invoices.Any(item => !IsInvoicePaid(item)),
            orders.Any(HasDiscrepancy) || invoices.Any(HasDiscrepancy) || receipts.Any(HasDiscrepancy),
            orders.FirstOrDefault()?.Id ?? Guid.Empty,
            amount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                supplier.Code,
                supplier.Name,
                supplier.TaxId,
                supplier.Phone,
                supplier.Email,
                supplier.Contract,
                supplier.SourceLabel
            }));
    }

    private PurchasingGridRow BuildInvoiceRow(OperationalPurchasingDocumentRecord invoice)
    {
        var paid = IsInvoicePaid(invoice) ? invoice.TotalAmount : 0m;
        var balance = Math.Max(invoice.TotalAmount - paid, 0m);
        return CreateRow(
            InvoicesSection,
            invoice.Id,
            invoice,
            invoice.DocumentType,
            invoice.SupplierName,
            invoice.Warehouse,
            invoice.Number,
            invoice.SupplierName,
            invoice.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            invoice.DueDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-",
            invoice.Warehouse,
            FormatMoney(invoice.TotalAmount),
            FormatMoney(paid),
            EmptyAsDash(invoice.RelatedOrderNumber),
            invoice.Status,
            EmptyAsDash(invoice.SourceLabel),
            invoice.Status,
            invoice.DueDate ?? invoice.DocumentDate,
            IsInvoicePaid(invoice),
            IsInvoiceOverdue(invoice),
            false,
            MissingReceiptForDocument(invoice),
            !IsInvoicePaid(invoice),
            HasDiscrepancy(invoice),
            invoice.RelatedOrderId,
            invoice.TotalAmount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                invoice.Number,
                invoice.SupplierName,
                invoice.Warehouse,
                invoice.Status,
                invoice.RelatedOrderNumber,
                invoice.Comment
            }));
    }

    private PurchasingGridRow BuildReceiptRow(OperationalPurchasingDocumentRecord receipt)
    {
        var invoice = GetInvoiceForOrder(receipt.RelatedOrderId);
        var paid = invoice is not null && IsInvoicePaid(invoice) ? invoice.TotalAmount : 0m;
        var balance = invoice is null ? 0m : Math.Max(invoice.TotalAmount - paid, 0m);
        return CreateRow(
            ReceiptsSection,
            receipt.Id,
            receipt,
            receipt.DocumentType,
            receipt.SupplierName,
            receipt.Warehouse,
            receipt.Number,
            receipt.SupplierName,
            receipt.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            EmptyAsDash(receipt.RelatedOrderNumber),
            receipt.Warehouse,
            FormatMoney(receipt.TotalAmount),
            receipt.PositionCount.ToString("N0", RuCulture),
            EmptyAsDash(receipt.SourceLabel),
            receipt.Status,
            ResolveResponsible(receipt.DocumentType, receipt.Id),
            receipt.Status,
            receipt.DocumentDate,
            IsReceiptCompleted(receipt),
            IsReceiptOverdue(receipt),
            MissingInvoiceForDocument(receipt),
            false,
            invoice is not null && !IsInvoicePaid(invoice),
            HasDiscrepancy(receipt),
            receipt.RelatedOrderId,
            receipt.TotalAmount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                receipt.Number,
                receipt.SupplierName,
                receipt.Warehouse,
                receipt.Status,
                receipt.RelatedOrderNumber,
                receipt.Comment
            }));
    }

    private PurchasingGridRow BuildPaymentRow(OperationalPurchasingDocumentRecord invoice)
    {
        var paymentStatus = IsInvoicePaid(invoice)
            ? "Проведена"
            : Ui(invoice.Status) switch
            {
                "К оплате" => "Ожидает оплаты",
                "Получен" => "Ожидает оплаты",
                _ => "Не оплачена"
            };
        var paid = IsInvoicePaid(invoice) ? invoice.TotalAmount : 0m;
        var balance = Math.Max(invoice.TotalAmount - paid, 0m);
        return CreateRow(
            PaymentsSection,
            invoice.Id,
            invoice,
            "Оплата",
            invoice.SupplierName,
            invoice.Warehouse,
            $"PAY ? {invoice.Number}",
            invoice.SupplierName,
            invoice.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            invoice.DueDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-",
            invoice.Warehouse,
            FormatMoney(invoice.TotalAmount),
            FormatMoney(paid),
            FormatMoney(balance),
            paymentStatus,
            EmptyAsDash(invoice.RelatedOrderNumber),
            paymentStatus,
            invoice.DueDate ?? invoice.DocumentDate,
            IsInvoicePaid(invoice),
            IsInvoiceOverdue(invoice),
            false,
            MissingReceiptForDocument(invoice),
            !IsInvoicePaid(invoice),
            HasDiscrepancy(invoice),
            invoice.RelatedOrderId,
            invoice.TotalAmount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                invoice.Number,
                invoice.SupplierName,
                invoice.Warehouse,
                paymentStatus,
                invoice.RelatedOrderNumber
            }));
    }

    private PurchasingGridRow BuildDiscrepancyRow(OperationalPurchasingDocumentRecord document)
    {
        var status = ResolveDiscrepancyStatus(document);
        return CreateRow(
            DiscrepanciesSection,
            document.Id,
            document,
            document.DocumentType,
            document.SupplierName,
            document.Warehouse,
            document.Number,
            document.SupplierName,
            document.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            EmptyAsDash(document.RelatedOrderNumber),
            document.Warehouse,
            FormatMoney(document.TotalAmount),
            TrimComment(document.Comment),
            EmptyAsDash(document.SourceLabel),
            status,
            ResolveResponsible(document.DocumentType, document.Id),
            status,
            document.DocumentDate,
            false,
            status.Equals("Просрочено", StringComparison.OrdinalIgnoreCase),
            MissingInvoiceForDocument(document),
            MissingReceiptForDocument(document),
            UnpaidForDocument(document),
            HasDiscrepancy(document),
            document.RelatedOrderId,
            document.TotalAmount,
            0m,
            document.TotalAmount,
            string.Join(" ", new[]
            {
                document.Number,
                document.SupplierName,
                document.Warehouse,
                document.Status,
                document.Comment,
                status
            }));
    }

    private PurchasingGridRow BuildJournalRow(PurchasingOperationLogEntry entry)
    {
        var status = Ui(entry.Result);
        return CreateRow(
            JournalSection,
            entry.Id,
            entry,
            Ui(entry.EntityType),
            ResolveSupplierNameForJournal(entry),
            ResolveWarehouseForJournal(entry),
            entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture),
            Ui(entry.EntityType),
            Ui(entry.EntityNumber),
            Ui(entry.Action),
            Ui(entry.Result),
            Ui(entry.Actor),
            TrimComment(entry.Message),
            ResolveJournalSection(entry),
            status,
            Ui(entry.Actor),
            status,
            entry.LoggedAt,
            true,
            false,
            false,
            false,
            false,
            false,
            Guid.Empty,
            0m,
            0m,
            0m,
            string.Join(" ", new[]
            {
                entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture),
                entry.Actor,
                entry.EntityType,
                entry.EntityNumber,
                entry.Action,
                entry.Result,
                entry.Message
            }));
    }

    private PurchasingGridRow CreateRow(
        string section,
        Guid id,
        object payload,
        string documentType,
        string supplierName,
        string warehouse,
        string col1,
        string col2,
        string col3,
        string col4,
        string col5,
        string col6,
        string col7,
        string col8,
        string rawStatus,
        string col9,
        string statusText,
        DateTime sortDate,
        bool isDisabled,
        bool isOverdue,
        bool missingInvoice,
        bool missingReceipt,
        bool isUnpaid,
        bool hasDiscrepancy,
        Guid relatedOrderId,
        decimal amountValue,
        decimal paidValue,
        decimal balanceValue,
        string searchText)
    {
        var (background, foreground) = ResolveStatusBrushes(statusText);
        var row = new PurchasingGridRow
        {
            Id = id,
            Section = section,
            SelectionKey = BuildSelectionKey(section, id),
            Payload = payload,
            DocumentType = Ui(documentType),
            SupplierName = Ui(supplierName),
            Warehouse = Ui(warehouse),
            Col1 = Ui(col1),
            Col2 = Ui(col2),
            Col3 = Ui(col3),
            Col4 = Ui(col4),
            Col5 = Ui(col5),
            Col6 = Ui(col6),
            Col7 = Ui(col7),
            Col8 = Ui(col8),
            Col9 = Ui(col9),
            RawStatus = Ui(rawStatus),
            StatusText = Ui(statusText),
            StatusBackground = background,
            StatusForeground = foreground,
            SearchText = Ui(searchText),
            IsDisabled = isDisabled,
            IsOverdue = isOverdue,
            MissingInvoice = missingInvoice,
            MissingReceipt = missingReceipt,
            IsUnpaid = isUnpaid,
            HasDiscrepancy = hasDiscrepancy,
            SortDate = sortDate,
            RelatedOrderId = relatedOrderId,
            AmountValue = amountValue,
            PaidValue = paidValue,
            BalanceValue = balanceValue,
            IsChecked = _checkedKeys.Contains(BuildSelectionKey(section, id))
        };
        return row;
    }

    private static (Brush Background, Brush Foreground) ResolveStatusBrushes(string status)
    {
        status = Ui(status);
        if (status.Equals("Активен", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Принят", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Размещена", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Оплачен", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Проведена", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Закрыт", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Проведен", StringComparison.OrdinalIgnoreCase))
        {
            return (SuccessSoftBrush, SuccessBrush);
        }

        if (status.Equals("Ожидает счет", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Ожидается поставка", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Ожидает приемку", StringComparison.OrdinalIgnoreCase)
            || status.Equals("К оплате", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Получен", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Заказан", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Размещена?", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Под контролем", StringComparison.OrdinalIgnoreCase))
        {
            return (WarningSoftBrush, WarningBrush);
        }

        if (status.Equals("Частично оплачено", StringComparison.OrdinalIgnoreCase))
        {
            return (PurpleSoftBrush, PurpleBrush);
        }

        if (status.Equals("Просрочено", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Просрочен", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Критично", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Отменен", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Отменена", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Архив", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Есть расхождения", StringComparison.OrdinalIgnoreCase))
        {
            return (DangerSoftBrush, DangerBrush);
        }

        return (NeutralSoftBrush, NeutralBrush);
    }

    private OperationalPurchasingDocumentRecord[] GetInvoicesForOrder(Guid orderId)
    {
        return _workspace.SupplierInvoices.Where(item => item.RelatedOrderId == orderId).ToArray();
    }

    private OperationalPurchasingDocumentRecord[] GetReceiptsForOrder(Guid orderId)
    {
        return _workspace.PurchaseReceipts.Where(item => item.RelatedOrderId == orderId).ToArray();
    }

    private OperationalPurchasingDocumentRecord? GetInvoiceForOrder(Guid orderId)
    {
        return _workspace.SupplierInvoices
            .Where(item => item.RelatedOrderId == orderId)
            .OrderByDescending(item => item.DocumentDate)
            .FirstOrDefault();
    }

    private OperationalPurchasingDocumentRecord? GetReceiptForOrder(Guid orderId)
    {
        return _workspace.PurchaseReceipts
            .Where(item => item.RelatedOrderId == orderId)
            .OrderByDescending(item => item.DocumentDate)
            .FirstOrDefault();
    }

    private OperationalPurchasingDocumentRecord? GetOrderById(Guid id)
    {
        return _workspace.PurchaseOrders.FirstOrDefault(item => item.Id == id);
    }

    private bool IsOrderClosed(OperationalPurchasingDocumentRecord order)
    {
        var status = Ui(order.Status);
        return status.Equals("Принят", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Закрыт", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Архив", StringComparison.OrdinalIgnoreCase)
               || status.StartsWith("Отмен", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInvoicePaid(OperationalPurchasingDocumentRecord invoice)
    {
        return Ui(invoice.Status).Equals("Оплачен", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsReceiptCompleted(OperationalPurchasingDocumentRecord receipt)
    {
        var status = Ui(receipt.Status);
        return status.Equals("Размещена", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Принята", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Архив", StringComparison.OrdinalIgnoreCase);
    }

    private DateTime? ResolvePlannedDate(OperationalPurchasingDocumentRecord document)
    {
        var planned = document.Lines
            .Where(item => item.PlannedDate.HasValue)
            .Select(item => (DateTime?)item.PlannedDate!.Value.Date)
            .OrderByDescending(item => item)
            .FirstOrDefault();

        return planned ?? document.DueDate?.Date;
    }

    private bool IsOrderOverdue(OperationalPurchasingDocumentRecord order)
    {
        var planned = ResolvePlannedDate(order);
        return planned.HasValue
               && planned.Value.Date < DateTime.Today
               && !IsOrderClosed(order);
    }

    private bool IsInvoiceOverdue(OperationalPurchasingDocumentRecord invoice)
    {
        return invoice.DueDate.HasValue
               && invoice.DueDate.Value.Date < DateTime.Today
               && !IsInvoicePaid(invoice);
    }

    private bool IsReceiptOverdue(OperationalPurchasingDocumentRecord receipt)
    {
        if (IsReceiptCompleted(receipt))
        {
            return false;
        }

        var order = GetOrderById(receipt.RelatedOrderId);
        return order is not null && IsOrderOverdue(order);
    }

    private bool IsDocumentOverdue(OperationalPurchasingDocumentRecord document)
    {
        return Ui(document.DocumentType) switch
        {
            "Счет поставщика" => IsInvoiceOverdue(document),
            "Приемка" => IsReceiptOverdue(document),
            _ => IsOrderOverdue(document)
        };
    }

    private static bool HasDiscrepancy(OperationalPurchasingDocumentRecord document)
    {
        return Ui(document.Comment).Contains("расхожд", StringComparison.OrdinalIgnoreCase);
    }

    private bool MissingInvoiceForDocument(OperationalPurchasingDocumentRecord document)
    {
        if (Ui(document.DocumentType).Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase))
        {
            return !GetInvoicesForOrder(document.Id).Any();
        }

        if (Ui(document.DocumentType).Equals("Приемка", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(document.RelatedOrderNumber) && GetInvoiceForOrder(document.RelatedOrderId) is null;
        }

        return false;
    }

    private bool MissingReceiptForDocument(OperationalPurchasingDocumentRecord document)
    {
        if (Ui(document.DocumentType).Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase))
        {
            return !GetReceiptsForOrder(document.Id).Any();
        }

        if (Ui(document.DocumentType).Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(document.RelatedOrderNumber) && GetReceiptForOrder(document.RelatedOrderId) is null;
        }

        return false;
    }

    private bool UnpaidForDocument(OperationalPurchasingDocumentRecord document)
    {
        if (Ui(document.DocumentType).Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase))
        {
            return !IsInvoicePaid(document);
        }

        if (Ui(document.DocumentType).Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase))
        {
            return GetInvoicesForOrder(document.Id).Any(item => !IsInvoicePaid(item));
        }

        if (Ui(document.DocumentType).Equals("Приемка", StringComparison.OrdinalIgnoreCase))
        {
            var invoice = GetInvoiceForOrder(document.RelatedOrderId);
            return invoice is not null && !IsInvoicePaid(invoice);
        }

        return false;
    }

    private string ResolveResponsible(string entityType, Guid entityId)
    {
        return _workspace.OperationLog
                   .Where(item => item.EntityId == entityId && Ui(item.EntityType).Equals(Ui(entityType), StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(item => item.LoggedAt)
                   .Select(item => Ui(item.Actor))
                   .FirstOrDefault()
               ?? GetCurrentOperator();
    }

    private DateTime ResolveUpdatedAt(string entityType, Guid entityId, DateTime fallback)
    {
        return _workspace.OperationLog
                   .Where(item => item.EntityId == entityId && Ui(item.EntityType).Equals(Ui(entityType), StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(item => item.LoggedAt)
                   .Select(item => item.LoggedAt)
                   .FirstOrDefault()
               == default
            ? fallback
            : _workspace.OperationLog
                .Where(item => item.EntityId == entityId && Ui(item.EntityType).Equals(Ui(entityType), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.LoggedAt)
                .Select(item => item.LoggedAt)
                .First();
    }

    private string ResolveDominantWarehouse(
        IEnumerable<OperationalPurchasingDocumentRecord> orders,
        IEnumerable<OperationalPurchasingDocumentRecord> invoices,
        IEnumerable<OperationalPurchasingDocumentRecord> receipts)
    {
        return orders
                   .Concat(invoices)
                   .Concat(receipts)
                   .Select(item => Ui(item.Warehouse))
                   .Where(item => !string.IsNullOrWhiteSpace(item))
                   .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
                   .OrderByDescending(group => group.Count())
                   .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                   .Select(group => group.Key)
                   .FirstOrDefault()
               ?? "-";
    }

    private string ResolveDiscrepancyStatus(OperationalPurchasingDocumentRecord document)
    {
        if (HasDiscrepancy(document))
        {
            return "Есть расхождения";
        }

        if (IsDocumentOverdue(document))
        {
            return "Просрочено";
        }

        if (MissingInvoiceForDocument(document))
        {
            return "Без счета";
        }

        if (MissingReceiptForDocument(document))
        {
            return "Без приемки";
        }

        if (UnpaidForDocument(document))
        {
            return "Частично оплачено";
        }

        return Ui(document.Status);
    }

    private string ResolveJournalSection(PurchasingOperationLogEntry entry)
    {
        var entityType = Ui(entry.EntityType);
        if (entityType.Equals("Поставщик", StringComparison.OrdinalIgnoreCase))
        {
            return "Поставщики";
        }

        if (entityType.Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase))
        {
            return "Счета";
        }

        if (entityType.Equals("Приемка", StringComparison.OrdinalIgnoreCase))
        {
            return "Приемки";
        }

        return "Заказы";
    }

    private string ResolveSupplierNameForJournal(PurchasingOperationLogEntry entry)
    {
        if (_workspace.PurchaseOrders.FirstOrDefault(item => item.Id == entry.EntityId) is { } order)
        {
            return order.SupplierName;
        }

        if (_workspace.SupplierInvoices.FirstOrDefault(item => item.Id == entry.EntityId) is { } invoice)
        {
            return invoice.SupplierName;
        }

        if (_workspace.PurchaseReceipts.FirstOrDefault(item => item.Id == entry.EntityId) is { } receipt)
        {
            return receipt.SupplierName;
        }

        if (_workspace.Suppliers.FirstOrDefault(item => item.Id == entry.EntityId) is { } supplier)
        {
            return supplier.Name;
        }

        return string.Empty;
    }

    private string ResolveWarehouseForJournal(PurchasingOperationLogEntry entry)
    {
        if (_workspace.PurchaseOrders.FirstOrDefault(item => item.Id == entry.EntityId) is { } order)
        {
            return order.Warehouse;
        }

        if (_workspace.SupplierInvoices.FirstOrDefault(item => item.Id == entry.EntityId) is { } invoice)
        {
            return invoice.Warehouse;
        }

        if (_workspace.PurchaseReceipts.FirstOrDefault(item => item.Id == entry.EntityId) is { } receipt)
        {
            return receipt.Warehouse;
        }

        return string.Empty;
    }

    private void RefreshDetails(PurchasingGridRow? row)
    {
        if (row is null)
        {
            DetailsPlaceholderPanel.Visibility = Visibility.Visible;
            DetailsScrollViewer.Visibility = Visibility.Collapsed;
            UpdateLockBanner(null);
            return;
        }

        DetailsPlaceholderPanel.Visibility = Visibility.Collapsed;
        DetailsScrollViewer.Visibility = Visibility.Visible;
        DetailsScrollViewer.ScrollToHome();

        switch (row.Section)
        {
            case SuppliersSection:
                RefreshSupplierDetails((OperationalPurchasingSupplierRecord)row.Payload);
                break;
            case JournalSection:
                RefreshJournalDetails((PurchasingOperationLogEntry)row.Payload);
                break;
            case PaymentsSection:
                RefreshDocumentDetails((OperationalPurchasingDocumentRecord)row.Payload, isPaymentView: true);
                break;
            default:
                RefreshDocumentDetails((OperationalPurchasingDocumentRecord)row.Payload, isPaymentView: false);
                break;
        }

        ConfigureCardActions(row);
        UpdateLockBanner(row);
    }

    private void RefreshSupplierDetails(OperationalPurchasingSupplierRecord supplier)
    {
        var orders = _workspace.PurchaseOrders.Where(item => item.SupplierId == supplier.Id).OrderByDescending(item => item.DocumentDate).ToArray();
        var invoices = _workspace.SupplierInvoices.Where(item => item.SupplierId == supplier.Id).OrderByDescending(item => item.DocumentDate).ToArray();
        var receipts = _workspace.PurchaseReceipts.Where(item => item.SupplierId == supplier.Id).OrderByDescending(item => item.DocumentDate).ToArray();
        var latestOrder = orders.FirstOrDefault();
        var latestInvoice = invoices.FirstOrDefault();
        var latestReceipt = receipts.FirstOrDefault();
        var paid = invoices.Where(IsInvoicePaid).Sum(item => item.TotalAmount);
        var amount = orders.Sum(item => item.TotalAmount);
        var balance = Math.Max(amount - paid, 0m);
        var updatedAt = ResolveUpdatedAt("Поставщик", supplier.Id, latestOrder?.DocumentDate ?? DateTime.Today);

        DetailsTitleText.Text = Ui(supplier.Name);
        DetailsSubtitleText.Text = string.Join(" ? ", new[]
        {
            EmptyAsDash(supplier.TaxId),
            EmptyAsDash(supplier.Phone),
            EmptyAsDash(supplier.Email)
        }.Where(item => item != "-"));

        ApplyBadge(DetailsStatusBadge, DetailsStatusBadgeText, supplier.Status);
        DetailsSupplierText.Text = Ui(supplier.Name);
        DetailsWarehouseText.Text = ResolveDominantWarehouse(orders, invoices, receipts);
        DetailsCreatedText.Text = latestOrder?.DocumentDate.ToString("dd.MM.yyyy", RuCulture) ?? "-";
        DetailsPlannedText.Text = ResolvePlannedDate(latestOrder ?? new OperationalPurchasingDocumentRecord())?.ToString("dd.MM.yyyy", RuCulture) ?? "-";
        DetailsNumberText.Text = Ui(supplier.Code);
        DetailsResponsibleText.Text = ResolveResponsible("Поставщик", supplier.Id);
        DetailsSourceText.Text = EmptyAsDash(supplier.SourceLabel);
        DetailsContractText.Text = EmptyAsDash(supplier.Contract);
        DetailsAmountText.Text = FormatMoney(amount);
        DetailsPaidText.Text = FormatMoney(paid);
        DetailsBalanceText.Text = FormatMoney(balance);
        DetailsCommentText.Text = string.Join(Environment.NewLine, new[]
        {
            supplier.Phone,
            supplier.Email
        }.Where(item => !string.IsNullOrWhiteSpace(item)));
        DetailsMetaResponsibleText.Text = ResolveResponsible("Поставщик", supplier.Id);
        DetailsCreatedByText.Text = ResolveResponsible("Поставщик", supplier.Id);
        DetailsUpdatedText.Text = updatedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);

        SetLinkedButton(LinkedOrderButton, latestOrder is null ? "Заказ: не создан" : $"Заказ: {latestOrder.Number}", latestOrder is null ? null : new LinkedTarget(OrdersSection, latestOrder.Id, latestOrder.Number));
        SetLinkedButton(LinkedInvoiceButton, latestInvoice is null ? "Счет: не создан" : $"Счет: {latestInvoice.Number}", latestInvoice is null ? null : new LinkedTarget(InvoicesSection, latestInvoice.Id, latestInvoice.Number));
        SetLinkedButton(LinkedReceiptButton, latestReceipt is null ? "Приемка: не создана" : $"Приемка: {latestReceipt.Number}", latestReceipt is null ? null : new LinkedTarget(ReceiptsSection, latestReceipt.Id, latestReceipt.Number));
        SetLinkedButton(LinkedPaymentButton, latestInvoice is null ? "Оплата: не создана" : $"Оплата: {latestInvoice.Number}", latestInvoice is null ? null : new LinkedTarget(PaymentsSection, latestInvoice.Id, latestInvoice.Number));

        RenderChain(latestOrder, latestInvoice, latestReceipt, latestInvoice);
        RenderDetailLines((latestOrder?.Lines ?? latestInvoice?.Lines ?? latestReceipt?.Lines)?.ToArray() ?? Array.Empty<OperationalPurchasingLineRecord>());
    }

    private void RefreshJournalDetails(PurchasingOperationLogEntry entry)
    {
        var linkedOrder = _workspace.PurchaseOrders.FirstOrDefault(item => item.Id == entry.EntityId);
        var linkedInvoice = _workspace.SupplierInvoices.FirstOrDefault(item => item.Id == entry.EntityId);
        var linkedReceipt = _workspace.PurchaseReceipts.FirstOrDefault(item => item.Id == entry.EntityId);
        var linkedSupplier = _workspace.Suppliers.FirstOrDefault(item => item.Id == entry.EntityId);

        DetailsTitleText.Text = Ui(entry.Action);
        DetailsSubtitleText.Text = Ui(entry.Message);
        ApplyBadge(DetailsStatusBadge, DetailsStatusBadgeText, entry.Result);
        DetailsSupplierText.Text = linkedOrder?.SupplierName ?? linkedInvoice?.SupplierName ?? linkedReceipt?.SupplierName ?? linkedSupplier?.Name ?? "-";
        DetailsWarehouseText.Text = linkedOrder?.Warehouse ?? linkedInvoice?.Warehouse ?? linkedReceipt?.Warehouse ?? "-";
        DetailsCreatedText.Text = entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);
        DetailsPlannedText.Text = "-";
        DetailsNumberText.Text = Ui(entry.EntityNumber);
        DetailsResponsibleText.Text = Ui(entry.Actor);
        DetailsSourceText.Text = Ui(entry.EntityType);
        DetailsContractText.Text = linkedSupplier?.Contract ?? linkedOrder?.Contract ?? linkedInvoice?.Contract ?? linkedReceipt?.Contract ?? "-";
        DetailsAmountText.Text = linkedOrder is not null
            ? FormatMoney(linkedOrder.TotalAmount)
            : linkedInvoice is not null
                ? FormatMoney(linkedInvoice.TotalAmount)
                : linkedReceipt is not null
                    ? FormatMoney(linkedReceipt.TotalAmount)
                    : "0 ₽";
        DetailsPaidText.Text = linkedInvoice is not null && IsInvoicePaid(linkedInvoice) ? FormatMoney(linkedInvoice.TotalAmount) : "0 ₽";
        DetailsBalanceText.Text = linkedInvoice is not null && !IsInvoicePaid(linkedInvoice) ? FormatMoney(linkedInvoice.TotalAmount) : "0 ₽";
        DetailsCommentText.Text = Ui(entry.Message);
        DetailsMetaResponsibleText.Text = Ui(entry.Actor);
        DetailsCreatedByText.Text = Ui(entry.Actor);
        DetailsUpdatedText.Text = entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);

        var order = linkedOrder ?? (linkedInvoice is not null ? GetOrderById(linkedInvoice.RelatedOrderId) : linkedReceipt is not null ? GetOrderById(linkedReceipt.RelatedOrderId) : null);
        var invoice = linkedInvoice ?? (order is not null ? GetInvoiceForOrder(order.Id) : null);
        var receipt = linkedReceipt ?? (order is not null ? GetReceiptForOrder(order.Id) : null);
        RenderChain(order, invoice, receipt, invoice);
        SetLinkedButton(LinkedOrderButton, order is null ? "Заказ: не найден" : $"Заказ: {order.Number}", order is null ? null : new LinkedTarget(OrdersSection, order.Id, order.Number));
        SetLinkedButton(LinkedInvoiceButton, invoice is null ? "Счет: не создан" : $"Счет: {invoice.Number}", invoice is null ? null : new LinkedTarget(InvoicesSection, invoice.Id, invoice.Number));
        SetLinkedButton(LinkedReceiptButton, receipt is null ? "Приемка: не найдена" : $"Приемка: {receipt.Number}", receipt is null ? null : new LinkedTarget(ReceiptsSection, receipt.Id, receipt.Number));
        SetLinkedButton(LinkedPaymentButton, invoice is null ? "Оплата: не найдена" : $"Оплата: {invoice.Number}", invoice is null ? null : new LinkedTarget(PaymentsSection, invoice.Id, invoice.Number));
        RenderDetailLines((linkedOrder?.Lines ?? linkedInvoice?.Lines ?? linkedReceipt?.Lines)?.ToArray() ?? Array.Empty<OperationalPurchasingLineRecord>());
    }

    private void RefreshDocumentDetails(OperationalPurchasingDocumentRecord document, bool isPaymentView)
    {
        var order = Ui(document.DocumentType).Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase)
            ? document
            : GetOrderById(document.RelatedOrderId);
        var invoice = Ui(document.DocumentType).Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase) || isPaymentView
            ? document
            : order is not null
                ? GetInvoiceForOrder(order.Id)
                : null;
        var receipt = Ui(document.DocumentType).Equals("Приемка", StringComparison.OrdinalIgnoreCase)
            ? document
            : order is not null
                ? GetReceiptForOrder(order.Id)
                : null;

        var paid = invoice is not null && IsInvoicePaid(invoice) ? invoice.TotalAmount : 0m;
        var amount = document.TotalAmount;
        var balance = invoice is not null ? Math.Max(invoice.TotalAmount - paid, 0m) : Math.Max(amount - paid, 0m);
        var updatedAt = ResolveUpdatedAt(document.DocumentType, document.Id, document.DocumentDate);

        DetailsTitleText.Text = Ui(document.Number);
        DetailsSubtitleText.Text = Ui(document.SupplierName);
        ApplyBadge(DetailsStatusBadge, DetailsStatusBadgeText, isPaymentView ? (IsInvoicePaid(document) ? "Проведена" : "Ожидает оплаты") : document.Status);
        DetailsSupplierText.Text = Ui(document.SupplierName);
        DetailsWarehouseText.Text = Ui(document.Warehouse);
        DetailsCreatedText.Text = document.DocumentDate.ToString("dd.MM.yyyy HH:mm", RuCulture);
        DetailsPlannedText.Text = (ResolvePlannedDate(order ?? document) ?? document.DueDate)?.ToString("dd.MM.yyyy", RuCulture) ?? "-";
        DetailsNumberText.Text = Ui(document.Number);
        DetailsResponsibleText.Text = ResolveResponsible(document.DocumentType, document.Id);
        DetailsSourceText.Text = EmptyAsDash(document.SourceLabel);
        DetailsContractText.Text = EmptyAsDash(document.Contract);
        DetailsAmountText.Text = FormatMoney(amount);
        DetailsPaidText.Text = FormatMoney(paid);
        DetailsBalanceText.Text = FormatMoney(balance);
        DetailsCommentText.Text = EmptyAsDash(document.Comment);
        DetailsMetaResponsibleText.Text = ResolveResponsible(document.DocumentType, document.Id);
        DetailsCreatedByText.Text = ResolveResponsible(document.DocumentType, document.Id);
        DetailsUpdatedText.Text = updatedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);

        RenderChain(order, invoice, receipt, invoice);
        SetLinkedButton(LinkedOrderButton, order is null ? "Заказ: не создан" : $"Заказ: {order.Number}", order is null ? null : new LinkedTarget(OrdersSection, order.Id, order.Number));
        SetLinkedButton(LinkedInvoiceButton, invoice is null ? "Счет: не создан" : $"Счет: {invoice.Number}", invoice is null ? null : new LinkedTarget(InvoicesSection, invoice.Id, invoice.Number));
        SetLinkedButton(LinkedReceiptButton, receipt is null ? "Приемка: не создана" : $"Приемка: {receipt.Number}", receipt is null ? null : new LinkedTarget(ReceiptsSection, receipt.Id, receipt.Number));
        SetLinkedButton(LinkedPaymentButton, invoice is null ? "Оплата: не создана" : $"Оплата: {invoice.Number}", invoice is null ? null : new LinkedTarget(PaymentsSection, invoice.Id, invoice.Number));
        RenderDetailLines(document.Lines.ToArray());
    }

    private void RenderDetailLines(IReadOnlyList<OperationalPurchasingLineRecord> lines)
    {
        _detailLines.Clear();
        foreach (var line in lines.Take(12))
        {
            _detailLines.Add(new PurchasingDetailLineRow(
                Ui(line.ItemName),
                line.Quantity.ToString("N0", RuCulture),
                string.IsNullOrWhiteSpace(line.Unit) ? "шт" : Ui(line.Unit),
                FormatMoney(line.Price),
                FormatMoney(line.Amount)));
        }

        PositionsTitleText.Text = $"Позиции ({lines.Count:N0})";
        ShowAllPositionsText.Visibility = lines.Count > 12 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderChain(
        OperationalPurchasingDocumentRecord? order,
        OperationalPurchasingDocumentRecord? invoice,
        OperationalPurchasingDocumentRecord? receipt,
        OperationalPurchasingDocumentRecord? paymentInvoice)
    {
        ApplyChainStep(OrderStepCircle, OrderStepCircleText, OrderStepBadge, OrderStepBadgeText, OrderStepMetaText, 1, "Заказ", order?.Number, order is null ? "Не создан" : ResolveOrderChainStatus(order));
        ApplyChainStep(InvoiceStepCircle, InvoiceStepCircleText, InvoiceStepBadge, InvoiceStepBadgeText, InvoiceStepMetaText, 2, "Счет", invoice?.Number, invoice is null ? "Не создан" : ResolveInvoiceChainStatus(invoice));
        ApplyChainStep(ReceiptStepCircle, ReceiptStepCircleText, ReceiptStepBadge, ReceiptStepBadgeText, ReceiptStepMetaText, 3, "Приемка", receipt?.Number, receipt is null ? "Не создан" : ResolveReceiptChainStatus(receipt));
        ApplyChainStep(PaymentStepCircle, PaymentStepCircleText, PaymentStepBadge, PaymentStepBadgeText, PaymentStepMetaText, 4, "Оплата", paymentInvoice?.Number, paymentInvoice is null ? "Не создан" : ResolvePaymentChainStatus(paymentInvoice));
    }

    private void ApplyChainStep(
        Border circle,
        TextBlock circleText,
        Border badge,
        TextBlock badgeText,
        TextBlock metaText,
        int stepNumber,
        string title,
        string? number,
        string status)
    {
        var normalizedStatus = Ui(status);
        Brush circleBackground;
        Brush circleForeground;

        if (normalizedStatus.Equals("Проведен", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Equals("Проведена", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Equals("Выполнено", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Equals("Принят", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Equals("Оплачен", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Equals("Размещена", StringComparison.OrdinalIgnoreCase))
        {
            circleBackground = SuccessSoftBrush;
            circleForeground = SuccessBrush;
        }
        else if (normalizedStatus.Equals("Не создан", StringComparison.OrdinalIgnoreCase))
        {
            circleBackground = NeutralSoftBrush;
            circleForeground = NeutralBrush;
        }
        else if (normalizedStatus.Equals("Просрочен", StringComparison.OrdinalIgnoreCase)
                 || normalizedStatus.Equals("Просрочена", StringComparison.OrdinalIgnoreCase)
                 || normalizedStatus.Equals("Просрочено", StringComparison.OrdinalIgnoreCase))
        {
            circleBackground = DangerSoftBrush;
            circleForeground = DangerBrush;
        }
        else
        {
            circleBackground = WarningSoftBrush;
            circleForeground = WarningBrush;
        }

        circle.Background = circleBackground;
        circleText.Foreground = circleForeground;
        circleText.Text = stepNumber.ToString(RuCulture);
        badge.Background = circleBackground;
        badgeText.Foreground = circleForeground;
        badgeText.Text = normalizedStatus;
        metaText.Text = string.IsNullOrWhiteSpace(number)
            ? "Не создан"
            : $"{title}: {Ui(number)}";
    }

    private static string ResolveOrderChainStatus(OperationalPurchasingDocumentRecord order)
    {
        if (IsClosedStatus(order.Status, "Принят"))
        {
            return "Принят";
        }

        if (Ui(order.Status).Equals("Заказан", StringComparison.OrdinalIgnoreCase)
            || Ui(order.Status).Equals("Размещена?", StringComparison.OrdinalIgnoreCase))
        {
            return "В процессе";
        }

        return Ui(order.Status);
    }

    private static string ResolveInvoiceChainStatus(OperationalPurchasingDocumentRecord invoice)
    {
        if (IsInvoicePaidStatic(invoice))
        {
            return "Оплачен";
        }

        if (Ui(invoice.Status).Equals("К оплате", StringComparison.OrdinalIgnoreCase))
        {
            return "Ожидается";
        }

        if (Ui(invoice.Status).Equals("Получен", StringComparison.OrdinalIgnoreCase))
        {
            return "Получен";
        }

        return Ui(invoice.Status);
    }

    private static string ResolveReceiptChainStatus(OperationalPurchasingDocumentRecord receipt)
    {
        if (Ui(receipt.Status).Equals("Размещена", StringComparison.OrdinalIgnoreCase))
        {
            return "Проведена";
        }

        if (Ui(receipt.Status).Equals("Принята", StringComparison.OrdinalIgnoreCase))
        {
            return "Принята";
        }

        return Ui(receipt.Status);
    }

    private static string ResolvePaymentChainStatus(OperationalPurchasingDocumentRecord invoice)
    {
        return IsInvoicePaidStatic(invoice) ? "Проведена" : "Не создан";
    }

    private static bool IsClosedStatus(string status, string expected)
    {
        return Ui(status).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvoicePaidStatic(OperationalPurchasingDocumentRecord invoice)
    {
        return Ui(invoice.Status).Equals("Оплачен", StringComparison.OrdinalIgnoreCase);
    }

    private void SetLinkedButton(WpfButton button, string caption, LinkedTarget? target)
    {
        button.Content = caption;
        button.Tag = target;
        button.IsEnabled = target is not null;
        button.Opacity = target is null ? 0.65 : 1;
    }

    private void ApplyBadge(Border badge, TextBlock text, string status)
    {
        var (background, foreground) = ResolveStatusBrushes(status);
        badge.Background = background;
        text.Foreground = foreground;
        text.Text = Ui(status);
    }

    private void ConfigureCardActions(PurchasingGridRow row)
    {
        var document = ResolveDocumentForRow(row);
        var invoice = ResolveInvoiceForRow(row);
        var receipt = ResolveReceiptForRow(row);
        var order = ResolveOrderForRow(row);

        PrimaryCardActionButton.Content = "Открыть";
        PrimaryCardActionButton.IsEnabled = true;
        CardCreateReceiptButton.Content = "Создать приемку";
        CardCreateReceiptButton.IsEnabled = false;
        CardPayButton.Content = "Провести оплату";
        CardPayButton.IsEnabled = false;
        CardEditButton.IsEnabled = true;
        CardSendButton.IsEnabled = true;
        CardDiscrepancyButton.IsEnabled = true;
        CardPrintButton.IsEnabled = document is not null;
        CardCloseButton.IsEnabled = document is not null;
        CardCancelButton.IsEnabled = document is not null || row.Section == SuppliersSection;
        CardCancelButton.Content = row.Section == SuppliersSection ? "Пауза" : "Отменить";
        _primaryCardAction = PurchasingCardAction.None;

        switch (row.Section)
        {
            case OrdersSection:
                PrimaryCardActionButton.Content = "Зарегистрировать счет";
                PrimaryCardActionButton.IsEnabled = true;
                _primaryCardAction = PurchasingCardAction.CreateInvoice;
                CardCreateReceiptButton.IsEnabled = true;
                CardPayButton.IsEnabled = invoice is not null;
                CardEditButton.IsEnabled = !row.IsDisabled;
                CardSendButton.IsEnabled = !row.IsDisabled;
                CardCloseButton.IsEnabled = !row.IsDisabled;
                CardCancelButton.IsEnabled = !row.IsDisabled;
                break;
            case SuppliersSection:
                PrimaryCardActionButton.Content = "Новый заказ";
                PrimaryCardActionButton.IsEnabled = true;
                _primaryCardAction = PurchasingCardAction.CreateOrder;
                CardCreateReceiptButton.IsEnabled = false;
                CardPayButton.IsEnabled = false;
                CardPrintButton.IsEnabled = false;
                CardCloseButton.IsEnabled = false;
                break;
            case InvoicesSection:
                if (document is null)
                {
                    break;
                }

                if (Ui(document.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
                {
                    PrimaryCardActionButton.Content = "Зарегистрировать счет";
                    _primaryCardAction = PurchasingCardAction.MarkInvoiceReceived;
                }
                else if (Ui(document.Status).Equals("Получен", StringComparison.OrdinalIgnoreCase))
                {
                    PrimaryCardActionButton.Content = "Передать к оплате";
                    _primaryCardAction = PurchasingCardAction.MarkInvoicePayable;
                }
                else if (!IsInvoicePaid(document))
                {
                    PrimaryCardActionButton.Content = "Провести оплату";
                    _primaryCardAction = PurchasingCardAction.PayInvoice;
                }
                else
                {
                    PrimaryCardActionButton.Content = "Счет оплачен";
                    PrimaryCardActionButton.IsEnabled = false;
                }

                CardCreateReceiptButton.IsEnabled = order is not null;
                CardPayButton.IsEnabled = !IsInvoicePaid(document);
                CardEditButton.IsEnabled = !IsInvoicePaid(document);
                CardCloseButton.IsEnabled = !IsInvoicePaid(document);
                CardCancelButton.IsEnabled = !IsInvoicePaid(document);
                break;
            case ReceiptsSection:
                if (document is null)
                {
                    break;
                }

                if (Ui(document.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
                {
                    PrimaryCardActionButton.Content = "Принять приемку";
                    _primaryCardAction = PurchasingCardAction.ReceiveReceipt;
                }
                else if (Ui(document.Status).Equals("Принята", StringComparison.OrdinalIgnoreCase))
                {
                    PrimaryCardActionButton.Content = "Разместить приемку";
                    _primaryCardAction = PurchasingCardAction.PlaceReceipt;
                }
                else
                {
                    PrimaryCardActionButton.Content = "Приемка размещена";
                    PrimaryCardActionButton.IsEnabled = false;
                }

                CardCreateReceiptButton.IsEnabled = false;
                CardPayButton.IsEnabled = invoice is not null && !IsInvoicePaid(invoice);
                CardEditButton.IsEnabled = !row.IsDisabled;
                CardSendButton.IsEnabled = false;
                CardCloseButton.IsEnabled = !row.IsDisabled;
                CardCancelButton.IsEnabled = !row.IsDisabled;
                break;
            case PaymentsSection:
                PrimaryCardActionButton.Content = invoice is not null && !IsInvoicePaid(invoice) ? "Провести оплату" : "Оплата проведена";
                PrimaryCardActionButton.IsEnabled = invoice is not null && !IsInvoicePaid(invoice);
                _primaryCardAction = PrimaryCardActionButton.IsEnabled ? PurchasingCardAction.PayInvoice : PurchasingCardAction.None;
                CardCreateReceiptButton.IsEnabled = order is not null && receipt is null;
                CardPayButton.IsEnabled = invoice is not null && !IsInvoicePaid(invoice);
                CardEditButton.IsEnabled = false;
                CardSendButton.IsEnabled = false;
                CardCloseButton.IsEnabled = false;
                CardCancelButton.IsEnabled = false;
                break;
            case DiscrepanciesSection:
                PrimaryCardActionButton.Content = "Открыть документ";
                _primaryCardAction = PurchasingCardAction.OpenDocument;
                CardCreateReceiptButton.IsEnabled = order is not null && receipt is null;
                CardPayButton.IsEnabled = invoice is not null && !IsInvoicePaid(invoice);
                CardEditButton.IsEnabled = document is not null;
                CardSendButton.IsEnabled = order is not null && !row.IsDisabled;
                CardCloseButton.IsEnabled = document is not null;
                CardCancelButton.IsEnabled = document is not null;
                break;
            case JournalSection:
                PrimaryCardActionButton.Content = "Открыть объект";
                _primaryCardAction = PurchasingCardAction.OpenDocument;
                PrimaryCardActionButton.IsEnabled = ResolveLinkedTargetFromJournal((PurchasingOperationLogEntry)row.Payload) is not null;
                CardCreateReceiptButton.IsEnabled = false;
                CardPayButton.IsEnabled = false;
                CardEditButton.IsEnabled = false;
                CardSendButton.IsEnabled = false;
                CardDiscrepancyButton.IsEnabled = false;
                CardPrintButton.IsEnabled = false;
                CardCloseButton.IsEnabled = false;
                CardCancelButton.IsEnabled = false;
                break;
        }
    }

    private void UpdateLockBanner(PurchasingGridRow? row)
    {
        if (row is null)
        {
            LockBannerBorder.Visibility = Visibility.Collapsed;
            return;
        }

        string? message = null;
        if (row.Section == JournalSection)
        {
            message = "Журнал операций доступен только для просмотра.";
        }
        else if (row.IsDisabled)
        {
            message = "Документ закрыт, оплачен, размещен или поставщик переведен на паузу.";
        }
        else if (row.Section == PaymentsSection && ResolveInvoiceForRow(row) is { } invoice && IsInvoicePaid(invoice))
        {
            message = "Оплата уже проведена. Изменение доступно только через связанные документы.";
        }

        if (string.IsNullOrWhiteSpace(message) || string.Equals(_dismissedLockKey, row.SelectionKey, StringComparison.OrdinalIgnoreCase))
        {
            LockBannerBorder.Visibility = Visibility.Collapsed;
            return;
        }

        LockBannerText.Text = message;
        LockBannerBorder.Visibility = Visibility.Visible;
    }

    private Guid? ResolveSelectedSupplierId()
    {
        return GetCurrentRow()?.Payload switch
        {
            OperationalPurchasingSupplierRecord supplier => supplier.Id,
            OperationalPurchasingDocumentRecord document when document.SupplierId != Guid.Empty => document.SupplierId,
            _ => null
        };
    }

    private OperationalPurchasingDocumentRecord? ResolveDocumentForRow(PurchasingGridRow? row)
    {
        return row?.Payload switch
        {
            OperationalPurchasingDocumentRecord document => document,
            _ => null
        };
    }

    private OperationalPurchasingDocumentRecord? ResolveOrderForRow(PurchasingGridRow? row)
    {
        if (row?.Payload is OperationalPurchasingDocumentRecord document)
        {
            if (Ui(document.DocumentType).Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }

            if (document.RelatedOrderId != Guid.Empty)
            {
                return GetOrderById(document.RelatedOrderId);
            }
        }

        if (row?.Payload is OperationalPurchasingSupplierRecord supplier)
        {
            return _workspace.PurchaseOrders
                .Where(item => item.SupplierId == supplier.Id)
                .OrderByDescending(item => item.DocumentDate)
                .FirstOrDefault();
        }

        if (row?.Payload is PurchasingOperationLogEntry entry)
        {
            return _workspace.PurchaseOrders.FirstOrDefault(item => item.Id == entry.EntityId)
                   ?? _workspace.PurchaseOrders.FirstOrDefault(item => Ui(item.Number).Equals(Ui(entry.EntityNumber), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private OperationalPurchasingDocumentRecord? ResolveInvoiceForRow(PurchasingGridRow? row)
    {
        if (row?.Payload is OperationalPurchasingDocumentRecord document)
        {
            if (Ui(document.DocumentType).Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }

            var orderId = document.RelatedOrderId != Guid.Empty ? document.RelatedOrderId : document.Id;
            return GetInvoiceForOrder(orderId);
        }

        if (row?.Payload is OperationalPurchasingSupplierRecord supplier)
        {
            return _workspace.SupplierInvoices
                .Where(item => item.SupplierId == supplier.Id)
                .OrderByDescending(item => item.DocumentDate)
                .FirstOrDefault();
        }

        if (row?.Payload is PurchasingOperationLogEntry entry)
        {
            return _workspace.SupplierInvoices.FirstOrDefault(item => item.Id == entry.EntityId)
                   ?? _workspace.SupplierInvoices.FirstOrDefault(item => Ui(item.Number).Equals(Ui(entry.EntityNumber), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private OperationalPurchasingDocumentRecord? ResolveReceiptForRow(PurchasingGridRow? row)
    {
        if (row?.Payload is OperationalPurchasingDocumentRecord document)
        {
            if (Ui(document.DocumentType).Equals("Приемка", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }

            var orderId = document.RelatedOrderId != Guid.Empty ? document.RelatedOrderId : document.Id;
            return GetReceiptForOrder(orderId);
        }

        if (row?.Payload is OperationalPurchasingSupplierRecord supplier)
        {
            return _workspace.PurchaseReceipts
                .Where(item => item.SupplierId == supplier.Id)
                .OrderByDescending(item => item.DocumentDate)
                .FirstOrDefault();
        }

        if (row?.Payload is PurchasingOperationLogEntry entry)
        {
            return _workspace.PurchaseReceipts.FirstOrDefault(item => item.Id == entry.EntityId)
                   ?? _workspace.PurchaseReceipts.FirstOrDefault(item => Ui(item.Number).Equals(Ui(entry.EntityNumber), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private LinkedTarget? ResolveLinkedTargetFromJournal(PurchasingOperationLogEntry entry)
    {
        if (_workspace.PurchaseOrders.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(OrdersSection, entry.EntityId, entry.EntityNumber);
        }

        if (_workspace.SupplierInvoices.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(InvoicesSection, entry.EntityId, entry.EntityNumber);
        }

        if (_workspace.PurchaseReceipts.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(ReceiptsSection, entry.EntityId, entry.EntityNumber);
        }

        if (_workspace.Suppliers.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(SuppliersSection, entry.EntityId, entry.EntityNumber);
        }

        return null;
    }

    private void HandleRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PurchasingGridRow row || e.PropertyName != nameof(PurchasingGridRow.IsChecked))
        {
            return;
        }

        if (row.IsChecked)
        {
            _checkedKeys.Add(row.SelectionKey);
        }
        else
        {
            _checkedKeys.Remove(row.SelectionKey);
        }

        UpdateBulkBar();
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearch)
        {
            return;
        }

        _syncingSearch = true;
        try
        {
            var source = (WpfTextBox)sender;
            if (!ReferenceEquals(source, HeaderSearchBox))
            {
                HeaderSearchBox.Text = source.Text;
            }

            if (!ReferenceEquals(source, TableSearchBox))
            {
                TableSearchBox.Text = source.Text;
            }
        }
        finally
        {
            _syncingSearch = false;
        }

        _page = 1;
        UpdateSearchPlaceholders();
        if (_initialized)
        {
            ApplyFilters(keepSelection: true);
        }
    }

    private void HandleImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт закупок",
            Filter = "CSV/TSV/TXT (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var imported = ImportPurchasingDocumentsFromDelimitedFile(dialog.FileName);
            TryPersistWorkspace();
            RefreshAll();
            ApplySection(OrdersSection, keepSelection: false, resetFilters: true);
            MessageBox.Show(
                Window.GetWindow(this),
                $"Импорт завершен. Загружено заказов: {imported:N0}.",
                "Закупки",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Не удалось импортировать файл.\n{ex.Message}",
                "Закупки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void HandleExportClick(object sender, RoutedEventArgs e)
    {
        ExportRows(_filteredRows, "Закупки");
    }

    private void HandleNewPurchaseClick(object sender, RoutedEventArgs e)
    {
        CreateNewPurchase();
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

    private void HandleDismissLockBannerClick(object sender, RoutedEventArgs e)
    {
        _dismissedLockKey = _selectedRowKey;
        UpdateLockBanner(GetCurrentRow());
    }

    private void HandleIssuePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string preset)
        {
            return;
        }

        switch (preset)
        {
            case "unpaid":
                ApplySection(PaymentsSection, keepSelection: false, resetFilters: true);
                UnpaidOnlyCheckBox.IsChecked = true;
                break;
            case "discrepancy":
                ApplySection(DiscrepanciesSection, keepSelection: false, resetFilters: true);
                break;
            case "missing-receipt":
                ApplySection(OrdersSection, keepSelection: false, resetFilters: true);
                MissingReceiptOnlyCheckBox.IsChecked = true;
                break;
            case "missing-invoice":
                ApplySection(OrdersSection, keepSelection: false, resetFilters: true);
                MissingInvoiceOnlyCheckBox.IsChecked = true;
                break;
            default:
                ApplySection(OrdersSection, keepSelection: false, resetFilters: true);
                OverdueOnlyCheckBox.IsChecked = true;
                break;
        }

        _page = 1;
        ApplyFilters(keepSelection: false);
    }

    private void HandleTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string section)
        {
            ApplySection(section, keepSelection: false, resetFilters: true);
        }
    }

    private void HandleFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressFilters || !_initialized)
        {
            return;
        }

        _page = 1;
        ApplyFilters(keepSelection: true);
    }

    private void HandleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilters || !_initialized)
        {
            return;
        }

        _page = 1;
        ApplyFilters(keepSelection: true);
    }

    private void HandleResetFiltersClick(object sender, RoutedEventArgs e)
    {
        ResetFilters(clearSearch: false);
    }

    private void ResetFilters(bool clearSearch)
    {
        _suppressFilters = true;
        try
        {
            if (clearSearch)
            {
                HeaderSearchBox.Text = string.Empty;
                TableSearchBox.Text = string.Empty;
            }

            StatusFilterCombo.SelectedIndex = 0;
            SupplierFilterCombo.SelectedIndex = 0;
            WarehouseFilterCombo.SelectedIndex = 0;
            DocumentTypeFilterCombo.SelectedIndex = 0;
            OverdueOnlyCheckBox.IsChecked = false;
            MissingInvoiceOnlyCheckBox.IsChecked = false;
            MissingReceiptOnlyCheckBox.IsChecked = false;
            UnpaidOnlyCheckBox.IsChecked = false;
            DateFromPicker.SelectedDate = _defaultDateFrom;
            DateToPicker.SelectedDate = _defaultDateTo;
        }
        finally
        {
            _suppressFilters = false;
        }

        UpdateSearchPlaceholders();
        _page = 1;
        ApplyFilters(keepSelection: false);
    }

    private void HandleClearSelectionClick(object sender, RoutedEventArgs e)
    {
        ClearCheckedRows();
    }

    private void HandleExportSelectedClick(object sender, RoutedEventArgs e)
    {
        ExportRows(GetCheckedOrCurrentRows(), "Закупки");
    }

    private void HandleBulkStatusClick(object sender, RoutedEventArgs e)
    {
        var rows = GetCheckedRows().ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите записи для массового изменения статуса.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var statuses = ResolveStatusesForSection(_activeSection);
        if (statuses.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Для текущей вкладки массовая смена статуса не поддерживается.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var value = PromptText("Изменить статус", $"Новый статус для выбранных записей: {rows.Length:N0}.", statuses[0], statuses);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var row in rows)
        {
            ApplyStatusToRow(row, value, "Массовое изменение статуса");
        }

        TryPersistWorkspace();
        RefreshAll();
    }

    private void HandleBulkWarehouseClick(object sender, RoutedEventArgs e)
    {
        var rows = GetCheckedRows().ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите документы для смены склада.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var value = PromptText("Назначить склад", "Выберите склад для выбранных документов.", _workspace.Warehouses.FirstOrDefault(), _workspace.Warehouses);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var row in rows)
        {
            UpdateWarehouseForRow(row, value);
        }

        TryPersistWorkspace();
        RefreshAll();
    }

    private void HandleBulkPrintClick(object sender, RoutedEventArgs e)
    {
        PrintRows(GetCheckedRows().ToArray());
    }

    private void HandleBulkArchiveClick(object sender, RoutedEventArgs e)
    {
        var rows = GetCheckedRows().ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            ArchiveRow(row);
        }

        TryPersistWorkspace();
        RefreshAll();
    }

    private void HandleGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PurchasingGrid.SelectedItem is not PurchasingGridRow row)
        {
            return;
        }

        _selectedRowKey = row.SelectionKey;
        _dismissedLockKey = null;
        RefreshDetails(row);
    }

    private void HandleGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        HandleEditSelectedClick(sender, new RoutedEventArgs());
    }

    private void HandleGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateBulkBar, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandlePageNavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string direction)
        {
            return;
        }

        if (direction.Equals("prev", StringComparison.OrdinalIgnoreCase))
        {
            _page--;
        }
        else
        {
            _page++;
        }

        RebuildPage(keepSelection: true);
    }

    private void HandlePageSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageSizeCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (int.TryParse(new string(Ui(item.Content?.ToString()).TakeWhile(char.IsDigit).ToArray()), out var value) && value > 0)
        {
            _pageSize = value;
            _page = 1;
            if (_initialized)
            {
                RebuildPage(keepSelection: true);
            }
        }
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PurchasingGridRow row)
        {
            OpenRowActionsMenu(button, row);
        }
    }

    private void HandleDetailsActionsClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null || sender is not Button button)
        {
            return;
        }

        OpenRowActionsMenu(button, row);
    }

    private void HandleLinkedDocumentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not LinkedTarget target)
        {
            return;
        }

        OpenLinkedTarget(target);
    }

    private void HandlePrimaryCardActionClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        switch (_primaryCardAction)
        {
            case PurchasingCardAction.CreateOrder:
                CreateNewPurchase(ResolveSelectedSupplierId());
                break;
            case PurchasingCardAction.CreateInvoice:
                CreateOrEditInvoice(row);
                break;
            case PurchasingCardAction.MarkInvoiceReceived:
                MarkInvoiceReceived(row);
                break;
            case PurchasingCardAction.MarkInvoicePayable:
                MarkInvoicePayable(row);
                break;
            case PurchasingCardAction.PayInvoice:
                PayInvoice(row);
                break;
            case PurchasingCardAction.ReceiveReceipt:
                ReceiveReceipt(row);
                break;
            case PurchasingCardAction.PlaceReceipt:
                PlaceReceipt(row);
                break;
            case PurchasingCardAction.OpenDocument:
                OpenLinkedObject(row);
                break;
        }
    }

    private void HandleCreateReceiptClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        CreateOrEditReceipt(row);
    }

    private void HandlePaySelectedClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        PayInvoice(row);
    }

    private void HandleEditSelectedClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Выберите документ закупки.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EditRow(row);
    }

    private void HandleSendSupplierClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        SendSupplier(row);
    }

    private void SendSupplier(PurchasingGridRow row)
    {
        if (row.Payload is OperationalPurchasingSupplierRecord supplier)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Контакты поставщика:\n{supplier.Name}\n{supplier.Phone}\n{supplier.Email}",
                "Закупки",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (ResolveOrderForRow(row) is { } order && !row.IsDisabled)
        {
            var result = _workspace.PlacePurchaseOrder(order.Id);
            ShowWorkflowResult(result);
            return;
        }

        MessageBox.Show(Window.GetWindow(this), "Для выбранной записи отправка поставщику не требуется.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HandleCreateDiscrepancyClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        var document = ResolveDocumentForRow(row) ?? ResolveInvoiceForRow(row) ?? ResolveReceiptForRow(row) ?? ResolveOrderForRow(row);
        if (document is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нельзя завести расхождение.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var comment = PromptText("Расхождение", "Опишите найденное расхождение.", string.Empty, Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        var result = _workspace.AppendDocumentComment(document.DocumentType, document.Id, $"[Расхождение] {comment}", "Создание расхождения");
        ShowWorkflowResult(result);
    }

    private void HandlePrintSelectedClick(object sender, RoutedEventArgs e)
    {
        PrintRows(GetCheckedOrCurrentRows());
    }

    private void HandleCloseSelectedClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        if (row.Payload is OperationalPurchasingSupplierRecord)
        {
            MessageBox.Show(Window.GetWindow(this), "Закрытие карточки поставщика не требуется.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (row.Payload is not OperationalPurchasingDocumentRecord document)
        {
            return;
        }

        PurchasingWorkflowActionResult result = Ui(document.DocumentType) switch
        {
            "Заказ поставщику" => _workspace.SetDocumentStatus(document.DocumentType, document.Id, "Принят", "Закрытие заказа", "Заказ закрыт."),
            "Счет поставщика" => _workspace.MarkSupplierInvoicePaid(document.Id),
            "Приемка" => _workspace.PlacePurchaseReceipt(document.Id),
            _ => new PurchasingWorkflowActionResult(false, "Операция недоступна.", "Документ не поддерживает закрытие.")
        };
        ShowWorkflowResult(result);
    }

    private void HandleCancelSelectedClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        if (row.Payload is OperationalPurchasingSupplierRecord supplier)
        {
            var copy = supplier.Clone();
            copy.Status = "Пауза";
            _workspace.UpdateSupplier(copy);
            MessageBox.Show(Window.GetWindow(this), $"Поставщик {supplier.Name} переведен на паузу.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var document = ResolveDocumentForRow(row);
        if (document is null)
        {
            return;
        }

        var canceledStatus = Ui(document.DocumentType).Equals("Приемка", StringComparison.OrdinalIgnoreCase) ? "Отменена" : "Отменен";
        var result = _workspace.SetDocumentStatus(document.DocumentType, document.Id, canceledStatus, "Отмена документа", "Документ отменен.", refreshLifecycle: false);
        ShowWorkflowResult(result);
    }

    private void CreateNewPurchase(Guid? supplierId = null)
    {
        var dialog = new PurchasingDocumentEditorWindow(_workspace, PurchasingDocumentEditorMode.PurchaseOrder, null, supplierId)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        _workspace.AddPurchaseOrder(dialog.ResultDocument);
    }

    private void OpenSupplierEditor(OperationalPurchasingSupplierRecord? supplier)
    {
        var dialog = new PurchasingSupplierEditorWindow(_workspace, supplier)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultSupplier is null)
        {
            return;
        }

        if (supplier is null)
        {
            _workspace.AddSupplier(dialog.ResultSupplier);
        }
        else
        {
            _workspace.UpdateSupplier(dialog.ResultSupplier);
        }
    }

    private void OpenDocumentEditor(
        PurchasingDocumentEditorMode mode,
        OperationalPurchasingDocumentRecord? document,
        Guid? preselectedSupplierId = null)
    {
        var dialog = new PurchasingDocumentEditorWindow(_workspace, mode, document, preselectedSupplierId)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        switch (mode)
        {
            case PurchasingDocumentEditorMode.SupplierInvoice:
                if (document is null)
                {
                    _workspace.AddSupplierInvoice(dialog.ResultDocument);
                }
                else
                {
                    _workspace.UpdateSupplierInvoice(dialog.ResultDocument);
                }

                break;
            case PurchasingDocumentEditorMode.PurchaseReceipt:
                if (document is null)
                {
                    _workspace.AddPurchaseReceipt(dialog.ResultDocument);
                }
                else
                {
                    _workspace.UpdatePurchaseReceipt(dialog.ResultDocument);
                }

                break;
            default:
                if (document is null)
                {
                    _workspace.AddPurchaseOrder(dialog.ResultDocument);
                }
                else
                {
                    _workspace.UpdatePurchaseOrder(dialog.ResultDocument);
                }

                break;
        }
    }

    private void EditRow(PurchasingGridRow row)
    {
        switch (row.Payload)
        {
            case OperationalPurchasingSupplierRecord supplier:
                OpenSupplierEditor(supplier);
                break;
            case OperationalPurchasingDocumentRecord document when Ui(document.DocumentType).Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase):
                OpenDocumentEditor(PurchasingDocumentEditorMode.PurchaseOrder, document, document.SupplierId);
                break;
            case OperationalPurchasingDocumentRecord document when Ui(document.DocumentType).Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase):
                OpenDocumentEditor(PurchasingDocumentEditorMode.SupplierInvoice, document, document.SupplierId);
                break;
            case OperationalPurchasingDocumentRecord document when Ui(document.DocumentType).Equals("Приемка", StringComparison.OrdinalIgnoreCase):
                OpenDocumentEditor(PurchasingDocumentEditorMode.PurchaseReceipt, document, document.SupplierId);
                break;
            case PurchasingOperationLogEntry entry:
                if (ResolveLinkedTargetFromJournal(entry) is { } target)
                {
                    OpenLinkedTarget(target);
                }

                break;
        }
    }

    private void CreateOrEditInvoice(PurchasingGridRow row)
    {
        if (ResolveOrderForRow(row) is not { } order)
        {
            OpenDocumentEditor(PurchasingDocumentEditorMode.SupplierInvoice, ResolveInvoiceForRow(row), ResolveSelectedSupplierId());
            return;
        }

        var existing = GetInvoiceForOrder(order.Id);
        var draft = existing ?? _workspace.CreateSupplierInvoiceDraftFromOrder(order.Id);
        OpenDocumentEditor(PurchasingDocumentEditorMode.SupplierInvoice, existing is null ? null : draft, order.SupplierId);
        if (existing is null)
        {
            // The editor already persists through OpenDocumentEditor when ResultDocument is returned.
        }
    }

    private void CreateOrEditReceipt(PurchasingGridRow row)
    {
        if (ResolveOrderForRow(row) is not { } order)
        {
            OpenDocumentEditor(PurchasingDocumentEditorMode.PurchaseReceipt, ResolveReceiptForRow(row), ResolveSelectedSupplierId());
            return;
        }

        var existing = GetReceiptForOrder(order.Id);
        var draft = existing ?? _workspace.CreateReceiptDraftFromOrder(order.Id);
        OpenDocumentEditor(PurchasingDocumentEditorMode.PurchaseReceipt, existing is null ? null : draft, order.SupplierId);
        if (existing is null)
        {
            // Persisted via OpenDocumentEditor.
        }
    }

    private void MarkInvoiceReceived(PurchasingGridRow row)
    {
        var invoice = ResolveInvoiceForRow(row);
        if (invoice is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нет счета поставщика.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = _workspace.MarkSupplierInvoiceReceived(invoice.Id);
        ShowWorkflowResult(result);
    }

    private void MarkInvoicePayable(PurchasingGridRow row)
    {
        var invoice = ResolveInvoiceForRow(row);
        if (invoice is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нет счета поставщика.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Ui(invoice.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.MarkSupplierInvoiceReceived(invoice.Id);
        }

        var result = _workspace.MarkSupplierInvoicePayable(invoice.Id);
        ShowWorkflowResult(result);
    }

    private void PayInvoice(PurchasingGridRow row)
    {
        var invoice = ResolveInvoiceForRow(row);
        if (invoice is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нет счета к оплате.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Ui(invoice.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.MarkSupplierInvoiceReceived(invoice.Id);
        }

        if (Ui(invoice.Status).Equals("Получен", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.MarkSupplierInvoicePayable(invoice.Id);
        }

        var result = _workspace.MarkSupplierInvoicePaid(invoice.Id);
        ShowWorkflowResult(result);
    }

    private void ReceiveReceipt(PurchasingGridRow row)
    {
        if (ResolveReceiptForRow(row) is not { } receipt)
        {
            return;
        }

        var result = _workspace.ReceivePurchaseReceipt(receipt.Id);
        ShowWorkflowResult(result);
    }

    private void PlaceReceipt(PurchasingGridRow row)
    {
        if (ResolveReceiptForRow(row) is not { } receipt)
        {
            return;
        }

        if (Ui(receipt.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.ReceivePurchaseReceipt(receipt.Id);
        }

        var result = _workspace.PlacePurchaseReceipt(receipt.Id);
        ShowWorkflowResult(result);
    }

    private void OpenLinkedObject(PurchasingGridRow row)
    {
        switch (row.Payload)
        {
            case PurchasingOperationLogEntry entry when ResolveLinkedTargetFromJournal(entry) is { } target:
                OpenLinkedTarget(target);
                break;
            case OperationalPurchasingDocumentRecord document:
                var section = Ui(document.DocumentType) switch
                {
                    "Счет поставщика" => InvoicesSection,
                    "Приемка" => ReceiptsSection,
                    _ => OrdersSection
                };
                OpenLinkedTarget(new LinkedTarget(section, document.Id, document.Number));
                break;
            case OperationalPurchasingSupplierRecord supplier:
                OpenLinkedTarget(new LinkedTarget(SuppliersSection, supplier.Id, supplier.Name));
                break;
        }
    }

    private void OpenLinkedTarget(LinkedTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Section))
        {
            return;
        }

        _selectedRowKey = target.DocumentId.HasValue ? BuildSelectionKey(target.Section, target.DocumentId.Value) : null;

        if (!_activeSection.Equals(target.Section, StringComparison.OrdinalIgnoreCase))
        {
            ApplySection(target.Section, keepSelection: true, resetFilters: false);
        }

        if (!string.IsNullOrWhiteSpace(target.SearchText))
        {
            _syncingSearch = true;
            try
            {
                HeaderSearchBox.Text = target.SearchText;
                TableSearchBox.Text = target.SearchText;
            }
            finally
            {
                _syncingSearch = false;
            }
        }

        ApplyFilters(keepSelection: true);
    }

    private void OpenRowActionsMenu(FrameworkElement placementTarget, PurchasingGridRow row)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Открыть карточку", (_, _) => OpenLinkedObject(row)));
        if (row.Section != JournalSection)
        {
            menu.Items.Add(CreateMenuItem("Изменить", (_, _) => EditRow(row)));
        }

        switch (row.Section)
        {
            case OrdersSection:
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem("Зарегистрировать счет", (_, _) => CreateOrEditInvoice(row)));
                menu.Items.Add(CreateMenuItem("Создать приемку", (_, _) => CreateOrEditReceipt(row)));
                menu.Items.Add(CreateMenuItem("Отправить поставщику", (_, _) => SendSupplier(row)));
                break;
            case InvoicesSection:
            case PaymentsSection:
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem("Провести оплату", (_, _) => PayInvoice(row)));
                break;
            case ReceiptsSection:
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem("Принять приемку", (_, _) => ReceiveReceipt(row)));
                menu.Items.Add(CreateMenuItem("Разместить приемку", (_, _) => PlaceReceipt(row)));
                break;
            case SuppliersSection:
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem("Новый заказ", (_, _) => CreateNewPurchase(((OperationalPurchasingSupplierRecord)row.Payload).Id)));
                break;
        }

        if (ResolveDocumentForRow(row) is not null || ResolveInvoiceForRow(row) is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Печать", (_, _) => PrintRows(new[] { row })));
        }

        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private void ApplyStatusToRow(PurchasingGridRow row, string status, string action)
    {
        switch (row.Payload)
        {
            case OperationalPurchasingSupplierRecord supplier:
            {
                var copy = supplier.Clone();
                copy.Status = status;
                _workspace.UpdateSupplier(copy);
                break;
            }
            case OperationalPurchasingDocumentRecord document:
                _workspace.SetDocumentStatus(document.DocumentType, document.Id, status, action, $"Статус изменен на {status}.", refreshLifecycle: true);
                break;
        }
    }

    private void UpdateWarehouseForRow(PurchasingGridRow row, string warehouse)
    {
        switch (row.Payload)
        {
            case OperationalPurchasingDocumentRecord document when Ui(document.DocumentType).Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase):
            {
                var copy = document.Clone();
                copy.Warehouse = warehouse;
                _workspace.UpdatePurchaseOrder(copy);
                break;
            }
            case OperationalPurchasingDocumentRecord document when Ui(document.DocumentType).Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase):
            {
                var copy = document.Clone();
                copy.Warehouse = warehouse;
                _workspace.UpdateSupplierInvoice(copy);
                break;
            }
            case OperationalPurchasingDocumentRecord document when Ui(document.DocumentType).Equals("Приемка", StringComparison.OrdinalIgnoreCase):
            {
                var copy = document.Clone();
                copy.Warehouse = warehouse;
                _workspace.UpdatePurchaseReceipt(copy);
                break;
            }
        }
    }

    private void ArchiveRow(PurchasingGridRow row)
    {
        switch (row.Payload)
        {
            case OperationalPurchasingSupplierRecord supplier:
            {
                var copy = supplier.Clone();
                copy.Status = "Пауза";
                _workspace.UpdateSupplier(copy);
                break;
            }
            case OperationalPurchasingDocumentRecord document:
                _workspace.AppendDocumentComment(document.DocumentType, document.Id, "[Архив] Документ помечен как архивный.", "Архивация документа");
                _workspace.SetDocumentStatus(document.DocumentType, document.Id, "Архив", "Архивация документа", "Документ перемещен в архив.", refreshLifecycle: false);
                break;
        }
    }

    private void ShowWorkflowResult(PurchasingWorkflowActionResult result)
    {
        MessageBox.Show(
            Window.GetWindow(this),
            result.Message + Environment.NewLine + result.Detail,
            "Закупки",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private string[] ResolveStatusesForSection(string section)
    {
        return section switch
        {
            SuppliersSection => _workspace.SupplierStatuses.ToArray(),
            InvoicesSection => _workspace.SupplierInvoiceStatuses.ToArray(),
            ReceiptsSection => _workspace.PurchaseReceiptStatuses.ToArray(),
            PaymentsSection => new[] { "Ожидает оплаты", "Проведена" },
            _ => _workspace.PurchaseOrderStatuses.ToArray()
        };
    }

    private void ExportRows(IReadOnlyList<PurchasingGridRow> rows, string title)
    {
        if (rows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Нет данных для экспорта.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "exports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"purchasing-{_activeSection}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(";",
            "Колонка 1",
            "Колонка 2",
            "Колонка 3",
            "Колонка 4",
            "Колонка 5",
            "Колонка 6",
            "Колонка 7",
            "Колонка 8",
            "Статус",
            "Колонка 9"));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(";",
                Csv(row.Col1),
                Csv(row.Col2),
                Csv(row.Col3),
                Csv(row.Col4),
                Csv(row.Col5),
                Csv(row.Col6),
                Csv(row.Col7),
                Csv(row.Col8),
                Csv(row.StatusText),
                Csv(row.Col9)));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show(Window.GetWindow(this), $"Экспорт завершен.\nФайл: {path}", title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PrintRows(IReadOnlyList<PurchasingGridRow> rows)
    {
        var printable = rows
            .Select(ResolvePrintableDocument)
            .Where(item => item is not null)
            .Cast<OperationalPurchasingDocumentRecord>()
            .DistinctBy(item => item.Id)
            .ToArray();

        if (printable.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Для печати выберите заказ, счет, приемку или оплату.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "print");
        Directory.CreateDirectory(directory);

        foreach (var document in printable)
        {
            var html = BuildPrintHtml(document);
            var safeNumber = new string(document.Number.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
            if (string.IsNullOrWhiteSpace(safeNumber))
            {
                safeNumber = document.Id.ToString("N");
            }

            var path = Path.Combine(directory, $"purchasing-{safeNumber}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(path, html, Encoding.UTF8);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private OperationalPurchasingDocumentRecord? ResolvePrintableDocument(PurchasingGridRow row)
    {
        return row.Payload switch
        {
            OperationalPurchasingDocumentRecord document => document,
            _ => ResolveInvoiceForRow(row) ?? ResolveReceiptForRow(row) ?? ResolveOrderForRow(row)
        };
    }

    private static string BuildPrintHtml(OperationalPurchasingDocumentRecord document)
    {
        return Ui(document.DocumentType) switch
        {
            "Счет поставщика" => OperationalDocumentPrintComposer.BuildSupplierInvoiceHtml(document),
            "Приемка" => OperationalDocumentPrintComposer.BuildPurchaseReceiptHtml(document),
            _ => OperationalDocumentPrintComposer.BuildPurchaseOrderHtml(document)
        };
    }

    private int ImportPurchasingDocumentsFromDelimitedFile(string path)
    {
        var lines = ReadAllLinesAuto(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
        {
            return 0;
        }

        var delimiter = DetectDelimiter(lines[0]);
        var firstRow = SplitDelimitedLine(lines[0], delimiter);
        var hasHeader = HasImportHeader(firstRow);
        var headerMap = hasHeader
            ? firstRow
                .Select((header, index) => (Header: NormalizeImportHeader(header), Index: index))
                .Where(item => !string.IsNullOrWhiteSpace(item.Header))
                .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        var documentsByNumber = new Dictionary<string, OperationalPurchasingDocumentRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Skip(hasHeader ? 1 : 0))
        {
            var cells = SplitDelimitedLine(line, delimiter);
            var number = FirstNonEmpty(
                Field(cells, headerMap, 0, "номер", "документ", "number"),
                $"PO-{DateTime.Now:yyMMdd}-{documentsByNumber.Count + 1:000}");
            var supplierName = FirstNonEmpty(Field(cells, headerMap, 1, "поставщик", "supplier"), "Новый поставщик");
            var warehouse = FirstNonEmpty(Field(cells, headerMap, 4, "склад", "warehouse"), _workspace.Warehouses.FirstOrDefault() ?? string.Empty);
            var status = FirstNonEmpty(Field(cells, headerMap, 8, "статус", "status"), _workspace.PurchaseOrderStatuses.First());
            var comment = Field(cells, headerMap, 9, "комментарий", "comment", "примечание");

            if (!documentsByNumber.TryGetValue(number, out var document))
            {
                var supplier = EnsureSupplier(supplierName);
                var existing = _workspace.PurchaseOrders.FirstOrDefault(item => Ui(item.Number).Equals(number, StringComparison.OrdinalIgnoreCase));
                document = existing?.Clone() ?? _workspace.CreatePurchaseOrderDraft(supplier.Id);
                document.Number = number;
                document.SupplierId = supplier.Id;
                document.SupplierName = supplier.Name;
                document.Warehouse = warehouse;
                document.Status = status;
                document.Contract = supplier.Contract;
                document.SourceLabel = "Импорт";

                var dateRaw = Field(cells, headerMap, 2, "дата", "датазаказа", "orderdate");
                if (TryParseImportDate(dateRaw, out var date))
                {
                    document.DocumentDate = date;
                }

                document.Comment = comment;
                document.Lines.Clear();
                documentsByNumber[number] = document;
            }

            var itemCode = FirstNonEmpty(Field(cells, headerMap, 5, "код", "артикул", "itemcode"), $"IMP-{Guid.NewGuid():N}"[..10]);
            var itemName = FirstNonEmpty(Field(cells, headerMap, 6, "товар", "номенклатура", "наименование", "item"), itemCode);
            var unit = FirstNonEmpty(Field(cells, headerMap, 7, "ед", "едизм", "единица", "unit"), "шт");
            var priceRaw = Field(cells, headerMap, 10, "цена", "price");
            var quantityRaw = Field(cells, headerMap, 11, "колво", "количество", "qty", "quantity");
            var plannedRaw = Field(cells, headerMap, 12, "поставка", "пландата", "planned", "planneddate");

            var quantity = TryParseImportDecimal(quantityRaw, out var qty) ? qty : 1m;
            var price = TryParseImportDecimal(priceRaw, out var parsedPrice) ? parsedPrice : 0m;
            DateTime? plannedDate = TryParseImportDate(plannedRaw, out var parsedDate) ? parsedDate : document.DocumentDate.AddDays(3);

            document.Lines.Add(new OperationalPurchasingLineRecord
            {
                Id = Guid.NewGuid(),
                SectionName = "Импорт",
                ItemCode = itemCode,
                ItemName = itemName,
                Quantity = quantity <= 0 ? 1m : quantity,
                Unit = unit,
                Price = price,
                PlannedDate = plannedDate,
                RelatedDocument = number
            });
        }

        foreach (var pair in documentsByNumber)
        {
            var existing = _workspace.PurchaseOrders.FirstOrDefault(item => Ui(item.Number).Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _workspace.AddPurchaseOrder(pair.Value);
            }
            else
            {
                pair.Value.Id = existing.Id;
                _workspace.UpdatePurchaseOrder(pair.Value);
            }

            imported++;
        }

        return imported;
    }

    private OperationalPurchasingSupplierRecord EnsureSupplier(string supplierName)
    {
        var existing = _workspace.Suppliers.FirstOrDefault(item => Ui(item.Name).Equals(Ui(supplierName), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var draft = _workspace.CreateSupplierDraft();
        draft.Name = Ui(supplierName);
        draft.Code = string.IsNullOrWhiteSpace(draft.Code) ? $"SUP-{DateTime.Now:yyMMddHHmmss}" : draft.Code;
        _workspace.AddSupplier(draft);
        return _workspace.Suppliers.First(item => item.Id == draft.Id);
    }

    private string? PromptText(string title, string prompt, string? initialValue, IEnumerable<string> options)
    {
        var dialog = new ProductTextInputWindow(title, prompt, initialValue, options)
        {
            Owner = Window.GetWindow(this)
        };

        return dialog.ShowDialog() == true ? dialog.ResultText : null;
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

    private string GetCurrentOperator()
    {
        return string.IsNullOrWhiteSpace(_salesWorkspace.CurrentOperator)
            ? Environment.UserName
            : _salesWorkspace.CurrentOperator;
    }

    private static string FormatMoney(decimal amount)
    {
        return $"{amount:N2} ₽";
    }

    private static string EmptyAsDash(string? value)
    {
        var normalized = Ui(value);
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }

    private static string TrimComment(string? value)
    {
        var normalized = Ui(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        normalized = normalized.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        return normalized.Length <= 42 ? normalized : normalized[..42] + "?";
    }

    private static string Csv(string? value)
    {
        return "\"" + Ui(value).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string[] ReadAllLinesAuto(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return SplitTextLines(strictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return SplitTextLines(Encoding.GetEncoding(1251).GetString(bytes));
        }
    }

    private static string[] SplitTextLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static char DetectDelimiter(string line)
    {
        var semicolon = line.Count(ch => ch == ';');
        var tab = line.Count(ch => ch == '\t');
        var comma = line.Count(ch => ch == ',');

        if (tab >= semicolon && tab >= comma)
        {
            return '\t';
        }

        return semicolon >= comma ? ';' : ',';
    }

    private static string[] SplitDelimitedLine(string line, char delimiter)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                result.Add(Ui(builder.ToString().Trim()));
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        result.Add(Ui(builder.ToString().Trim()));
        return result.ToArray();
    }

    private static bool HasImportHeader(IReadOnlyList<string> row)
    {
        return row
            .Select(NormalizeImportHeader)
            .Any(header => header is "номер" or "документ" or "поставщик" or "товар" or "наименование" or "номенклатура" or "цена" or "количество" or "склад");
    }

    private static string NormalizeImportHeader(string value)
    {
        var normalized = Ui(value).Trim().ToLowerInvariant();
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string Field(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> headerMap, int fallbackIndex, params string[] aliases)
    {
        foreach (var alias in aliases.Select(NormalizeImportHeader))
        {
            if (headerMap.TryGetValue(alias, out var index) && index >= 0 && index < cells.Count)
            {
                return Ui(cells[index]);
            }
        }

        return headerMap.Count == 0 && fallbackIndex >= 0 && fallbackIndex < cells.Count ? Ui(cells[fallbackIndex]) : string.Empty;
    }

    private static bool TryParseImportDecimal(string value, out decimal result)
    {
        value = Ui(value)
            .Replace("₽", string.Empty, StringComparison.Ordinal)
            .Replace('\u00A0', ' ')
            .Replace(" ", string.Empty);

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

    private static bool TryParseImportDate(string value, out DateTime result)
    {
        value = Ui(value);
        return DateTime.TryParse(value, RuCulture, DateTimeStyles.AssumeLocal, out result)
               || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.Select(Ui).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private enum PurchasingCardAction
    {
        None,
        CreateOrder,
        CreateInvoice,
        MarkInvoiceReceived,
        MarkInvoicePayable,
        PayInvoice,
        ReceiveReceipt,
        PlaceReceipt,
        OpenDocument
    }

    private sealed class PurchasingGridRow : INotifyPropertyChanged
    {
        private bool _isChecked;

        public Guid Id { get; init; }

        public string Section { get; init; } = string.Empty;

        public string SelectionKey { get; init; } = string.Empty;

        public object Payload { get; init; } = default!;

        public string DocumentType { get; init; } = string.Empty;

        public string SupplierName { get; init; } = string.Empty;

        public string Warehouse { get; init; } = string.Empty;

        public string Col1 { get; init; } = string.Empty;

        public string Col2 { get; init; } = string.Empty;

        public string Col3 { get; init; } = string.Empty;

        public string Col4 { get; init; } = string.Empty;

        public string Col5 { get; init; } = string.Empty;

        public string Col6 { get; init; } = string.Empty;

        public string Col7 { get; init; } = string.Empty;

        public string Col8 { get; init; } = string.Empty;

        public string Col9 { get; init; } = string.Empty;

        public string RawStatus { get; init; } = string.Empty;

        public string StatusText { get; init; } = string.Empty;

        public Brush StatusBackground { get; init; } = Brushes.Transparent;

        public Brush StatusForeground { get; init; } = Brushes.Black;

        public string SearchText { get; init; } = string.Empty;

        public bool IsDisabled { get; init; }

        public bool IsOverdue { get; init; }

        public bool MissingInvoice { get; init; }

        public bool MissingReceipt { get; init; }

        public bool IsUnpaid { get; init; }

        public bool HasDiscrepancy { get; init; }

        public DateTime SortDate { get; init; }

        public Guid RelatedOrderId { get; init; }

        public decimal AmountValue { get; init; }

        public decimal PaidValue { get; init; }

        public decimal BalanceValue { get; init; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }

                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record PurchasingDetailLineRow(string ItemName, string QuantityText, string Unit, string PriceText, string AmountText);

    private sealed record LinkedTarget(string Section, Guid? DocumentId, string? SearchText = null);
}
