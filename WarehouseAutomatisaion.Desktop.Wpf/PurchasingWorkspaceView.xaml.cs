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
    private bool _initialized;
    private bool _dateRangeInitialized;
    private int _page = 1;
    // Release 1.0.133: дефолт 50 (было 20 → 50 после init в HandleLoaded). Combo
    // SelectedIndex=1 = "50/стр". Хороший компромисс: видно достаточно, грид
    // отзывчив. Можно увеличить до 100/200 через combo.
    private int _pageSize = 50;
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
        EmptyStateTitleText.Text = "Нет закупочных документов";
        EmptyStateHintText.Text = "Создайте первый документ вручную или импортируйте данные.";
        ShowAllPositionsText.Text = "Показать все";
    }

    private void InitializeFilters()
    {
        PageSizeCombo.SelectedIndex = 1;
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

    // Release 1.0.126: флаг защищает от race condition после закрытия вкладки.
    // Раньше HandleUnloaded НЕ отписывался от _salesWorkspace.Changed /
    // _workspace.Changed → после Unloaded событие срабатывало → Dispatcher.BeginInvoke
    // RefreshAll работал по выгруженному visual-tree → fatal-краш (часть причины
    // отката 1.0.117/118).
    private bool _isDisposed;

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        _isDisposed = true;
        try { TryPersistWorkspace(); } catch { }
        UnhookEvents();
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_isDisposed) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_isDisposed) return;
            TryPersistWorkspace();
            RefreshAll();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_isDisposed) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_isDisposed) return;
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

    public void ActivateSubSection(string subSectionKey)
    {
        if (string.IsNullOrWhiteSpace(subSectionKey))
        {
            return;
        }

        ApplySection(subSectionKey, keepSelection: false, resetFilters: false);
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
        // Sub-tabs «Заказы поставщикам / Заказы покупателей» показываем только когда активен раздел Заказы.
        var ordersActive = _activeSection == OrdersSection;
        OrderSubTabsPanel.Visibility = ordersActive ? Visibility.Visible : Visibility.Collapsed;
        ApplySectionButton(OrderSupplierSubTabButton, ordersActive && _activeOrderSubTab == OrderSupplierSubTab);
        ApplySectionButton(OrderCustomerRequestSubTabButton, ordersActive && _activeOrderSubTab == OrderCustomerRequestSubTab);
        OrderCustomerRequestPrimaryButton.Visibility = ordersActive && _activeOrderSubTab == OrderCustomerRequestSubTab
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private const string OrderSupplierSubTab = "supplier-orders";
    private const string OrderCustomerRequestSubTab = "customer-requests";
    private string _activeOrderSubTab = OrderSupplierSubTab;

    private static void ApplySectionButton(WpfButton button, bool isActive)
    {
        button.BorderBrush = isActive ? PrimaryBrush : Brushes.Transparent;
        button.Foreground = isActive ? PrimaryBrush : NeutralBrush;
    }

    private void RefreshFilterOptions(bool resetSelections)
    {
    }

    private void ApplyFilters(bool keepSelection)
    {
        _filteredRows = _allRows
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
        PagerPrevButton.IsEnabled = _page > 1;
        PagerPrevButton.Opacity = PagerPrevButton.IsEnabled ? 1d : 0.45d;
        PagerNextButton.IsEnabled = _page < totalPages;
        PagerNextButton.Opacity = PagerNextButton.IsEnabled ? 1d : 0.45d;

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

        BulkStatusButton.Visibility = ResolveBulkStatuses(checkedRows).Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BulkWarehouseButton.Visibility = checkedRows.Any(CanAssignWarehouse) ? Visibility.Visible : Visibility.Collapsed;
        BulkPrintButton.Visibility = checkedRows.Any(CanPrintRow) ? Visibility.Visible : Visibility.Collapsed;
        BulkArchiveButton.Visibility = checkedRows.Any(CanArchiveRow) ? Visibility.Visible : Visibility.Collapsed;
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
                // Release 1.0.119: 1в1 со скрином 4 1С УНФ «Приходные накладные».
                // Раньше показывались «Основание / Позиций / Источник / Ответственный» —
                // в 1С их нет на главном виде. Соответственно BuildReceiptRow тоже
                // переставляет данные в этом порядке.
                SetGridHeaders("Дата", "Номер", "Поставщик / Покупатель", "Сумма", "Склад", "Операция", "Состояние оригинала", "", "Статус", "");
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
                // 1С УНФ-layout «Заказы поставщикам»: Дата | Номер | Состояние | Поставщик | Сумма |
                // Дата поступления | Состояние оригинала | (служебные колонки скрыты по факту 1в1 со скрином 1С).
                // Release 1.0.113: переставлен порядок столбцов и заменены поздние заголовки на
                // «Состояние оригинала» / «-» / «-» — это убирает лишние «Договор»/«Склад»/«Ответственный»,
                // которые в 1С на главном виде заказов не показываются.
                SetGridHeaders("Дата", "Номер", "Состояние", "Поставщик", "Сумма", "Дата поступления", "Состояние оригинала", "", "", "");
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
    }

    private void UpdateEmptyStateCopy()
    {
        EmptyStatePrimaryButtonText.Text = _activeSection == SuppliersSection
            ? "Создать поставщика"
            : "Создать первую закупку";

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

        CardCloseButton.IsEnabled = CardCloseButton.IsEnabled && CanCloseRow(row);
        CardPrintButton.IsEnabled = CardPrintButton.IsEnabled && CanPrintRow(row);
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

    private void HandleEmptyStatePrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_activeSection == SuppliersSection)
        {
            OpenSupplierEditor(null);
            return;
        }

        CreateNewPurchase();
    }

    private void HandleDismissLockBannerClick(object sender, RoutedEventArgs e)
    {
        _dismissedLockKey = _selectedRowKey;
        UpdateLockBanner(GetCurrentRow());
    }

    private void HandleIssuePresetClick(object sender, RoutedEventArgs e)
    {
    }

    private void ResetFilters(bool clearSearch)
    {
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

        var statuses = ResolveBulkStatuses(rows);
        if (statuses.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранных записей нет общего набора статусов.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var rows = GetCheckedRows().Where(CanAssignWarehouse).ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранных записей нельзя назначить склад.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var rows = GetCheckedRows().Where(CanArchiveRow).ToArray();
        if (rows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранных записей архивирование недоступно.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
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

        if (!CanCloseRow(row))
        {
            MessageBox.Show(Window.GetWindow(this), "Закрытие недоступно для выбранной записи.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (row.Payload is not OperationalPurchasingDocumentRecord document)
        {
            return;
        }

        var documentType = Ui(document.DocumentType);
        PurchasingWorkflowActionResult result;
        if (documentType.Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase))
        {
            result = _workspace.SetDocumentStatus(document.DocumentType, document.Id, "Принят", "Закрытие заказа", "Заказ закрыт.");
        }
        else if (documentType.Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase))
        {
            result = _workspace.MarkSupplierInvoicePaid(document.Id);
        }
        else if (documentType.Equals("Приемка", StringComparison.OrdinalIgnoreCase))
        {
            result = _workspace.PlacePurchaseReceipt(document.Id);
        }
        else
        {
            MessageBox.Show(Window.GetWindow(this), "Закрытие недоступно для выбранной записи.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

    private void OpenDocumentEditor(
        PurchasingDocumentEditorMode mode,
        OperationalPurchasingDocumentRecord? document,
        Guid? preselectedSupplierId = null)
    {
        var dialog = new PurchasingDocumentEditorWindow(
            _workspace,
            mode,
            document,
            preselectedSupplierId,
            ResolveStorageCellOptions())
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

    private string[] ResolveStatusesForRow(PurchasingGridRow row)
    {
        return row.Payload switch
        {
            OperationalPurchasingSupplierRecord => _workspace.SupplierStatuses.ToArray(),
            OperationalPurchasingDocumentRecord document => Ui(document.DocumentType) switch
            {
                "Заказ поставщику" => _workspace.PurchaseOrderStatuses.ToArray(),
                "Счет поставщика" => _workspace.SupplierInvoiceStatuses.ToArray(),
                "Приемка" => _workspace.PurchaseReceiptStatuses.ToArray(),
                _ => Array.Empty<string>()
            },
            _ => Array.Empty<string>()
        };
    }

    private string[] ResolveBulkStatuses(IReadOnlyList<PurchasingGridRow> rows)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<string>();
        }

        var statusSets = rows.Select(ResolveStatusesForRow).ToArray();
        if (statusSets.Any(item => item.Length == 0))
        {
            return Array.Empty<string>();
        }

        IEnumerable<string> commonStatuses = statusSets[0];
        foreach (var statusSet in statusSets.Skip(1))
        {
            commonStatuses = commonStatuses.Where(status => statusSet.Contains(status, StringComparer.OrdinalIgnoreCase));
        }

        return commonStatuses
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool CanPrintRow(PurchasingGridRow row)
    {
        return ResolvePrintableDocument(row) is not null;
    }

    private static bool CanAssignWarehouse(PurchasingGridRow row)
    {
        return row.Payload is OperationalPurchasingDocumentRecord document && IsWarehouseAssignableDocument(document);
    }

    private static bool CanArchiveRow(PurchasingGridRow row)
    {
        return row.Payload is OperationalPurchasingSupplierRecord or OperationalPurchasingDocumentRecord;
    }

    private static bool CanCloseRow(PurchasingGridRow row)
    {
        return row.Payload is OperationalPurchasingDocumentRecord document && IsClosableDocument(document);
    }

    private static bool IsWarehouseAssignableDocument(OperationalPurchasingDocumentRecord document)
    {
        var documentType = Ui(document.DocumentType);
        return documentType.Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase)
            || documentType.Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase)
            || documentType.Equals("Приемка", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClosableDocument(OperationalPurchasingDocumentRecord document)
    {
        var documentType = Ui(document.DocumentType);
        return documentType.Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase)
            || documentType.Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase)
            || documentType.Equals("Приемка", StringComparison.OrdinalIgnoreCase);
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

        var definitions = printable.Select(BuildPrintDefinition).ToArray();
        var jobTitle = definitions.Length == 1 ? definitions[0].Title : "Закупочные документы";
        PrintDocumentComposer.Print(
            Window.GetWindow(this),
            jobTitle,
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildTableDocument(definitions, pageWidth, pageHeight));
    }

    private OperationalPurchasingDocumentRecord? ResolvePrintableDocument(PurchasingGridRow row)
    {
        return row.Payload switch
        {
            OperationalPurchasingDocumentRecord document => document,
            _ => ResolveInvoiceForRow(row) ?? ResolveReceiptForRow(row) ?? ResolveOrderForRow(row)
        };
    }

    private static PrintableTableDocumentDefinition BuildPrintDefinition(OperationalPurchasingDocumentRecord document)
    {
        var title = string.IsNullOrWhiteSpace(Ui(document.DocumentType)) ? "Документ закупки" : Ui(document.DocumentType);
        var rows = document.Lines
            .Select(line => new PrintableTableRow(new[]
            {
                Ui(line.ItemCode),
                Ui(line.ItemName),
                Ui(line.Unit),
                line.Quantity.ToString("N2", RuCulture),
                FormatMoney(line.Price),
                FormatMoney(line.Amount),
                line.PlannedDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-"
            }))
            .ToArray();

        return new PrintableTableDocumentDefinition(
            title,
            $"№ {Ui(document.Number)} от {document.DocumentDate:dd.MM.yyyy}",
            new[]
            {
                new PrintableField("Поставщик", Ui(document.SupplierName)),
                new PrintableField("Договор", EmptyAsDash(document.Contract)),
                new PrintableField("Склад", EmptyAsDash(document.Warehouse)),
                new PrintableField("Статус", EmptyAsDash(document.Status)),
                new PrintableField("Основание", EmptyAsDash(document.RelatedOrderNumber)),
                new PrintableField("Оплатить до", document.DueDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-")
            },
            new[]
            {
                new PrintableTableColumn("Код", 0.13),
                new PrintableTableColumn("Номенклатура", 0.34),
                new PrintableTableColumn("Ед.", 0.07),
                new PrintableTableColumn("Кол-во", 0.1, TextAlignment.Right),
                new PrintableTableColumn("Цена", 0.12, TextAlignment.Right),
                new PrintableTableColumn("Сумма", 0.13, TextAlignment.Right),
                new PrintableTableColumn("План", 0.11)
            },
            rows,
            new[]
            {
                new PrintableField("Итого", FormatMoney(document.TotalAmount))
            },
            document.Comment);
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
        catch (Exception exception)
        {
            // Release 1.0.123: popup «Не удалось сохранить закупки в общей базе»
            // больше не показываем — он спамил пользователя при каждом неудачном
            // авто-сейве (срабатывал при загрузке вкладки). Данные в Backplane
            // не теряются: при следующем явном изменении save повторится.
            // Логируем тихо для разработчика.
            try
            {
                System.Diagnostics.Debug.WriteLine($"[TryPersistWorkspace] purchasing save failed: {exception}");
            }
            catch
            {
            }
        }
    }

    private IReadOnlyList<string> ResolveStorageCellOptions()
    {
        try
        {
            var warehouseStore = WarehouseOperationalWorkspaceStore.CreateDefault();
            var warehouseWorkspace = warehouseStore.TryLoadExisting(GetCurrentOperator(), _workspace.CatalogItems, _workspace.Warehouses);
            var codes = warehouseWorkspace?.GetActiveStorageCellCodes();
            if (codes is { Count: > 0 })
            {
                return codes;
            }
        }
        catch
        {
            // Fallback below keeps receipt editing available even when the warehouse module is not loaded yet.
        }

        return OperationalWarehouseWorkspace
            .CreateDefaultStorageCells(_workspace.Warehouses)
            .Where(item => item.IsActive)
            .Select(item => item.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
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

        // 1С УНФ-style индикаторы-кружки. BuildOrderRow выставляет зелёный (закрыт/оплачен)
        // или красный (не закрыт/не оплачен), для остальных секций остаются серым.
        public string StatusDot1Color { get; set; } = "#BFC8DB";
        public string StatusDot1Fill { get; set; } = "Transparent";
        public string StatusDot2Color { get; set; } = "#BFC8DB";
        public string StatusDot2Fill { get; set; } = "Transparent";

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
