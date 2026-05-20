using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public enum SalesDocumentEditorMode
{
    Order,
    Invoice,
    Shipment
}

public sealed class SalesDocumentHostedSaveEventArgs : EventArgs
{
    public bool Succeeded { get; private set; } = true;

    public string? ErrorMessage { get; private set; }

    public void Fail(string message)
    {
        Succeeded = false;
        ErrorMessage = string.IsNullOrWhiteSpace(message)
            ? "Документ не сохранен."
            : message.Trim();
    }
}

public partial class SalesDocumentEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Brush PaidIndicatorBrush = new SolidColorBrush(Color.FromRgb(31, 164, 95));
    private static readonly Brush PartialIndicatorBrush = new SolidColorBrush(Color.FromRgb(242, 154, 23));
    private static readonly Brush EmptyIndicatorBrush = new SolidColorBrush(Color.FromRgb(205, 46, 46));
    private static readonly Brush NeutralIndicatorBrush = new SolidColorBrush(Color.FromRgb(110, 124, 150));
    private static readonly Brush ValidationMessageBrush = new SolidColorBrush(Color.FromRgb(217, 45, 32));
    private static readonly Brush SuccessMessageBrush = new SolidColorBrush(Color.FromRgb(31, 164, 95));

    private readonly SalesWorkspace _workspace;
    private readonly SalesDocumentEditorMode _mode;
    private readonly ObservableCollection<SalesLineEditorRow> _lines = [];
    private readonly ObservableCollection<SalesRelatedDocumentRow> _relatedDocuments = [];
    private readonly Dictionary<string, SalesCustomerRecord> _customerOptions = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, SalesOrderRecord> _orderOptions = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly bool _editingExistingDocument;
    private string[] _customerOptionTexts = [];
    private bool _loading;
    private bool _hostedInWorkspace;
    private bool _updatingCustomerLookup;
    private bool _updatingDiscountFields;
    private bool _discountPercentMode;
    private bool _syncingStatusSelection;
    private string? _selectedStatusValue;
    private decimal _manualDiscountPercent;
    private decimal _manualDiscountAmount;
    private SalesOrderRecord? _orderDraft;
    private SalesInvoiceRecord? _invoiceDraft;
    private SalesShipmentRecord? _shipmentDraft;
    private IReadOnlyList<SalesCatalogItemOption>? _lineCatalogItems;

    public SalesDocumentEditorWindow(SalesWorkspace workspace, SalesDocumentEditorMode mode)
        : this(workspace, mode, null, null, null)
    {
    }

    public SalesDocumentEditorWindow(SalesWorkspace workspace, SalesOrderRecord order)
        : this(workspace, SalesDocumentEditorMode.Order, order.Clone(), null, null)
    {
    }

    public SalesDocumentEditorWindow(SalesWorkspace workspace, SalesInvoiceRecord invoice)
        : this(workspace, SalesDocumentEditorMode.Invoice, null, invoice.Clone(), null)
    {
    }

    public SalesDocumentEditorWindow(SalesWorkspace workspace, SalesShipmentRecord shipment)
        : this(workspace, SalesDocumentEditorMode.Shipment, null, null, shipment.Clone())
    {
    }

    private SalesDocumentEditorWindow(
        SalesWorkspace workspace,
        SalesDocumentEditorMode mode,
        SalesOrderRecord? orderDraft,
        SalesInvoiceRecord? invoiceDraft,
        SalesShipmentRecord? shipmentDraft)
    {
        _workspace = workspace;
        _mode = mode;
        _orderDraft = orderDraft;
        _invoiceDraft = invoiceDraft;
        _shipmentDraft = shipmentDraft;
        _editingExistingDocument = orderDraft is not null || invoiceDraft is not null || shipmentDraft is not null;

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        CustomerComboBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(HandleCustomerLookupTextChanged));
        LinesGrid.ItemsSource = _lines;
        RelatedDocumentsGrid.ItemsSource = _relatedDocuments;
        LoadOptionSources();
        ConfigureMode();
        LoadInitialDraft();
    }

    public SalesOrderRecord? ResultOrder { get; private set; }

    public SalesInvoiceRecord? ResultInvoice { get; private set; }

    public SalesShipmentRecord? ResultShipment { get; private set; }

    public event EventHandler<SalesDocumentHostedSaveEventArgs>? HostedSaved;

    public event EventHandler? HostedCanceled;

    public FrameworkElement DetachContentForWorkspaceTab()
    {
        _hostedInWorkspace = true;
        var content = Content as FrameworkElement
            ?? throw new InvalidOperationException("Editor content is not available.");
        Content = null;
        return content;
    }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void LoadOptionSources()
    {
        foreach (var customer in _workspace.Customers.OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            _customerOptions[BuildCustomerOption(customer)] = customer;
        }

        foreach (var order in _workspace.Orders.OrderByDescending(item => item.OrderDate))
        {
            _orderOptions[BuildOrderOption(order)] = order;
        }

        _customerOptionTexts = _customerOptions.Keys.ToArray();
        CustomerComboBox.ItemsSource = _customerOptionTexts;
        OrderComboBox.ItemsSource = _orderOptions.Keys.ToArray();
        WarehouseComboBox.ItemsSource = _workspace.Warehouses.Select(Ui).ToArray();
        OrganizationComboBox.ItemsSource = _workspace.Organizations.Select(Ui).ToArray();
        ManagerComboBox.ItemsSource = _workspace.Managers
            .Select(SalesManagerDisplayResolver.Resolve)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        CurrencyComboBox.ItemsSource = _workspace.Currencies.Select(Ui).ToArray();
    }

    private void ConfigureMode()
    {
        _loading = true;

        switch (_mode)
        {
            case SalesDocumentEditorMode.Order:
                Title = "Заказ покупателя";
                HeaderTitleText.Text = "Новый заказ";
                HeaderSubtitleText.Text = " (Заказ покупателя)";
                DocumentDateLabelText.Text = "от:";
                StatusComboBox.ItemsSource = _workspace.OrderStatuses.Select(Ui).ToArray();
                OrderPanel.Visibility = Visibility.Collapsed;
                OrderPanelLabel.Visibility = Visibility.Collapsed;
                CustomerPanel.Visibility = Visibility.Visible;
                SecondaryDatePanel.Visibility = Visibility.Collapsed;
                SecondaryDateLabelText.Visibility = Visibility.Collapsed;
                OrganizationPanel.Visibility = Visibility.Visible;
                // 1С-поля: видимы только в режиме Order.
                OrderFlagsPanel.Visibility = Visibility.Visible;
                ActSectionPanel.Visibility = Visibility.Visible;
                EasyCeilingLabel.Visibility = Visibility.Visible;
                EasyCeilingOrderNumberTextBox.Visibility = Visibility.Visible;
                break;
            case SalesDocumentEditorMode.Invoice:
                Title = "Счет покупателю";
                HeaderTitleText.Text = "Новый счет";
                HeaderSubtitleText.Text = " (Счет покупателю)";
                DocumentDateLabelText.Text = "от:";
                SecondaryDateLabelText.Text = "Срок оплаты:";
                StatusComboBox.ItemsSource = _workspace.InvoiceStatuses.Select(Ui).ToArray();
                CustomerPanel.Visibility = Visibility.Collapsed;
                CustomerComboBox.IsEnabled = false;
                OrderPanel.Visibility = Visibility.Visible;
                OrderPanelLabel.Visibility = Visibility.Visible;
                SecondaryDatePanel.Visibility = Visibility.Visible;
                SecondaryDateLabelText.Visibility = Visibility.Visible;
                OrganizationPanel.Visibility = Visibility.Collapsed;
                OrderFlagsPanel.Visibility = Visibility.Collapsed;
                ActSectionPanel.Visibility = Visibility.Collapsed;
                EasyCeilingLabel.Visibility = Visibility.Collapsed;
                EasyCeilingOrderNumberTextBox.Visibility = Visibility.Collapsed;
                break;
            case SalesDocumentEditorMode.Shipment:
                Title = "Расходная накладная";
                HeaderTitleText.Text = "Новая отгрузка";
                HeaderSubtitleText.Text = " (Расходная накладная)";
                DocumentDateLabelText.Text = "от:";
                StatusComboBox.ItemsSource = _workspace.ShipmentStatuses.Select(Ui).ToArray();
                CustomerPanel.Visibility = Visibility.Collapsed;
                CustomerComboBox.IsEnabled = false;
                OrderPanel.Visibility = Visibility.Visible;
                OrderPanelLabel.Visibility = Visibility.Visible;
                SecondaryDatePanel.Visibility = Visibility.Collapsed;
                SecondaryDateLabelText.Visibility = Visibility.Collapsed;
                OrganizationPanel.Visibility = Visibility.Collapsed;
                OrderFlagsPanel.Visibility = Visibility.Collapsed;
                ActSectionPanel.Visibility = Visibility.Collapsed;
                EasyCeilingLabel.Visibility = Visibility.Collapsed;
                EasyCeilingOrderNumberTextBox.Visibility = Visibility.Collapsed;
                break;
        }

        // Кнопка «Возврат» убрана из редактора заказа — возвраты создаются из списка заказов через «...».
        CreateReturnButton.Visibility = Visibility.Collapsed;
        _loading = false;
    }

    private void LoadInitialDraft()
    {
        _loading = true;

        if (_mode == SalesDocumentEditorMode.Order)
        {
            if (_orderDraft is null)
            {
                var customer = _workspace.Customers.FirstOrDefault();
                _orderDraft = _workspace.CreateOrderDraft(customer?.Id);
            }

            LoadOrder(_orderDraft);
            ApplyEditTitle($"Заказ {_orderDraft.Number}", "Заказ покупателя");
        }
        else if (_invoiceDraft is not null)
        {
            var baseOrder = FindOrder(_invoiceDraft.SalesOrderId, _invoiceDraft.SalesOrderNumber);
            if (baseOrder is not null)
            {
                SelectComboValue(OrderComboBox, BuildOrderOption(baseOrder));
            }

            OrderComboBox.IsEnabled = false;
            LoadInvoice(_invoiceDraft);
            ApplyEditTitle($"Счет {_invoiceDraft.Number}", "Счет покупателя");
        }
        else if (_shipmentDraft is not null)
        {
            var baseOrder = FindOrder(_shipmentDraft.SalesOrderId, _shipmentDraft.SalesOrderNumber);
            if (baseOrder is not null)
            {
                SelectComboValue(OrderComboBox, BuildOrderOption(baseOrder));
            }

            OrderComboBox.IsEnabled = false;
            LoadShipment(_shipmentDraft);
            ApplyEditTitle($"Отгрузка {_shipmentDraft.Number}", "Отгрузка покупателя");
        }
        else
        {
            var firstOrder = _workspace.Orders.OrderByDescending(item => item.OrderDate).FirstOrDefault();
            if (firstOrder is not null)
            {
                SelectComboValue(OrderComboBox, BuildOrderOption(firstOrder));
                LoadFromBaseOrder(firstOrder);
            }
        }

        _loading = false;
        RefreshTotal();
        RenderRelatedDocuments();
    }

    private void ApplyEditTitle(string title, string header)
    {
        if (!_editingExistingDocument)
        {
            return;
        }

        Title = title;
        HeaderTitleText.Text = header;
    }

    private SalesOrderRecord? FindOrder(Guid orderId, string orderNumber)
    {
        return _workspace.Orders.FirstOrDefault(item => item.Id == orderId)
            ?? _workspace.Orders.FirstOrDefault(item => Ui(item.Number).Equals(Ui(orderNumber), StringComparison.OrdinalIgnoreCase));
    }

    private void LoadOrder(SalesOrderRecord order)
    {
        NumberTextBox.Text = Ui(order.Number);
        DocumentDatePicker.SelectedDate = order.OrderDate == default ? DateTime.Today : order.OrderDate;
        SelectComboValue(CustomerComboBox, BuildCustomerOption(order));
        SetStatusComboValue(_workspace.NormalizeOrderStatus(order.Status));
        SelectComboValue(WarehouseComboBox, Ui(order.Warehouse));
        SelectComboValue(OrganizationComboBox, _workspace.NormalizeOrganization(order.Organization));
        SelectComboValue(ManagerComboBox, SalesManagerDisplayResolver.Resolve(order.Manager));
        SelectComboValue(CurrencyComboBox, Ui(order.CurrencyCode));
        CommentTextBox.Text = Ui(order.Comment);
        LoadDiscount(order.ManualDiscountPercent, order.ManualDiscountAmount);
        // 1С-поля: флажки + блок «Акт» + Розничная цена УДМ / EasyCeiling / Отгрузка.
        ContractComboBox.Text = Ui(order.ContractNumber);
        IsPhoneInstallCheckBox.IsChecked = order.IsPhoneInstall;
        IsAirConditionerCheckBox.IsChecked = order.IsAirConditioner;
        IsYekaterinburgCheckBox.IsChecked = order.IsYekaterinburg;
        VatEnabledCheckBox.IsChecked = order.VatEnabled;
        EasyCeilingOrderNumberTextBox.Text = Ui(order.EasyCeilingOrderNumber);
        ShippingDatePicker.SelectedDate = order.ShippingDate ?? (order.OrderDate == default ? DateTime.Today : order.OrderDate);
        SurveyorComboBox.Text = Ui(order.SurveyorName);
        ActNumberTextBox.Text = Ui(order.ActNumber);
        ActDatePicker.SelectedDate = order.ActDate;
        ComplexityScoreTextBox.Text = order.ComplexityScore == 0m ? string.Empty : order.ComplexityScore.ToString("N2", RuCulture);
        ComplexityDiscountAmountTextBox.Text = order.ComplexityDiscountAmount == 0m ? string.Empty : order.ComplexityDiscountAmount.ToString("N2", RuCulture);
        ComplexityDiscountPercentTextBox.Text = order.ComplexityDiscountPercent == 0m ? "0,00" : order.ComplexityDiscountPercent.ToString("N2", RuCulture);
        ReplaceLines(order.Lines);
    }

    private void LoadInvoice(SalesInvoiceRecord invoice)
    {
        NumberTextBox.Text = Ui(invoice.Number);
        DocumentDatePicker.SelectedDate = invoice.InvoiceDate == default ? DateTime.Today : invoice.InvoiceDate;
        SecondaryDatePicker.SelectedDate = invoice.DueDate == default ? DateTime.Today.AddDays(3) : invoice.DueDate;
        SelectComboValue(CustomerComboBox, BuildCustomerOption(invoice));
        SetStatusComboValue(Ui(invoice.Status));
        SelectComboValue(ManagerComboBox, SalesManagerDisplayResolver.Resolve(invoice.Manager));
        SelectComboValue(CurrencyComboBox, Ui(invoice.CurrencyCode));
        CommentTextBox.Text = Ui(invoice.Comment);
        LoadDiscount(invoice.ManualDiscountPercent, invoice.ManualDiscountAmount);
        ReplaceLines(invoice.Lines);
    }

    private void LoadShipment(SalesShipmentRecord shipment)
    {
        NumberTextBox.Text = Ui(shipment.Number);
        DocumentDatePicker.SelectedDate = shipment.ShipmentDate == default ? DateTime.Today : shipment.ShipmentDate;
        SelectComboValue(CustomerComboBox, BuildCustomerOption(shipment));
        SetStatusComboValue(Ui(shipment.Status));
        SelectComboValue(WarehouseComboBox, Ui(shipment.Warehouse));
        SelectComboValue(ManagerComboBox, SalesManagerDisplayResolver.Resolve(shipment.Manager));
        CarrierTextBox.Text = Ui(shipment.Carrier);
        CommentTextBox.Text = Ui(shipment.Comment);
        LoadDiscount(shipment.ManualDiscountPercent, shipment.ManualDiscountAmount);
        ReplaceLines(shipment.Lines);
    }

    private void HandleCustomerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _updatingCustomerLookup || _mode != SalesDocumentEditorMode.Order)
        {
            return;
        }

        ApplySelectedCustomerDefaults();
    }

    private void HandleCustomerLookupLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _mode != SalesDocumentEditorMode.Order)
        {
            return;
        }

        if (!ApplySelectedCustomerDefaults())
        {
            ResetCustomerLookupOptions();
        }
    }

    private bool ApplySelectedCustomerDefaults()
    {
        var customer = GetSelectedCustomer();
        if (customer is null)
        {
            return false;
        }

        ResetCustomerLookupOptions();
        SelectComboValue(CustomerComboBox, BuildCustomerOption(customer));
        SelectComboValue(CurrencyComboBox, Ui(customer.CurrencyCode));
        return true;
    }

    private void HandleCustomerLookupTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading
            || _updatingCustomerLookup
            || _mode != SalesDocumentEditorMode.Order
            || !CustomerComboBox.IsKeyboardFocusWithin)
        {
            return;
        }

        var query = Ui(CustomerComboBox.Text).Trim();
        if (CustomerComboBox.SelectedItem is string selected
            && Ui(selected).Equals(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var matches = BuildCustomerLookupMatches(query);

        _updatingCustomerLookup = true;
        try
        {
            CustomerComboBox.ItemsSource = matches;
            CustomerComboBox.SelectedItem = null;
            CustomerComboBox.Text = query;
            CustomerComboBox.IsDropDownOpen = query.Length > 0 && matches.Length > 0;

            if (CustomerComboBox.Template.FindName("PART_EditableTextBox", CustomerComboBox) is TextBox textBox)
            {
                textBox.Text = query;
                textBox.CaretIndex = textBox.Text.Length;
            }
        }
        finally
        {
            _updatingCustomerLookup = false;
        }
    }

    private string[] BuildCustomerLookupMatches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _customerOptionTexts;
        }

        var tokens = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        if (tokens.Length == 0)
        {
            return _customerOptionTexts;
        }

        return _customerOptionTexts
            .Where(option => tokens.All(token => option.Contains(token, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(option => option.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(option => option, StringComparer.CurrentCultureIgnoreCase)
            .Take(80)
            .ToArray();
    }

    private void ResetCustomerLookupOptions()
    {
        _updatingCustomerLookup = true;
        try
        {
            CustomerComboBox.ItemsSource = _customerOptionTexts;
        }
        finally
        {
            _updatingCustomerLookup = false;
        }
    }

    private void HandleOrderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mode == SalesDocumentEditorMode.Order)
        {
            return;
        }

        var order = GetSelectedOrder();
        if (order is not null)
        {
            LoadFromBaseOrder(order);
        }
    }

    private void HandleStatusSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _syncingStatusSelection)
        {
            return;
        }

        var selectedStatus = ResolveStatusSelection(e);
        if (string.IsNullOrWhiteSpace(selectedStatus))
        {
            return;
        }

        _selectedStatusValue = NormalizeStatusForMode(selectedStatus);
        ApplySelectedStatusToDraft();
        // RenderRelatedDocuments сюда вызывать не нужно: статус не влияет на цепочку документов.
        if (sender is ComboBox combo && combo.SelectedItem is not null)
        {
            var picked = _selectedStatusValue;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.IsNullOrWhiteSpace(picked)
                    && !Ui(combo.SelectedItem?.ToString()).Equals(picked, StringComparison.OrdinalIgnoreCase))
                {
                    SetStatusComboValue(picked);
                }
            }));
        }
    }

    private string ResolveStatusSelection(SelectionChangedEventArgs e)
    {
        return Ui(e.AddedItems.OfType<object>().FirstOrDefault()?.ToString()
                  ?? StatusComboBox.SelectedItem?.ToString()
                  ?? StatusComboBox.Text);
    }

    private string NormalizeStatusForMode(string status)
    {
        return _mode == SalesDocumentEditorMode.Order
            ? _workspace.NormalizeOrderStatus(status)
            : Ui(status).Trim();
    }

    private void SetStatusComboValue(string status)
    {
        var normalized = NormalizeStatusForMode(status);
        _selectedStatusValue = normalized;

        _syncingStatusSelection = true;
        try
        {
            SelectComboValue(StatusComboBox, normalized);
        }
        finally
        {
            _syncingStatusSelection = false;
        }
    }

    private string GetSelectedStatus()
    {
        var status = _selectedStatusValue;
        if (string.IsNullOrWhiteSpace(status))
        {
            status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text;
        }

        return NormalizeStatusForMode(status ?? string.Empty);
    }

    private void LoadFromBaseOrder(SalesOrderRecord order)
    {
        _loading = true;
        if (_mode == SalesDocumentEditorMode.Invoice)
        {
            _invoiceDraft = _workspace.CreateInvoiceDraftFromOrder(order.Id);
            LoadInvoice(_invoiceDraft);
        }
        else if (_mode == SalesDocumentEditorMode.Shipment)
        {
            _shipmentDraft = _workspace.CreateShipmentDraftFromOrder(order.Id);
            LoadShipment(_shipmentDraft);
        }

        SelectComboValue(OrderComboBox, BuildOrderOption(order));
        _loading = false;
        RefreshTotal();
        RenderRelatedDocuments();
    }

    private const double CatalogPickerPanelWidth = 640d;
    private double _widthBeforePicker;

    private void HandleAddLineClick(object sender, RoutedEventArgs e)
    {
        if (CatalogPicker.Visibility == Visibility.Visible)
        {
            HideCatalogPicker();
            return;
        }

        var catalog = GetLineCatalogItems();
        if (catalog.Count == 0)
        {
            ValidationText.Text = "Каталог номенклатуры пуст. Откройте раздел «Товары» или проверьте подключение к базе.";
            return;
        }

        ShowCatalogPicker(catalog);
    }

    private void ShowCatalogPicker(IReadOnlyList<SalesCatalogItemOption> catalog)
    {
        ValidationText.Text = string.Empty;
        CatalogPicker.LoadCatalog(catalog);
        CatalogPickerColumn.Width = new GridLength(CatalogPickerPanelWidth);
        CatalogPicker.Visibility = Visibility.Visible;

        _widthBeforePicker = Width;
        var screenWidth = SystemParameters.WorkArea.Width;
        var targetWidth = Math.Min(Width + CatalogPickerPanelWidth, screenWidth);
        if (targetWidth > Width)
        {
            Width = targetWidth;
            if (Left + Width > screenWidth)
            {
                Left = Math.Max(0d, screenWidth - Width);
            }
        }

        CatalogPicker.FocusSearch();
    }

    private void HideCatalogPicker()
    {
        CatalogPicker.Visibility = Visibility.Collapsed;
        CatalogPickerColumn.Width = new GridLength(0);
        if (_widthBeforePicker > 0d && Math.Abs(Width - _widthBeforePicker) > 1d)
        {
            Width = _widthBeforePicker;
        }
    }

    private void HandleCatalogPickerLinesTransferred(object? sender, SalesPickerLinesEventArgs e)
    {
        if (e.Lines.Count == 0)
        {
            return;
        }

        foreach (var line in e.Lines)
        {
            _lines.Add(new SalesLineEditorRow(
                Ui(line.ItemCode),
                Ui(line.ItemName),
                NormalizeUnit(line.Unit, line.ItemName),
                line.Quantity,
                line.Price,
                LineNo: _lines.Count + 1));
        }
        RenumberLines();
        RefreshTotal();
        HideCatalogPicker();
    }

    private void HandleCatalogPickerCloseRequested(object? sender, EventArgs e)
    {
        HideCatalogPicker();
    }

    private void RenumberLines()
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].LineNo != i + 1)
            {
                _lines[i] = _lines[i] with { LineNo = i + 1 };
            }
        }
    }

    private void HandleEditLineClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LinesGrid.SelectedItem is not SalesLineEditorRow row)
            {
                ValidationText.Text = "Выберите позицию для изменения.";
                return;
            }

            var quantity = PromptDecimal(
                "Изменить позицию",
                $"Введите новое количество ({NormalizeUnit(row.Unit, row.ItemName)}).",
                row.Quantity.ToString("N2", RuCulture));
            if (quantity <= 0m)
            {
                return;
            }

            var price = PromptDecimal("Изменить позицию", "Введите новую цену.", row.Price.ToString("N2", RuCulture));
            if (price < 0m)
            {
                return;
            }

            var index = _lines.IndexOf(row);
            if (index >= 0)
            {
                _lines[index] = row with { Quantity = quantity, Price = price };
            }

            RefreshTotal();
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, "SalesDocumentEditorWindow.HandleEditLineClick");
            ValidationText.Text = $"Не удалось изменить позицию: {exception.Message}";
        }
    }

    // 1C-style inline edit: ОДИН клик по ячейке кол-ва/цены/ед./кода сразу включает редактирование.
    // Раньше двойной клик открывал отдельное окно — теперь окно вызывается только кнопкой «Изменить».
    private void HandleLinesGridPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        // Ищем DataGridCell, по которой кликнули
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not DataGridCell)
        {
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }

        if (dep is not DataGridCell cell || cell.IsReadOnly)
        {
            return;
        }

        // Не входим в edit если ячейка из read-only колонки (Сумма)
        if (cell.Column is DataGridColumn col && col.IsReadOnly)
        {
            return;
        }

        // Если ячейка ещё не сфокусирована — сначала фокусируем
        if (!cell.IsFocused)
        {
            cell.Focus();
        }

        // Включаем режим редактирования сразу
        grid.BeginEdit();
    }

    private void HandleLinesGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit
            && e.Row.Item is SalesLineEditorRow row
            && e.EditingElement is TextBox textBox
            && e.Column.DisplayIndex is 2 or 3)
        {
            e.Cancel = true;
            var index = _lines.IndexOf(row);
            if (index >= 0 && TryApplyInlineLineEdit(row, e.Column.DisplayIndex, textBox.Text, out var updated))
            {
                _lines[index] = updated;
                ValidationText.Text = string.Empty;
            }
            else
            {
                ValidationText.Text = "Введите корректное значение позиции.";
            }
        }

        Dispatcher.BeginInvoke(RefreshTotal);
    }

    private void HandleLinesGridCurrentCellChanged(object sender, EventArgs e)
    {
        RefreshTotal();
    }

    private void HandleDiscountPercentChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _updatingDiscountFields)
        {
            return;
        }

        var text = DiscountPercentTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _manualDiscountPercent = 0m;
            _manualDiscountAmount = 0m;
            _discountPercentMode = true;
            RefreshTotal();
            return;
        }

        if (!TryParseDecimal(text, out var value))
        {
            return;
        }

        _manualDiscountPercent = Math.Clamp(value, 0m, 100m);
        _manualDiscountAmount = 0m;
        _discountPercentMode = true;
        RefreshTotal();
    }

    private void HandleDiscountAmountChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _updatingDiscountFields)
        {
            return;
        }

        var text = DiscountAmountTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _manualDiscountAmount = 0m;
            _manualDiscountPercent = 0m;
            _discountPercentMode = false;
            RefreshTotal();
            return;
        }

        if (!TryParseDecimal(text, out var value))
        {
            return;
        }

        _manualDiscountAmount = Math.Max(0m, value);
        _manualDiscountPercent = 0m;
        _discountPercentMode = false;
        RefreshTotal();
    }

    private static bool TryApplyInlineLineEdit(
        SalesLineEditorRow row,
        int displayIndex,
        string value,
        out SalesLineEditorRow updated)
    {
        updated = row;
        switch (displayIndex)
        {
            case 2:
                if (!TryParseDecimal(value, out var quantity) || quantity <= 0m)
                {
                    return false;
                }

                updated = row with { Quantity = quantity };
                return true;
            case 3:
                if (!TryParseDecimal(value, out var price) || price < 0m)
                {
                    return false;
                }

                updated = row with { Price = price };
                return true;
            default:
                return false;
        }
    }

    private void HandleRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is SalesLineEditorRow row)
        {
            _lines.Remove(row);
            RefreshTotal();
            return;
        }

        ValidationText.Text = "Выберите позицию для удаления.";
    }

    private void HandleCreateReturnClick(object sender, RoutedEventArgs e)
    {
        if (_mode != SalesDocumentEditorMode.Order)
        {
            return;
        }

        var order = _orderDraft is null
            ? null
            : _workspace.Orders.FirstOrDefault(item => item.Id == _orderDraft.Id);
        if (order is null)
        {
            ValidationText.Text = "Сначала сохраните заказ, после этого можно создать возврат.";
            return;
        }

        var selectedRows = LinesGrid.SelectedItems
            .OfType<SalesLineEditorRow>()
            .Distinct()
            .ToArray();
        if (selectedRows.Length == 0)
        {
            ValidationText.Text = "Выделите позиции, которые нужно вернуть.";
            return;
        }

        var returnDocument = _workspace.CreateReturnDraftFromOrder(order.Id);
        returnDocument.Lines = ToSalesLines(selectedRows);
        if (selectedRows.Length != _lines.Count)
        {
            returnDocument.ManualDiscountAmount = 0m;
            returnDocument.ManualDiscountPercent = 0m;
        }

        var dialog = new SalesReturnEditorWindow(_workspace, returnDocument);
        WpfDialogOwner.TrySetOwner(dialog, ResolvePromptOwner());

        if (dialog.ShowDialog() != true || dialog.ResultReturn is null)
        {
            return;
        }

        try
        {
            _workspace.AddReturn(dialog.ResultReturn);
        }
        catch (InvalidOperationException exception)
        {
            ValidationText.Text = exception.Message;
            return;
        }

        RenderRelatedDocuments();
        RelatedDocumentsGrid.SelectedItem = _relatedDocuments.FirstOrDefault(item =>
            item.Category == "return" && item.Id == dialog.ResultReturn.Id);
        ValidationText.Text = string.Empty;
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ClearValidationMessage();
        if (string.IsNullOrWhiteSpace(NumberTextBox.Text))
        {
            ValidationText.Text = "Укажите номер документа.";
            return;
        }

        if (DocumentDatePicker.SelectedDate is null)
        {
            ValidationText.Text = "Укажите дату документа.";
            return;
        }

        if (_lines.Count == 0)
        {
            ValidationText.Text = "Добавьте хотя бы одну позицию.";
            return;
        }

        if (!ValidateLines())
        {
            return;
        }

        switch (_mode)
        {
            case SalesDocumentEditorMode.Order:
                SaveOrder();
                break;
            case SalesDocumentEditorMode.Invoice:
                SaveInvoice();
                break;
            case SalesDocumentEditorMode.Shipment:
                SaveShipment();
                break;
        }
    }

    private void HandlePrintButtonClick(object sender, RoutedEventArgs e)
    {
        ClearValidationMessage();

        var menu = BuildPrintMenu();
        if (menu.Items.Count == 0)
        {
            ValidationText.Text = "Нет доступных печатных форм для текущего документа.";
            return;
        }

        PrintButton.ContextMenu = menu;
        menu.PlacementTarget = PrintButton;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private ContextMenu BuildPrintMenu()
    {
        var menu = new ContextMenu();
        var order = BuildCurrentOrderSnapshot() ?? ResolveRelatedOrder();
        if (order is not null)
        {
            AddPrintMenuItem(menu, "Заказ покупателя", () => RecordsWorkspaceCatalog.PrintOrderCustomer(order));
            AddPrintMenuItem(menu, "Лист сборки", () => RecordsWorkspaceCatalog.PrintOrderPicking(_workspace, order));
        }

        var currentInvoice = BuildCurrentInvoiceSnapshot();
        if (currentInvoice is not null)
        {
            AddSeparator(menu);
            AddPrintMenuItem(menu, $"Счет на оплату {currentInvoice.Number}", () => RecordsWorkspaceCatalog.PrintInvoice(currentInvoice));
        }

        var currentShipment = BuildCurrentShipmentSnapshot();
        if (currentShipment is not null)
        {
            AddSeparator(menu);
            AddPrintMenuItem(menu, $"Расходная накладная {currentShipment.Number}", () => RecordsWorkspaceCatalog.PrintShipment(currentShipment));
        }

        if (order is null)
        {
            return menu;
        }

        AddRelatedPrintItems(menu, order, currentInvoice?.Id, currentShipment?.Id);
        return menu;
    }

    private void AddRelatedPrintItems(ContextMenu menu, SalesOrderRecord order, Guid? currentInvoiceId, Guid? currentShipmentId)
    {
        var hasRelatedItems = false;

        foreach (var invoice in _workspace.Invoices
                     .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order) && item.Id != currentInvoiceId)
                     .OrderByDescending(item => item.InvoiceDate))
        {
            if (!hasRelatedItems)
            {
                AddSeparator(menu);
                hasRelatedItems = true;
            }

            var snapshot = invoice.Clone();
            AddPrintMenuItem(menu, $"Счет на оплату {snapshot.Number}", () => RecordsWorkspaceCatalog.PrintInvoice(snapshot));
        }

        foreach (var shipment in _workspace.Shipments
                     .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order) && item.Id != currentShipmentId)
                     .OrderByDescending(item => item.ShipmentDate))
        {
            if (!hasRelatedItems)
            {
                AddSeparator(menu);
                hasRelatedItems = true;
            }

            var snapshot = shipment.Clone();
            AddPrintMenuItem(menu, $"Расходная накладная {snapshot.Number}", () => RecordsWorkspaceCatalog.PrintShipment(snapshot));
        }

        foreach (var returnDocument in _workspace.Returns
                     .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order))
                     .OrderByDescending(item => item.ReturnDate))
        {
            if (!hasRelatedItems)
            {
                AddSeparator(menu);
                hasRelatedItems = true;
            }

            var snapshot = returnDocument.Clone();
            AddPrintMenuItem(menu, $"Возврат {snapshot.Number}", () => RecordsWorkspaceCatalog.PrintReturn(snapshot));
        }

        foreach (var cashReceipt in _workspace.CashReceipts
                     .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order))
                     .OrderByDescending(item => item.ReceiptDate))
        {
            if (!hasRelatedItems)
            {
                AddSeparator(menu);
                hasRelatedItems = true;
            }

            var snapshot = cashReceipt.Clone();
            AddPrintMenuItem(menu, $"ПКО {snapshot.Number}", () => RecordsWorkspaceCatalog.PrintCashReceipt(snapshot));
        }
    }

    private static void AddSeparator(ContextMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
        {
            menu.Items.Add(new Separator());
        }
    }

    private static void AddPrintMenuItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private SalesOrderRecord? BuildCurrentOrderSnapshot()
    {
        if (_mode != SalesDocumentEditorMode.Order)
        {
            return null;
        }

        var customer = GetSelectedCustomer();
        var order = _orderDraft?.Clone()
                    ?? (customer is null ? null : _workspace.CreateOrderDraft(customer.Id));
        if (order is null)
        {
            return null;
        }

        order.Number = NumberTextBox.Text.Trim();
        order.OrderDate = DocumentDatePicker.SelectedDate?.Date ?? DateTime.Today;
        if (customer is not null)
        {
            ApplyCustomer(order, customer);
        }

        order.Warehouse = WarehouseComboBox.Text.Trim();
        order.Organization = OrganizationComboBox.Text.Trim();
        order.Status = GetSelectedStatus();
        order.Manager = ManagerComboBox.Text.Trim();
        order.CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim();
        order.Comment = CommentTextBox.Text.Trim();
        order.ManualDiscountPercent = _discountPercentMode ? _manualDiscountPercent : 0m;
        order.ManualDiscountAmount = _discountPercentMode ? 0m : _manualDiscountAmount;
        ApplyOrderExtrasFromUi(order);
        order.Lines = ToSalesLines();
        return order;
    }

    private SalesInvoiceRecord? BuildCurrentInvoiceSnapshot()
    {
        if (_mode != SalesDocumentEditorMode.Invoice)
        {
            return null;
        }

        var order = GetSelectedOrder();
        var invoice = _invoiceDraft?.Clone()
                      ?? (order is null ? null : _workspace.CreateInvoiceDraftFromOrder(order.Id));
        if (invoice is null)
        {
            return null;
        }

        if (order is not null)
        {
            ApplyBaseOrder(invoice, order);
        }

        invoice.Number = NumberTextBox.Text.Trim();
        invoice.InvoiceDate = DocumentDatePicker.SelectedDate?.Date ?? DateTime.Today;
        invoice.DueDate = SecondaryDatePicker.SelectedDate?.Date ?? DateTime.Today.AddDays(3);
        invoice.Status = GetSelectedStatus();
        invoice.Manager = ManagerComboBox.Text.Trim();
        invoice.CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim();
        invoice.Comment = CommentTextBox.Text.Trim();
        invoice.ManualDiscountPercent = _discountPercentMode ? _manualDiscountPercent : 0m;
        invoice.ManualDiscountAmount = _discountPercentMode ? 0m : _manualDiscountAmount;
        invoice.Lines = ToSalesLines();
        return invoice;
    }

    private SalesShipmentRecord? BuildCurrentShipmentSnapshot()
    {
        if (_mode != SalesDocumentEditorMode.Shipment)
        {
            return null;
        }

        var order = GetSelectedOrder();
        var shipment = _shipmentDraft?.Clone()
                       ?? (order is null ? null : _workspace.CreateShipmentDraftFromOrder(order.Id));
        if (shipment is null)
        {
            return null;
        }

        if (order is not null)
        {
            ApplyBaseOrder(shipment, order);
        }

        shipment.Number = NumberTextBox.Text.Trim();
        shipment.ShipmentDate = DocumentDatePicker.SelectedDate?.Date ?? DateTime.Today;
        shipment.Warehouse = WarehouseComboBox.Text.Trim();
        shipment.Status = GetSelectedStatus();
        shipment.Carrier = CarrierTextBox.Text.Trim();
        shipment.Manager = ManagerComboBox.Text.Trim();
        shipment.Comment = CommentTextBox.Text.Trim();
        shipment.ManualDiscountPercent = _discountPercentMode ? _manualDiscountPercent : 0m;
        shipment.ManualDiscountAmount = _discountPercentMode ? 0m : _manualDiscountAmount;
        shipment.Lines = ToSalesLines();
        return shipment;
    }

    private void SaveOrder()
    {
        var customer = GetSelectedCustomer();
        if (customer is null)
        {
            ValidationText.Text = "Выберите клиента.";
            return;
        }

        if (string.IsNullOrWhiteSpace(WarehouseComboBox.Text))
        {
            ValidationText.Text = "Укажите склад заказа.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ManagerComboBox.Text))
        {
            ValidationText.Text = "Укажите ответственного менеджера.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrencyComboBox.Text))
        {
            ValidationText.Text = "Укажите валюту заказа.";
            return;
        }

        if (string.IsNullOrWhiteSpace(OrganizationComboBox.Text))
        {
            ValidationText.Text = "Укажите организацию.";
            return;
        }

        var order = _orderDraft ?? _workspace.CreateOrderDraft(customer.Id);
        order.Number = NumberTextBox.Text.Trim();
        order.OrderDate = DocumentDatePicker.SelectedDate!.Value.Date;
        ApplyCustomer(order, customer);
        order.Warehouse = WarehouseComboBox.Text.Trim();
        order.Organization = OrganizationComboBox.Text.Trim();
        order.Status = GetSelectedStatus();
        order.Manager = ManagerComboBox.Text.Trim();
        order.CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim();
        order.Comment = CommentTextBox.Text.Trim();
        order.ManualDiscountPercent = _discountPercentMode ? _manualDiscountPercent : 0m;
        order.ManualDiscountAmount = _discountPercentMode ? 0m : _manualDiscountAmount;
        ApplyOrderExtrasFromUi(order);
        order.Lines = ToSalesLines();

        ResultOrder = order;
        CompleteEditing(success: true);
    }

    private void ApplyOrderExtrasFromUi(SalesOrderRecord order)
    {
        // 1С-поля: флажки шапки + Акт + EasyCeiling + Отгрузка + Договор.
        var contract = ContractComboBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(contract))
        {
            order.ContractNumber = contract;
        }
        order.IsPhoneInstall = IsPhoneInstallCheckBox.IsChecked == true;
        order.IsAirConditioner = IsAirConditionerCheckBox.IsChecked == true;
        order.IsYekaterinburg = IsYekaterinburgCheckBox.IsChecked == true;
        order.VatEnabled = VatEnabledCheckBox.IsChecked == true;
        order.EasyCeilingOrderNumber = EasyCeilingOrderNumberTextBox.Text?.Trim() ?? string.Empty;
        order.ShippingDate = ShippingDatePicker.SelectedDate?.Date;
        order.SurveyorName = SurveyorComboBox.Text?.Trim() ?? string.Empty;
        order.ActNumber = ActNumberTextBox.Text?.Trim() ?? string.Empty;
        order.ActDate = ActDatePicker.SelectedDate?.Date;
        order.ComplexityScore = TryParseDecimal(ComplexityScoreTextBox.Text, out var complexityScore) ? complexityScore : 0m;
        order.ComplexityDiscountAmount = TryParseDecimal(ComplexityDiscountAmountTextBox.Text, out var complexityAmount) ? complexityAmount : 0m;
        order.ComplexityDiscountPercent = TryParseDecimal(ComplexityDiscountPercentTextBox.Text, out var complexityPercent) ? complexityPercent : 0m;
    }

    private void SaveInvoice()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ValidationText.Text = "Выберите заказ-основание.";
            return;
        }

        if (!ValidateBaseOrder(order, "счета"))
        {
            return;
        }

        if (SecondaryDatePicker.SelectedDate is null)
        {
            ValidationText.Text = "Укажите срок оплаты.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ManagerComboBox.Text))
        {
            ValidationText.Text = "Укажите ответственного менеджера.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrencyComboBox.Text))
        {
            ValidationText.Text = "Укажите валюту счета.";
            return;
        }

        var invoice = _invoiceDraft ?? _workspace.CreateInvoiceDraftFromOrder(order.Id);
        invoice.Number = NumberTextBox.Text.Trim();
        invoice.InvoiceDate = DocumentDatePicker.SelectedDate!.Value.Date;
        invoice.DueDate = SecondaryDatePicker.SelectedDate.Value.Date;
        invoice.Status = GetSelectedStatus();
        invoice.Manager = ManagerComboBox.Text.Trim();
        invoice.CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim();
        invoice.Comment = CommentTextBox.Text.Trim();
        invoice.ManualDiscountPercent = _discountPercentMode ? _manualDiscountPercent : 0m;
        invoice.ManualDiscountAmount = _discountPercentMode ? 0m : _manualDiscountAmount;
        invoice.Lines = ToSalesLines();

        ResultInvoice = invoice;
        CompleteEditing(success: true);
    }

    private void SaveShipment()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ValidationText.Text = "Выберите заказ-основание.";
            return;
        }

        if (!ValidateBaseOrder(order, "отгрузки"))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(WarehouseComboBox.Text))
        {
            ValidationText.Text = "Укажите склад отгрузки.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ManagerComboBox.Text))
        {
            ValidationText.Text = "Укажите ответственного менеджера.";
            return;
        }

        var shipment = _shipmentDraft ?? _workspace.CreateShipmentDraftFromOrder(order.Id);
        shipment.Number = NumberTextBox.Text.Trim();
        shipment.ShipmentDate = DocumentDatePicker.SelectedDate!.Value.Date;
        shipment.Warehouse = WarehouseComboBox.Text.Trim();
        shipment.Status = GetSelectedStatus();
        shipment.Carrier = CarrierTextBox.Text.Trim();
        shipment.Manager = ManagerComboBox.Text.Trim();
        shipment.Comment = CommentTextBox.Text.Trim();
        shipment.ManualDiscountPercent = _discountPercentMode ? _manualDiscountPercent : 0m;
        shipment.ManualDiscountAmount = _discountPercentMode ? 0m : _manualDiscountAmount;
        shipment.Lines = ToSalesLines();

        ResultShipment = shipment;
        CompleteEditing(success: true);
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        CompleteEditing(success: false);
    }

    // «Записать» — алиас для сохранения (без особого поведения, как в УНФ это просто Save).
    private void HandleRecordClick(object sender, RoutedEventArgs e)
    {
        HandleSaveClick(sender, e);
    }

    // «Провести» — алиас для сохранения. В УНФ «Провести» отличается тем, что
    // применяет движения по регистрам (склад, касса, цены). В нашем случае
    // сохранение и так пишет в SalesWorkspace — поэтому совпадает с «Записать».
    private void HandlePostClick(object sender, RoutedEventArgs e)
    {
        HandleSaveClick(sender, e);
    }

    // «Создать на основании» — меню действий по образцу 1С УНФ.
    // Активны: Счёт на оплату, Поступление в кассу, Расходная накладная, Возврат от покупателя.
    // Остальные показаны как «в разработке».
    private void HandleCreateBasedOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
        {
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu();

        AddCreateBasedOnItem(menu, "Счёт на оплату", true, () => CreateInvoiceFromCurrentOrder());
        AddCreateBasedOnItem(menu, "Ввести фактический платёж", false);
        AddCreateBasedOnItem(menu, "Поступление в кассу", true, () => CreateCashReceiptFromCurrentOrder());
        AddCreateBasedOnItem(menu, "Поступление на счёт", false);
        AddCreateBasedOnItem(menu, "Оплата картой", false);
        AddCreateBasedOnItem(menu, "Чек ККМ", false);
        AddCreateBasedOnItem(menu, "Расходная накладная", true, () => CreateShipmentFromCurrentOrder());
        AddCreateBasedOnItem(menu, "Акт выполненных работ", false);
        AddCreateBasedOnItem(menu, "Возврат от покупателя", true, () => CreateReturnFromCurrentOrder());
        AddCreateBasedOnItem(menu, "Заказ поставщику", false);
        AddCreateBasedOnItem(menu, "Заказ поставщику (по калькуляции)", false);
        AddCreateBasedOnItem(menu, "Производство", false);
        AddCreateBasedOnItem(menu, "Заказ на производство", false);
        AddCreateBasedOnItem(menu, "Перемещение запасов", false);

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void AddCreateBasedOnItem(System.Windows.Controls.ContextMenu menu, string title, bool isEnabled, Action? action = null)
    {
        var item = new System.Windows.Controls.MenuItem
        {
            Header = title,
            IsEnabled = isEnabled,
            Padding = new Thickness(12, 6, 18, 6)
        };
        if (action is not null)
        {
            item.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception exception)
                {
                    ValidationText.Text = $"Не удалось: {exception.Message}";
                }
            };
        }
        else
        {
            item.ToolTip = "В разработке";
        }
        menu.Items.Add(item);
    }

    private void CreateInvoiceFromCurrentOrder()
    {
        var order = BuildCurrentOrderSnapshot() ?? ResolveRelatedOrder();
        if (order is null)
        {
            ValidationText.Text = "Сначала сохраните заказ — счёт создаётся на основании.";
            return;
        }
        RecordsWorkspaceCatalog.CreateInvoiceFromOrder(_workspace, order);
        RenderRelatedDocuments();
    }

    private void CreateShipmentFromCurrentOrder()
    {
        var order = BuildCurrentOrderSnapshot() ?? ResolveRelatedOrder();
        if (order is null)
        {
            ValidationText.Text = "Сначала сохраните заказ — расходная создаётся на основании.";
            return;
        }
        RecordsWorkspaceCatalog.CreateShipmentFromOrder(_workspace, order);
        RenderRelatedDocuments();
    }

    private void CreateReturnFromCurrentOrder()
    {
        var order = BuildCurrentOrderSnapshot() ?? ResolveRelatedOrder();
        if (order is null)
        {
            ValidationText.Text = "Сначала сохраните заказ — возврат создаётся на основании.";
            return;
        }
        RecordsWorkspaceCatalog.CreateReturnFromOrder(_workspace, order);
        RenderRelatedDocuments();
    }

    private void CreateCashReceiptFromCurrentOrder()
    {
        var order = BuildCurrentOrderSnapshot() ?? ResolveRelatedOrder();
        if (order is null)
        {
            ValidationText.Text = "Сначала сохраните заказ — поступление в кассу создаётся на основании.";
            return;
        }
        var result = _workspace.RecordCashReceiptForOrder(order.Id);
        ValidationText.Text = result.Succeeded
            ? $"Создано поступление в кассу: {result.Detail}"
            : $"Не удалось: {result.Message}";
        ValidationText.Foreground = result.Succeeded ? Brushes.SeaGreen : Brushes.IndianRed;
        RenderRelatedDocuments();
    }

    // Открывает отдельное окно «Связанные документы» по образцу 1С (структура подчинённости).
    private void HandleStructureLinksClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Window dialog;
            switch (_mode)
            {
                case SalesDocumentEditorMode.Order:
                    var order = BuildCurrentOrderSnapshot() ?? ResolveRelatedOrder();
                    if (order is null)
                    {
                        ValidationText.Text = "Сначала сохраните заказ — затем можно посмотреть цепочку связанных документов.";
                        return;
                    }
                    dialog = new SalesDocumentLinksWindow(_workspace, order);
                    break;
                case SalesDocumentEditorMode.Invoice:
                    var invoice = BuildCurrentInvoiceSnapshot();
                    if (invoice is null)
                    {
                        ValidationText.Text = "Сначала сохраните счёт.";
                        return;
                    }
                    dialog = new SalesDocumentLinksWindow(_workspace, invoice);
                    break;
                case SalesDocumentEditorMode.Shipment:
                    var shipment = BuildCurrentShipmentSnapshot();
                    if (shipment is null)
                    {
                        ValidationText.Text = "Сначала сохраните отгрузку.";
                        return;
                    }
                    dialog = new SalesDocumentLinksWindow(_workspace, shipment);
                    break;
                default:
                    return;
            }

            WpfDialogOwner.TrySetOwner(dialog, ResolvePromptOwner());
            dialog.ShowDialog();
            RenderRelatedDocuments();
        }
        catch (Exception exception)
        {
            ValidationText.Text = $"Не удалось открыть структуру: {exception.Message}";
        }
    }

    private void HandleSetPurchaseMinClick(object sender, RoutedEventArgs e)
    {
        if (_lines.Count == 0)
        {
            ValidationText.Text = "Добавьте хотя бы одну позицию.";
            return;
        }
        var total = _lines.Sum(item => item.Quantity);
        MessageBox.Show(
            this,
            $"Минимальное количество для закупки по этому заказу: {total:N2}.\nПозиций: {_lines.Count}.",
            "Установить закуп",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // Реквизиты — popup с банковскими данными ИП по выбранной организации.
    private void HandleOrganizationRequisitesClick(object sender, RoutedEventArgs e)
    {
        var orgName = OrganizationComboBox.Text?.Trim() ?? string.Empty;
        var details = OrganizationBankRegistry.ResolveOrDefault(orgName);
        var lines = new List<string>
        {
            $"Получатель: {details.LegalName}",
            $"ИНН: {(string.IsNullOrWhiteSpace(details.Inn) ? "—" : details.Inn)}",
            $"КПП: {(string.IsNullOrWhiteSpace(details.Kpp) ? "—" : details.Kpp)}",
            $"Адрес: {(string.IsNullOrWhiteSpace(details.LegalAddress) ? "—" : details.LegalAddress)}",
            string.Empty,
            $"Банк: {(string.IsNullOrWhiteSpace(details.BankName) ? "—" : details.BankName)}",
            $"БИК: {(string.IsNullOrWhiteSpace(details.Bik) ? "—" : details.Bik)}",
            $"К/с: {(string.IsNullOrWhiteSpace(details.CorrespondentAccount) ? "—" : details.CorrespondentAccount)}",
            $"Р/с: {(string.IsNullOrWhiteSpace(details.PaymentAccount) ? "—" : details.PaymentAccount)}"
        };
        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, lines),
            "Реквизиты организации",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HandleOrganizationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var orgName = OrganizationComboBox.Text?.Trim() ?? OrganizationComboBox.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        var details = OrganizationBankRegistry.Resolve(orgName);
        OrganizationRequisitesText.Text = details is null
            ? "Банковский счёт, подписи и другие реквизиты"
            : $"{details.BankName} · БИК {details.Bik} · Р/с {details.PaymentAccount}";
    }

    private void CompleteEditing(bool success)
    {
        if (_hostedInWorkspace)
        {
            if (success)
            {
                var args = new SalesDocumentHostedSaveEventArgs();
                HostedSaved?.Invoke(this, args);
                if (!args.Succeeded)
                {
                    ValidationText.Foreground = ValidationMessageBrush;
                    ValidationText.Text = args.ErrorMessage ?? "Документ не сохранен.";
                    return;
                }

                ReloadCurrentDocumentFromWorkspace();
                RefreshTotal();
                RenderRelatedDocuments();
                ShowSuccessMessage("Документ сохранен.");
            }
            else
            {
                HostedCanceled?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        DialogResult = success;
    }

    private void ClearValidationMessage()
    {
        ValidationText.Foreground = ValidationMessageBrush;
        ValidationText.Text = string.Empty;
    }

    private void ShowSuccessMessage(string message)
    {
        ValidationText.Foreground = SuccessMessageBrush;
        ValidationText.Text = message;
    }

    private void ReloadCurrentDocumentFromWorkspace()
    {
        _loading = true;
        try
        {
            if (_mode == SalesDocumentEditorMode.Order && _orderDraft is not null)
            {
                var order = _workspace.Orders.FirstOrDefault(item => item.Id == _orderDraft.Id);
                if (order is not null)
                {
                    _orderDraft = order.Clone();
                    LoadOrder(_orderDraft);
                }
            }
            else if (_mode == SalesDocumentEditorMode.Invoice && _invoiceDraft is not null)
            {
                var invoice = _workspace.Invoices.FirstOrDefault(item => item.Id == _invoiceDraft.Id);
                if (invoice is not null)
                {
                    _invoiceDraft = invoice.Clone();
                    LoadInvoice(_invoiceDraft);
                }
            }
            else if (_mode == SalesDocumentEditorMode.Shipment && _shipmentDraft is not null)
            {
                var shipment = _workspace.Shipments.FirstOrDefault(item => item.Id == _shipmentDraft.Id);
                if (shipment is not null)
                {
                    _shipmentDraft = shipment.Clone();
                    LoadShipment(_shipmentDraft);
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private SalesCustomerRecord? GetSelectedCustomer()
    {
        var selected = CustomerComboBox.SelectedItem?.ToString();
        var selectedCustomer = ResolveCustomer(selected);
        if (selectedCustomer is not null)
        {
            return selectedCustomer;
        }

        return ResolveCustomer(CustomerComboBox.Text);
    }

    private bool ValidateLines()
    {
        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            if (string.IsNullOrWhiteSpace(line.ItemName) && string.IsNullOrWhiteSpace(line.ItemCode))
            {
                ValidationText.Text = $"Позиция {index + 1}: укажите товар.";
                return false;
            }

            if (line.Quantity <= 0m)
            {
                ValidationText.Text = $"Позиция {index + 1}: количество должно быть больше нуля.";
                return false;
            }

            if (line.Price < 0m)
            {
                ValidationText.Text = $"Позиция {index + 1}: цена не может быть отрицательной.";
                return false;
            }
        }

        return true;
    }

    private bool ValidateBaseOrder(SalesOrderRecord order, string documentKind)
    {
        var customerExists = order.CustomerId != Guid.Empty
            && _workspace.Customers.Any(item => item.Id == order.CustomerId);
        if (!customerExists)
        {
            ValidationText.Text = $"Нельзя создать {documentKind}: у заказа-основания не найден клиент.";
            return false;
        }

        if (order.Lines.Count == 0)
        {
            ValidationText.Text = $"Нельзя создать {documentKind}: в заказе-основании нет позиций.";
            return false;
        }

        return true;
    }

    private SalesOrderRecord? GetSelectedOrder()
    {
        var selected = OrderComboBox.SelectedItem?.ToString() ?? OrderComboBox.Text;
        return ResolveOrder(selected);
    }

    private SalesOrderRecord? ResolveOrder(string? value)
    {
        var query = Ui(value).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        if (_orderOptions.TryGetValue(query, out var direct))
        {
            return direct;
        }

        var exact = _workspace.Orders.FirstOrDefault(order =>
            Ui(order.Number).Equals(query, StringComparison.CurrentCultureIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var matches = _workspace.Orders
            .Where(order =>
                BuildOrderOption(order).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || Ui(order.Number).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || Ui(order.CustomerName).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(order => order.OrderDate)
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private SalesCustomerRecord? ResolveCustomer(string? value)
    {
        var query = Ui(value).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        if (_customerOptions.TryGetValue(query, out var direct))
        {
            return direct;
        }

        var exact = _workspace.Customers.FirstOrDefault(customer =>
            Ui(customer.Name).Equals(query, StringComparison.CurrentCultureIgnoreCase)
            || Ui(customer.Code).Equals(query, StringComparison.CurrentCultureIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var matches = _workspace.Customers
            .Where(customer =>
                Ui(customer.Name).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || Ui(customer.Code).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || BuildCustomerOption(customer).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(customer => Ui(customer.Name), StringComparer.CurrentCultureIgnoreCase)
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private IReadOnlyList<SalesCatalogItemOption> GetLineCatalogItems()
    {
        if (_lineCatalogItems is not null)
        {
            return _lineCatalogItems;
        }

        var result = new List<SalesCatalogItemOption>();
        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCatalogItems(_workspace.CatalogItems);

        if (_workspace.OperationalSnapshot?.CatalogItems is { Count: > 0 } operationalItems)
        {
            AddCatalogItems(operationalItems);
        }

        try
        {
            var operatorName = string.IsNullOrWhiteSpace(_workspace.CurrentOperator)
                ? Environment.UserName
                : _workspace.CurrentOperator;
            var catalogWorkspace = CatalogWorkspaceStore
                .CreateDefault()
                .TryLoadExisting(operatorName, _workspace.Currencies, _workspace.Warehouses);
            if (catalogWorkspace is not null)
            {
                AddCatalogItems(catalogWorkspace.BuildSalesCatalogItems());
            }
        }
        catch
        {
            // Catalog from the sales workspace is still usable if the catalog module cache is unavailable.
        }

        _lineCatalogItems = result
            .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.Code), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return _lineCatalogItems;

        void AddCatalogItems(IEnumerable<SalesCatalogItemOption> items)
        {
            foreach (var item in items)
            {
                var code = Ui(item.Code).Trim();
                var name = Ui(item.Name).Trim();
                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var key = !string.IsNullOrWhiteSpace(code)
                    ? $"code:{code}"
                    : $"name:{name}";
                if (!knownKeys.Add(key))
                {
                    continue;
                }

                result.Add(new SalesCatalogItemOption(
                    code,
                    name,
                    NormalizeUnit(item.Unit, name),
                    item.DefaultPrice));
            }
        }
    }

    private static SalesCatalogItemOption? ResolveCatalogItem(
        IReadOnlyList<SalesCatalogItemOption> catalog,
        string value)
    {
        var query = Ui(value).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var exact = catalog.FirstOrDefault(item =>
            BuildCatalogOption(item).Equals(query, StringComparison.CurrentCultureIgnoreCase)
            || Ui(item.Code).Equals(query, StringComparison.CurrentCultureIgnoreCase)
            || Ui(item.Name).Equals(query, StringComparison.CurrentCultureIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = catalog
            .Where(item => MatchesCatalogQuery(item, tokens))
            .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool MatchesCatalogQuery(SalesCatalogItemOption item, IReadOnlyCollection<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var option = BuildCatalogOption(item);
        var code = Ui(item.Code);
        var name = Ui(item.Name);
        var unit = NormalizeUnit(item.Unit, item.Name);
        return tokens.All(token =>
            option.Contains(token, StringComparison.CurrentCultureIgnoreCase)
            || code.Contains(token, StringComparison.CurrentCultureIgnoreCase)
            || name.Contains(token, StringComparison.CurrentCultureIgnoreCase)
            || unit.Contains(token, StringComparison.CurrentCultureIgnoreCase));
    }

    private void ReplaceLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        _lines.Clear();
        var no = 1;
        foreach (var line in lines)
        {
            _lines.Add(new SalesLineEditorRow(
                Ui(line.ItemCode),
                Ui(line.ItemName),
                NormalizeUnit(line.Unit, line.ItemName),
                line.Quantity,
                line.Price,
                line.DiscountAutoPercent,
                line.DiscountAutoAmount,
                line.DiscountManualPercent,
                line.DiscountManualAmount,
                line.VatPercent,
                line.VatAmount,
                no++));
        }
    }

    private void LoadDiscount(decimal percent, decimal amount)
    {
        _manualDiscountPercent = Math.Clamp(percent, 0m, 100m);
        _manualDiscountAmount = Math.Max(0m, amount);
        _discountPercentMode = _manualDiscountAmount <= 0m;
    }

    private System.ComponentModel.BindingList<SalesOrderLineRecord> ToSalesLines()
    {
        return ToSalesLines(_lines);
    }

    private static System.ComponentModel.BindingList<SalesOrderLineRecord> ToSalesLines(IEnumerable<SalesLineEditorRow> lines)
    {
        return new System.ComponentModel.BindingList<SalesOrderLineRecord>(lines.Select(line => new SalesOrderLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = line.ItemCode,
            ItemName = line.ItemName,
            Unit = NormalizeUnit(line.Unit, line.ItemName),
            Quantity = line.Quantity,
            Price = line.Price,
            DiscountAutoPercent = line.DiscountAutoPercent,
            DiscountAutoAmount = line.DiscountAutoAmount,
            DiscountManualPercent = line.DiscountManualPercent,
            DiscountManualAmount = line.DiscountManualAmount,
            VatPercent = line.VatPercent,
            VatAmount = line.VatAmount
        }).ToList());
    }

    private static void ApplyCustomer(SalesOrderRecord order, SalesCustomerRecord customer)
    {
        order.CustomerId = customer.Id;
        order.CustomerCode = customer.Code;
        order.CustomerName = customer.Name;
        order.ContractNumber = customer.ContractNumber;
    }

    private static void ApplyBaseOrder(SalesInvoiceRecord invoice, SalesOrderRecord order)
    {
        invoice.SalesOrderId = order.Id;
        invoice.SalesOrderNumber = order.Number;
        invoice.CustomerId = order.CustomerId;
        invoice.CustomerCode = order.CustomerCode;
        invoice.CustomerName = order.CustomerName;
        invoice.ContractNumber = order.ContractNumber;
    }

    private static void ApplyBaseOrder(SalesShipmentRecord shipment, SalesOrderRecord order)
    {
        shipment.SalesOrderId = order.Id;
        shipment.SalesOrderNumber = order.Number;
        shipment.CustomerId = order.CustomerId;
        shipment.CustomerCode = order.CustomerCode;
        shipment.CustomerName = order.CustomerName;
        shipment.ContractNumber = order.ContractNumber;
        shipment.CurrencyCode = order.CurrencyCode;
    }

    private string? PromptValue(string title, string prompt, string? initialValue = null, IEnumerable<string>? options = null)
    {
        var dialog = new ProductTextInputWindow(title, prompt, initialValue, options);
        WpfDialogOwner.TrySetOwner(dialog, ResolvePromptOwner());

        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }

    private decimal PromptDecimal(string title, string prompt, string initialValue)
    {
        var text = PromptValue(title, prompt, initialValue, Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1m;
        }

        if (TryParseDecimal(text, out var value))
        {
            return value;
        }

        var owner = ResolvePromptOwner();
        if (owner is null)
        {
            MessageBox.Show("Введите корректное число.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(owner, "Введите корректное число.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return -1m;
    }

    private Window? ResolvePromptOwner()
    {
        return WpfDialogOwner.Resolve(_hostedInWorkspace ? System.Windows.Application.Current?.MainWindow : this);
    }

    private void RefreshTotal()
    {
        var subtotal = Math.Round(_lines.Sum(item => item.Amount), 2, MidpointRounding.AwayFromZero);
        var discount = CalculateEditorDiscount(subtotal);
        var total = Math.Round(Math.Max(0m, subtotal - discount), 2, MidpointRounding.AwayFromZero);
        var vat = Math.Round(total * 20m / 120m, 2, MidpointRounding.AwayFromZero);
        var derivedPercent = subtotal <= 0m ? 0m : Math.Round(discount / subtotal * 100m, 2, MidpointRounding.AwayFromZero);

        TotalText.Text = $"Позиции: {_lines.Count:N0}. Сумма: {subtotal:N2} ₽. Скидка: {discount:N2} ₽.";

        _updatingDiscountFields = true;
        try
        {
            if (!DiscountPercentTextBox.IsKeyboardFocusWithin)
            {
                DiscountPercentTextBox.Text = (_discountPercentMode ? _manualDiscountPercent : derivedPercent).ToString("N2", RuCulture);
            }

            if (!DiscountAmountTextBox.IsKeyboardFocusWithin)
            {
                DiscountAmountTextBox.Text = discount.ToString("N2", RuCulture);
            }

            VatAmountTextBox.Text = vat.ToString("N2", RuCulture);
            GrandTotalTextBox.Text = total.ToString("N2", RuCulture);
        }
        finally
        {
            _updatingDiscountFields = false;
        }
    }

    private decimal CalculateEditorDiscount(decimal subtotal)
    {
        if (subtotal <= 0m)
        {
            return 0m;
        }

        var rawDiscount = _discountPercentMode
            ? subtotal * Math.Clamp(_manualDiscountPercent, 0m, 100m) / 100m
            : _manualDiscountAmount;
        return Math.Min(subtotal, Math.Round(Math.Max(0m, rawDiscount), 2, MidpointRounding.AwayFromZero));
    }

    private void RenderRelatedDocuments()
    {
        _relatedDocuments.Clear();

        var order = ResolveRelatedOrder();
        if (order is null)
        {
            RelatedDocumentsSummaryText.Text = "Выберите заказ-основание, чтобы увидеть связанную цепочку документов.";
            return;
        }

        var activeCashReceipts = _workspace.CashReceipts
            .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order))
            .Where(item => IsActiveCashReceiptStatus(item.Status))
            .ToArray();
        var paidAmount = activeCashReceipts.Sum(item => item.Amount);
        if (!IsCurrentDocumentRow("order", order.Id))
        {
            var orderIndicator = BuildPaymentIndicator(order.TotalAmount, paidAmount, order.Status);
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Заказ {order.Number}",
                order.OrderDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(order.TotalAmount, order.CurrencyCode),
                Ui(order.Status),
                "order",
                order.Id,
                orderIndicator.Brush,
                orderIndicator.FillVisibility,
                orderIndicator.Text));
        }

        foreach (var invoice in _workspace.Invoices.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.InvoiceDate))
        {
            if (IsCurrentDocumentRow("invoice", invoice.Id))
            {
                continue;
            }

            var indicator = BuildPaymentIndicator(invoice.TotalAmount, paidAmount, invoice.Status);
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Счет {invoice.Number}",
                invoice.InvoiceDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(invoice.TotalAmount, invoice.CurrencyCode),
                Ui(invoice.Status),
                "invoice",
                invoice.Id,
                indicator.Brush,
                indicator.FillVisibility,
                indicator.Text));
        }

        foreach (var shipment in _workspace.Shipments.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.ShipmentDate))
        {
            if (IsCurrentDocumentRow("shipment", shipment.Id))
            {
                continue;
            }

            var indicator = BuildShipmentIndicator(shipment.Status);
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Расходная {shipment.Number}",
                shipment.ShipmentDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(shipment.TotalAmount, shipment.CurrencyCode),
                Ui(shipment.Status),
                "shipment",
                shipment.Id,
                indicator.Brush,
                indicator.FillVisibility,
                indicator.Text));
        }

        foreach (var cashReceipt in _workspace.CashReceipts.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.ReceiptDate))
        {
            var indicator = BuildCashReceiptIndicator(cashReceipt.Status);
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Поступление в кассу {cashReceipt.Number}",
                cashReceipt.ReceiptDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(cashReceipt.Amount, cashReceipt.CurrencyCode),
                Ui(cashReceipt.Status),
                "cash",
                cashReceipt.Id,
                indicator.Brush,
                indicator.FillVisibility,
                indicator.Text));
        }

        foreach (var returnDocument in _workspace.Returns.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.ReturnDate))
        {
            var indicator = BuildReturnIndicator(returnDocument.Status);
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Возврат {returnDocument.Number}",
                returnDocument.ReturnDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(returnDocument.TotalAmount, returnDocument.CurrencyCode),
                Ui(returnDocument.Status),
                "return",
                returnDocument.Id,
                indicator.Brush,
                indicator.FillVisibility,
                indicator.Text));
        }

        RelatedDocumentsSummaryText.Text = _relatedDocuments.Count == 0
            ? $"Связанных документов пока нет. Оплачено через кассу: {FormatMoney(paidAmount, order.CurrencyCode)}."
            : $"Документов: {_relatedDocuments.Count:N0}. Оплачено через кассу: {FormatMoney(paidAmount, order.CurrencyCode)}.";
    }

    private void HandleOpenRelatedDocumentClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedRelatedDocument(showSelectionWarning: true);
    }

    private void HandleRelatedDocumentsGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenSelectedRelatedDocument(showSelectionWarning: false);
    }

    private void OpenSelectedRelatedDocument(bool showSelectionWarning)
    {
        var row = RelatedDocumentsGrid.SelectedItem as SalesRelatedDocumentRow;
        if (row is null && showSelectionWarning && _relatedDocuments.Count == 0)
        {
            ValidationText.Text = "Связанных документов пока нет: счет, расходка или возврат появятся здесь после создания.";
            return;
        }

        if (row is null && showSelectionWarning && _relatedDocuments.Count == 1)
        {
            row = _relatedDocuments[0];
            RelatedDocumentsGrid.SelectedItem = row;
        }

        if (row is null)
        {
            if (showSelectionWarning)
            {
                ValidationText.Text = "Выберите связанный документ.";
            }

            return;
        }

        if (row.Category == "order" && _mode == SalesDocumentEditorMode.Order && _orderDraft?.Id == row.Id)
        {
            if (showSelectionWarning)
            {
                ValidationText.Text = "Этот заказ уже открыт. Связанные счет, расходка или возврат появятся здесь после создания.";
            }

            return;
        }

        switch (row.Category)
        {
            case "order":
                if (_workspace.Orders.FirstOrDefault(item => item.Id == row.Id) is { } order)
                {
                    var dialog = new SalesDocumentEditorWindow(_workspace, order);
                    ShowChildDialog(dialog, () => dialog.ResultOrder is not null, () => _workspace.UpdateOrder(dialog.ResultOrder!));
                }
                break;
            case "invoice":
                if (_workspace.Invoices.FirstOrDefault(item => item.Id == row.Id) is { } invoice)
                {
                    var dialog = new SalesDocumentEditorWindow(_workspace, invoice);
                    ShowChildDialog(dialog, () => dialog.ResultInvoice is not null, () => _workspace.UpdateInvoice(dialog.ResultInvoice!));
                }
                break;
            case "shipment":
                if (_workspace.Shipments.FirstOrDefault(item => item.Id == row.Id) is { } shipment)
                {
                    var dialog = new SalesDocumentEditorWindow(_workspace, shipment);
                    ShowChildDialog(dialog, () => dialog.ResultShipment is not null, () => _workspace.UpdateShipment(dialog.ResultShipment!));
                }
                break;
            case "return":
                if (_workspace.Returns.FirstOrDefault(item => item.Id == row.Id) is { } returnDocument)
                {
                    var dialog = new SalesReturnEditorWindow(_workspace, returnDocument);
                    ShowChildDialog(dialog, () => dialog.ResultReturn is not null, () => _workspace.UpdateReturn(dialog.ResultReturn!));
                }
                break;
            case "cash":
                if (_workspace.CashReceipts.FirstOrDefault(item => item.Id == row.Id) is { } cashReceipt)
                {
                    var dialog = new SalesDocumentLinksWindow(_workspace, cashReceipt);
                    WpfDialogOwner.TrySetOwner(dialog, ResolvePromptOwner());

                    dialog.ShowDialog();
                }
                break;
        }

        RenderRelatedDocuments();
    }

    private void ShowChildDialog(Window dialog, Func<bool> hasResult, Action save)
    {
        WpfDialogOwner.TrySetOwner(dialog, ResolvePromptOwner());

        if (dialog.ShowDialog() == true && hasResult())
        {
            try
            {
                save();
            }
            catch (InvalidOperationException exception)
            {
                ValidationText.Text = exception.Message;
            }
        }
    }

    private void ApplySelectedStatusToDraft()
    {
        var selectedStatus = GetSelectedStatus();
        if (string.IsNullOrWhiteSpace(selectedStatus))
        {
            return;
        }

        switch (_mode)
        {
            case SalesDocumentEditorMode.Order when _orderDraft is not null:
                _orderDraft.Status = selectedStatus;
                break;
            case SalesDocumentEditorMode.Invoice when _invoiceDraft is not null:
                _invoiceDraft.Status = selectedStatus;
                break;
            case SalesDocumentEditorMode.Shipment when _shipmentDraft is not null:
                _shipmentDraft.Status = selectedStatus;
                break;
        }
    }

    private bool IsCurrentDocumentRow(string category, Guid id)
    {
        return category switch
        {
            "order" => _mode == SalesDocumentEditorMode.Order && _orderDraft?.Id == id,
            "invoice" => _mode == SalesDocumentEditorMode.Invoice && _invoiceDraft?.Id == id,
            "shipment" => _mode == SalesDocumentEditorMode.Shipment && _shipmentDraft?.Id == id,
            _ => false
        };
    }

    private SalesOrderRecord? ResolveRelatedOrder()
    {
        if (_mode == SalesDocumentEditorMode.Order)
        {
            return _orderDraft;
        }

        return GetSelectedOrder()
            ?? (_invoiceDraft is null ? null : FindOrder(_invoiceDraft.SalesOrderId, _invoiceDraft.SalesOrderNumber))
            ?? (_shipmentDraft is null ? null : FindOrder(_shipmentDraft.SalesOrderId, _shipmentDraft.SalesOrderNumber));
    }

    private static bool IsRelatedToOrder(Guid orderId, string orderNumber, SalesOrderRecord order)
    {
        return orderId == order.Id
            || Ui(orderNumber).Equals(Ui(order.Number), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveCashReceiptStatus(string status)
    {
        return !Ui(status).Equals("Отменено", StringComparison.OrdinalIgnoreCase);
    }

    private static SalesDocumentIndicator BuildPaymentIndicator(decimal totalAmount, decimal paidAmount, string status)
    {
        var cleanStatus = Ui(status);
        if (totalAmount > 0m && paidAmount >= totalAmount)
        {
            return new SalesDocumentIndicator(PaidIndicatorBrush, Visibility.Visible, "Оплачено полностью");
        }

        if (paidAmount > 0m || cleanStatus.Contains("част", StringComparison.OrdinalIgnoreCase))
        {
            return new SalesDocumentIndicator(
                PartialIndicatorBrush,
                Visibility.Visible,
                $"Оплачено частично: {paidAmount:N2} ₽");
        }

        if (IsPaidStatus(cleanStatus))
        {
            return new SalesDocumentIndicator(PaidIndicatorBrush, Visibility.Visible, "Оплачено по статусу документа");
        }

        return new SalesDocumentIndicator(EmptyIndicatorBrush, Visibility.Collapsed, "Оплаты нет");
    }

    private static bool IsPaidStatus(string status)
    {
        var cleanStatus = Ui(status);
        return cleanStatus.Equals("Оплачен", StringComparison.OrdinalIgnoreCase)
            || cleanStatus.Equals("Оплачено", StringComparison.OrdinalIgnoreCase)
            || cleanStatus.Contains("полностью оплачен", StringComparison.OrdinalIgnoreCase);
    }

    private static SalesDocumentIndicator BuildShipmentIndicator(string status)
    {
        var cleanStatus = Ui(status);
        if (cleanStatus.Contains("отгруж", StringComparison.OrdinalIgnoreCase)
            || cleanStatus.Contains("достав", StringComparison.OrdinalIgnoreCase)
            || cleanStatus.Contains("выполн", StringComparison.OrdinalIgnoreCase))
        {
            return new SalesDocumentIndicator(PaidIndicatorBrush, Visibility.Visible, "Отгружено полностью");
        }

        if (cleanStatus.Contains("част", StringComparison.OrdinalIgnoreCase)
            || cleanStatus.Contains("сбор", StringComparison.OrdinalIgnoreCase)
            || cleanStatus.Contains("пути", StringComparison.OrdinalIgnoreCase))
        {
            return new SalesDocumentIndicator(PartialIndicatorBrush, Visibility.Visible, "Отгрузка в работе");
        }

        if (cleanStatus.Contains("отмен", StringComparison.OrdinalIgnoreCase))
        {
            return new SalesDocumentIndicator(NeutralIndicatorBrush, Visibility.Collapsed, "Отгрузка отменена");
        }

        return new SalesDocumentIndicator(NeutralIndicatorBrush, Visibility.Collapsed, "Отгрузка не проведена");
    }

    private static SalesDocumentIndicator BuildCashReceiptIndicator(string status)
    {
        return IsActiveCashReceiptStatus(status)
            ? new SalesDocumentIndicator(PaidIndicatorBrush, Visibility.Visible, "Оплата учтена")
            : new SalesDocumentIndicator(NeutralIndicatorBrush, Visibility.Collapsed, "Оплата отменена");
    }

    private static SalesDocumentIndicator BuildReturnIndicator(string status)
    {
        var cleanStatus = Ui(status);
        if (cleanStatus.Contains("пров", StringComparison.OrdinalIgnoreCase)
            || cleanStatus.Contains("выполн", StringComparison.OrdinalIgnoreCase))
        {
            return new SalesDocumentIndicator(PaidIndicatorBrush, Visibility.Visible, "Возврат проведен");
        }

        return new SalesDocumentIndicator(PartialIndicatorBrush, Visibility.Visible, "Возврат в работе");
    }

    private static string FormatMoney(decimal amount, string currencyCode)
    {
        var currency = string.Equals(currencyCode, "RUB", StringComparison.OrdinalIgnoreCase)
            ? "₽"
            : Ui(currencyCode);
        return $"{amount:N2} {currency}";
    }

    private static string BuildCustomerOption(SalesCustomerRecord customer)
    {
        return $"{Ui(customer.Name)} - {Ui(customer.Code)}";
    }

    private static string BuildCustomerOption(SalesOrderRecord order)
    {
        return $"{Ui(order.CustomerName)} - {Ui(order.CustomerCode)}";
    }

    private static string BuildCustomerOption(SalesInvoiceRecord invoice)
    {
        return $"{Ui(invoice.CustomerName)} - {Ui(invoice.CustomerCode)}";
    }

    private static string BuildCustomerOption(SalesShipmentRecord shipment)
    {
        return $"{Ui(shipment.CustomerName)} - {Ui(shipment.CustomerCode)}";
    }

    private static string BuildOrderOption(SalesOrderRecord order)
    {
        return $"{Ui(order.Number)} - {Ui(order.CustomerName)} - {order.OrderDate:dd.MM.yyyy}";
    }

    private static string BuildCatalogOption(SalesCatalogItemOption item)
    {
        return $"{Ui(item.Code)} - {Ui(item.Name)} | ед.: {NormalizeUnit(item.Unit, item.Name)}";
    }

    private static string NormalizeUnit(string? unit, string? itemName = null)
    {
        return SalesDocumentDisplayFormatter.NormalizeUnit(unit, itemName);
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var selected = comboBox.Items
                .Cast<object>()
                .Select(item => item?.ToString() ?? string.Empty)
                .FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                comboBox.SelectedItem = selected;
                return;
            }

            if (comboBox.IsEditable)
            {
                comboBox.SelectedItem = null;
                comboBox.Text = value;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static bool TryParseDecimal(string value, out decimal result)
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

    private sealed record SalesLineEditorRow(
        string ItemCode,
        string ItemName,
        string Unit,
        decimal Quantity,
        decimal Price,
        decimal DiscountAutoPercent = 0m,
        decimal DiscountAutoAmount = 0m,
        decimal DiscountManualPercent = 0m,
        decimal DiscountManualAmount = 0m,
        decimal VatPercent = 0m,
        decimal VatAmount = 0m,
        int LineNo = 0)
    {
        public decimal Amount => Math.Round(Quantity * Price - DiscountManualAmount - DiscountAutoAmount, 2, MidpointRounding.AwayFromZero);

        public decimal LineTotalAmount => Math.Round(Amount + VatAmount, 2, MidpointRounding.AwayFromZero);

        public string QuantityDisplay => Quantity.ToString("N3", RuCulture);

        public string PriceDisplay => Price.ToString("N2", RuCulture);

        public string AmountDisplay => Amount.ToString("N2", RuCulture);

        public string DiscountAutoPercentDisplay => DiscountAutoPercent > 0m ? DiscountAutoPercent.ToString("N2", RuCulture) : string.Empty;

        public string DiscountAutoAmountDisplay => DiscountAutoAmount > 0m ? DiscountAutoAmount.ToString("N2", RuCulture) : string.Empty;

        public string DiscountManualPercentDisplay => DiscountManualPercent > 0m ? DiscountManualPercent.ToString("N2", RuCulture) : string.Empty;

        public string DiscountManualAmountDisplay => DiscountManualAmount > 0m ? DiscountManualAmount.ToString("N2", RuCulture) : string.Empty;

        public string VatPercentDisplay => VatPercent > 0m ? $"{VatPercent:N0}%" : string.Empty;

        public string VatAmountDisplay => VatAmount > 0m ? VatAmount.ToString("N2", RuCulture) : string.Empty;

        public string LineTotalAmountDisplay => LineTotalAmount.ToString("N2", RuCulture);
    }

    private sealed record SalesRelatedDocumentRow(
        string Document,
        string Date,
        string AmountDisplay,
        string Status,
        string Category,
        Guid Id,
        Brush IndicatorBrush,
        Visibility IndicatorFillVisibility,
        string IndicatorText);

    private sealed record SalesDocumentIndicator(Brush Brush, Visibility FillVisibility, string Text);
}
