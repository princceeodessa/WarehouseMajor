using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public enum SalesDocumentEditorMode
{
    Order,
    Invoice,
    Shipment
}

public partial class SalesDocumentEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly SalesWorkspace _workspace;
    private readonly SalesDocumentEditorMode _mode;
    private readonly ObservableCollection<SalesLineEditorRow> _lines = [];
    private readonly ObservableCollection<SalesRelatedDocumentRow> _relatedDocuments = [];
    private readonly Dictionary<string, SalesCustomerRecord> _customerOptions = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, SalesOrderRecord> _orderOptions = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly bool _editingExistingDocument;
    private bool _loading;
    private bool _hostedInWorkspace;
    private bool _updatingDiscountFields;
    private bool _discountPercentMode;
    private decimal _manualDiscountPercent;
    private decimal _manualDiscountAmount;
    private SalesOrderRecord? _orderDraft;
    private SalesInvoiceRecord? _invoiceDraft;
    private SalesShipmentRecord? _shipmentDraft;

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

        LinesGrid.ItemsSource = _lines;
        RelatedDocumentsGrid.ItemsSource = _relatedDocuments;
        LoadOptionSources();
        ConfigureMode();
        LoadInitialDraft();
    }

    public SalesOrderRecord? ResultOrder { get; private set; }

    public SalesInvoiceRecord? ResultInvoice { get; private set; }

    public SalesShipmentRecord? ResultShipment { get; private set; }

    public event EventHandler? HostedSaved;

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

        CustomerComboBox.ItemsSource = _customerOptions.Keys.ToArray();
        OrderComboBox.ItemsSource = _orderOptions.Keys.ToArray();
        WarehouseComboBox.ItemsSource = _workspace.Warehouses.Select(Ui).ToArray();
        ManagerComboBox.ItemsSource = _workspace.Managers.Select(Ui).ToArray();
        CurrencyComboBox.ItemsSource = _workspace.Currencies.Select(Ui).ToArray();
    }

    private void ConfigureMode()
    {
        _loading = true;

        switch (_mode)
        {
            case SalesDocumentEditorMode.Order:
                Title = "Новый заказ";
                HeaderTitleText.Text = "Новый заказ";
                HeaderSubtitleText.Text = "Создание заказа покупателя с клиентом, складом и позициями.";
                DocumentDateLabelText.Text = "Дата заказа";
                LinesHintText.Text = "Добавьте позиции, которые нужно обработать дальше: резерв, счет, отгрузка.";
                StatusComboBox.ItemsSource = _workspace.OrderStatuses.Select(Ui).ToArray();
                OrderPanel.Visibility = Visibility.Collapsed;
                CustomerPanel.Visibility = Visibility.Visible;
                SecondaryDatePanel.Visibility = Visibility.Collapsed;
                WarehousePanel.Visibility = Visibility.Visible;
                CurrencyPanel.Visibility = Visibility.Visible;
                CarrierPanel.Visibility = Visibility.Collapsed;
                break;
            case SalesDocumentEditorMode.Invoice:
                Title = "Новый счет";
                HeaderTitleText.Text = "Новый счет";
                HeaderSubtitleText.Text = "Счет создается на основании заказа и наследует его позиции.";
                DocumentDateLabelText.Text = "Дата счета";
                SecondaryDateLabelText.Text = "Срок оплаты";
                LinesHintText.Text = "Позиции подтягиваются из заказа. При необходимости их можно уточнить.";
                StatusComboBox.ItemsSource = _workspace.InvoiceStatuses.Select(Ui).ToArray();
                CustomerPanel.Visibility = Visibility.Visible;
                CustomerComboBox.IsEnabled = false;
                OrderPanel.Visibility = Visibility.Visible;
                SecondaryDatePanel.Visibility = Visibility.Visible;
                WarehousePanel.Visibility = Visibility.Collapsed;
                CurrencyPanel.Visibility = Visibility.Visible;
                CarrierPanel.Visibility = Visibility.Collapsed;
                break;
            case SalesDocumentEditorMode.Shipment:
                Title = "Новая отгрузка";
                HeaderTitleText.Text = "Новая отгрузка";
                HeaderSubtitleText.Text = "Отгрузка создается на основании заказа и фиксирует склад исполнения.";
                DocumentDateLabelText.Text = "Дата отгрузки";
                LinesHintText.Text = "Позиции подтягиваются из заказа. Проведение отгрузки выполняется отдельным действием.";
                StatusComboBox.ItemsSource = _workspace.ShipmentStatuses.Select(Ui).ToArray();
                CustomerPanel.Visibility = Visibility.Visible;
                CustomerComboBox.IsEnabled = false;
                OrderPanel.Visibility = Visibility.Visible;
                SecondaryDatePanel.Visibility = Visibility.Collapsed;
                WarehousePanel.Visibility = Visibility.Visible;
                CurrencyPanel.Visibility = Visibility.Collapsed;
                CarrierPanel.Visibility = Visibility.Visible;
                break;
        }

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
        SelectComboValue(StatusComboBox, Ui(order.Status));
        SelectComboValue(WarehouseComboBox, Ui(order.Warehouse));
        SelectComboValue(ManagerComboBox, Ui(order.Manager));
        SelectComboValue(CurrencyComboBox, Ui(order.CurrencyCode));
        CommentTextBox.Text = Ui(order.Comment);
        LoadDiscount(order.ManualDiscountPercent, order.ManualDiscountAmount);
        ReplaceLines(order.Lines);
    }

    private void LoadInvoice(SalesInvoiceRecord invoice)
    {
        NumberTextBox.Text = Ui(invoice.Number);
        DocumentDatePicker.SelectedDate = invoice.InvoiceDate == default ? DateTime.Today : invoice.InvoiceDate;
        SecondaryDatePicker.SelectedDate = invoice.DueDate == default ? DateTime.Today.AddDays(3) : invoice.DueDate;
        SelectComboValue(CustomerComboBox, BuildCustomerOption(invoice));
        SelectComboValue(StatusComboBox, Ui(invoice.Status));
        SelectComboValue(ManagerComboBox, Ui(invoice.Manager));
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
        SelectComboValue(StatusComboBox, Ui(shipment.Status));
        SelectComboValue(WarehouseComboBox, Ui(shipment.Warehouse));
        SelectComboValue(ManagerComboBox, Ui(shipment.Manager));
        CarrierTextBox.Text = Ui(shipment.Carrier);
        CommentTextBox.Text = Ui(shipment.Comment);
        LoadDiscount(shipment.ManualDiscountPercent, shipment.ManualDiscountAmount);
        ReplaceLines(shipment.Lines);
    }

    private void HandleCustomerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mode != SalesDocumentEditorMode.Order)
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

        ApplySelectedCustomerDefaults();
    }

    private void ApplySelectedCustomerDefaults()
    {
        var customer = GetSelectedCustomer();
        if (customer is null)
        {
            return;
        }

        SelectComboValue(CustomerComboBox, BuildCustomerOption(customer));
        SelectComboValue(ManagerComboBox, Ui(customer.Manager));
        SelectComboValue(CurrencyComboBox, Ui(customer.CurrencyCode));
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

    private void HandleAddLineClick(object sender, RoutedEventArgs e)
    {
        var catalog = _workspace.CatalogItems
            .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var options = catalog.Select(BuildCatalogOption).ToArray();
        var selected = PromptValue("Добавить позицию", "Выберите товар.", options.FirstOrDefault(), options);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        var item = ResolveCatalogItem(catalog, selected);
        var quantity = PromptDecimal("Количество", "Введите количество.", "1");
        if (quantity <= 0m)
        {
            return;
        }

        var price = PromptDecimal("Цена", "Введите цену.", (item?.DefaultPrice ?? 0m).ToString("N2", RuCulture));
        if (price < 0m)
        {
            return;
        }

        _lines.Add(new SalesLineEditorRow(
            item?.Code ?? selected.Trim(),
            item?.Name ?? selected.Trim(),
            string.IsNullOrWhiteSpace(item?.Unit) ? "шт" : item.Unit,
            quantity,
            price));
        RefreshTotal();
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

            var quantity = PromptDecimal("Изменить позицию", "Введите новое количество.", row.Quantity.ToString("N2", RuCulture));
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

    private void HandleLinesGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        HandleEditLineClick(sender, e);
    }

    private void HandleLinesGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit
            && e.Row.Item is SalesLineEditorRow row
            && e.EditingElement is TextBox textBox
            && e.Column.DisplayIndex is >= 0 and <= 4)
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
            case 0:
                updated = row with { ItemCode = Ui(value).Trim() };
                return true;
            case 1:
                updated = row with { ItemName = Ui(value).Trim() };
                return true;
            case 2:
                updated = row with { Unit = Ui(value).Trim() };
                return true;
            case 3:
                if (!TryParseDecimal(value, out var quantity) || quantity <= 0m)
                {
                    return false;
                }

                updated = row with { Quantity = quantity };
                return true;
            case 4:
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

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
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

        var order = _orderDraft ?? _workspace.CreateOrderDraft(customer.Id);
        order.Number = NumberTextBox.Text.Trim();
        order.OrderDate = DocumentDatePicker.SelectedDate!.Value.Date;
        ApplyCustomer(order, customer);
        order.Warehouse = WarehouseComboBox.Text.Trim();
        order.Status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text.Trim();
        order.Manager = ManagerComboBox.Text.Trim();
        order.CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim();
        order.Comment = CommentTextBox.Text.Trim();
        order.ManualDiscountPercent = _discountPercentMode ? _manualDiscountPercent : 0m;
        order.ManualDiscountAmount = _discountPercentMode ? 0m : _manualDiscountAmount;
        order.Lines = ToSalesLines();

        ResultOrder = order;
        CompleteEditing(success: true);
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
        invoice.Status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text.Trim();
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
        shipment.Status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text.Trim();
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

    private void CompleteEditing(bool success)
    {
        if (_hostedInWorkspace)
        {
            if (success)
            {
                HostedSaved?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                HostedCanceled?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        DialogResult = success;
    }

    private SalesCustomerRecord? GetSelectedCustomer()
    {
        var selected = CustomerComboBox.SelectedItem?.ToString();
        var text = string.IsNullOrWhiteSpace(CustomerComboBox.Text) ? selected : CustomerComboBox.Text;
        return ResolveCustomer(text);
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

        var matches = catalog
            .Where(item =>
                BuildCatalogOption(item).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || Ui(item.Code).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || Ui(item.Name).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private void ReplaceLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        _lines.Clear();
        foreach (var line in lines)
        {
            _lines.Add(new SalesLineEditorRow(
                Ui(line.ItemCode),
                Ui(line.ItemName),
                string.IsNullOrWhiteSpace(line.Unit) ? "шт" : Ui(line.Unit),
                line.Quantity,
                line.Price));
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
        return new System.ComponentModel.BindingList<SalesOrderLineRecord>(_lines.Select(line => new SalesOrderLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = line.ItemCode,
            ItemName = line.ItemName,
            Unit = line.Unit,
            Quantity = line.Quantity,
            Price = line.Price
        }).ToList());
    }

    private static void ApplyCustomer(SalesOrderRecord order, SalesCustomerRecord customer)
    {
        order.CustomerId = customer.Id;
        order.CustomerCode = customer.Code;
        order.CustomerName = customer.Name;
        order.ContractNumber = customer.ContractNumber;
    }

    private string? PromptValue(string title, string prompt, string? initialValue = null, IEnumerable<string>? options = null)
    {
        var dialog = new ProductTextInputWindow(title, prompt, initialValue, options);
        var owner = ResolvePromptOwner();
        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

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
        if (!_hostedInWorkspace)
        {
            return this;
        }

        return System.Windows.Application.Current?.MainWindow
            ?? System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive);
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

        _relatedDocuments.Add(new SalesRelatedDocumentRow(
            $"Заказ {order.Number}",
            order.OrderDate.ToString("dd.MM.yyyy", RuCulture),
            FormatMoney(order.TotalAmount, order.CurrencyCode),
            Ui(order.Status)));

        foreach (var invoice in _workspace.Invoices.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.InvoiceDate))
        {
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Счет {invoice.Number}",
                invoice.InvoiceDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(invoice.TotalAmount, invoice.CurrencyCode),
                Ui(invoice.Status)));
        }

        foreach (var shipment in _workspace.Shipments.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.ShipmentDate))
        {
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Расходная {shipment.Number}",
                shipment.ShipmentDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(shipment.TotalAmount, shipment.CurrencyCode),
                Ui(shipment.Status)));
        }

        foreach (var cashReceipt in _workspace.CashReceipts.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.ReceiptDate))
        {
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Поступление в кассу {cashReceipt.Number}",
                cashReceipt.ReceiptDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(cashReceipt.Amount, cashReceipt.CurrencyCode),
                Ui(cashReceipt.Status)));
        }

        foreach (var returnDocument in _workspace.Returns.Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order)).OrderByDescending(item => item.ReturnDate))
        {
            _relatedDocuments.Add(new SalesRelatedDocumentRow(
                $"Возврат {returnDocument.Number}",
                returnDocument.ReturnDate.ToString("dd.MM.yyyy", RuCulture),
                FormatMoney(returnDocument.TotalAmount, returnDocument.CurrencyCode),
                Ui(returnDocument.Status)));
        }

        var paidAmount = _workspace.CashReceipts
            .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, order))
            .Sum(item => item.Amount);
        RelatedDocumentsSummaryText.Text = $"Документов: {_relatedDocuments.Count:N0}. Оплачено через кассу: {FormatMoney(paidAmount, order.CurrencyCode)}.";
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
        return $"{Ui(item.Code)} - {Ui(item.Name)}";
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
        decimal Price)
    {
        public decimal Amount => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);

        public string QuantityDisplay => Quantity.ToString("N2", RuCulture);

        public string PriceDisplay => $"{Price:N2} ₽";

        public string AmountDisplay => $"{Amount:N2} ₽";
    }

    private sealed record SalesRelatedDocumentRow(
        string Document,
        string Date,
        string AmountDisplay,
        string Status);
}
