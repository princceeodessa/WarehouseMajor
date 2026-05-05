using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Data;

public static class DataIntegrityAuditor
{
    public static DataIntegrityAuditResult Audit(
        SalesWorkspace salesWorkspace,
        CatalogWorkspace? catalogWorkspace,
        OperationalPurchasingWorkspace? purchasingWorkspace)
    {
        var issues = new List<DataIntegrityIssue>();

        AuditSalesWorkspace(salesWorkspace, issues);
        if (catalogWorkspace is not null)
        {
            AuditCatalogWorkspace(catalogWorkspace, issues);
        }

        if (purchasingWorkspace is not null)
        {
            AuditPurchasingWorkspace(purchasingWorkspace, issues);
        }

        return new DataIntegrityAuditResult(
            issues
                .OrderBy(item => item.SeverityRank)
                .ThenBy(item => item.Module, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ObjectType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ObjectNumber, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AuditSalesWorkspace(SalesWorkspace workspace, ICollection<DataIntegrityIssue> issues)
    {
        AddDuplicateIssues(
            issues,
            workspace.Customers,
            item => item.Code,
            item => item.Name,
            "Клиенты",
            "Код клиента",
            "Дублируется код клиента",
            "Оставьте один основной код или разведите карточки по разным кодам.");

        AddDuplicateIssues(
            issues,
            workspace.Orders,
            item => item.Number,
            item => item.CustomerName,
            "Заказы",
            "Номер заказа",
            "Дублируется номер заказа",
            "Проверьте источник данных и оставьте один документ с этим номером.");

        AddDuplicateIssues(
            issues,
            workspace.Invoices,
            item => item.Number,
            item => item.CustomerName,
            "Счета",
            "Номер счета",
            "Дублируется номер счета",
            "Проверьте источник данных и оставьте один счет с этим номером.");

        AddDuplicateIssues(
            issues,
            workspace.Shipments,
            item => item.Number,
            item => item.CustomerName,
            "Отгрузки",
            "Номер отгрузки",
            "Дублируется номер отгрузки",
            "Проверьте источник данных и оставьте одну отгрузку с этим номером.");

        var customerIds = workspace.Customers
            .Where(item => item.Id != Guid.Empty)
            .Select(item => item.Id)
            .ToHashSet();
        var customerCodes = BuildLookup(workspace.Customers.Select(item => item.Code));
        var customerNames = BuildLookup(workspace.Customers.Select(item => item.Name));

        var orderIds = workspace.Orders
            .Where(item => item.Id != Guid.Empty)
            .Select(item => item.Id)
            .ToHashSet();
        var orderNumbers = BuildLookup(workspace.Orders.Select(item => item.Number));

        foreach (var order in workspace.Orders)
        {
            if (!HasCustomerLink(order.CustomerId, order.CustomerCode, order.CustomerName, customerIds, customerCodes, customerNames))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Заказы",
                    "Заказ",
                    SafeNumber(order.Number),
                    "Заказ не связан с клиентом",
                    $"Клиент в документе: {SafeValue(order.CustomerName)}.",
                    "Выберите существующего клиента в карточке заказа или создайте недостающую карточку клиента.",
                    order.OrderDate));
            }

            AuditSalesLines(
                issues,
                "Заказы",
                "Заказ",
                SafeNumber(order.Number),
                order.OrderDate,
                order.Lines);
        }

        foreach (var invoice in workspace.Invoices)
        {
            if (!HasOrderLink(invoice.SalesOrderId, invoice.SalesOrderNumber, orderIds, orderNumbers))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Счета",
                    "Счет",
                    SafeNumber(invoice.Number),
                    "Счет не связан с заказом",
                    $"Заказ-основание: {SafeValue(invoice.SalesOrderNumber)}.",
                    "Привяжите счет к существующему заказу, иначе нельзя надежно собрать цепочку заказ -> счет -> отгрузка.",
                    invoice.InvoiceDate));
            }

            if (!HasCustomerLink(invoice.CustomerId, invoice.CustomerCode, invoice.CustomerName, customerIds, customerCodes, customerNames))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Счета",
                    "Счет",
                    SafeNumber(invoice.Number),
                    "Счет не связан с клиентом",
                    $"Клиент в документе: {SafeValue(invoice.CustomerName)}.",
                    "Выберите существующего клиента в карточке счета.",
                    invoice.InvoiceDate));
            }

            if (invoice.DueDate.Date < invoice.InvoiceDate.Date)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    "Счета",
                    "Счет",
                    SafeNumber(invoice.Number),
                    "Срок оплаты раньше даты счета",
                    $"Дата счета: {invoice.InvoiceDate:dd.MM.yyyy}, срок оплаты: {invoice.DueDate:dd.MM.yyyy}.",
                    "Исправьте дату оплаты, чтобы просрочка считалась корректно.",
                    invoice.InvoiceDate));
            }

            AuditSalesLines(
                issues,
                "Счета",
                "Счет",
                SafeNumber(invoice.Number),
                invoice.InvoiceDate,
                invoice.Lines);
        }

        foreach (var shipment in workspace.Shipments)
        {
            if (!HasOrderLink(shipment.SalesOrderId, shipment.SalesOrderNumber, orderIds, orderNumbers))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Отгрузки",
                    "Отгрузка",
                    SafeNumber(shipment.Number),
                    "Отгрузка не связана с заказом",
                    $"Заказ-основание: {SafeValue(shipment.SalesOrderNumber)}.",
                    "Привяжите отгрузку к существующему заказу, иначе складское движение нельзя проверить по заказу.",
                    shipment.ShipmentDate));
            }

            if (!HasCustomerLink(shipment.CustomerId, shipment.CustomerCode, shipment.CustomerName, customerIds, customerCodes, customerNames))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Отгрузки",
                    "Отгрузка",
                    SafeNumber(shipment.Number),
                    "Отгрузка не связана с клиентом",
                    $"Клиент в документе: {SafeValue(shipment.CustomerName)}.",
                    "Выберите существующего клиента в карточке отгрузки.",
                    shipment.ShipmentDate));
            }

            AuditSalesLines(
                issues,
                "Отгрузки",
                "Отгрузка",
                SafeNumber(shipment.Number),
                shipment.ShipmentDate,
                shipment.Lines);
        }

        foreach (var returnDocument in workspace.Returns)
        {
            if (!HasOrderLink(returnDocument.SalesOrderId, returnDocument.SalesOrderNumber, orderIds, orderNumbers))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Возвраты",
                    "Возврат",
                    SafeNumber(returnDocument.Number),
                    "Возврат не связан с заказом",
                    $"Заказ-основание: {SafeValue(returnDocument.SalesOrderNumber)}.",
                    "Привяжите возврат к существующему заказу, иначе нельзя надежно собрать цепочку заказа и проверить корректность возврата.",
                    returnDocument.ReturnDate));
            }

            if (!HasCustomerLink(returnDocument.CustomerId, returnDocument.CustomerCode, returnDocument.CustomerName, customerIds, customerCodes, customerNames))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Возвраты",
                    "Возврат",
                    SafeNumber(returnDocument.Number),
                    "Возврат не связан с клиентом",
                    $"Клиент в документе: {SafeValue(returnDocument.CustomerName)}.",
                    "Выберите существующего клиента в документе возврата.",
                    returnDocument.ReturnDate));
            }

            AuditSalesLines(
                issues,
                "Возвраты",
                "Возврат",
                SafeNumber(returnDocument.Number),
                returnDocument.ReturnDate,
                returnDocument.Lines);
        }

        foreach (var cashReceipt in workspace.CashReceipts)
        {
            if (!HasOrderLink(cashReceipt.SalesOrderId, cashReceipt.SalesOrderNumber, orderIds, orderNumbers))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Деньги",
                    "Поступление в кассу",
                    SafeNumber(cashReceipt.Number),
                    "Поступление в кассу не связано с заказом",
                    $"Заказ-основание: {SafeValue(cashReceipt.SalesOrderNumber)}.",
                    "Привяжите поступление к заказу, чтобы оплата попадала в цепочку документов и сумму оплаты счета.",
                    cashReceipt.ReceiptDate));
            }

            if (!HasCustomerLink(cashReceipt.CustomerId, cashReceipt.CustomerCode, cashReceipt.CustomerName, customerIds, customerCodes, customerNames))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Деньги",
                    "Поступление в кассу",
                    SafeNumber(cashReceipt.Number),
                    "Поступление в кассу не связано с клиентом",
                    $"Клиент в документе: {SafeValue(cashReceipt.CustomerName)}.",
                    "Выберите существующего клиента в поступлении или пересоздайте оплату из заказа.",
                    cashReceipt.ReceiptDate));
            }

            if (cashReceipt.Amount <= 0m)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    "Деньги",
                    "Поступление в кассу",
                    SafeNumber(cashReceipt.Number),
                    "Поступление в кассу с нулевой или отрицательной суммой",
                    $"Сумма: {cashReceipt.Amount:N2} {SafeValue(cashReceipt.CurrencyCode)}.",
                    "Исправьте сумму оплаты или удалите ошибочное поступление.",
                    cashReceipt.ReceiptDate));
            }
        }
    }

    private static void AuditCatalogWorkspace(CatalogWorkspace workspace, ICollection<DataIntegrityIssue> issues)
    {
        AddDuplicateIssues(
            issues,
            workspace.Items,
            item => item.Code,
            item => item.Name,
            "Товары",
            "Артикул",
            "Дублируется артикул товара",
            "Выберите основную карточку товара и объедините остатки, цены и историю вручную.");

        foreach (var item in workspace.Items)
        {
            if (string.IsNullOrWhiteSpace(Clean(item.Code)))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Товары",
                    "Номенклатура",
                    SafeValue(item.Name),
                    "У товара не заполнен артикул",
                    "Без артикула нельзя надежно сопоставлять товар с заказами, закупками и остатками.",
                    "Заполните уникальный артикул в карточке товара."));
            }

            if (string.IsNullOrWhiteSpace(Clean(item.Name)))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Critical,
                    "Товары",
                    "Номенклатура",
                    SafeValue(item.Code),
                    "У товара не заполнено наименование",
                    "Пользователь не сможет отличить карточку в подборе и документах.",
                    "Заполните наименование товара."));
            }

            if (item.DefaultPrice <= 0m)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    "Товары",
                    "Номенклатура",
                    SafeNumber(item.Code),
                    "У товара нулевая или отрицательная цена",
                    $"Товар: {SafeValue(item.Name)}. Цена: {item.DefaultPrice:N2} {SafeValue(item.CurrencyCode)}.",
                    "Заполните базовую цену или отдельно подтвердите правило для бесплатной позиции."));
            }

            if (string.IsNullOrWhiteSpace(Clean(item.Supplier)))
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Info,
                    "Товары",
                    "Номенклатура",
                    SafeNumber(item.Code),
                    "У товара не указан поставщик",
                    $"Товар: {SafeValue(item.Name)}.",
                    "Заполните поставщика, если товар участвует в закупках и пополнении склада."));
            }
        }

        foreach (var document in workspace.PriceRegistrations)
        {
            if (document.Lines.Count == 0)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    "Товары",
                    "Установка цен",
                    SafeNumber(document.Number),
                    "Документ изменения цен без строк",
                    "Проведение такого документа не изменит ни одной цены.",
                    "Добавьте строки цен или удалите пустой документ.",
                    document.DocumentDate));
            }
        }
    }

    private static void AuditPurchasingWorkspace(OperationalPurchasingWorkspace workspace, ICollection<DataIntegrityIssue> issues)
    {
        AddDuplicateIssues(
            issues,
            workspace.Suppliers,
            item => item.Code,
            item => item.Name,
            "Закупки",
            "Код поставщика",
            "Дублируется код поставщика",
            "Разведите поставщиков по разным кодам или объедините дубли вручную.");

        AddDuplicateIssues(
            issues,
            workspace.Suppliers,
            item => item.Name,
            item => item.Code,
            "Закупки",
            "Поставщик",
            "Дублируется название поставщика",
            "Проверьте, один это поставщик или разные юридические лица.");

        var supplierIds = workspace.Suppliers
            .Where(item => item.Id != Guid.Empty)
            .Select(item => item.Id)
            .ToHashSet();
        var supplierNames = BuildLookup(workspace.Suppliers.Select(item => item.Name));

        var purchaseOrderIds = workspace.PurchaseOrders
            .Where(item => item.Id != Guid.Empty)
            .Select(item => item.Id)
            .ToHashSet();
        var purchaseOrderNumbers = BuildLookup(workspace.PurchaseOrders.Select(item => item.Number));

        foreach (var document in workspace.PurchaseOrders)
        {
            AuditPurchasingDocument(issues, document, supplierIds, supplierNames, null, null, requireRelatedOrder: false);
        }

        foreach (var document in workspace.SupplierInvoices)
        {
            AuditPurchasingDocument(issues, document, supplierIds, supplierNames, purchaseOrderIds, purchaseOrderNumbers, requireRelatedOrder: true);
        }

        foreach (var document in workspace.PurchaseReceipts)
        {
            AuditPurchasingDocument(issues, document, supplierIds, supplierNames, purchaseOrderIds, purchaseOrderNumbers, requireRelatedOrder: true);
        }
    }

    private static void AuditPurchasingDocument(
        ICollection<DataIntegrityIssue> issues,
        OperationalPurchasingDocumentRecord document,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<string> supplierNames,
        IReadOnlySet<Guid>? purchaseOrderIds,
        IReadOnlySet<string>? purchaseOrderNumbers,
        bool requireRelatedOrder)
    {
        if (!HasNamedLink(document.SupplierId, document.SupplierName, supplierIds, supplierNames))
        {
            issues.Add(new DataIntegrityIssue(
                DataIntegritySeverity.Critical,
                "Закупки",
                SafeValue(document.DocumentType),
                SafeNumber(document.Number),
                "Документ закупки не связан с поставщиком",
                $"Поставщик в документе: {SafeValue(document.SupplierName)}.",
                "Выберите существующего поставщика в документе закупки.",
                document.DocumentDate));
        }

        if (requireRelatedOrder
            && purchaseOrderIds is not null
            && purchaseOrderNumbers is not null
            && !HasOrderLink(document.RelatedOrderId, document.RelatedOrderNumber, purchaseOrderIds, purchaseOrderNumbers))
        {
            issues.Add(new DataIntegrityIssue(
                DataIntegritySeverity.Warning,
                "Закупки",
                SafeValue(document.DocumentType),
                SafeNumber(document.Number),
                "Документ закупки не связан с заказом поставщику",
                $"Заказ-основание: {SafeValue(document.RelatedOrderNumber)}.",
                "Привяжите счет или приемку к заказу поставщику, если документ создан по заказу.",
                document.DocumentDate));
        }

        if (document.Lines.Count == 0)
        {
            issues.Add(new DataIntegrityIssue(
                DataIntegritySeverity.Warning,
                "Закупки",
                SafeValue(document.DocumentType),
                SafeNumber(document.Number),
                "Документ закупки без строк",
                "Такой документ не формирует сумму и не влияет на поступление товара.",
                "Добавьте строки или удалите пустой документ.",
                document.DocumentDate));
            return;
        }

        foreach (var line in document.Lines)
        {
            var lineNumber = string.IsNullOrWhiteSpace(Clean(line.ItemCode))
                ? SafeValue(line.ItemName)
                : SafeValue(line.ItemCode);

            if (line.Quantity <= 0m)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    "Закупки",
                    SafeValue(document.DocumentType),
                    SafeNumber(document.Number),
                    "В строке закупки нулевое или отрицательное количество",
                    $"Позиция: {lineNumber}. Количество: {line.Quantity:N2}.",
                    "Исправьте количество в строке документа.",
                    document.DocumentDate));
            }

            if (line.Price < 0m)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    "Закупки",
                    SafeValue(document.DocumentType),
                    SafeNumber(document.Number),
                    "В строке закупки отрицательная цена",
                    $"Позиция: {lineNumber}. Цена: {line.Price:N2}.",
                    "Исправьте цену в строке документа.",
                    document.DocumentDate));
            }
        }
    }

    private static void AuditSalesLines(
        ICollection<DataIntegrityIssue> issues,
        string module,
        string objectType,
        string objectNumber,
        DateTime date,
        IEnumerable<SalesOrderLineRecord> lines)
    {
        var lineArray = lines.ToArray();
        if (lineArray.Length == 0)
        {
            issues.Add(new DataIntegrityIssue(
                DataIntegritySeverity.Warning,
                module,
                objectType,
                objectNumber,
                "Документ без строк",
                "Документ не формирует сумму и не может быть полноценно проведен.",
                "Добавьте строки документа или удалите пустой документ.",
                date));
            return;
        }

        foreach (var line in lineArray)
        {
            var lineNumber = string.IsNullOrWhiteSpace(Clean(line.ItemCode))
                ? SafeValue(line.ItemName)
                : SafeValue(line.ItemCode);

            if (line.Quantity <= 0m)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    module,
                    objectType,
                    objectNumber,
                    "В строке документа нулевое или отрицательное количество",
                    $"Позиция: {lineNumber}. Количество: {line.Quantity:N2}.",
                    "Исправьте количество в строке документа.",
                    date));
            }

            if (line.Price < 0m)
            {
                issues.Add(new DataIntegrityIssue(
                    DataIntegritySeverity.Warning,
                    module,
                    objectType,
                    objectNumber,
                    "В строке документа отрицательная цена",
                    $"Позиция: {lineNumber}. Цена: {line.Price:N2}.",
                    "Исправьте цену в строке документа.",
                    date));
            }
        }
    }

    private static void AddDuplicateIssues<T>(
        ICollection<DataIntegrityIssue> issues,
        IEnumerable<T> source,
        Func<T, string> keySelector,
        Func<T, string> descriptionSelector,
        string module,
        string objectType,
        string problem,
        string recommendation)
    {
        foreach (var group in source
                     .Select(item => new
                     {
                         Key = Clean(keySelector(item)),
                         Description = SafeValue(descriptionSelector(item))
                     })
                     .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                     .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var examples = string.Join(", ", group.Select(item => item.Description).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
            issues.Add(new DataIntegrityIssue(
                DataIntegritySeverity.Critical,
                module,
                objectType,
                group.Key,
                problem,
                $"Найдено записей: {group.Count():N0}. Примеры: {examples}.",
                recommendation));
        }
    }

    private static bool HasCustomerLink(
        Guid id,
        string code,
        string name,
        IReadOnlySet<Guid> ids,
        IReadOnlySet<string> codes,
        IReadOnlySet<string> names)
    {
        return HasNamedLink(id, code, ids, codes) || HasNamedLink(Guid.Empty, name, ids, names);
    }

    private static bool HasOrderLink(
        Guid id,
        string number,
        IReadOnlySet<Guid> ids,
        IReadOnlySet<string> numbers)
    {
        return HasNamedLink(id, number, ids, numbers);
    }

    private static bool HasNamedLink(
        Guid id,
        string name,
        IReadOnlySet<Guid> ids,
        IReadOnlySet<string> names)
    {
        if (id != Guid.Empty && ids.Contains(id))
        {
            return true;
        }

        var cleanName = Clean(name);
        return !string.IsNullOrWhiteSpace(cleanName) && names.Contains(cleanName);
    }

    private static IReadOnlySet<string> BuildLookup(IEnumerable<string> values)
    {
        return values
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string SafeNumber(string? value)
    {
        var clean = Clean(value);
        return string.IsNullOrWhiteSpace(clean) ? "без номера" : clean;
    }

    private static string SafeValue(string? value)
    {
        var clean = Clean(value);
        return string.IsNullOrWhiteSpace(clean) ? "не заполнено" : clean;
    }

    private static string Clean(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }
}

public sealed record DataIntegrityAuditResult(IReadOnlyList<DataIntegrityIssue> Issues)
{
    public int CriticalCount => Issues.Count(item => item.Severity == DataIntegritySeverity.Critical);

    public int WarningCount => Issues.Count(item => item.Severity == DataIntegritySeverity.Warning);

    public int InfoCount => Issues.Count(item => item.Severity == DataIntegritySeverity.Info);
}

public sealed record DataIntegrityIssue(
    DataIntegritySeverity Severity,
    string Module,
    string ObjectType,
    string ObjectNumber,
    string Problem,
    string Details,
    string Recommendation,
    DateTime? Date = null)
{
    public int SeverityRank => Severity switch
    {
        DataIntegritySeverity.Critical => 0,
        DataIntegritySeverity.Warning => 1,
        _ => 2
    };

    public string Priority => Severity switch
    {
        DataIntegritySeverity.Critical => "Критично",
        DataIntegritySeverity.Warning => "Важно",
        _ => "План"
    };

    public string Status => Severity switch
    {
        DataIntegritySeverity.Critical => "Требует решения",
        DataIntegritySeverity.Warning => "Нужна проверка",
        _ => "Наблюдение"
    };
}

public enum DataIntegritySeverity
{
    Critical,
    Warning,
    Info
}
