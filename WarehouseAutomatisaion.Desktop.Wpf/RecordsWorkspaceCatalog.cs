using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class RecordsWorkspaceCatalog
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] OrderFilters = ["Все заказы", "Новые", "Подтвержденные", "В производстве", "Выполненные"];
    private static readonly string[] CustomerFilters = ["Все клиенты", "Активные", "Новые", "Неактивные"];
    private static readonly string[] InvoiceFilters = ["Все счета", "Оплачено", "Частично оплачено", "Просрочено", "Не оплачено"];
    private static readonly string[] ShipmentFilters = ["Все отгрузки", "Запланировано", "В пути", "Доставлено", "Отменено"];
    private static readonly string[] PurchasingFilters = ["Все документы", "Заказы", "Счета", "Приемки"];
    private static readonly string[] ScenarioFilters = ["Все сценарии", "Критично", "Важно", "План"];

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
                Metric("Всего заказов", salesWorkspace.Orders.Count, "+12%", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE14C"),
                Metric("Новые", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "Новые"), "+8%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8A5"),
                Metric("Подтвержденные", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "Подтвержденные"), "+15%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE73E"),
                Metric("В производстве", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "В производстве"), "-4%", "#7B68EE", "#F1EEFF", "#FF6B6B", "\uE7C1"),
                Metric("Выполненные", salesWorkspace.Orders.Count(item => NormalizeOrderFilter(item.Status) == "Выполненные"), "+7%", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uEC47")
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
                Metric("Всего клиентов", salesWorkspace.Customers.Count, "+5%", "#FFB648", "#FFF5E8", "#1BC47D", "\uE77B"),
                Metric("Активные", salesWorkspace.Customers.Count(item => NormalizeCustomerFilter(item.Status) == "Активные"), "+8%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8D4"),
                Metric("Новые", salesWorkspace.Customers.Count(item => NormalizeCustomerFilter(item.Status) == "Новые"), "+12%", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE77B"),
                Metric("Неактивные", salesWorkspace.Customers.Count(item => NormalizeCustomerFilter(item.Status) == "Неактивные"), "-3%", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uE711")
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
                    RowActions: BuildCustomerActions(salesWorkspace, item)))
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
            UnsubscribeFromChanges: handler => salesWorkspace.Changed -= handler);
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
                Metric("Все счета", salesWorkspace.Invoices.Count, "+8%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8C7"),
                Metric("Оплачено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(item) == "Оплачено"), "+10%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8FB"),
                Metric("Частично оплачено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(item) == "Частично оплачено"), "+5%", "#FFB648", "#FFF5E8", "#1BC47D", "\uE7C2"),
                Metric("Просрочено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(item) == "Просрочено"), "-2%", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uEA39"),
                Metric("Не оплачено", salesWorkspace.Invoices.Count(item => NormalizeInvoiceFilter(item) == "Не оплачено"), "+12%", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uE8C7")
            ],
            RowsFactory: () => salesWorkspace.Invoices
                .OrderByDescending(item => item.InvoiceDate)
                .Select(item => new RecordsGridItem(
                    SearchText: SearchIndex(item.Number, item.SalesOrderNumber, item.CustomerName, item.Manager, item.Status),
                    FilterValue: NormalizeInvoiceFilter(item),
                    DateValue: item.InvoiceDate,
                    Cells:
                    [
                        Cell(Clean(item.Number)),
                        Cell(Clean(item.SalesOrderNumber)),
                        Cell(Clean(item.CustomerName)),
                        Cell(item.InvoiceDate.ToString("dd.MM.yyyy", RuCulture)),
                        Cell(FormatMoney(item.TotalAmount, item.CurrencyCode), semiBold: true),
                        Cell(FormatMoney(GetPaidAmount(item), item.CurrencyCode)),
                        StatusCell(NormalizeInvoiceStatusLabel(item)),
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
                Metric("Все отгрузки", salesWorkspace.Shipments.Count, "+15%", "#FFB648", "#FFF5E8", "#1BC47D", "\uEC47"),
                Metric("Запланировано", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "Запланировано"), "+7%", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE823"),
                Metric("В пути", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "В пути"), "+12%", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uE7C1"),
                Metric("Доставлено", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "Доставлено"), "+10%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE73E"),
                Metric("Отменено", salesWorkspace.Shipments.Count(item => NormalizeShipmentFilter(item.Status) == "Отменено"), "-3%", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uE711")
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
                Metric("Поставщики", workspace.Suppliers.Count, "+4%", "#FFB648", "#FFF5E8", "#1BC47D", "\uE77B"),
                Metric("Заказы", workspace.PurchaseOrders.Count, "+9%", "#6C63FF", "#F0EDFF", "#1BC47D", "\uE14C"),
                Metric("Счета", workspace.SupplierInvoices.Count, "+5%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE8C7"),
                Metric("Приемки", workspace.PurchaseReceipts.Count, "+7%", "#4F8CFF", "#EEF4FF", "#1BC47D", "\uE7C1")
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
                    Metric("Всего товаров", items.Length, "+6%", "#6C63FF", "#F0EDFF", "#1BC47D", "\uEECA"),
                    Metric("В наличии", inStock, "+4%", "#59C36A", "#EBF9EF", "#1BC47D", "\uE7BA"),
                    Metric("Низкий остаток", lowStock, "-2%", "#FFB648", "#FFF5E8", "#FF6B6B", "\uEA39"),
                    Metric("Нет в наличии", outOfStock, "+1%", "#FF8A65", "#FFF0ED", "#FF6B6B", "\uE711")
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
        var audit = DataIntegrityAuditor.Audit(salesWorkspace, catalogWorkspace, purchasingWorkspace);
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
        var rows = auditRows
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
                new WorkspaceMetricCardDefinition("Критично", audit.CriticalCount.ToString("N0", RuCulture), audit.CriticalCount == 0 ? "нет блокеров" : "исправить вручную", "Разорванные связи и дубли ключей", "#F15B5B", "#FFF0F0", "#F15B5B", "\uEA39"),
                new WorkspaceMetricCardDefinition("Важно", audit.WarningCount.ToString("N0", RuCulture), audit.WarningCount == 0 ? "нет замечаний" : "проверить", "Пустые строки, цены и даты", "#F29A17", "#FFF4E3", "#F29A17", "\uE7BA"),
                new WorkspaceMetricCardDefinition("План", audit.InfoCount.ToString("N0", RuCulture), audit.InfoCount == 0 ? "чисто" : "дополнить", "Данные, которые улучшают аналитику", "#7180A0", "#F3F6FB", "#7180A0", "\uE9D2"),
                new WorkspaceMetricCardDefinition("Сценарии", scenarioRows.Length.ToString("N0", RuCulture), "+0%", "Карта покрытия функций", "#4F5BFF", "#EEF2FF", "#8C97B0", "\uE9CE")
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
            new RecordsGridActionDefinition("Подтвердить", () => ShowWorkflowResult("Заказы", salesWorkspace.ConfirmOrder(order.Id))),
            new RecordsGridActionDefinition("В резерв", () => ShowWorkflowResult("Заказы", salesWorkspace.ReserveOrder(order.Id))),
            new RecordsGridActionDefinition("Снять резерв", () => ShowWorkflowResult("Заказы", salesWorkspace.ReleaseOrderReserve(order.Id))),
            new RecordsGridActionDefinition("Сформировать счет", () => CreateInvoiceFromOrder(salesWorkspace, order)),
            new RecordsGridActionDefinition("Подготовить отгрузку", () => CreateShipmentFromOrder(salesWorkspace, order)),
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
            new RecordsGridActionDefinition("Выставить", () => ShowWorkflowResult("Счета", salesWorkspace.MarkInvoiceIssued(invoice.Id))),
            new RecordsGridActionDefinition("Отметить оплату", () => ShowWorkflowResult("Счета", salesWorkspace.MarkInvoicePaid(invoice.Id))),
            new RecordsGridActionDefinition("Создать отгрузку", () => CreateShipmentFromInvoice(salesWorkspace, invoice))
        ];
    }

    private static IReadOnlyList<RecordsGridActionDefinition> BuildShipmentActions(SalesWorkspace salesWorkspace, SalesShipmentRecord shipment)
    {
        return
        [
            new RecordsGridActionDefinition("Открыть отгрузку", () => EditShipment(salesWorkspace, shipment)),
            new RecordsGridActionDefinition("К сборке", () => ShowWorkflowResult("Отгрузки", salesWorkspace.PrepareShipment(shipment.Id))),
            new RecordsGridActionDefinition("Провести отгрузку", () => ShowWorkflowResult("Отгрузки", salesWorkspace.ShipShipment(shipment.Id)))
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
        salesWorkspace.AddInvoice(invoice);
        ShowMessage("Счета", $"Создан счет {invoice.Number} по заказу {order.Number}.");
    }

    private static void CreateShipmentFromOrder(SalesWorkspace salesWorkspace, SalesOrderRecord order)
    {
        var shipment = salesWorkspace.CreateShipmentDraftFromOrder(order.Id);
        salesWorkspace.AddShipment(shipment);
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
            salesWorkspace.AddOrder(dialog.ResultOrder);
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
            salesWorkspace.AddInvoice(dialog.ResultInvoice);
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
            salesWorkspace.AddShipment(dialog.ResultShipment);
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
                    salesWorkspace.AddOrder(editor.ResultOrder);
                    ShowMessage("Заказы", $"Создан заказ {editor.ResultOrder.Number}.");
                }
                else
                {
                    salesWorkspace.UpdateOrder(editor.ResultOrder);
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
                    salesWorkspace.AddInvoice(editor.ResultInvoice);
                    ShowMessage("Счета", $"Создан счет {editor.ResultInvoice.Number}.");
                }
                else
                {
                    salesWorkspace.UpdateInvoice(editor.ResultInvoice);
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
                    salesWorkspace.AddShipment(editor.ResultShipment);
                    ShowMessage("Отгрузки", $"Создана отгрузка {editor.ResultShipment.Number}.");
                }
                else
                {
                    salesWorkspace.UpdateShipment(editor.ResultShipment);
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
        if (salesWorkspace.OperationalSnapshot?.StockBalances is { Count: > 0 } balances)
        {
            return balances
                .GroupBy(item => Clean(item.ItemCode), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.Sum(item => item.FreeQuantity), StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
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
            delta,
            "к прошлому месяцу",
            accentHex,
            iconBackgroundHex,
            deltaHex,
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
            "Подтвержден" or "Подтвержденные" or "Активен" or "Активные" or "Оплачено" or "Доставлено" or "В наличии" or "Норма" => ("#1DAA63", "#EAF9F0"),
            "Выставлен" or "Частично оплачено" or "Под контролем" or "Запланировано" or "Низкий остаток" => ("#F29A17", "#FFF4E3"),
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
            "Отгружена" or "Выполнен" => "Выполненные",
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

    private static string NormalizeInvoiceFilter(SalesInvoiceRecord invoice)
    {
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

    private static string NormalizeInvoiceStatusLabel(SalesInvoiceRecord invoice)
    {
        return NormalizeInvoiceFilter(invoice);
    }

    private static decimal GetPaidAmount(SalesInvoiceRecord invoice)
    {
        return NormalizeInvoiceFilter(invoice) switch
        {
            "Оплачено" => invoice.TotalAmount,
            "Частично оплачено" => Math.Round(invoice.TotalAmount * 0.4m, 2),
            _ => 0m
        };
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

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd.MM.yyyy", RuCulture) : "—";
    }

    private static string Clean(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private static string Hex(System.Drawing.Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
