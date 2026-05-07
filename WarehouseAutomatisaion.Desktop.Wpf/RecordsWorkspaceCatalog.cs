using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class RecordsWorkspaceCatalog
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const char CustomerGroupSeparator = '\u001F';

    private static readonly string[] OrderFilters = ["Все заказы", "Новые", "Подтвержденные", "В производстве", "Выполненные"];
    private static readonly string[] CustomerFilters = ["Все клиенты", "Активные", "Новые", "Неактивные"];
    private static readonly string[] InvoiceFilters = ["Все счета", "Оплачено", "Частично оплачено", "Просрочено", "Не оплачено"];
    private static readonly string[] ShipmentFilters = ["Все отгрузки", "Запланировано", "В пути", "Доставлено", "Отменено"];
    private static readonly string[] FinanceFilters = ["Все документы", "Возвраты", "Поступления в кассу"];
    private static readonly string[] PurchasingFilters = ["Все документы", "Заказы", "Счета", "Приемки"];
    private static readonly string[] ScenarioFilters = ["Все сценарии", "Критично", "Важно", "План", "Готово"];

    public static RecordsWorkspaceDefinition CreateSales(SalesWorkspace salesWorkspace)
    {
        return new RecordsWorkspaceDefinition(
            Title: "Заказы",
            Subtitle: "Просмотр и управление заказами клиентов.",
            SearchPlaceholder: "Поиск по заказам...",
            PrimaryActionText: "Новый заказ",
            PrimaryFilterOptions: OrderFilters,
            ShowDateRange: true,
            PrimaryAction: () => CreateSalesOrder(salesWorkspace),
            MetricsFactory: () =>
            [
                Metric("Всего заказов", salesWorkspace.Orders.Count, "Актуально", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE14C"),
                Metric("Новые", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "Новые"), "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8A5"),
                Metric("Подтвержденные", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "Подтвержденные"), "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE73E"),
                Metric("В производстве", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "В производстве"), "Актуально", "#7B68EE", "#F1EEFF", "#FF6B6B", "\uE7C1"),
                Metric("Выполненные", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "Выполненные"), "Актуально", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uEC47")
            ],
            RowsFactory: () => salesWorkspace.Orders
                .OrderByDescending(item => item.OrderDate)
                .Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Number, item.CustomerName, item.Manager, item.Status, item.Warehouse),
                    FilterValue: NormalizeOrderFilter(item.Status),
                    DateValue: item.OrderDate,
                    Cells:
                    [
                        Cell(Clean(item.Number)),
                        Cell(Clean(item.CustomerName)),
                        Cell(item.OrderDate.ToString("dd.MM.yyyy", RuCulture)),
                        Cell(FormatMoney(item.TotalAmount, item.CurrencyCode), semiBold: true),
                        StatusCell(NormalizeOrderStatusLabel(item.Status)),
                        Cell(item.OrderDate.AddDays(3).ToString("dd.MM.yyyy", RuCulture)),
                        ActionCell()
                    ],
                    RowActions: BuildOrderActions(salesWorkspace, item)))
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Действия", 6, RecordsColumnKind.Action, 72, false),
                new RecordsGridColumnDefinition("№ заказа", 0, WidthValue: 1.15),
                new RecordsGridColumnDefinition("Клиент", 1, WidthValue: 1.7),
                new RecordsGridColumnDefinition("Дата заказа", 2, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Сумма", 3, WidthValue: 1.0, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Статус", 4, RecordsColumnKind.Status, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Срок отгрузки", 5, WidthValue: 1.0)
            ],
            SubscribeToChanges: handler => salesWorkspace.Changed += handler,
            UnsubscribeFromChanges: handler => salesWorkspace.Changed -= handler);
    }

    public static RecordsWorkspaceDefinition CreateCustomers(SalesWorkspace salesWorkspace)
    {
        return new RecordsWorkspaceDefinition(
            Title: "Клиенты",
            Subtitle: "База клиентов и контактные данные.",
            SearchPlaceholder: "Поиск по клиентам...",
            PrimaryActionText: "Новый клиент",
            PrimaryFilterOptions: CustomerFilters,
            ShowDateRange: false,
            PrimaryAction: () => CreateCustomer(salesWorkspace),
            MetricsFactory: () =>
            [
                Metric("Всего клиентов", salesWorkspace.Customers.Count, "Актуально", "#FFB648", "#FFF5E8", "#1BC47D", "\uE77B"),
                Metric("Активные", salesWorkspace.Customers.Count(item => NormalizeCustomerFilter(item.Status) == "Активные"), "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8D4"),
                Metric("Новые", salesWorkspace.Customers.Count(item => NormalizeCustomerFilter(item.Status) == "Новые"), "Актуально", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE77B"),
                Metric("Неактивные", salesWorkspace.Customers.Count(item => NormalizeCustomerFilter(item.Status) == "Неактивные"), "Актуально", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uE711")
            ],
            RowsFactory: () => salesWorkspace.Customers
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Name, item.Phone, item.Email, item.Manager, item.ContractNumber, item.Inn, item.Region, item.City, item.Source, item.Tags),
                    FilterValue: NormalizeCustomerFilter(item.Status),
                    DateValue: null,
                    Cells:
                    [
                        Cell(Clean(item.Name), semiBold: true),
                        Cell(GetCustomerType(item)),
                        Cell(GetCustomerInn(item)),
                        Cell(GetCustomerResponsible(item)),
                        Cell(Clean(item.Phone)),
                        Cell(Clean(item.Email), "#4F5BFF"),
                        Cell(GetCustomerLocation(item)),
                        StatusCell(NormalizeCustomerStatusLabel(item.Status)),
                        ActionCell()
                    ],
                    RowActions: BuildCustomerActions(salesWorkspace, item),
                    GroupPath: BuildCustomerGroupPath(item)))
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Действия", 8, RecordsColumnKind.Action, 72, false),
                new RecordsGridColumnDefinition("Клиент", 0, WidthValue: 1.55),
                new RecordsGridColumnDefinition("Тип", 1, WidthValue: 0.7),
                new RecordsGridColumnDefinition("ИНН", 2, WidthValue: 0.9),
                new RecordsGridColumnDefinition("Ответственный", 3, WidthValue: 1.05),
                new RecordsGridColumnDefinition("Телефон", 4, WidthValue: 1.0),
                new RecordsGridColumnDefinition("E-mail", 5, WidthValue: 1.05),
                new RecordsGridColumnDefinition("Регион / город", 6, WidthValue: 1.1),
                new RecordsGridColumnDefinition("Статус", 7, RecordsColumnKind.Status, WidthValue: 0.85)
            ],
            SubscribeToChanges: handler => salesWorkspace.Changed += handler,
            UnsubscribeFromChanges: handler => salesWorkspace.Changed -= handler,
            GroupTreeFactory: () => BuildCustomerGroupTree(salesWorkspace),
            GroupTreeTitle: "Покупатели по регионам");
    }

    public static RecordsWorkspaceDefinition CreateInvoices(SalesWorkspace salesWorkspace)
    {
        return new RecordsWorkspaceDefinition(
            Title: "Счета",
            Subtitle: "Выставление и контроль оплат по клиентским счетам.",
            SearchPlaceholder: "Поиск по счетам...",
            PrimaryActionText: "Новый счет",
            PrimaryFilterOptions: InvoiceFilters,
            ShowDateRange: true,
            PrimaryAction: () => CreateInvoice(salesWorkspace),
            MetricsFactory: () =>
            [
                Metric("Все счета", salesWorkspace.Invoices.Count, "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8C7"),
                Metric("Оплачено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(salesWorkspace, item) == "Оплачено"), "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8FB"),
                Metric("Частично оплачено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(salesWorkspace, item) == "Частично оплачено"), "Актуально", "#FFB648", "#FFF5E8", "#1BC47D", "\uE7C2"),
                Metric("Просрочено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(salesWorkspace, item) == "Просрочено"), "Актуально", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uEA39"),
                Metric("Не оплачено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(salesWorkspace, item) == "Не оплачено"), "Актуально", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uE8C7")
            ],
            RowsFactory: () => salesWorkspace.Invoices
                .OrderByDescending(item => item.InvoiceDate)
                .Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Number, item.SalesOrderNumber, item.CustomerName, item.Manager, item.Status),
                    FilterValue: NormalizeInvoiceFilter(salesWorkspace, item),
                    DateValue: item.InvoiceDate,
                    Cells:
                    [
                        Cell(Clean(item.Number)),
                        Cell(Clean(item.SalesOrderNumber)),
                        Cell(Clean(item.CustomerName)),
                        Cell(item.InvoiceDate.ToString("dd.MM.yyyy", RuCulture)),
                        Cell(FormatMoney(item.TotalAmount, item.CurrencyCode), semiBold: true),
                        Cell(FormatMoney(GetPaidAmount(salesWorkspace, item), item.CurrencyCode)),
                        StatusCell(NormalizeInvoiceStatusLabel(salesWorkspace, item)),
                        Cell(item.DueDate.ToString("dd.MM.yyyy", RuCulture)),
                        ActionCell()
                    ],
                    RowActions: BuildInvoiceActions(salesWorkspace, item)))
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Действия", 8, RecordsColumnKind.Action, 72, false),
                new RecordsGridColumnDefinition("№ счета", 0, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Заказ", 1, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Клиент", 2, WidthValue: 1.35),
                new RecordsGridColumnDefinition("Дата счета", 3, WidthValue: 0.9),
                new RecordsGridColumnDefinition("Сумма", 4, WidthValue: 0.95, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Оплачено", 5, WidthValue: 0.95, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Статус", 6, RecordsColumnKind.Status, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Срок оплаты", 7, WidthValue: 0.9)
            ],
            SubscribeToChanges: handler => salesWorkspace.Changed += handler,
            UnsubscribeFromChanges: handler => salesWorkspace.Changed -= handler);
    }

    public static RecordsWorkspaceDefinition CreateShipments(SalesWorkspace salesWorkspace)
    {
        return new RecordsWorkspaceDefinition(
            Title: "Отгрузки",
            Subtitle: "Документы отгрузки и контроль доставки.",
            SearchPlaceholder: "Поиск по отгрузкам...",
            PrimaryActionText: "Новая отгрузка",
            PrimaryFilterOptions: ShipmentFilters,
            ShowDateRange: true,
            PrimaryAction: () => CreateShipment(salesWorkspace),
            MetricsFactory: () =>
            [
                Metric("Все отгрузки", salesWorkspace.Shipments.Count, "Актуально", "#FFB648", "#FFF5E8", "#1BC47D", "\uEC47"),
                Metric("Запланировано", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "Запланировано"), "Актуально", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE823"),
                Metric("В пути", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "В пути"), "Актуально", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uE7C1"),
                Metric("Доставлено", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "Доставлено"), "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE73E"),
                Metric("Отменено", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "Отменено"), "Актуально", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uE711")
            ],
            RowsFactory: () => salesWorkspace.Shipments
                .OrderByDescending(item => item.ShipmentDate)
                .Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Number, item.SalesOrderNumber, item.CustomerName, item.Status, item.Warehouse, item.Carrier),
                    FilterValue: NormalizeShipmentFilter(item.Status),
                    DateValue: item.ShipmentDate,
                    Cells:
                    [
                        Cell(Clean(item.Number)),
                        Cell(Clean(item.SalesOrderNumber)),
                        Cell(Clean(item.CustomerName)),
                        Cell(item.ShipmentDate.ToString("dd.MM.yyyy", RuCulture)),
                        Cell(FormatMoney(item.TotalAmount, item.CurrencyCode), semiBold: true),
                        StatusCell(NormalizeShipmentStatusLabel(item.Status)),
                        Cell(item.ShipmentDate.AddDays(2).ToString("dd.MM.yyyy", RuCulture)),
                        ActionCell()
                    ],
                    RowActions: BuildShipmentActions(salesWorkspace, item)))
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Действия", 7, RecordsColumnKind.Action, 72, false),
                new RecordsGridColumnDefinition("№ отгрузки", 0, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Заказ", 1, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Клиент", 2, WidthValue: 1.35),
                new RecordsGridColumnDefinition("Дата отгрузки", 3, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Сумма", 4, WidthValue: 0.9, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Статус", 5, RecordsColumnKind.Status, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Дата доставки", 6, WidthValue: 0.95)
            ],
            SubscribeToChanges: handler => salesWorkspace.Changed += handler,
            UnsubscribeFromChanges: handler => salesWorkspace.Changed -= handler);
    }

    public static RecordsWorkspaceDefinition CreateReturnsAndPayments(SalesWorkspace salesWorkspace)
    {
        return new RecordsWorkspaceDefinition(
            Title: "Возвраты и оплаты",
            Subtitle: "Журнал возвратов покупателей и поступлений в кассу по заказам.",
            SearchPlaceholder: "Поиск по номеру, заказу, клиенту или комментарию...",
            PrimaryActionText: "Выгрузить CSV",
            PrimaryFilterOptions: FinanceFilters,
            ShowDateRange: true,
            PrimaryAction: () => ExportReturnsAndPaymentsJournal(salesWorkspace),
            MetricsFactory: () =>
            [
                Metric("Все документы", salesWorkspace.Returns.Count + salesWorkspace.CashReceipts.Count, "Актуально", "#4F5BFF", "#EEF2FF", "#1BC47D", "\uE9D2"),
                Metric("Возвраты", salesWorkspace.Returns.Count, "Черновики и проведенные", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uE7BA"),
                Metric("Поступления", salesWorkspace.CashReceipts.Count, "Касса", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8FB"),
                new WorkspaceMetricCardDefinition("Сумма оплат", FormatMoney(salesWorkspace.CashReceipts.Where(IsActiveCashReceipt).Sum(item => item.Amount), GetJournalCurrencyCode(salesWorkspace)), "Без отмененных", "поступления в кассу", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uE8C7"),
                Metric("Черновики возвратов", salesWorkspace.Returns.Count(item => Clean(item.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase)), "Не проводят склад", "#F29A17", "#FFF4E3", "#F29A17", "\uE9CE")
            ],
            RowsFactory: () => salesWorkspace.Returns
                .Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Number, item.SalesOrderNumber, item.CustomerName, item.Status, item.Reason, item.Comment, item.Manager),
                    FilterValue: "Возвраты",
                    DateValue: item.ReturnDate,
                    Cells:
                    [
                        Cell("Возврат"),
                        Cell(Clean(item.Number)),
                        Cell(Clean(item.SalesOrderNumber)),
                        Cell(Clean(item.CustomerName)),
                        Cell(item.ReturnDate.ToString("dd.MM.yyyy", RuCulture)),
                        Cell(FormatMoney(item.TotalAmount, item.CurrencyCode), semiBold: true),
                        StatusCell(Clean(item.Status)),
                        Cell(string.IsNullOrWhiteSpace(Clean(item.Reason)) ? Clean(item.Comment) : Clean(item.Reason)),
                        ActionCell()
                    ],
                    RowActions: BuildReturnActions(salesWorkspace, item)))
                .Concat(salesWorkspace.CashReceipts.Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Number, item.SalesOrderNumber, item.CustomerName, item.Status, item.CashBox, item.Comment, item.Manager),
                    FilterValue: "Поступления в кассу",
                    DateValue: item.ReceiptDate,
                    Cells:
                    [
                        Cell("Поступление в кассу"),
                        Cell(Clean(item.Number)),
                        Cell(Clean(item.SalesOrderNumber)),
                        Cell(Clean(item.CustomerName)),
                        Cell(item.ReceiptDate.ToString("dd.MM.yyyy", RuCulture)),
                        Cell(FormatMoney(item.Amount, item.CurrencyCode), semiBold: true),
                        StatusCell(Clean(item.Status)),
                        Cell(string.IsNullOrWhiteSpace(Clean(item.Comment)) ? Clean(item.CashBox) : Clean(item.Comment)),
                        ActionCell()
                    ],
                    RowActions: BuildCashReceiptActions(salesWorkspace, item))))
                .OrderByDescending(item => item.DateValue ?? DateTime.MinValue)
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Действия", 8, RecordsColumnKind.Action, 72, false),
                new RecordsGridColumnDefinition("Тип", 0, WidthValue: 1.05),
                new RecordsGridColumnDefinition("Документ", 1, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Заказ", 2, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Клиент", 3, WidthValue: 1.35),
                new RecordsGridColumnDefinition("Дата", 4, WidthValue: 0.85),
                new RecordsGridColumnDefinition("Сумма", 5, WidthValue: 0.95, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Статус", 6, RecordsColumnKind.Status, WidthValue: 0.9),
                new RecordsGridColumnDefinition("Комментарий", 7, WidthValue: 1.35)
            ],
            EmptyStateText: "Возвратов и поступлений в кассу пока нет",
            ShowImportAction: false,
            SubscribeToChanges: handler => salesWorkspace.Changed += handler,
            UnsubscribeFromChanges: handler => salesWorkspace.Changed -= handler);
    }

    private static void ExportReturnsAndPaymentsJournal(SalesWorkspace salesWorkspace)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "exports");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"returns-payments-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var builder = new StringBuilder();
        builder.AppendLine("Тип;Документ;Заказ;Клиент;Дата;Сумма;Валюта;Статус;Комментарий");

        foreach (var item in salesWorkspace.Returns
            .Select(returnRecord => new
            {
                Type = "Возврат",
                Document = Clean(returnRecord.Number),
                Order = Clean(returnRecord.SalesOrderNumber),
                Customer = Clean(returnRecord.CustomerName),
                Date = returnRecord.ReturnDate,
                Amount = returnRecord.TotalAmount,
                Currency = Clean(returnRecord.CurrencyCode),
                Status = Clean(returnRecord.Status),
                Comment = string.IsNullOrWhiteSpace(Clean(returnRecord.Reason)) ? Clean(returnRecord.Comment) : Clean(returnRecord.Reason)
            })
            .Concat(salesWorkspace.CashReceipts.Select(receipt => new
            {
                Type = "Поступление в кассу",
                Document = Clean(receipt.Number),
                Order = Clean(receipt.SalesOrderNumber),
                Customer = Clean(receipt.CustomerName),
                Date = receipt.ReceiptDate,
                Amount = receipt.Amount,
                Currency = Clean(receipt.CurrencyCode),
                Status = Clean(receipt.Status),
                Comment = string.IsNullOrWhiteSpace(Clean(receipt.Comment)) ? Clean(receipt.CashBox) : Clean(receipt.Comment)
            }))
            .OrderByDescending(item => item.Date))
        {
            builder.AppendLine(string.Join(
                ';',
                Csv(item.Type),
                Csv(item.Document),
                Csv(item.Order),
                Csv(item.Customer),
                item.Date.ToString("dd.MM.yyyy", RuCulture),
                item.Amount.ToString("N2", RuCulture),
                Csv(item.Currency),
                Csv(item.Status),
                Csv(item.Comment)));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        ShowMessage("Возвраты и оплаты", $"Журнал выгружен.\nФайл: {path}");
    }

    public static RecordsWorkspaceDefinition CreatePurchasing(SalesWorkspace salesWorkspace)
    {
        var workspace = PurchasingWorkspace.Create(salesWorkspace);
        var documents = workspace.PurchaseOrders
            .Select(item => (Document: item, Filter: "Заказы"))
            .Concat(workspace.SupplierInvoices.Select(item => (Document: item, Filter: "Счета")))
            .Concat(workspace.PurchaseReceipts.Select(item => (Document: item, Filter: "Приемки")))
            .OrderByDescending(item => item.Document.Date ?? DateTime.MinValue)
            .ToArray();

        return new RecordsWorkspaceDefinition(
            Title: "Закупки",
            Subtitle: "Поставщики, закупочные документы и приемка.",
            SearchPlaceholder: "Поиск по закупкам...",
            PrimaryActionText: "Новая закупка",
            PrimaryFilterOptions: PurchasingFilters,
            ShowDateRange: true,
            MetricsFactory: () =>
            [
                Metric("Поставщики", workspace.Suppliers.Count, "Актуально", "#FFB648", "#FFF5E8", "#1BC47D", "\uE77B"),
                Metric("Заказы", workspace.PurchaseOrders.Count, "Актуально", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE14C"),
                Metric("Счета", workspace.SupplierInvoices.Count, "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8C7"),
                Metric("Приемки", workspace.PurchaseReceipts.Count, "Актуально", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uE7C1")
            ],
            RowsFactory: () => documents
                .Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Document.DocumentType, item.Document.Number, item.Document.SupplierName, item.Document.Status, item.Document.Warehouse),
                    FilterValue: item.Filter,
                    DateValue: item.Document.Date,
                    Cells:
                    [
                        Cell(Clean(item.Document.DocumentType)),
                        Cell(Clean(item.Document.Number)),
                        Cell(Clean(item.Document.SupplierName)),
                        Cell(FormatDate(item.Document.Date)),
                        Cell(Clean(item.Document.Warehouse)),
                        Cell(FormatMoney(item.Document.TotalAmount, "RUB"), semiBold: true),
                        StatusCell(Clean(item.Document.Status)),
                        ActionCell()
                    ]))
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Тип", 0, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Документ", 1, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Поставщик", 2, WidthValue: 1.45),
                new RecordsGridColumnDefinition("Дата", 3, WidthValue: 0.9),
                new RecordsGridColumnDefinition("Склад", 4, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Сумма", 5, WidthValue: 0.9, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Статус", 6, RecordsColumnKind.Status, WidthValue: 0.9),
                new RecordsGridColumnDefinition("Действия", 7, RecordsColumnKind.Action, 72, false)
            ]);
    }

    public static RecordsWorkspaceDefinition CreateCatalog(SalesWorkspace salesWorkspace)
    {
        var workspace = LoadCatalogWorkspaceSnapshot(salesWorkspace);
        var stockLookup = BuildStockLookup(salesWorkspace);
        var categoryOptions = new[] { "Все категории" }
            .Concat(workspace.Items
                .Select(item => Clean(item.Category))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();

        return new RecordsWorkspaceDefinition(
            Title: "Товары",
            Subtitle: "Каталог товаров и остатки на складах.",
            SearchPlaceholder: "Поиск...",
            PrimaryActionText: "Новый товар",
            PrimaryFilterOptions: categoryOptions,
            ShowDateRange: false,
            MetricsFactory: () =>
            {
                var items = workspace.Items.ToArray();
                var inStock = items.Count(item => GetStockValue(stockLookup, item.Code) > 0m);
                var lowStock = items.Count(item => GetStockValue(stockLookup, item.Code) is > 0m and < 20m);
                var outOfStock = items.Count(item => GetStockValue(stockLookup, item.Code) <= 0m);
                return
                [
                    Metric("Всего товаров", items.Length, "Актуально", "#6C63FF", "#F0EDFF", "#1BC47D", "\uEECA"),
                    Metric("В наличии", inStock, "Актуально", "#59C36A", "#EBF9EF", "#1BC47D", "\uE7BA"),
                    Metric("Низкий остаток", lowStock, "Актуально", "#FFB648", "#FFF5E8", "#FF6B6B", "\uEA39"),
                    Metric("Нет в наличии", outOfStock, "Актуально", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uE711")
                ];
            },
            RowsFactory: () => workspace.Items
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item =>
                {
                    var stock = GetStockValue(stockLookup, item.Code);
                    return new RecordsGridItem(
                        SearchText: SearchIndex(item.Name, item.Code, item.Category, item.Supplier, item.DefaultWarehouse),
                        FilterValue: string.IsNullOrWhiteSpace(item.Category) ? categoryOptions[0] : Clean(item.Category),
                        DateValue: null,
                        Cells:
                        [
                            Cell(Clean(item.Name), semiBold: true),
                            Cell(Clean(item.Code)),
                            Cell(Clean(item.Category)),
                            Cell(Clean(item.Unit)),
                            Cell(stock.ToString("N0", RuCulture)),
                            Cell(FormatMoney(item.DefaultPrice, item.CurrencyCode), semiBold: true),
                            StatusCell(NormalizeCatalogStatus(stock)),
                            ActionCell()
                        ]);
                })
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Товар", 0, WidthValue: 1.7),
                new RecordsGridColumnDefinition("Артикул", 1, WidthValue: 0.9),
                new RecordsGridColumnDefinition("Категория", 2, WidthValue: 1.0),
                new RecordsGridColumnDefinition("Ед. изм.", 3, WidthValue: 0.75),
                new RecordsGridColumnDefinition("Остаток", 4, WidthValue: 0.8, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Цена", 5, WidthValue: 0.9, Alignment: TextAlignment.Right),
                new RecordsGridColumnDefinition("Статус", 6, RecordsColumnKind.Status, WidthValue: 0.95),
                new RecordsGridColumnDefinition("Действия", 7, RecordsColumnKind.Action, 72, false)
            ]);
    }

    public static RecordsWorkspaceDefinition CreateModel(FunctionalCoverageSnapshot coverage, SalesWorkspace salesWorkspace)
    {
        var currentOperator = string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator)
            ? Environment.UserName
            : salesWorkspace.CurrentOperator;
        var catalogWorkspace = CatalogWorkspaceStore.CreateDefault()
            .TryLoadExisting(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses)
            ?? LoadCatalogWorkspaceSnapshot(salesWorkspace);
        var purchasingWorkspace = PurchasingOperationalWorkspaceStore.CreateDefault()
            .TryLoadExisting(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
        var warehouseWorkspace = WarehouseOperationalWorkspaceStore.CreateDefault()
            .TryLoadExisting(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
        var audit = DataIntegrityAuditor.Audit(salesWorkspace, catalogWorkspace, purchasingWorkspace);
        var migrationRows = BuildMigrationCoverageRows(salesWorkspace, catalogWorkspace, purchasingWorkspace, warehouseWorkspace);
        var scenarioRows = coverage.Modules
            .SelectMany(module => module.Scenarios.Select(item => new DataModelRow(
                Module: Clean(module.Title),
                Priority: NormalizeScenarioFilter(item.Priority),
                Title: Clean(item.Scenario),
                Scope: Clean(item.Scope),
                RelatedObjects: Clean(item.RelatedObjects),
                Status: Clean(item.Status),
                SearchText: SearchIndex(module.Title, item.Priority, item.Scenario, item.Scope, item.Status),
                SortOrder: 10,
                DateValue: null)))
            .ToArray();
        var auditRows = audit.Issues
            .Select(item => new DataModelRow(
                Module: Clean(item.Module),
                Priority: Clean(item.Priority),
                Title: Clean(item.Problem),
                Scope: Clean(item.Details),
                RelatedObjects: $"{Clean(item.ObjectType)}: {Clean(item.ObjectNumber)}",
                Status: Clean(item.Status),
                SearchText: SearchIndex(item.Module, item.Priority, item.Problem, item.Details, item.Recommendation, item.ObjectType, item.ObjectNumber),
                SortOrder: item.SeverityRank,
                DateValue: item.Date))
            .ToArray();
        var rows = migrationRows
            .Concat(auditRows)
            .Concat(scenarioRows)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Module, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new RecordsWorkspaceDefinition(
            Title: "Связи данных",
            Subtitle: "Карта сценариев и встроенная проверка целостности данных.",
            SearchPlaceholder: "Поиск по проблеме, сценарию или документу...",
            PrimaryActionText: string.Empty,
            PrimaryFilterOptions: ScenarioFilters,
            ShowDateRange: false,
            MetricsFactory: () =>
            [
                new WorkspaceMetricCardDefinition("Пробелы 1С", migrationRows.Count(item => item.Priority is "Критично" or "Важно").ToString("N0", RuCulture), migrationRows.Any(item => item.Priority is "Критично") ? "есть блокеры" : "под контролем", "Сверка с 1С от 03.05.2026", "#4F5BFF", "#EEF2FF", "#4F5BFF", "\uE9D2"),
                new WorkspaceMetricCardDefinition("Критично", audit.CriticalCount.ToString("N0", RuCulture), audit.CriticalCount == 0 ? "нет блокеров" : "исправить вручную", "Разорванные связи и дубли ключей", "#F15B5B", "#FFF0F0", "#F15B5B", "\uEA39"),
                new WorkspaceMetricCardDefinition("Важно", audit.WarningCount.ToString("N0", RuCulture), audit.WarningCount == 0 ? "нет замечаний" : "проверить", "Пустые строки, цены и даты", "#F29A17", "#FFF4E3", "#F29A17", "\uE7BA"),
                new WorkspaceMetricCardDefinition("План", audit.InfoCount.ToString("N0", RuCulture), audit.InfoCount == 0 ? "чисто" : "дополнить", "Данные, которые улучшают аналитику", "#7180A0", "#F3F6FB", "#7180A0", "\uE9D2"),
                new WorkspaceMetricCardDefinition("Сценарии", scenarioRows.Length.ToString("N0", RuCulture), "Актуально", "Карта покрытия функций", "#4F5BFF", "#EEF2FF", "#8C97B0", "\uE9CE")
            ],
            RowsFactory: () => rows
                .Select(item => new RecordsGridItem(
                    SearchText: item.SearchText,
                    FilterValue: item.Priority,
                    DateValue: item.DateValue,
                    Cells:
                    [
                        Cell(Clean(item.Module)),
                        Cell(Clean(item.Priority)),
                        Cell(Clean(item.Title), semiBold: true),
                        Cell(Clean(item.Scope)),
                        Cell(Clean(item.RelatedObjects)),
                        StatusCell(Clean(item.Status))
                    ]))
                .ToArray(),
            Columns:
            [
                new RecordsGridColumnDefinition("Модуль", 0, WidthValue: 0.9),
                new RecordsGridColumnDefinition("Приоритет", 1, WidthValue: 0.75),
                new RecordsGridColumnDefinition("Проверка / сценарий", 2, WidthValue: 1.45),
                new RecordsGridColumnDefinition("Проблема или перенос", 3, WidthValue: 1.55),
                new RecordsGridColumnDefinition("Запись / связи", 4, WidthValue: 1.25),
                new RecordsGridColumnDefinition("Статус", 5, RecordsColumnKind.Status, WidthValue: 1.0)
            ],
            ShowImportAction: false,
            ShowPrimaryAction: false);
    }

    private static IReadOnlyList<DataModelRow> BuildMigrationCoverageRows(
        SalesWorkspace salesWorkspace,
        CatalogWorkspace catalogWorkspace,
        OperationalPurchasingWorkspace? purchasingWorkspace,
        OperationalWarehouseWorkspace? warehouseWorkspace)
    {
        var contractCount = CountDistinct(
            salesWorkspace.Customers.Select(item => item.ContractNumber)
                .Concat(salesWorkspace.Orders.Select(item => item.ContractNumber))
                .Concat(salesWorkspace.Invoices.Select(item => item.ContractNumber))
                .Concat(salesWorkspace.Shipments.Select(item => item.ContractNumber)));
        var bankAccountCount = CountDistinct(salesWorkspace.Customers.Select(item => item.BankAccount));

        var references = new[]
        {
            MigrationReference("Справочники", "Catalog.Контрагенты", "Клиенты", 2_077, salesWorkspace.Customers.Count),
            MigrationReference("Справочники", "Catalog.Номенклатура", "Товары", 6_255, catalogWorkspace.Items.Count),
            MigrationReference("Справочники", "Catalog.ВидыЦен", "Типы цен", 21, catalogWorkspace.PriceTypes.Count),
            MigrationReference("Справочники", "Catalog.ДоговорыКонтрагентов", "Договоры в карточках", 3_796, contractCount),
            MigrationReference("Справочники", "Catalog.БанковскиеСчета", "Банковские счета клиентов", 601, bankAccountCount),
            MigrationReference("Справочники", "Catalog.Организации", "Организации", 7, 0),
            MigrationReference("Склад", "Catalog.СтруктурныеЕдиницы", "Склады", 32, salesWorkspace.Warehouses.Count),
            MigrationReference("Деньги", "Catalog.Кассы", "Кассы", 17, 0),
            MigrationReference("Продажи", "Document.ЗаказПокупателя", "Заказы", 20_619, salesWorkspace.Orders.Count),
            MigrationReference("Продажи", "Document.ЗаказПокупателя.Запасы", "Строки заказов", 97_018, salesWorkspace.Orders.Sum(item => item.Lines.Count)),
            MigrationReference("Продажи", "Document.СчетНаОплату", "Счета", 711, salesWorkspace.Invoices.Count),
            MigrationReference("Продажи", "Document.СчетНаОплату.Запасы", "Строки счетов", 5_067, salesWorkspace.Invoices.Sum(item => item.Lines.Count)),
            MigrationReference("Продажи", "Document.РасходнаяНакладная", "Отгрузки", 19_552, salesWorkspace.Shipments.Count),
            MigrationReference("Продажи", "Document.РасходнаяНакладная.Запасы", "Строки отгрузок", 90_944, salesWorkspace.Shipments.Sum(item => item.Lines.Count)),
            MigrationReference("Закупки", "Document.ЗаказПоставщику", "Заказы поставщикам", 588, purchasingWorkspace?.PurchaseOrders.Count ?? 0),
            MigrationReference("Закупки", "Document.ПриходнаяНакладная", "Приемки", 1_844, purchasingWorkspace?.PurchaseReceipts.Count ?? 0),
            MigrationReference("Закупки", "Document.ПриходнаяНакладная.Запасы", "Строки приемок", 9_464, purchasingWorkspace?.PurchaseReceipts.Sum(item => item.Lines.Count) ?? 0),
            MigrationReference("Закупки", "Document.ДополнительныеРасходы", "Доп. расходы", 86, 0),
            MigrationReference("Склад", "Document.ЗаказНаПеремещение", "Заявки на перемещение", 47, warehouseWorkspace?.TransferOrders.Count ?? 0),
            MigrationReference("Склад", "Document.ПеремещениеЗапасов", "Перемещения запасов", 911, warehouseWorkspace?.TransferOrders.Count ?? 0),
            MigrationReference("Склад", "Document.ИнвентаризацияЗапасов", "Инвентаризации", 193, warehouseWorkspace?.InventoryCounts.Count ?? 0),
            MigrationReference("Склад", "Document.СписаниеЗапасов", "Списания", 162, warehouseWorkspace?.WriteOffs.Count ?? 0),
            MigrationReference("Склад", "Document.ОприходованиеЗапасов", "Оприходования", 158, 0),
            MigrationReference("Цены", "Document.УстановкаЦенНоменклатуры", "Установки цен", 683, catalogWorkspace.PriceRegistrations.Count),
            MigrationReference("Цены", "Document.УстановкаЦенНоменклатуры.Запасы", "Строки установки цен", 99_026, catalogWorkspace.PriceRegistrations.Sum(item => item.Lines.Count)),
            MigrationReference("Деньги", "Document.ПоступлениеВКассу", "Поступления в кассу", 4_605, salesWorkspace.CashReceipts.Count),
            MigrationReference("Деньги", "Document.ПоступлениеНаСчет", "Банковские поступления", 3_421, 0),
            MigrationReference("Деньги", "Document.РасходИзКассы", "Расходы из кассы", 855, 0),
            MigrationReference("Деньги", "Document.РасходСоСчета", "Банковские расходы", 3_406, 0),
            MigrationReference("Деньги", "Document.КассоваяСмена", "Кассовые смены", 498, 0),
            MigrationReference("Права", "UserRoles", "Роли и права", 1_051, 0)
        };

        return references
            .Select(BuildMigrationCoverageRow)
            .ToArray();
    }

    private static DataModelRow BuildMigrationCoverageRow(MigrationCoverageReference reference)
    {
        var priority = GetMigrationPriority(reference.OneCCount, reference.AppCount);
        var status = priority switch
        {
            "Критично" => "Критично",
            "Важно" => "Под контролем",
            "План" => "Сверить избыток",
            _ => "Готово"
        };
        var coverage = reference.OneCCount == 0
            ? 100m
            : Math.Round(reference.AppCount / (decimal)reference.OneCCount * 100m, 1, MidpointRounding.AwayFromZero);
        var gap = reference.OneCCount - reference.AppCount;
        var scope = $"1С: {FormatCount(reference.OneCCount)} / приложение: {FormatCount(reference.AppCount)} / покрытие {coverage:N1}%";
        var related = $"Разница: {FormatSignedCount(gap)}";
        var title = $"{reference.OneCObject} -> {reference.AppObject}";

        return new DataModelRow(
            Module: $"Сверка 1С / {reference.Module}",
            Priority: priority,
            Title: title,
            Scope: scope,
            RelatedObjects: related,
            Status: status,
            SearchText: SearchIndex("миграция", "1С", reference.Module, reference.OneCObject, reference.AppObject, priority, status, scope, related),
            SortOrder: GetMigrationSortOrder(priority),
            DateValue: new DateTime(2026, 5, 3));
    }

    private static MigrationCoverageReference MigrationReference(
        string module,
        string oneCObject,
        string appObject,
        int oneCCount,
        int appCount)
    {
        return new MigrationCoverageReference(module, oneCObject, appObject, oneCCount, Math.Max(0, appCount));
    }

    private static string GetMigrationPriority(int oneCCount, int appCount)
    {
        if (oneCCount > 0 && appCount == 0)
        {
            return "Критично";
        }

        if (appCount < oneCCount)
        {
            return "Важно";
        }

        if (appCount > oneCCount)
        {
            return "План";
        }

        return "Готово";
    }

    private static int GetMigrationSortOrder(string priority)
    {
        return priority switch
        {
            "Критично" => -30,
            "Важно" => -20,
            "План" => -10,
            _ => -5
        };
    }

    private static int CountDistinct(IEnumerable<string> values)
    {
        return values
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static string FormatCount(int value)
    {
        return value.ToString("N0", RuCulture);
    }

    private static string FormatSignedCount(int value)
    {
        return value > 0
            ? $"+{FormatCount(value)}"
            : value.ToString("N0", RuCulture);
    }

    private sealed record MigrationCoverageReference(
        string Module,
        string OneCObject,
        string AppObject,
        int OneCCount,
        int AppCount);

    private sealed record DataModelRow(
        string Module,
        string Priority,
        string Title,
        string Scope,
        string RelatedObjects,
        string Status,
        string SearchText,
        int SortOrder,
        DateTime? DateValue);

    internal static CatalogWorkspace LoadCatalogWorkspaceSnapshot(SalesWorkspace salesWorkspace)
    {
        var currentOperator = string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator)
            ? Environment.UserName
            : salesWorkspace.CurrentOperator;
        return CatalogWorkspaceStore.CreateDefault().LoadOrCreate(currentOperator, salesWorkspace);
    }

    private static IReadOnlyList<RecordsGridActionDefinition> BuildOrderActions(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        return
        [
            new RecordsGridActionDefinition("Открыть заказ", () => EditOrder(salesWorkspace, order)),
            new RecordsGridActionDefinition("Связанные документы", () => ShowSalesDocumentLinks(salesWorkspace, order)),
            new RecordsGridActionDefinition("Печать для заказчика", () => PrintOrderCustomer(order)),
            new RecordsGridActionDefinition("Печать для сборки", () => PrintOrderPicking(salesWorkspace, order)),
            new RecordsGridActionDefinition("Выгрузить PDF", () => ExportOrder(order, SalesDocumentExportFormat.Pdf)),
            new RecordsGridActionDefinition("Выгрузить Excel", () => ExportOrder(order, SalesDocumentExportFormat.Excel)),
            new RecordsGridActionDefinition("Подтвердить", () => ShowWorkflowResult("Заказы", salesWorkspace.ConfirmOrder(order.Id))),
            new RecordsGridActionDefinition("В резерв", () => ShowWorkflowResult("Заказы", salesWorkspace.ReserveOrder(order.Id))),
            new RecordsGridActionDefinition("Снять резерв", () => ShowWorkflowResult("Заказы", salesWorkspace.ReleaseOrderReserve(order.Id))),
            new RecordsGridActionDefinition("Сформировать счет", () => CreateInvoiceFromOrder(salesWorkspace, order)),
            new RecordsGridActionDefinition("Подготовить отгрузку", () => CreateShipmentFromOrder(salesWorkspace, order)),
            new RecordsGridActionDefinition("Создать черновик возврата", () => CreateReturnFromOrder(salesWorkspace, order)),
            new RecordsGridActionDefinition("Провести расходку и закрыть", () => ShowWorkflowResult("Заказы", salesWorkspace.ConductExpenseAndCloseOrder(order.Id))),
            new RecordsGridActionDefinition("Поступление в кассу", () => ShowWorkflowResult("Заказы", salesWorkspace.RecordCashReceiptForOrder(order.Id))),
            new RecordsGridActionDefinition("Дублировать", () => DuplicateOrder(salesWorkspace, order))
        ];
    }

    private static IReadOnlyList<RecordsGridActionDefinition> BuildCustomerActions(SalesWorkspace salesWorkspace, SalesCustomerRecord customer)
    {
        var isInactive = Clean(customer.Status).Equals("Неактивен", StringComparison.OrdinalIgnoreCase)
            || Clean(customer.Status).Equals("Пауза", StringComparison.OrdinalIgnoreCase);

        return
        [
            new RecordsGridActionDefinition("Открыть карточку", () => EditCustomer(salesWorkspace, customer)),
            new RecordsGridActionDefinition(
                isInactive ? "Сделать активным" : "Перевести в паузу",
                () => SetCustomerStatus(salesWorkspace, customer, isInactive ? "Активен" : "Пауза"),
                IsDanger: !isInactive)
        ];
    }

    private static IReadOnlyList<RecordsGridActionDefinition> BuildInvoiceActions(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        return
        [
            new RecordsGridActionDefinition("Открыть счет", () => EditInvoice(salesWorkspace, invoice)),
            new RecordsGridActionDefinition("Связанные документы", () => ShowSalesDocumentLinks(salesWorkspace, invoice)),
            new RecordsGridActionDefinition("Печать", () => PrintInvoice(invoice)),
            new RecordsGridActionDefinition("Выгрузить PDF", () => ExportInvoice(invoice, SalesDocumentExportFormat.Pdf)),
            new RecordsGridActionDefinition("Выгрузить Excel", () => ExportInvoice(invoice, SalesDocumentExportFormat.Excel)),
            new RecordsGridActionDefinition("Выставить", () => ShowWorkflowResult("Счета", salesWorkspace.MarkInvoiceIssued(invoice.Id))),
            new RecordsGridActionDefinition("Отметить оплату", () => ShowWorkflowResult("Счета", salesWorkspace.MarkInvoicePaid(invoice.Id))),
            new RecordsGridActionDefinition("Поступление в кассу", () => ShowWorkflowResult("Счета", salesWorkspace.RecordCashReceiptForInvoice(invoice.Id))),
            new RecordsGridActionDefinition("Создать отгрузку", () => CreateShipmentFromInvoice(salesWorkspace, invoice))
        ];
    }

    private static IReadOnlyList<RecordsGridActionDefinition> BuildShipmentActions(SalesWorkspace salesWorkspace, SalesShipmentRecord shipment)
    {
        return
        [
            new RecordsGridActionDefinition("Открыть отгрузку", () => EditShipment(salesWorkspace, shipment)),
            new RecordsGridActionDefinition("Связанные документы", () => ShowSalesDocumentLinks(salesWorkspace, shipment)),
            new RecordsGridActionDefinition("Печать", () => PrintShipment(shipment)),
            new RecordsGridActionDefinition("Выгрузить PDF", () => ExportShipment(shipment, SalesDocumentExportFormat.Pdf)),
            new RecordsGridActionDefinition("Выгрузить Excel", () => ExportShipment(shipment, SalesDocumentExportFormat.Excel)),
            new RecordsGridActionDefinition("К сборке", () => ShowWorkflowResult("Отгрузки", salesWorkspace.PrepareShipment(shipment.Id))),
            new RecordsGridActionDefinition("Создать черновик возврата", () => CreateReturnFromShipment(salesWorkspace, shipment)),
            new RecordsGridActionDefinition("Провести отгрузку", () => ShowWorkflowResult("Отгрузки", salesWorkspace.ShipShipment(shipment.Id)))
        ];
    }

    private static IReadOnlyList<RecordsGridActionDefinition> BuildReturnActions(SalesWorkspace salesWorkspace, SalesReturnRecord returnDocument)
    {
        return
        [
            new RecordsGridActionDefinition("Связанные документы", () => ShowSalesDocumentLinks(salesWorkspace, returnDocument)),
            new RecordsGridActionDefinition("Открыть заказ-основание", () => OpenOrderFromLink(salesWorkspace, returnDocument.SalesOrderId, returnDocument.SalesOrderNumber, "Возвраты")),
            new RecordsGridActionDefinition("Печать возврата", () => PrintReturn(returnDocument)),
            new RecordsGridActionDefinition("Провести возврат", () => ShowWorkflowResult("Возвраты", salesWorkspace.ConductReturn(returnDocument.Id))),
            new RecordsGridActionDefinition("Отменить возврат", () => ShowWorkflowResult("Возвраты", salesWorkspace.CancelReturn(returnDocument.Id)))
        ];
    }

    private static IReadOnlyList<RecordsGridActionDefinition> BuildCashReceiptActions(SalesWorkspace salesWorkspace, SalesCashReceiptRecord cashReceipt)
    {
        return
        [
            new RecordsGridActionDefinition("Связанные документы", () => ShowSalesDocumentLinks(salesWorkspace, cashReceipt)),
            new RecordsGridActionDefinition("Открыть заказ-основание", () => OpenOrderFromLink(salesWorkspace, cashReceipt.SalesOrderId, cashReceipt.SalesOrderNumber, "Поступления в кассу")),
            new RecordsGridActionDefinition("Печать ПКО", () => PrintCashReceipt(cashReceipt)),
            new RecordsGridActionDefinition("Провести поступление", () => ShowWorkflowResult("Поступления в кассу", salesWorkspace.ConductCashReceipt(cashReceipt.Id))),
            new RecordsGridActionDefinition("Отменить поступление", () => ShowWorkflowResult("Поступления в кассу", salesWorkspace.CancelCashReceipt(cashReceipt.Id)))
        ];
    }

    private static void EditCustomer(SalesWorkspace salesWorkspace, SalesCustomerRecord customer)
    {
        if (TryOpenCustomerEditorTab(salesWorkspace, customer))
        {
            return;
        }

        var dialog = new SalesCustomerEditorWindow(salesWorkspace, customer);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultCustomer is not null)
        {
            salesWorkspace.UpdateCustomer(dialog.ResultCustomer);
            ShowMessage("Клиенты", $"Обновлена карточка {dialog.ResultCustomer.Name}.");
        }
    }

    private static void EditOrder(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        if (TryOpenOrderEditorTab(salesWorkspace, order))
        {
            return;
        }

        var dialog = new SalesDocumentEditorWindow(salesWorkspace, order);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultOrder is not null)
        {
            salesWorkspace.UpdateOrder(dialog.ResultOrder);
            ShowMessage("Заказы", $"Обновлен заказ {dialog.ResultOrder.Number}.");
        }
    }

    private static void PrintOrderCustomer(SalesOrderRecord order)
    {
        PrintDocumentComposer.Print(
            ResolveOwnerWindow(),
            $"Заказ покупателя {order.Number}",
            (pageWidth, pageHeight) => SalesOrderPrintDocumentComposer.Build(order, pageWidth, pageHeight));
    }

    private static void PrintOrderPicking(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        var definition = BuildOrderPickingPrintDefinition(salesWorkspace, order);
        PrintDocumentComposer.Print(
            ResolveOwnerWindow(),
            $"Лист сборки {order.Number}",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildTableDocument(definition, pageWidth, pageHeight));
    }

    private static PrintableTableDocumentDefinition BuildOrderPickingPrintDefinition(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        var currentOperator = ResolveCurrentOperator(salesWorkspace);
        var warehouseView = WarehouseWorkspace.Create(salesWorkspace);
        var warehouseWorkspace = ResolveOperationalWarehouseWorkspace(salesWorkspace, currentOperator);
        var purchasingWorkspace = ResolveOperationalPurchasingWorkspace(salesWorkspace, currentOperator);
        var pickLines = WarehouseCellStorageOperations.BuildOrderPickLines(order, warehouseView, warehouseWorkspace, purchasingWorkspace);
        var requiredTotal = pickLines.Sum(item => item.RequiredQuantity);
        var availableTotal = pickLines.Sum(item => Math.Min(item.RequiredQuantity, item.AvailableQuantity));
        var shortageTotal = pickLines.Sum(item => item.ShortageQuantity);
        var cellShortageCount = pickLines.Count(item => item.IsStockCovered && !item.IsCellCovered);
        var stockShortageCount = pickLines.Count(item => !item.IsStockCovered);
        var readiness = pickLines.Count == 0
            ? "Нет позиций"
            : stockShortageCount > 0
                ? $"Не хватает товара: {stockShortageCount:N0} строк"
                : cellShortageCount > 0
                    ? $"Нужно указать ячейки: {cellShortageCount:N0} строк"
                    : "Готово к сборке";

        return new PrintableTableDocumentDefinition(
            Title: $"Лист сборки заказа № {Clean(order.Number)} от {order.OrderDate:dd.MM.yyyy}",
            Subtitle: $"Для кладовщика: отбор товара без цен. Покупатель: {Clean(order.CustomerName)}",
            Facts:
            [
                new PrintableField("Заказ", Clean(order.Number)),
                new PrintableField("Покупатель", Clean(order.CustomerName)),
                new PrintableField("Склад", Clean(order.Warehouse)),
                new PrintableField("Дата печати", DateTime.Now.ToString("dd.MM.yyyy HH:mm", RuCulture)),
                new PrintableField("Менеджер", Clean(order.Manager)),
                new PrintableField("Статус", Clean(order.Status)),
                new PrintableField("Позиций", pickLines.Count.ToString("N0", RuCulture)),
                new PrintableField("Готовность", readiness)
            ],
            Columns:
            [
                new PrintableTableColumn("№", 0.32, TextAlignment.Center),
                new PrintableTableColumn("Код", 0.85),
                new PrintableTableColumn("Товар", 2.35),
                new PrintableTableColumn("Ячейки", 2.1),
                new PrintableTableColumn("К сборке", 0.78, TextAlignment.Right),
                new PrintableTableColumn("Доступно", 0.78, TextAlignment.Right),
                new PrintableTableColumn("Нехватка", 0.78, TextAlignment.Right),
                new PrintableTableColumn("Ед.", 0.45),
                new PrintableTableColumn("Статус", 1.0)
            ],
            Rows: pickLines
                .Select((line, index) => new PrintableTableRow(
                [
                    (index + 1).ToString("N0", RuCulture),
                    Clean(line.ItemCode),
                    Clean(line.ItemName),
                    Clean(line.CellSummary),
                    FormatQuantity(line.RequiredQuantity),
                    FormatQuantity(line.AvailableQuantity),
                    line.ShortageQuantity > 0m ? FormatQuantity(line.ShortageQuantity) : "—",
                    Clean(line.Unit),
                    Clean(line.PickStatus)
                ]))
                .ToArray(),
            Totals:
            [
                new PrintableField("Позиций", pickLines.Count.ToString("N0", RuCulture)),
                new PrintableField("К отбору", FormatQuantity(requiredTotal)),
                new PrintableField("Покрыто остатком", FormatQuantity(availableTotal)),
                new PrintableField("Нехватка товара", shortageTotal > 0m ? FormatQuantity(shortageTotal) : "—"),
                new PrintableField("Строк без полных ячеек", cellShortageCount > 0 ? cellShortageCount.ToString("N0", RuCulture) : "—")
            ],
            Comment: "Форма для сборки скрывает цены и суммы. Если строка без ячейки или с нехваткой, проверьте складские ячейки и остатки перед отгрузкой.");
    }

    private static OperationalWarehouseWorkspace ResolveOperationalWarehouseWorkspace(SalesWorkspace salesWorkspace, string currentOperator)
    {
        try
        {
            return WarehouseOperationalWorkspaceStore.CreateDefault()
                       .TryLoadExisting(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses)
                   ?? OperationalWarehouseWorkspace.Create(currentOperator, salesWorkspace);
        }
        catch
        {
            return OperationalWarehouseWorkspace.Create(currentOperator, salesWorkspace);
        }
    }

    private static OperationalPurchasingWorkspace? ResolveOperationalPurchasingWorkspace(SalesWorkspace salesWorkspace, string currentOperator)
    {
        try
        {
            return PurchasingOperationalWorkspaceStore.CreateDefault()
                       .TryLoadExisting(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses)
                   ?? OperationalPurchasingWorkspace.Create(currentOperator, salesWorkspace);
        }
        catch
        {
            return OperationalPurchasingWorkspace.Create(currentOperator, salesWorkspace);
        }
    }

    private static string ResolveCurrentOperator(SalesWorkspace salesWorkspace)
    {
        return string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator)
            ? Environment.UserName
            : salesWorkspace.CurrentOperator;
    }

    private static void ExportOrder(SalesOrderRecord order, SalesDocumentExportFormat format)
    {
        SalesDocumentExportService.ExportOrder(ResolveOwnerWindow(), order, format);
    }

    private static void ExportInvoice(SalesInvoiceRecord invoice, SalesDocumentExportFormat format)
    {
        SalesDocumentExportService.ExportTableDocument(
            ResolveOwnerWindow(),
            $"Счет {invoice.Number}",
            BuildInvoicePrintDefinition(invoice),
            format);
    }

    private static void ExportShipment(SalesShipmentRecord shipment, SalesDocumentExportFormat format)
    {
        SalesDocumentExportService.ExportTableDocument(
            ResolveOwnerWindow(),
            $"Расходная накладная {shipment.Number}",
            BuildShipmentPrintDefinition(shipment),
            format);
    }

    private static void PrintInvoice(SalesInvoiceRecord invoice)
    {
        var definition = BuildInvoicePrintDefinition(invoice);
        PrintDocumentComposer.Print(
            ResolveOwnerWindow(),
            $"Счет {invoice.Number}",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildTableDocument(definition, pageWidth, pageHeight));
    }

    private static void PrintShipment(SalesShipmentRecord shipment)
    {
        var definition = BuildShipmentPrintDefinition(shipment);
        PrintDocumentComposer.Print(
            ResolveOwnerWindow(),
            $"Отгрузка {shipment.Number}",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildTableDocument(definition, pageWidth, pageHeight));
    }

    private static void PrintReturn(SalesReturnRecord returnDocument)
    {
        var definition = BuildReturnPrintDefinition(returnDocument);
        PrintDocumentComposer.Print(
            ResolveOwnerWindow(),
            $"Возврат {returnDocument.Number}",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildTableDocument(definition, pageWidth, pageHeight));
    }

    private static void PrintCashReceipt(SalesCashReceiptRecord cashReceipt)
    {
        var definition = BuildCashReceiptPrintDefinition(cashReceipt);
        PrintDocumentComposer.Print(
            ResolveOwnerWindow(),
            $"ПКО {cashReceipt.Number}",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildTableDocument(definition, pageWidth, pageHeight));
    }

    private static PrintableTableDocumentDefinition BuildInvoicePrintDefinition(SalesInvoiceRecord invoice)
    {
        return new PrintableTableDocumentDefinition(
            Title: $"Счет на оплату № {Clean(invoice.Number)} от {invoice.InvoiceDate:dd.MM.yyyy}",
            Subtitle: $"Покупатель: {Clean(invoice.CustomerName)}",
            Facts:
            [
                new PrintableField("Покупатель", Clean(invoice.CustomerName)),
                new PrintableField("Заказ-основание", Clean(invoice.SalesOrderNumber)),
                new PrintableField("Дата счета", invoice.InvoiceDate.ToString("dd.MM.yyyy", RuCulture)),
                new PrintableField("Оплатить до", invoice.DueDate.ToString("dd.MM.yyyy", RuCulture)),
                new PrintableField("Договор", Clean(invoice.ContractNumber)),
                new PrintableField("Менеджер", Clean(invoice.Manager)),
                new PrintableField("Статус", Clean(invoice.Status)),
                new PrintableField("Валюта", Clean(invoice.CurrencyCode))
            ],
            Columns: BuildSalesPrintColumns(),
            Rows: BuildSalesLinePrintRows(invoice.Lines, invoice.CurrencyCode),
            Totals: BuildSalesPrintTotals(invoice.SubtotalAmount, invoice.EffectiveDiscountAmount, invoice.TotalAmount, invoice.CurrencyCode),
            Comment: invoice.Comment);
    }

    private static PrintableTableDocumentDefinition BuildShipmentPrintDefinition(SalesShipmentRecord shipment)
    {
        return new PrintableTableDocumentDefinition(
            Title: $"Расходная накладная № {Clean(shipment.Number)} от {shipment.ShipmentDate:dd.MM.yyyy}",
            Subtitle: $"Покупатель: {Clean(shipment.CustomerName)}",
            Facts:
            [
                new PrintableField("Покупатель", Clean(shipment.CustomerName)),
                new PrintableField("Заказ-основание", Clean(shipment.SalesOrderNumber)),
                new PrintableField("Дата отгрузки", shipment.ShipmentDate.ToString("dd.MM.yyyy", RuCulture)),
                new PrintableField("Склад", Clean(shipment.Warehouse)),
                new PrintableField("Перевозчик", Clean(shipment.Carrier)),
                new PrintableField("Статус", Clean(shipment.Status)),
                new PrintableField("Договор", Clean(shipment.ContractNumber)),
                new PrintableField("Менеджер", Clean(shipment.Manager))
            ],
            Columns: BuildSalesPrintColumns(),
            Rows: BuildSalesLinePrintRows(shipment.Lines, shipment.CurrencyCode),
            Totals: BuildSalesPrintTotals(shipment.SubtotalAmount, shipment.EffectiveDiscountAmount, shipment.TotalAmount, shipment.CurrencyCode),
            Comment: shipment.Comment);
    }

    private static PrintableTableDocumentDefinition BuildReturnPrintDefinition(SalesReturnRecord returnDocument)
    {
        return new PrintableTableDocumentDefinition(
            Title: $"Возврат покупателя № {Clean(returnDocument.Number)} от {returnDocument.ReturnDate:dd.MM.yyyy}",
            Subtitle: $"Покупатель: {Clean(returnDocument.CustomerName)}",
            Facts:
            [
                new PrintableField("Покупатель", Clean(returnDocument.CustomerName)),
                new PrintableField("Заказ-основание", Clean(returnDocument.SalesOrderNumber)),
                new PrintableField("Дата возврата", returnDocument.ReturnDate.ToString("dd.MM.yyyy", RuCulture)),
                new PrintableField("Склад", Clean(returnDocument.Warehouse)),
                new PrintableField("Причина", Clean(returnDocument.Reason)),
                new PrintableField("Статус", Clean(returnDocument.Status)),
                new PrintableField("Договор", Clean(returnDocument.ContractNumber)),
                new PrintableField("Менеджер", Clean(returnDocument.Manager))
            ],
            Columns: BuildSalesPrintColumns(),
            Rows: BuildSalesLinePrintRows(returnDocument.Lines, returnDocument.CurrencyCode),
            Totals: BuildSalesPrintTotals(returnDocument.SubtotalAmount, returnDocument.EffectiveDiscountAmount, returnDocument.TotalAmount, returnDocument.CurrencyCode, "Итого к возврату"),
            Comment: returnDocument.Comment);
    }

    private static PrintableTableDocumentDefinition BuildCashReceiptPrintDefinition(SalesCashReceiptRecord cashReceipt)
    {
        return new PrintableTableDocumentDefinition(
            Title: $"Приходный кассовый ордер № {Clean(cashReceipt.Number)} от {cashReceipt.ReceiptDate:dd.MM.yyyy}",
            Subtitle: $"Плательщик: {Clean(cashReceipt.CustomerName)}",
            Facts:
            [
                new PrintableField("Плательщик", Clean(cashReceipt.CustomerName)),
                new PrintableField("Заказ-основание", Clean(cashReceipt.SalesOrderNumber)),
                new PrintableField("Дата поступления", cashReceipt.ReceiptDate.ToString("dd.MM.yyyy", RuCulture)),
                new PrintableField("Касса", Clean(cashReceipt.CashBox)),
                new PrintableField("Договор", Clean(cashReceipt.ContractNumber)),
                new PrintableField("Статус", Clean(cashReceipt.Status)),
                new PrintableField("Менеджер", Clean(cashReceipt.Manager)),
                new PrintableField("Валюта", Clean(cashReceipt.CurrencyCode))
            ],
            Columns:
            [
                new PrintableTableColumn("№", 0.35, TextAlignment.Center),
                new PrintableTableColumn("Основание", 2.6),
                new PrintableTableColumn("Касса", 1.2),
                new PrintableTableColumn("Сумма", 1.0, TextAlignment.Right),
                new PrintableTableColumn("Комментарий", 1.8)
            ],
            Rows:
            [
                new PrintableTableRow(
                [
                    "1",
                    Clean(cashReceipt.SalesOrderNumber),
                    Clean(cashReceipt.CashBox),
                    FormatMoney(cashReceipt.Amount, cashReceipt.CurrencyCode),
                    Clean(cashReceipt.Comment)
                ])
            ],
            Totals:
            [
                new PrintableField("Принято", FormatMoney(cashReceipt.Amount, cashReceipt.CurrencyCode))
            ],
            Comment: cashReceipt.Comment);
    }

    private static IReadOnlyList<PrintableField> BuildSalesPrintTotals(
        decimal subtotal,
        decimal discount,
        decimal total,
        string currencyCode,
        string totalLabel = "Итого")
    {
        var totals = new List<PrintableField>();
        if (discount > 0m)
        {
            totals.Add(new PrintableField("Сумма без скидки", FormatMoney(subtotal, currencyCode)));
            totals.Add(new PrintableField("Скидка", FormatMoney(discount, currencyCode)));
        }

        totals.Add(new PrintableField(totalLabel, FormatMoney(total, currencyCode)));
        totals.Add(new PrintableField("Без налога (НДС)", string.Empty));
        return totals;
    }

    private static IReadOnlyList<PrintableTableColumn> BuildSalesPrintColumns()
    {
        return
        [
            new PrintableTableColumn("№", 0.35, TextAlignment.Center),
            new PrintableTableColumn("Код", 0.9),
            new PrintableTableColumn("Товары (работы, услуги)", 2.6),
            new PrintableTableColumn("Кол-во", 0.75, TextAlignment.Right),
            new PrintableTableColumn("Ед.", 0.55),
            new PrintableTableColumn("Цена", 0.9, TextAlignment.Right),
            new PrintableTableColumn("Сумма", 0.95, TextAlignment.Right)
        ];
    }

    private static IReadOnlyList<PrintableTableRow> BuildSalesLinePrintRows(IEnumerable<SalesOrderLineRecord> lines, string currencyCode)
    {
        return lines
            .Select((line, index) => new PrintableTableRow(
            [
                (index + 1).ToString("N0", RuCulture),
                Clean(line.ItemCode),
                Clean(line.ItemName),
                FormatQuantity(line.Quantity),
                Clean(line.Unit),
                FormatMoney(line.Price, currencyCode),
                FormatMoney(line.Amount, currencyCode)
            ]))
            .ToArray();
    }

    private static void EditInvoice(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        if (TryOpenInvoiceEditorTab(salesWorkspace, invoice))
        {
            return;
        }

        var dialog = new SalesDocumentEditorWindow(salesWorkspace, invoice);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultInvoice is not null)
        {
            salesWorkspace.UpdateInvoice(dialog.ResultInvoice);
            ShowMessage("Счета", $"Обновлен счет {dialog.ResultInvoice.Number}.");
        }
    }

    private static void EditShipment(SalesWorkspace salesWorkspace, SalesShipmentRecord shipment)
    {
        if (TryOpenShipmentEditorTab(salesWorkspace, shipment))
        {
            return;
        }

        var dialog = new SalesDocumentEditorWindow(salesWorkspace, shipment);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultShipment is not null)
        {
            salesWorkspace.UpdateShipment(dialog.ResultShipment);
            ShowMessage("Отгрузки", $"Обновлена отгрузка {dialog.ResultShipment.Number}.");
        }
    }

    private static void ShowSalesDocumentLinks(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        ShowSalesDocumentLinksDialog(new SalesDocumentLinksWindow(salesWorkspace, order));
    }

    private static void ShowSalesDocumentLinks(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        ShowSalesDocumentLinksDialog(new SalesDocumentLinksWindow(salesWorkspace, invoice));
    }

    private static void ShowSalesDocumentLinks(SalesWorkspace salesWorkspace, SalesShipmentRecord shipment)
    {
        ShowSalesDocumentLinksDialog(new SalesDocumentLinksWindow(salesWorkspace, shipment));
    }

    private static void ShowSalesDocumentLinks(SalesWorkspace salesWorkspace, SalesReturnRecord returnDocument)
    {
        ShowSalesDocumentLinksDialog(new SalesDocumentLinksWindow(salesWorkspace, returnDocument));
    }

    private static void ShowSalesDocumentLinks(SalesWorkspace salesWorkspace, SalesCashReceiptRecord cashReceipt)
    {
        ShowSalesDocumentLinksDialog(new SalesDocumentLinksWindow(salesWorkspace, cashReceipt));
    }

    private static void ShowSalesDocumentLinksDialog(SalesDocumentLinksWindow dialog)
    {
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }

    private static void OpenOrderFromLink(SalesWorkspace salesWorkspace, Guid orderId, string orderNumber, string title)
    {
        var order = FindOrderByLink(salesWorkspace, orderId, orderNumber);
        if (order is null)
        {
            ShowMessage(title, "Заказ-основание не найден.", MessageBoxImage.Warning);
            return;
        }

        EditOrder(salesWorkspace, order);
    }

    private static SalesOrderRecord? FindOrderByLink(SalesWorkspace salesWorkspace, Guid orderId, string orderNumber)
    {
        if (orderId != Guid.Empty)
        {
            var orderById = salesWorkspace.Orders.FirstOrDefault(item => item.Id == orderId);
            if (orderById is not null)
            {
                return orderById;
            }
        }

        var cleanOrderNumber = Clean(orderNumber);
        return string.IsNullOrWhiteSpace(cleanOrderNumber)
            ? null
            : salesWorkspace.Orders.FirstOrDefault(item => Clean(item.Number).Equals(cleanOrderNumber, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetCustomerStatus(SalesWorkspace salesWorkspace, SalesCustomerRecord customer, string status)
    {
        var copy = customer.Clone();
        copy.Status = status;
        salesWorkspace.UpdateCustomer(copy);
        ShowMessage("Клиенты", $"Статус клиента {copy.Name}: {status}.");
    }

    private static void DuplicateOrder(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        var copy = salesWorkspace.DuplicateOrder(order.Id);
        ShowMessage("Заказы", $"Создан дубликат {copy.Number}.");
    }

    private static void CreateInvoiceFromOrder(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        var invoice = salesWorkspace.CreateInvoiceDraftFromOrder(order.Id);
        if (!TryRunSalesEditorSave(() => salesWorkspace.AddInvoice(invoice)))
        {
            return;
        }

        ShowMessage("Счета", $"Создан счет {invoice.Number} по заказу {order.Number}.");
    }

    private static void CreateShipmentFromOrder(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        var shipment = salesWorkspace.CreateShipmentDraftFromOrder(order.Id);
        if (!TryRunSalesEditorSave(() => salesWorkspace.AddShipment(shipment)))
        {
            return;
        }

        ShowMessage("Отгрузки", $"Создана отгрузка {shipment.Number} по заказу {order.Number}.");
    }

    private static void CreateShipmentFromInvoice(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        var order = salesWorkspace.Orders.FirstOrDefault(item => item.Id == invoice.SalesOrderId)
            ?? salesWorkspace.Orders.FirstOrDefault(item => Clean(item.Number).Equals(Clean(invoice.SalesOrderNumber), StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            ShowMessage("Отгрузки", "Не найден заказ-основание для создания отгрузки.", MessageBoxImage.Warning);
            return;
        }

        CreateShipmentFromOrder(salesWorkspace, order);
    }

    private static void CreateReturnFromOrder(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        var returnDocument = salesWorkspace.CreateReturnDraftFromOrder(order.Id);
        salesWorkspace.AddReturn(returnDocument);
        ShowMessage("Возвраты", $"Создан черновик возврата {returnDocument.Number} по заказу {order.Number}. Складские остатки не изменены.");
        ShowSalesDocumentLinks(salesWorkspace, order);
    }

    private static void CreateReturnFromShipment(SalesWorkspace salesWorkspace, SalesShipmentRecord shipment)
    {
        var returnDocument = salesWorkspace.CreateReturnDraftFromShipment(shipment.Id);
        salesWorkspace.AddReturn(returnDocument);
        ShowMessage("Возвраты", $"Создан черновик возврата {returnDocument.Number} по отгрузке {shipment.Number}. Складские остатки не изменены.");
        ShowSalesDocumentLinks(salesWorkspace, shipment);
    }

    private static void ShowWorkflowResult(string title, SalesWorkflowActionResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Detail)
            ? result.Message
            : $"{result.Message}{Environment.NewLine}{result.Detail}";
        ShowMessage(title, message, result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static void CreateCustomer(SalesWorkspace salesWorkspace)
    {
        if (TryOpenCustomerEditorTab(salesWorkspace, null))
        {
            return;
        }

        var dialog = new SalesCustomerEditorWindow(salesWorkspace);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultCustomer is not null)
        {
            salesWorkspace.AddCustomer(dialog.ResultCustomer);
            ShowMessage("Клиенты", $"Создан клиент {dialog.ResultCustomer.Name}.");
        }
    }

    private static void CreateSalesOrder(SalesWorkspace salesWorkspace)
    {
        if (salesWorkspace.Customers.Count == 0)
        {
            ShowMessage("Заказы", "Сначала добавьте клиента для нового заказа.", MessageBoxImage.Information);
            return;
        }

        if (TryOpenOrderEditorTab(salesWorkspace, null))
        {
            return;
        }

        var dialog = new SalesDocumentEditorWindow(salesWorkspace, SalesDocumentEditorMode.Order);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultOrder is not null)
        {
            if (!TryRunSalesEditorSave(() => salesWorkspace.AddOrder(dialog.ResultOrder)))
            {
                return;
            }

            ShowMessage("Заказы", $"Создан заказ {dialog.ResultOrder.Number}.");
        }
    }

    private static void CreateInvoice(SalesWorkspace salesWorkspace)
    {
        if (salesWorkspace.Orders.Count == 0)
        {
            ShowMessage("Счета", "Сначала добавьте заказ, чтобы сформировать счет.", MessageBoxImage.Information);
            return;
        }

        if (TryOpenInvoiceEditorTab(salesWorkspace, null))
        {
            return;
        }

        var dialog = new SalesDocumentEditorWindow(salesWorkspace, SalesDocumentEditorMode.Invoice);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultInvoice is not null)
        {
            if (!TryRunSalesEditorSave(() => salesWorkspace.AddInvoice(dialog.ResultInvoice)))
            {
                return;
            }

            ShowMessage("Счета", $"Создан счет {dialog.ResultInvoice.Number}.");
        }
    }

    private static void CreateShipment(SalesWorkspace salesWorkspace)
    {
        if (salesWorkspace.Orders.Count == 0)
        {
            ShowMessage("Отгрузки", "Сначала добавьте заказ, чтобы сформировать отгрузку.", MessageBoxImage.Information);
            return;
        }

        if (TryOpenShipmentEditorTab(salesWorkspace, null))
        {
            return;
        }

        var dialog = new SalesDocumentEditorWindow(salesWorkspace, SalesDocumentEditorMode.Shipment);
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() == true && dialog.ResultShipment is not null)
        {
            if (!TryRunSalesEditorSave(() => salesWorkspace.AddShipment(dialog.ResultShipment)))
            {
                return;
            }

            ShowMessage("Отгрузки", $"Создана отгрузка {dialog.ResultShipment.Number}.");
        }
    }

    private static bool TryOpenCustomerEditorTab(SalesWorkspace salesWorkspace, SalesCustomerRecord? customer)
    {
        var mainWindow = ResolveMainWindow();
        if (mainWindow is null)
        {
            return false;
        }

        var isNew = customer is null;
        var key = isNew ? $"customer-new-{Guid.NewGuid():N}" : $"customer-{customer!.Id:N}";
        var caption = isNew ? "Новый клиент" : $"Клиент {Clean(customer!.Code)}";
        var subtitle = isNew
            ? "Создание карточки контрагента без блокировки основного окна."
            : $"Карточка, контакты и документы контрагента {Clean(customer!.Name)}.";

        return mainWindow.OpenWorkspaceEditorTab(key, caption, subtitle, () =>
        {
            var editor = isNew
                ? new SalesCustomerEditorWindow(salesWorkspace)
                : new SalesCustomerEditorWindow(salesWorkspace, customer);

            editor.HostedSaved += (_, _) =>
            {
                if (editor.ResultCustomer is null)
                {
                    return;
                }

                if (isNew)
                {
                    salesWorkspace.AddCustomer(editor.ResultCustomer);
                    ShowMessage("Клиенты", $"Создан клиент {editor.ResultCustomer.Name}.");
                }
                else
                {
                    salesWorkspace.UpdateCustomer(editor.ResultCustomer);
                    ShowMessage("Клиенты", $"Обновлена карточка {editor.ResultCustomer.Name}.");
                }

                mainWindow.CloseWorkspaceTab(key);
            };
            editor.HostedCanceled += (_, _) => mainWindow.CloseWorkspaceTab(key);
            return editor.DetachContentForWorkspaceTab();
        });
    }

    private static bool TryOpenOrderEditorTab(SalesWorkspace salesWorkspace, SalesOrderRecord? order)
    {
        var mainWindow = ResolveMainWindow();
        if (mainWindow is null)
        {
            return false;
        }

        var isNew = order is null;
        var key = isNew ? $"sales-order-new-{Guid.NewGuid():N}" : $"sales-order-{order!.Id:N}";
        var caption = isNew ? "Новый заказ" : $"Заказ {Clean(order!.Number)}";
        var subtitle = isNew
            ? "Создание заказа покупателя в рабочей вкладке."
            : $"Заказ покупателя {Clean(order!.CustomerName)}.";

        return mainWindow.OpenWorkspaceEditorTab(key, caption, subtitle, () =>
        {
            var editor = isNew
                ? new SalesDocumentEditorWindow(salesWorkspace, SalesDocumentEditorMode.Order)
                : new SalesDocumentEditorWindow(salesWorkspace, order!);

            editor.HostedSaved += (_, _) =>
            {
                if (editor.ResultOrder is null)
                {
                    return;
                }

                if (isNew)
                {
                    if (!TryRunSalesEditorSave(() => salesWorkspace.AddOrder(editor.ResultOrder)))
                    {
                        return;
                    }

                    ShowMessage("Заказы", $"Создан заказ {editor.ResultOrder.Number}.");
                }
                else
                {
                    if (!TryRunSalesEditorSave(() => salesWorkspace.UpdateOrder(editor.ResultOrder)))
                    {
                        return;
                    }

                    ShowMessage("Заказы", $"Обновлен заказ {editor.ResultOrder.Number}.");
                }

                mainWindow.CloseWorkspaceTab(key);
            };
            editor.HostedCanceled += (_, _) => mainWindow.CloseWorkspaceTab(key);
            return editor.DetachContentForWorkspaceTab();
        });
    }

    private static bool TryOpenInvoiceEditorTab(SalesWorkspace salesWorkspace, SalesInvoiceRecord? invoice)
    {
        var mainWindow = ResolveMainWindow();
        if (mainWindow is null)
        {
            return false;
        }

        var isNew = invoice is null;
        var key = isNew ? $"sales-invoice-new-{Guid.NewGuid():N}" : $"sales-invoice-{invoice!.Id:N}";
        var caption = isNew ? "Новый счет" : $"Счет {Clean(invoice!.Number)}";
        var subtitle = isNew
            ? "Создание счета на основании заказа."
            : $"Счет покупателя {Clean(invoice!.CustomerName)}.";

        return mainWindow.OpenWorkspaceEditorTab(key, caption, subtitle, () =>
        {
            var editor = isNew
                ? new SalesDocumentEditorWindow(salesWorkspace, SalesDocumentEditorMode.Invoice)
                : new SalesDocumentEditorWindow(salesWorkspace, invoice!);

            editor.HostedSaved += (_, _) =>
            {
                if (editor.ResultInvoice is null)
                {
                    return;
                }

                if (isNew)
                {
                    if (!TryRunSalesEditorSave(() => salesWorkspace.AddInvoice(editor.ResultInvoice)))
                    {
                        return;
                    }

                    ShowMessage("Счета", $"Создан счет {editor.ResultInvoice.Number}.");
                }
                else
                {
                    if (!TryRunSalesEditorSave(() => salesWorkspace.UpdateInvoice(editor.ResultInvoice)))
                    {
                        return;
                    }

                    ShowMessage("Счета", $"Обновлен счет {editor.ResultInvoice.Number}.");
                }

                mainWindow.CloseWorkspaceTab(key);
            };
            editor.HostedCanceled += (_, _) => mainWindow.CloseWorkspaceTab(key);
            return editor.DetachContentForWorkspaceTab();
        });
    }

    private static bool TryOpenShipmentEditorTab(SalesWorkspace salesWorkspace, SalesShipmentRecord? shipment)
    {
        var mainWindow = ResolveMainWindow();
        if (mainWindow is null)
        {
            return false;
        }

        var isNew = shipment is null;
        var key = isNew ? $"sales-shipment-new-{Guid.NewGuid():N}" : $"sales-shipment-{shipment!.Id:N}";
        var caption = isNew ? "Новая отгрузка" : $"Отгрузка {Clean(shipment!.Number)}";
        var subtitle = isNew
            ? "Создание отгрузки на основании заказа."
            : $"Отгрузка покупателя {Clean(shipment!.CustomerName)}.";

        return mainWindow.OpenWorkspaceEditorTab(key, caption, subtitle, () =>
        {
            var editor = isNew
                ? new SalesDocumentEditorWindow(salesWorkspace, SalesDocumentEditorMode.Shipment)
                : new SalesDocumentEditorWindow(salesWorkspace, shipment!);

            editor.HostedSaved += (_, _) =>
            {
                if (editor.ResultShipment is null)
                {
                    return;
                }

                if (isNew)
                {
                    if (!TryRunSalesEditorSave(() => salesWorkspace.AddShipment(editor.ResultShipment)))
                    {
                        return;
                    }

                    ShowMessage("Отгрузки", $"Создана отгрузка {editor.ResultShipment.Number}.");
                }
                else
                {
                    if (!TryRunSalesEditorSave(() => salesWorkspace.UpdateShipment(editor.ResultShipment)))
                    {
                        return;
                    }

                    ShowMessage("Отгрузки", $"Обновлена отгрузка {editor.ResultShipment.Number}.");
                }

                mainWindow.CloseWorkspaceTab(key);
            };
            editor.HostedCanceled += (_, _) => mainWindow.CloseWorkspaceTab(key);
            return editor.DetachContentForWorkspaceTab();
        });
    }

    private static string? PromptValue(string title, string prompt, string? initialValue = null, IEnumerable<string>? options = null)
    {
        var dialog = new ProductTextInputWindow(title, prompt, initialValue, options);
        var owner = ResolveOwnerWindow();
        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }

    private static bool TryRunSalesEditorSave(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            ShowMessage("Проверка документа", exception.Message, MessageBoxImage.Warning);
            return false;
        }
    }

    private static Window? ResolveOwnerWindow()
    {
        return System.Windows.Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;
    }

    private static MainWindow? ResolveMainWindow()
    {
        return System.Windows.Application.Current?.MainWindow as MainWindow;
    }

    private static void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information)
    {
        var owner = ResolveOwnerWindow();
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
            return;
        }

        MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
    }

    private static IReadOnlyDictionary<string, decimal> BuildStockLookup(SalesWorkspace salesWorkspace)
    {
        return WarehouseWorkspace.Create(salesWorkspace).StockBalances
            .GroupBy(item => Clean(item.ItemCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.FreeQuantity), StringComparer.OrdinalIgnoreCase);
    }

    private static decimal GetStockValue(IReadOnlyDictionary<string, decimal> stockLookup, string code)
    {
        return stockLookup.TryGetValue(Clean(code), out var value) ? value : 0m;
    }

    private static WorkspaceMetricCardDefinition Metric(string title, int value, string delta, string accentHex, string iconBackgroundHex, string deltaHex, string iconGlyph)
    {
        return new WorkspaceMetricCardDefinition(
            title,
            value.ToString("N0", RuCulture),
            "Актуально",
            "по текущим данным",
            accentHex,
            iconBackgroundHex,
            "#6E7A96",
            iconGlyph);
    }

    private static RecordsGridCellDefinition Cell(string text, string foregroundHex = "#1B2440", string backgroundHex = "Transparent", bool semiBold = false)
    {
        return new RecordsGridCellDefinition(Clean(text), foregroundHex, backgroundHex, semiBold);
    }

    private static RecordsGridCellDefinition ActionCell()
    {
        return new RecordsGridCellDefinition("...", "#7A86A5", "Transparent", true);
    }

    private static RecordsGridCellDefinition StatusCell(string status)
    {
        var normalized = Clean(status);
        var palette = normalized switch
        {
            "Новый" or "Новые" => ("#4F5BFF", "#EEF2FF"),
            "Подтвержден" or "Подтвержденные" or "Активен" or "Активные" or "Оплачено" or "Доставлено" or "В наличии" or "Норма" or "Проведено" => ("#1DAA63", "#EAF9F0"),
            "Черновик" or "Выставлен" or "Частично оплачено" or "Под контролем" or "Запланировано" or "Низкий остаток" => ("#F29A17", "#FFF4E3"),
            "Просрочено" or "Не оплачено" or "Неактивен" or "Отменено" or "Нет в наличии" or "Критично" => ("#F15B5B", "#FFF0F0"),
            "В производстве" or "В пути" => ("#7B68EE", "#F1EEFF"),
            _ => ("#7180A0", "#F3F6FB")
        };

        return new RecordsGridCellDefinition(normalized, palette.Item1, palette.Item2, true);
    }

    private static string NormalizeOrderFilter(string status)
    {
        return Clean(status) switch
        {
            "План" or "Черновик" => "Новые",
            "Подтвержден" or "В резерве" => "Подтвержденные",
            "Готов к отгрузке" or "К сборке" => "В производстве",
            "Отгружена" or "Отгружен" or "Выполнен" or "Закрыт" => "Выполненные",
            _ => "Новые"
        };
    }

    private static string NormalizeOrderStatusLabel(string status)
    {
        return NormalizeOrderFilter(status) switch
        {
            "Новые" => "Новый",
            "Подтвержденные" => "Подтвержден",
            "В производстве" => "В производстве",
            "Выполненные" => "Выполнен",
            _ => Clean(status)
        };
    }

    private static string NormalizeCustomerFilter(string status)
    {
        return Clean(status) switch
        {
            "Активен" => "Активные",
            "На проверке" => "Новые",
            "Пауза" or "Неактивен" => "Неактивные",
            _ => "Активные"
        };
    }

    private static string NormalizeCustomerStatusLabel(string status)
    {
        return NormalizeCustomerFilter(status) switch
        {
            "Активные" => "Активен",
            "Новые" => "Новый",
            "Неактивные" => "Неактивен",
            _ => Clean(status)
        };
    }

    private static string NormalizeInvoiceFilter(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        var recordedPaidAmount = GetRecordedPaidAmount(salesWorkspace, invoice);
        if (invoice.TotalAmount > 0m && recordedPaidAmount >= invoice.TotalAmount)
        {
            return "Оплачено";
        }

        if (recordedPaidAmount > 0m)
        {
            return "Частично оплачено";
        }

        var status = Clean(invoice.Status);
        if (status.Equals("Оплачен", StringComparison.OrdinalIgnoreCase))
        {
            return "Оплачено";
        }

        if (status.Equals("Частично оплачен", StringComparison.OrdinalIgnoreCase))
        {
            return "Частично оплачено";
        }

        if (invoice.DueDate.Date < DateTime.Today && !status.Equals("Оплачен", StringComparison.OrdinalIgnoreCase))
        {
            return "Просрочено";
        }

        return "Не оплачено";
    }

    private static string NormalizeInvoiceStatusLabel(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        return NormalizeInvoiceFilter(salesWorkspace, invoice);
    }

    private static decimal GetPaidAmount(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        var recordedPaidAmount = GetRecordedPaidAmount(salesWorkspace, invoice);
        if (recordedPaidAmount > 0m)
        {
            return Math.Min(invoice.TotalAmount, recordedPaidAmount);
        }

        return Clean(invoice.Status).Equals("Оплачен", StringComparison.OrdinalIgnoreCase)
            ? invoice.TotalAmount
            : 0m;
    }

    private static decimal GetRecordedPaidAmount(SalesWorkspace salesWorkspace, SalesInvoiceRecord invoice)
    {
        return salesWorkspace.CashReceipts
            .Where(item =>
                IsActiveCashReceipt(item)
                && (
                item.SalesOrderId == invoice.SalesOrderId
                || Clean(item.SalesOrderNumber).Equals(Clean(invoice.SalesOrderNumber), StringComparison.OrdinalIgnoreCase)))
            .Sum(item => item.Amount);
    }

    private static string GetJournalCurrencyCode(SalesWorkspace salesWorkspace)
    {
        var currencies = salesWorkspace.CashReceipts
            .Where(IsActiveCashReceipt)
            .Select(item => Clean(item.CurrencyCode))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return currencies.Length == 1 ? currencies[0] : "RUB";
    }

    private static bool IsActiveCashReceipt(SalesCashReceiptRecord cashReceipt)
    {
        return !Clean(cashReceipt.Status).Equals("Отменено", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeShipmentFilter(string status)
    {
        return Clean(status) switch
        {
            "Черновик" or "К сборке" => "Запланировано",
            "Готова к отгрузке" or "В пути" => "В пути",
            "Отгружена" or "Доставлено" => "Доставлено",
            "Отменена" => "Отменено",
            _ => "Запланировано"
        };
    }

    private static string NormalizeShipmentStatusLabel(string status)
    {
        return NormalizeShipmentFilter(status) switch
        {
            "Запланировано" => "Запланировано",
            "В пути" => "В пути",
            "Доставлено" => "Доставлено",
            "Отменено" => "Отменено",
            _ => Clean(status)
        };
    }

    private static string NormalizeCatalogStatus(decimal stock)
    {
        if (stock <= 0m)
        {
            return "Нет в наличии";
        }

        if (stock < 20m)
        {
            return "Низкий остаток";
        }

        return "В наличии";
    }

    private static string NormalizeScenarioFilter(string priority)
    {
        return Clean(priority) switch
        {
            "Критично" => "Критично",
            "Важно" => "Важно",
            _ => "План"
        };
    }

    private static string GetCustomerType(SalesCustomerRecord customer)
    {
        var type = Clean(customer.CounterpartyType);
        if (!string.IsNullOrWhiteSpace(type))
        {
            return type switch
            {
                "Индивидуальный предприниматель" => "ИП",
                "Юридическое лицо" => "Юр. лицо",
                "Физическое лицо" => "Физ. лицо",
                "Государственный орган" => "Госорган",
                _ => type
            };
        }

        return Clean(customer.Name).Contains("ИП", StringComparison.OrdinalIgnoreCase) ? "ИП" : "Юр. лицо";
    }

    private static string GetCustomerInn(SalesCustomerRecord customer)
    {
        return string.IsNullOrWhiteSpace(customer.Inn)
            ? BuildPseudoInn(customer.Code, customer.Name)
            : Clean(customer.Inn);
    }

    private static string GetCustomerResponsible(SalesCustomerRecord customer)
    {
        return string.IsNullOrWhiteSpace(customer.Responsible)
            ? Clean(customer.Manager)
            : Clean(customer.Responsible);
    }

    private static string BuildPseudoInn(string code, string name)
    {
        var source = $"{Clean(code)}|{Clean(name)}";
        long accumulator = 0;
        for (var index = 0; index < source.Length; index++)
        {
            accumulator += source[index] * (index + 11);
        }

        return accumulator.ToString(CultureInfo.InvariantCulture).PadLeft(10, '7')[..10];
    }

    private static string GetCustomerLocation(SalesCustomerRecord customer)
    {
        var region = Clean(customer.Region);
        var city = Clean(customer.City);
        if (!string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(city))
        {
            return $"{region} / {city}";
        }

        return !string.IsNullOrWhiteSpace(city) ? city : GetCustomerCity(customer.Name);
    }

    private static IReadOnlyList<RecordsGroupNodeDefinition> BuildCustomerGroupTree(SalesWorkspace salesWorkspace)
    {
        var customers = salesWorkspace.Customers
            .OrderBy(item => GetCustomerRegion(item), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => GetCustomerCityGroup(item), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Clean(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var regionNodes = customers
            .GroupBy(GetCustomerRegion, StringComparer.CurrentCultureIgnoreCase)
            .Select(regionGroup =>
            {
                var region = regionGroup.Key;
                var cityNodes = regionGroup
                    .GroupBy(GetCustomerCityGroup, StringComparer.CurrentCultureIgnoreCase)
                    .Select(cityGroup =>
                    {
                        var city = cityGroup.Key;
                        return new RecordsGroupNodeDefinition(
                            city,
                            BuildCustomerGroupPath(region, city),
                            cityGroup.Count(),
                            IconGlyph: "\uE80F");
                    })
                    .OrderBy(node => node.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();

                return new RecordsGroupNodeDefinition(
                    region,
                    BuildCustomerGroupPath(region),
                    regionGroup.Count(),
                    cityNodes,
                    "\uE8B7");
            })
            .OrderBy(node => node.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return
        [
            new RecordsGroupNodeDefinition(
                "Все покупатели",
                string.Empty,
                customers.Length,
                regionNodes,
                "\uE8D4")
        ];
    }

    private static string BuildCustomerGroupPath(SalesCustomerRecord customer)
    {
        return BuildCustomerGroupPath(GetCustomerRegion(customer), GetCustomerCityGroup(customer), customer.Id.ToString("N"));
    }

    private static string BuildCustomerGroupPath(params string[] parts)
    {
        return string.Join(CustomerGroupSeparator, parts.Select(Clean).Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string GetCustomerRegion(SalesCustomerRecord customer)
    {
        var region = Clean(customer.Region);
        return string.IsNullOrWhiteSpace(region) ? "Без региона" : region;
    }

    private static string GetCustomerCityGroup(SalesCustomerRecord customer)
    {
        var city = Clean(customer.City);
        return string.IsNullOrWhiteSpace(city) ? GetCustomerCity(customer.Name) : city;
    }

    private static string GetCustomerCity(string name)
    {
        var cities = new[] { "Москва", "Санкт-Петербург", "Казань", "Екатеринбург", "Ростов-на-Дону", "Самара", "Уфа" };
        var sum = Clean(name).Sum(ch => ch);
        return cities[Math.Abs(sum) % cities.Length];
    }

    private static string SearchIndex(params string?[] values)
    {
        return string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean));
    }

    private static string FormatMoney(decimal amount, string currencyCode)
    {
        var currency = string.Equals(currencyCode, "RUB", StringComparison.OrdinalIgnoreCase)
            ? "₽"
            : Clean(currencyCode);
        return $"{amount:N2} {currency}";
    }

    private static string FormatQuantity(decimal quantity)
    {
        return decimal.Truncate(quantity) == quantity
            ? quantity.ToString("N0", RuCulture)
            : quantity.ToString("N2", RuCulture);
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd.MM.yyyy", RuCulture) : "—";
    }

    private static string Clean(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private static string Csv(string? value)
    {
        var text = Clean(value);
        return text.IndexOfAny([';', '"', '\r', '\n']) < 0
            ? text
            : $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string Hex(System.Drawing.Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
