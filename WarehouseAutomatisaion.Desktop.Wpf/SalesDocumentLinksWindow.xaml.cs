using System.Globalization;
using System.Text;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesDocumentLinksWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly SalesWorkspace _salesWorkspace;
    private IReadOnlyList<DocumentLinkGridRow> _documentRows = Array.Empty<DocumentLinkGridRow>();

    public SalesDocumentLinksWindow(SalesWorkspace salesWorkspace, SalesOrderRecord order)
        : this(
            salesWorkspace,
            order.Id,
            order.Number,
            order.CustomerId,
            $"Заказ {Clean(order.Number)}",
            BuildOrderRow(order, "Текущий заказ"))
    {
    }

    public SalesDocumentLinksWindow(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
        : this(
            salesWorkspace,
            invoice.SalesOrderId,
            invoice.SalesOrderNumber,
            invoice.CustomerId,
            $"Счет {Clean(invoice.Number)}",
            BuildInvoiceRow(invoice, "Текущий счет"))
    {
    }

    public SalesDocumentLinksWindow(SalesWorkspace salesWorkspace, SalesShipmentRecord shipment)
        : this(
            salesWorkspace,
            shipment.SalesOrderId,
            shipment.SalesOrderNumber,
            shipment.CustomerId,
            $"Отгрузка {Clean(shipment.Number)}",
            BuildShipmentRow(shipment, "Текущая отгрузка"))
    {
    }

    public SalesDocumentLinksWindow(SalesWorkspace salesWorkspace, SalesReturnRecord returnDocument)
        : this(
            salesWorkspace,
            returnDocument.SalesOrderId,
            returnDocument.SalesOrderNumber,
            returnDocument.CustomerId,
            $"Возврат {Clean(returnDocument.Number)}",
            BuildReturnRow(returnDocument))
    {
    }

    public SalesDocumentLinksWindow(SalesWorkspace salesWorkspace, SalesCashReceiptRecord cashReceipt)
        : this(
            salesWorkspace,
            cashReceipt.SalesOrderId,
            cashReceipt.SalesOrderNumber,
            cashReceipt.CustomerId,
            $"Поступление в кассу {Clean(cashReceipt.Number)}",
            BuildCashReceiptRow(cashReceipt))
    {
    }

    private SalesDocumentLinksWindow(
        SalesWorkspace salesWorkspace,
        Guid salesOrderId,
        string salesOrderNumber,
        Guid customerId,
        string sourceCaption,
        DocumentLinkGridRow? sourceFallback)
    {
        _salesWorkspace = salesWorkspace;

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        LoadDocumentChain(salesOrderId, salesOrderNumber, customerId, sourceCaption, sourceFallback);
    }

    private void LoadDocumentChain(
        Guid salesOrderId,
        string salesOrderNumber,
        Guid customerId,
        string sourceCaption,
        DocumentLinkGridRow? sourceFallback)
    {
        var normalizedOrderNumber = Clean(salesOrderNumber);
        var order = FindOrder(salesOrderId, normalizedOrderNumber);
        if (order is not null)
        {
            salesOrderId = order.Id;
            normalizedOrderNumber = Clean(order.Number);
            customerId = order.CustomerId;
        }

        var rows = new List<DocumentLinkGridRow>();
        if (order is not null)
        {
            rows.Add(BuildOrderRow(order, "Основание"));
        }

        rows.AddRange(_salesWorkspace.Invoices
            .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, salesOrderId, normalizedOrderNumber))
            .Select(item => BuildInvoiceRow(item, "Счет по заказу")));

        rows.AddRange(_salesWorkspace.Shipments
            .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, salesOrderId, normalizedOrderNumber))
            .Select(item => BuildShipmentRow(item, "Расходная по заказу")));

        rows.AddRange(_salesWorkspace.Returns
            .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, salesOrderId, normalizedOrderNumber))
            .Select(item => BuildReturnRow(item)));

        rows.AddRange(_salesWorkspace.CashReceipts
            .Where(item => IsRelatedToOrder(item.SalesOrderId, item.SalesOrderNumber, salesOrderId, normalizedOrderNumber))
            .Select(item => BuildCashReceiptRow(item)));

        if (sourceFallback is not null && rows.All(item => item.Id != sourceFallback.Id))
        {
            rows.Add(sourceFallback);
        }

        _documentRows = rows
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.DateValue)
            .ThenBy(item => item.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var operationRows = BuildOperationRows(_documentRows, normalizedOrderNumber).ToArray();
        DocumentsGrid.ItemsSource = _documentRows;
        OperationLogGrid.ItemsSource = operationRows;
        EmptyLogText.Visibility = operationRows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateSummary(order, _documentRows, sourceCaption, normalizedOrderNumber, customerId);
    }

    private SalesOrderRecord? FindOrder(Guid orderId, string orderNumber)
    {
        if (orderId != Guid.Empty)
        {
            var orderById = _salesWorkspace.Orders.FirstOrDefault(item => item.Id == orderId);
            if (orderById is not null)
            {
                return orderById;
            }
        }

        return string.IsNullOrWhiteSpace(orderNumber)
            ? null
            : _salesWorkspace.Orders.FirstOrDefault(item => SameText(item.Number, orderNumber));
    }

    private IEnumerable<OperationLogGridRow> BuildOperationRows(
        IEnumerable<DocumentLinkGridRow> documents,
        string orderNumber)
    {
        var ids = documents
            .Where(item => item.Id != Guid.Empty)
            .Select(item => item.Id)
            .ToHashSet();

        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var number in documents.Select(item => Clean(item.Number)))
        {
            if (!string.IsNullOrWhiteSpace(number))
            {
                numbers.Add(number);
            }
        }

        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            numbers.Add(orderNumber);
        }

        return _salesWorkspace.OperationLog
            .Where(item =>
                ids.Contains(item.EntityId)
                || numbers.Contains(Clean(item.EntityNumber))
                || ContainsText(item.Message, orderNumber))
            .OrderByDescending(item => item.LoggedAt)
            .Select(item => new OperationLogGridRow
            {
                DateValue = item.LoggedAt,
                DateText = item.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture),
                Entity = $"{Clean(item.EntityType)} {Clean(item.EntityNumber)}".Trim(),
                Action = Clean(item.Action),
                Result = Clean(item.Result),
                Message = Clean(item.Message),
                Actor = Clean(item.Actor)
            });
    }

    private void UpdateSummary(
        SalesOrderRecord? order,
        IReadOnlyList<DocumentLinkGridRow> rows,
        string sourceCaption,
        string orderNumber,
        Guid customerId)
    {
        HeaderTitleText.Text = "Связанные документы";
        HeaderSubtitleText.Text = string.IsNullOrWhiteSpace(orderNumber)
            ? $"{sourceCaption}: основание не найдено."
            : $"{sourceCaption}: цепочка по заказу {orderNumber}.";

        OrderSummaryValueText.Text = order is null
            ? (string.IsNullOrWhiteSpace(orderNumber) ? "Не найден" : orderNumber)
            : Clean(order.Number);
        OrderSummaryHintText.Text = order is null ? "Заказ-основание не найден" : Clean(order.Status);

        var customerName = Clean(order?.CustomerName)
            ?? Clean(_salesWorkspace.Customers.FirstOrDefault(item => item.Id == customerId)?.Name);
        if (string.IsNullOrWhiteSpace(customerName))
        {
            customerName = rows.Select(item => Clean(item.CustomerName)).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "Не указан";
        }

        CustomerSummaryValueText.Text = customerName;
        CustomerSummaryHintText.Text = order is null ? "По данным связанных документов" : Clean(order.ContractNumber);

        var invoiceCount = rows.Count(item => item.Category == "invoice");
        var shipmentCount = rows.Count(item => item.Category == "shipment");
        var returnCount = rows.Count(item => item.Category == "return");
        DocumentSummaryValueText.Text = rows.Count.ToString("N0", RuCulture);
        DocumentSummaryHintText.Text = $"Счета: {invoiceCount:N0}; отгрузки: {shipmentCount:N0}; возвраты: {returnCount:N0}";

        var cashRows = rows.Where(item => item.Category == "cash").ToArray();
        var currencyCode = Clean(order?.CurrencyCode);
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            currencyCode = rows.Select(item => Clean(item.CurrencyCode)).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "RUB";
        }

        CashSummaryValueText.Text = FormatMoney(cashRows.Sum(item => item.Amount), currencyCode);
        CashSummaryHintText.Text = cashRows.Length == 0
            ? "Поступлений не найдено"
            : $"Поступлений: {cashRows.Length:N0}";
    }

    private void HandleCopyClick(object sender, RoutedEventArgs e)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Документ\tНомер\tДата\tКлиент\tСумма\tСтатус\tСвязь\tКомментарий");
        foreach (var row in _documentRows)
        {
            builder.AppendLine(
                $"{row.Kind}\t{row.Number}\t{row.DateText}\t{row.CustomerName}\t{row.AmountText}\t{row.Status}\t{row.Relation}\t{row.Comment}");
        }

        Clipboard.SetText(builder.ToString());
        MessageBox.Show(this, "Список связанных документов скопирован.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HandleCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static DocumentLinkGridRow BuildOrderRow(SalesOrderRecord order, string relation)
    {
        return new DocumentLinkGridRow
        {
            Id = order.Id,
            Category = "order",
            Kind = "Заказ",
            Number = Clean(order.Number),
            DateValue = order.OrderDate,
            DateText = FormatDate(order.OrderDate),
            CustomerName = Clean(order.CustomerName),
            Amount = order.TotalAmount,
            CurrencyCode = Clean(order.CurrencyCode),
            AmountText = FormatMoney(order.TotalAmount, order.CurrencyCode),
            Status = Clean(order.Status),
            Relation = relation,
            Comment = Clean(order.Comment),
            SortOrder = 10
        };
    }

    private static DocumentLinkGridRow BuildInvoiceRow(SalesInvoiceRecord invoice, string relation)
    {
        return new DocumentLinkGridRow
        {
            Id = invoice.Id,
            Category = "invoice",
            Kind = "Счет",
            Number = Clean(invoice.Number),
            DateValue = invoice.InvoiceDate,
            DateText = FormatDate(invoice.InvoiceDate),
            CustomerName = Clean(invoice.CustomerName),
            Amount = invoice.TotalAmount,
            CurrencyCode = Clean(invoice.CurrencyCode),
            AmountText = FormatMoney(invoice.TotalAmount, invoice.CurrencyCode),
            Status = Clean(invoice.Status),
            Relation = relation,
            Comment = Clean(invoice.Comment),
            SortOrder = 20
        };
    }

    private static DocumentLinkGridRow BuildShipmentRow(SalesShipmentRecord shipment, string relation)
    {
        return new DocumentLinkGridRow
        {
            Id = shipment.Id,
            Category = "shipment",
            Kind = "Отгрузка",
            Number = Clean(shipment.Number),
            DateValue = shipment.ShipmentDate,
            DateText = FormatDate(shipment.ShipmentDate),
            CustomerName = Clean(shipment.CustomerName),
            Amount = shipment.TotalAmount,
            CurrencyCode = Clean(shipment.CurrencyCode),
            AmountText = FormatMoney(shipment.TotalAmount, shipment.CurrencyCode),
            Status = Clean(shipment.Status),
            Relation = relation,
            Comment = Clean(shipment.Comment),
            SortOrder = 30
        };
    }

    private static DocumentLinkGridRow BuildReturnRow(SalesReturnRecord returnDocument)
    {
        return new DocumentLinkGridRow
        {
            Id = returnDocument.Id,
            Category = "return",
            Kind = "Возврат",
            Number = Clean(returnDocument.Number),
            DateValue = returnDocument.ReturnDate,
            DateText = FormatDate(returnDocument.ReturnDate),
            CustomerName = Clean(returnDocument.CustomerName),
            Amount = returnDocument.TotalAmount,
            CurrencyCode = Clean(returnDocument.CurrencyCode),
            AmountText = FormatMoney(returnDocument.TotalAmount, returnDocument.CurrencyCode),
            Status = Clean(returnDocument.Status),
            Relation = "Возврат по заказу",
            Comment = Clean(returnDocument.Reason),
            SortOrder = 40
        };
    }

    private static DocumentLinkGridRow BuildCashReceiptRow(SalesCashReceiptRecord receipt)
    {
        return new DocumentLinkGridRow
        {
            Id = receipt.Id,
            Category = "cash",
            Kind = "Поступление в кассу",
            Number = Clean(receipt.Number),
            DateValue = receipt.ReceiptDate,
            DateText = FormatDate(receipt.ReceiptDate),
            CustomerName = Clean(receipt.CustomerName),
            Amount = receipt.Amount,
            CurrencyCode = Clean(receipt.CurrencyCode),
            AmountText = FormatMoney(receipt.Amount, receipt.CurrencyCode),
            Status = Clean(receipt.Status),
            Relation = "Оплата по заказу",
            Comment = Clean(receipt.CashBox),
            SortOrder = 50
        };
    }

    private static bool IsRelatedToOrder(Guid documentOrderId, string documentOrderNumber, Guid orderId, string orderNumber)
    {
        return orderId != Guid.Empty && documentOrderId == orderId
            || !string.IsNullOrWhiteSpace(orderNumber) && SameText(documentOrderNumber, orderNumber);
    }

    private static bool SameText(string? left, string? right)
    {
        return Clean(left).Equals(Clean(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsText(string? text, string? value)
    {
        var cleanValue = Clean(value);
        if (string.IsNullOrWhiteSpace(cleanValue))
        {
            return false;
        }

        return Clean(text).IndexOf(cleanValue, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Clean(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value?.Trim() ?? string.Empty);
    }

    private static string FormatDate(DateTime value)
    {
        return value == default ? string.Empty : value.ToString("dd.MM.yyyy", RuCulture);
    }

    private static string FormatMoney(decimal amount, string currencyCode)
    {
        var cleanCurrency = Clean(currencyCode);
        return string.IsNullOrWhiteSpace(cleanCurrency)
            ? amount.ToString("N2", RuCulture)
            : $"{amount:N2} {cleanCurrency}";
    }

    public sealed class DocumentLinkGridRow
    {
        public Guid Id { get; init; }

        public string Category { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public string Number { get; init; } = string.Empty;

        public DateTime DateValue { get; init; }

        public string DateText { get; init; } = string.Empty;

        public string CustomerName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public string CurrencyCode { get; init; } = string.Empty;

        public string AmountText { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Relation { get; init; } = string.Empty;

        public string Comment { get; init; } = string.Empty;

        public int SortOrder { get; init; }
    }

    public sealed class OperationLogGridRow
    {
        public DateTime DateValue { get; init; }

        public string DateText { get; init; } = string.Empty;

        public string Entity { get; init; } = string.Empty;

        public string Action { get; init; } = string.Empty;

        public string Result { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string Actor { get; init; } = string.Empty;
    }
}
