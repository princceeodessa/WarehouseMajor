using System.ComponentModel;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class OperationalPurchasingWorkspace
{
    private OperationalPurchasingWorkspace(
        BindingList<OperationalPurchasingSupplierRecord> suppliers,
        BindingList<OperationalPurchasingDocumentRecord> purchaseOrders,
        BindingList<OperationalPurchasingDocumentRecord> supplierInvoices,
        BindingList<OperationalPurchasingDocumentRecord> purchaseReceipts,
        BindingList<PurchasingOperationLogEntry> operationLog,
        IReadOnlyList<string> supplierStatuses,
        IReadOnlyList<string> purchaseOrderStatuses,
        IReadOnlyList<string> supplierInvoiceStatuses,
        IReadOnlyList<string> purchaseReceiptStatuses,
        IReadOnlyList<string> warehouses,
        IReadOnlyList<SalesCatalogItemOption> catalogItems)
    {
        Suppliers = suppliers;
        PurchaseOrders = purchaseOrders;
        SupplierInvoices = supplierInvoices;
        PurchaseReceipts = purchaseReceipts;
        OperationLog = operationLog;
        SupplierStatuses = supplierStatuses;
        PurchaseOrderStatuses = purchaseOrderStatuses;
        SupplierInvoiceStatuses = supplierInvoiceStatuses;
        PurchaseReceiptStatuses = purchaseReceiptStatuses;
        Warehouses = warehouses;
        CatalogItems = catalogItems;
    }

    public BindingList<OperationalPurchasingSupplierRecord> Suppliers { get; }

    public BindingList<OperationalPurchasingDocumentRecord> PurchaseOrders { get; }

    public BindingList<OperationalPurchasingDocumentRecord> SupplierInvoices { get; }

    public BindingList<OperationalPurchasingDocumentRecord> PurchaseReceipts { get; }

    public BindingList<PurchasingOperationLogEntry> OperationLog { get; }

    public IReadOnlyList<string> SupplierStatuses { get; }

    public IReadOnlyList<string> PurchaseOrderStatuses { get; }

    public IReadOnlyList<string> SupplierInvoiceStatuses { get; }

    public IReadOnlyList<string> PurchaseReceiptStatuses { get; }

    public IReadOnlyList<string> Warehouses { get; internal set; }

    public IReadOnlyList<SalesCatalogItemOption> CatalogItems { get; internal set; }

    public string CurrentOperator { get; internal set; } = string.Empty;

    public event EventHandler? Changed;

    public static OperationalPurchasingWorkspace Create(string currentOperator, SalesWorkspace salesWorkspace)
    {
        var snapshot = PurchasingWorkspace.Create(salesWorkspace);
        var catalogItems = salesWorkspace.CatalogItems.ToArray();
        var warehouses = salesWorkspace.Warehouses.ToArray();
        var workspace = CreateEmpty(currentOperator, catalogItems, warehouses);

        var supplierMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var supplier in snapshot.Suppliers)
        {
            var mapped = MapSupplier(supplier);
            workspace.Suppliers.Add(mapped);
            if (!string.IsNullOrWhiteSpace(mapped.Name))
            {
                supplierMap[mapped.Name] = mapped.Id;
            }
        }

        foreach (var order in snapshot.PurchaseOrders)
        {
            workspace.PurchaseOrders.Add(MapDocument(order, "Заказ поставщику", supplierMap));
        }

        var orderIdsByNumber = workspace.PurchaseOrders
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, item => item.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var invoice in snapshot.SupplierInvoices)
        {
            workspace.SupplierInvoices.Add(MapDocument(invoice, "Счет поставщика", supplierMap, orderIdsByNumber));
        }

        foreach (var receipt in snapshot.PurchaseReceipts)
        {
            workspace.PurchaseReceipts.Add(MapDocument(receipt, "Приемка", supplierMap, orderIdsByNumber));
        }

        if (workspace.Suppliers.Count == 0)
        {
            workspace.Suppliers.Add(new OperationalPurchasingSupplierRecord
            {
                Id = Guid.NewGuid(),
                Code = "SUP-001",
                Name = "Новый поставщик",
                Status = workspace.SupplierStatuses.First(),
                SourceLabel = "Локальный контур"
            });
        }

        return workspace;
    }

    internal static OperationalPurchasingWorkspace CreateEmpty(
        string currentOperator,
        IReadOnlyList<SalesCatalogItemOption>? catalogItems = null,
        IReadOnlyList<string>? warehouses = null)
    {
        return new OperationalPurchasingWorkspace(
            new BindingList<OperationalPurchasingSupplierRecord>(),
            new BindingList<OperationalPurchasingDocumentRecord>(),
            new BindingList<OperationalPurchasingDocumentRecord>(),
            new BindingList<OperationalPurchasingDocumentRecord>(),
            new BindingList<PurchasingOperationLogEntry>(),
            new[] { "Активен", "На проверке", "Пауза" },
            new[] { "Черновик", "Согласован", "Заказан", "Ожидается поставка", "Принят" },
            new[] { "Черновик", "Получен", "К оплате", "Оплачен" },
            new[] { "Черновик", "Принята", "Размещена" },
            (warehouses is { Count: > 0 } ? warehouses : new[] { "Главный склад", "Шоурум", "Монтажный склад" }).ToArray(),
            (catalogItems ?? Array.Empty<SalesCatalogItemOption>()).ToArray())
        {
            CurrentOperator = string.IsNullOrWhiteSpace(currentOperator) ? Environment.UserName : currentOperator
        };
    }

    public void ReplaceFrom(OperationalPurchasingWorkspace source)
    {
        ReplaceBindingList(Suppliers, source.Suppliers, item => item.Clone());
        ReplaceBindingList(PurchaseOrders, source.PurchaseOrders, item => item.Clone());
        ReplaceBindingList(SupplierInvoices, source.SupplierInvoices, item => item.Clone());
        ReplaceBindingList(PurchaseReceipts, source.PurchaseReceipts, item => item.Clone());
        ReplaceBindingList(OperationLog, source.OperationLog, item => item.Clone());
        CurrentOperator = source.CurrentOperator;
        Warehouses = source.Warehouses.ToArray();
        CatalogItems = source.CatalogItems.Select(item => item with { }).ToArray();
        OnChanged();
    }

    public OperationalPurchasingSupplierRecord CreateSupplierDraft()
    {
        return new OperationalPurchasingSupplierRecord
        {
            Id = Guid.NewGuid(),
            Code = GetNextSupplierCode(),
            Status = SupplierStatuses.First(),
            SourceLabel = "Локальный контур"
        };
    }

    public OperationalPurchasingDocumentRecord CreatePurchaseOrderDraft(Guid? supplierId = null)
    {
        var supplier = supplierId is null
            ? Suppliers.FirstOrDefault()
            : Suppliers.FirstOrDefault(item => item.Id == supplierId.Value);

        return new OperationalPurchasingDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = "Заказ поставщику",
            Number = GetNextOrderNumber(),
            DocumentDate = DateTime.Today,
            SupplierId = supplier?.Id ?? Guid.Empty,
            SupplierName = supplier?.Name ?? string.Empty,
            Status = PurchaseOrderStatuses.First(),
            Contract = supplier?.Contract ?? string.Empty,
            Warehouse = Warehouses.FirstOrDefault() ?? string.Empty,
            SourceLabel = "Локальный контур",
            Lines = new BindingList<OperationalPurchasingLineRecord>()
        };
    }

    public OperationalPurchasingDocumentRecord CreateSupplierInvoiceDraftFromOrder(Guid orderId)
    {
        var order = PurchaseOrders.First(item => item.Id == orderId);
        return new OperationalPurchasingDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = "Счет поставщика",
            Number = GetNextInvoiceNumber(),
            DocumentDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(5),
            SupplierId = order.SupplierId,
            SupplierName = order.SupplierName,
            Status = SupplierInvoiceStatuses.First(),
            Contract = order.Contract,
            Warehouse = order.Warehouse,
            RelatedOrderId = order.Id,
            RelatedOrderNumber = order.Number,
            Comment = $"Основание: заказ {order.Number}",
            SourceLabel = "Локальный контур",
            Lines = CloneLines(order.Lines)
        };
    }

    public OperationalPurchasingDocumentRecord CreateReceiptDraftFromOrder(Guid orderId)
    {
        var order = PurchaseOrders.First(item => item.Id == orderId);
        return new OperationalPurchasingDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = "Приемка",
            Number = GetNextReceiptNumber(),
            DocumentDate = DateTime.Today,
            SupplierId = order.SupplierId,
            SupplierName = order.SupplierName,
            Status = PurchaseReceiptStatuses.First(),
            Contract = order.Contract,
            Warehouse = order.Warehouse,
            RelatedOrderId = order.Id,
            RelatedOrderNumber = order.Number,
            Comment = $"Основание: заказ {order.Number}",
            SourceLabel = "Локальный контур",
            Lines = CloneLines(order.Lines)
        };
    }

    public void AddSupplier(OperationalPurchasingSupplierRecord supplier)
    {
        var copy = supplier.Clone();
        Suppliers.Add(copy);
        WriteOperationLog("Поставщик", copy.Id, copy.Code, "Создание поставщика", "Успех", $"Создан поставщик {copy.Name}.");
        OnChanged();
    }

    public void UpdateSupplier(OperationalPurchasingSupplierRecord supplier)
    {
        var existing = Suppliers.First(item => item.Id == supplier.Id);
        existing.CopyFrom(supplier);
        SyncDocumentsForSupplier(existing);
        WriteOperationLog("Поставщик", existing.Id, existing.Code, "Изменение поставщика", "Успех", $"Обновлена карточка {existing.Name}.");
        OnChanged();
    }

    public void AddPurchaseOrder(OperationalPurchasingDocumentRecord document)
    {
        AddDocument(PurchaseOrders, document, "Создание заказа поставщику");
    }

    public void UpdatePurchaseOrder(OperationalPurchasingDocumentRecord document)
    {
        UpdateDocument(PurchaseOrders, document, "Изменение заказа поставщику");
        SyncDerivedDocumentsFromOrder(document.Id);
        OnChanged();
    }

    public void AddSupplierInvoice(OperationalPurchasingDocumentRecord document)
    {
        AddDocument(SupplierInvoices, document, "Создание счета поставщика");
        RefreshOrderLifecycle(document.RelatedOrderId);
        OnChanged();
    }

    public void AddPurchaseReceipt(OperationalPurchasingDocumentRecord document)
    {
        AddDocument(PurchaseReceipts, document, "Создание приемки");
        RefreshOrderLifecycle(document.RelatedOrderId);
        OnChanged();
    }

    public void UpdateSupplierInvoice(OperationalPurchasingDocumentRecord document)
    {
        var existing = SupplierInvoices.First(item => item.Id == document.Id);
        existing.CopyFrom(document);
        RefreshOrderLifecycle(existing.RelatedOrderId);
        WriteOperationLog(existing.DocumentType, existing.Id, existing.Number, "Изменение счета поставщика", "Успех", $"Обновлен документ {existing.Number}.");
        OnChanged();
    }

    public void UpdatePurchaseReceipt(OperationalPurchasingDocumentRecord document)
    {
        var existing = PurchaseReceipts.First(item => item.Id == document.Id);
        existing.CopyFrom(document);
        RefreshOrderLifecycle(existing.RelatedOrderId);
        WriteOperationLog(existing.DocumentType, existing.Id, existing.Number, "Изменение приемки", "Успех", $"Обновлен документ {existing.Number}.");
        OnChanged();
    }

    public PurchasingWorkflowActionResult SetDocumentStatus(
        string documentType,
        Guid documentId,
        string targetStatus,
        string action,
        string detail,
        bool refreshLifecycle = true)
    {
        var document = ResolveDocument(documentType, documentId);
        if (document is null)
        {
            return CreateWorkflowResult(false, "Документ не найден.", "Операцию выполнить не удалось.");
        }

        document.Status = targetStatus;
        if (refreshLifecycle && document.DocumentType.Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase))
        {
            RefreshOrderLifecycle(document.Id);
        }
        else if (refreshLifecycle)
        {
            RefreshOrderLifecycle(document.RelatedOrderId);
        }

        WriteOperationLog(document.DocumentType, document.Id, document.Number, action, "Успех", $"{document.Number}: {detail}");
        OnChanged();
        return CreateWorkflowResult(true, $"{document.Number}: {targetStatus}.", detail);
    }

    public PurchasingWorkflowActionResult AppendDocumentComment(
        string documentType,
        Guid documentId,
        string comment,
        string action)
    {
        var document = ResolveDocument(documentType, documentId);
        if (document is null)
        {
            return CreateWorkflowResult(false, "Документ не найден.", "Комментарий не записан.");
        }

        var trimmed = comment.Trim();
        document.Comment = string.IsNullOrWhiteSpace(document.Comment)
            ? trimmed
            : $"{document.Comment}{Environment.NewLine}{trimmed}";
        WriteOperationLog(document.DocumentType, document.Id, document.Number, action, "Успех", $"{document.Number}: {trimmed}");
        OnChanged();
        return CreateWorkflowResult(true, $"Комментарий добавлен в {document.Number}.", trimmed);
    }

    public PurchasingWorkflowActionResult ApprovePurchaseOrder(Guid orderId)
    {
        var order = PurchaseOrders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return CreateWorkflowResult(false, "Заказ поставщику не найден.", "Не удалось согласовать документ.");
        }

        order.Status = "Согласован";
        WriteOperationLog("Заказ поставщику", order.Id, order.Number, "Согласование заказа", "Успех", $"Заказ {order.Number} согласован.");
        OnChanged();
        return CreateWorkflowResult(true, $"Заказ {order.Number} согласован.", "Можно размещать заказ у поставщика и формировать связанные документы.");
    }

    public PurchasingWorkflowActionResult PlacePurchaseOrder(Guid orderId)
    {
        var order = PurchaseOrders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return CreateWorkflowResult(false, "Заказ поставщику не найден.", "Не удалось разместить заказ.");
        }

        order.Status = "Заказан";
        WriteOperationLog("Заказ поставщику", order.Id, order.Number, "Размещение заказа", "Успех", $"Заказ {order.Number} размещен у поставщика.");
        OnChanged();
        return CreateWorkflowResult(true, $"Заказ {order.Number} размещен.", "Теперь по нему можно принимать входящий счет и приемку.");
    }

    public PurchasingWorkflowActionResult MarkSupplierInvoiceReceived(Guid invoiceId)
    {
        return UpdateInvoiceStatus(invoiceId, "Получен", "Получение счета", "Счет поставщика получен.");
    }

    public PurchasingWorkflowActionResult MarkSupplierInvoicePayable(Guid invoiceId)
    {
        return UpdateInvoiceStatus(invoiceId, "К оплате", "Передача к оплате", "Счет поставщика передан к оплате.");
    }

    public PurchasingWorkflowActionResult MarkSupplierInvoicePaid(Guid invoiceId)
    {
        return UpdateInvoiceStatus(invoiceId, "Оплачен", "Оплата счета", "Счет поставщика оплачен.");
    }

    public PurchasingWorkflowActionResult ReceivePurchaseReceipt(Guid receiptId)
    {
        return UpdateReceiptStatus(receiptId, "Принята", "Приемка товара", "Поставка принята на склад.");
    }

    public PurchasingWorkflowActionResult PlacePurchaseReceipt(Guid receiptId)
    {
        return UpdateReceiptStatus(receiptId, "Размещена", "Размещение приемки", "Товар размещен по складу.");
    }

    private PurchasingWorkflowActionResult UpdateInvoiceStatus(Guid invoiceId, string targetStatus, string action, string detail)
    {
        var invoice = SupplierInvoices.FirstOrDefault(item => item.Id == invoiceId);
        if (invoice is null)
        {
            return CreateWorkflowResult(false, "Счет поставщика не найден.", "Не удалось выполнить операцию.");
        }

        invoice.Status = targetStatus;
        RefreshOrderLifecycle(invoice.RelatedOrderId);
        WriteOperationLog("Счет поставщика", invoice.Id, invoice.Number, action, "Успех", $"{invoice.Number}: {detail}");
        OnChanged();
        return CreateWorkflowResult(true, $"{invoice.Number}: {targetStatus}.", detail);
    }

    private PurchasingWorkflowActionResult UpdateReceiptStatus(Guid receiptId, string targetStatus, string action, string detail)
    {
        var receipt = PurchaseReceipts.FirstOrDefault(item => item.Id == receiptId);
        if (receipt is null)
        {
            return CreateWorkflowResult(false, "Приемка не найдена.", "Не удалось выполнить операцию.");
        }

        receipt.Status = targetStatus;
        RefreshOrderLifecycle(receipt.RelatedOrderId);
        WriteOperationLog("Приемка", receipt.Id, receipt.Number, action, "Успех", $"{receipt.Number}: {detail}");
        OnChanged();
        return CreateWorkflowResult(true, $"{receipt.Number}: {targetStatus}.", detail);
    }

    private OperationalPurchasingDocumentRecord? ResolveDocument(string documentType, Guid documentId)
    {
        if (documentType.Equals("Заказ поставщику", StringComparison.OrdinalIgnoreCase))
        {
            return PurchaseOrders.FirstOrDefault(item => item.Id == documentId);
        }

        if (documentType.Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase))
        {
            return SupplierInvoices.FirstOrDefault(item => item.Id == documentId);
        }

        if (documentType.Equals("Приемка", StringComparison.OrdinalIgnoreCase))
        {
            return PurchaseReceipts.FirstOrDefault(item => item.Id == documentId);
        }

        return PurchaseOrders.FirstOrDefault(item => item.Id == documentId)
               ?? SupplierInvoices.FirstOrDefault(item => item.Id == documentId)
               ?? PurchaseReceipts.FirstOrDefault(item => item.Id == documentId);
    }

    private void AddDocument(BindingList<OperationalPurchasingDocumentRecord> target, OperationalPurchasingDocumentRecord document, string action)
    {
        var copy = document.Clone();
        target.Add(copy);
        WriteOperationLog(copy.DocumentType, copy.Id, copy.Number, action, "Успех", $"Создан документ {copy.Number}.");
        OnChanged();
    }

    private void UpdateDocument(BindingList<OperationalPurchasingDocumentRecord> target, OperationalPurchasingDocumentRecord document, string action)
    {
        var existing = target.First(item => item.Id == document.Id);
        existing.CopyFrom(document);
        WriteOperationLog(existing.DocumentType, existing.Id, existing.Number, action, "Успех", $"Обновлен документ {existing.Number}.");
        OnChanged();
    }

    private void SyncDocumentsForSupplier(OperationalPurchasingSupplierRecord supplier)
    {
        foreach (var document in PurchaseOrders.Where(item => item.SupplierId == supplier.Id)
                     .Concat(SupplierInvoices.Where(item => item.SupplierId == supplier.Id))
                     .Concat(PurchaseReceipts.Where(item => item.SupplierId == supplier.Id)))
        {
            document.SupplierName = supplier.Name;
            document.Contract = supplier.Contract;
        }
    }

    private void SyncDerivedDocumentsFromOrder(Guid orderId)
    {
        var order = PurchaseOrders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return;
        }

        foreach (var invoice in SupplierInvoices.Where(item => item.RelatedOrderId == order.Id))
        {
            invoice.SupplierId = order.SupplierId;
            invoice.SupplierName = order.SupplierName;
            invoice.Contract = order.Contract;
            invoice.Warehouse = order.Warehouse;
            invoice.RelatedOrderNumber = order.Number;
            invoice.Lines = CloneLines(order.Lines);
        }

        foreach (var receipt in PurchaseReceipts.Where(item => item.RelatedOrderId == order.Id))
        {
            receipt.SupplierId = order.SupplierId;
            receipt.SupplierName = order.SupplierName;
            receipt.Contract = order.Contract;
            receipt.Warehouse = order.Warehouse;
            receipt.RelatedOrderNumber = order.Number;
            receipt.Lines = CloneLines(order.Lines);
        }
    }

    private void RefreshOrderLifecycle(Guid relatedOrderId)
    {
        if (relatedOrderId == Guid.Empty)
        {
            return;
        }

        var order = PurchaseOrders.FirstOrDefault(item => item.Id == relatedOrderId);
        if (order is null)
        {
            return;
        }

        var receipts = PurchaseReceipts.Where(item => item.RelatedOrderId == order.Id).ToArray();
        var invoices = SupplierInvoices.Where(item => item.RelatedOrderId == order.Id).ToArray();

        if (receipts.Any(item =>
                item.Status.Equals("Принята", StringComparison.OrdinalIgnoreCase)
                || item.Status.Equals("Размещена", StringComparison.OrdinalIgnoreCase)))
        {
            order.Status = "Принят";
            return;
        }

        if (receipts.Any())
        {
            order.Status = "Ожидается поставка";
            return;
        }

        if (invoices.Any(item =>
                item.Status.Equals("Получен", StringComparison.OrdinalIgnoreCase)
                || item.Status.Equals("К оплате", StringComparison.OrdinalIgnoreCase)
                || item.Status.Equals("Оплачен", StringComparison.OrdinalIgnoreCase))
            && order.Status.Equals("Черновик", StringComparison.OrdinalIgnoreCase))
        {
            order.Status = "Согласован";
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PurchasingWorkflowActionResult CreateWorkflowResult(bool succeeded, string message, string detail)
    {
        return new PurchasingWorkflowActionResult(succeeded, message, detail);
    }

    private void WriteOperationLog(string entityType, Guid entityId, string entityNumber, string action, string result, string message)
    {
        OperationLog.Insert(0, new PurchasingOperationLogEntry
        {
            Id = Guid.NewGuid(),
            LoggedAt = DateTime.Now,
            Actor = EnsureCurrentOperator(),
            EntityType = entityType,
            EntityId = entityId,
            EntityNumber = entityNumber,
            Action = action,
            Result = result,
            Message = message
        });

        while (OperationLog.Count > 500)
        {
            OperationLog.RemoveAt(OperationLog.Count - 1);
        }
    }

    private string EnsureCurrentOperator()
    {
        if (!string.IsNullOrWhiteSpace(CurrentOperator))
        {
            return CurrentOperator;
        }

        CurrentOperator = Environment.UserName;
        return CurrentOperator;
    }

    private string GetNextSupplierCode()
    {
        var next = Suppliers
            .Select(item => ParseNumericSuffix(item.Code))
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"SUP-{next:000}";
    }

    private string GetNextOrderNumber()
    {
        var next = PurchaseOrders
            .Select(item => ParseNumericSuffix(item.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"PO-{DateTime.Today:yyMMdd}-{next:000}";
    }

    private string GetNextInvoiceNumber()
    {
        var next = SupplierInvoices
            .Select(item => ParseNumericSuffix(item.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"PINV-{DateTime.Today:yyMMdd}-{next:000}";
    }

    private string GetNextReceiptNumber()
    {
        var next = PurchaseReceipts
            .Select(item => ParseNumericSuffix(item.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"PRC-{DateTime.Today:yyMMdd}-{next:000}";
    }

    private static OperationalPurchasingSupplierRecord MapSupplier(PurchasingSupplierRecord supplier)
    {
        return new OperationalPurchasingSupplierRecord
        {
            Id = Guid.NewGuid(),
            Name = supplier.Name,
            Code = supplier.Code,
            Status = string.IsNullOrWhiteSpace(supplier.Status) ? "Активен" : supplier.Status,
            TaxId = supplier.TaxId,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Contract = supplier.Contract,
            SourceLabel = supplier.SourceLabel,
            Fields = supplier.Fields.ToArray()
        };
    }

    private static OperationalPurchasingDocumentRecord MapDocument(
        PurchasingDocumentRecord document,
        string defaultType,
        IDictionary<string, Guid> supplierMap,
        IReadOnlyDictionary<string, Guid>? orderIdsByNumber = null)
    {
        if (!supplierMap.TryGetValue(document.SupplierName, out var supplierId))
        {
            supplierId = Guid.Empty;
        }

        Guid relatedOrderId = Guid.Empty;
        if (orderIdsByNumber is not null
            && !string.IsNullOrWhiteSpace(document.RelatedDocument)
            && orderIdsByNumber.TryGetValue(document.RelatedDocument, out var orderId))
        {
            relatedOrderId = orderId;
        }

        return new OperationalPurchasingDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = string.IsNullOrWhiteSpace(document.DocumentType) ? defaultType : document.DocumentType,
            Number = document.Number,
            DocumentDate = document.Date ?? DateTime.Today,
            DueDate = document.DocumentType.Contains("Счет", StringComparison.OrdinalIgnoreCase)
                ? (document.Date ?? DateTime.Today).AddDays(5)
                : null,
            SupplierId = supplierId,
            SupplierName = document.SupplierName,
            Status = document.Status,
            Contract = document.Contract,
            Warehouse = document.Warehouse,
            RelatedOrderId = relatedOrderId,
            RelatedOrderNumber = document.RelatedDocument,
            Comment = document.Comment,
            SourceLabel = document.SourceLabel,
            Fields = document.Fields.ToArray(),
            Lines = new BindingList<OperationalPurchasingLineRecord>(document.Lines.Select(MapLine).ToList())
        };
    }

    private static OperationalPurchasingLineRecord MapLine(PurchasingDocumentLineRecord line)
    {
        return new OperationalPurchasingLineRecord
        {
            Id = Guid.NewGuid(),
            SectionName = line.SectionName,
            ItemCode = string.IsNullOrWhiteSpace(line.Item) ? string.Empty : line.Item,
            ItemName = line.Item,
            Quantity = line.Quantity,
            Unit = line.Unit,
            Price = line.Price,
            PlannedDate = line.PlannedDate,
            TargetLocation = FirstNonEmpty(
                GetFieldDisplay(line.Fields, "Ячейка"),
                GetFieldDisplay(line.Fields, "МестоХранения"),
                GetFieldDisplay(line.Fields, "Размещение"),
                GetFieldDisplay(line.Fields, "СкладскаяЯчейка")),
            RelatedDocument = line.RelatedDocument,
            Fields = line.Fields.ToArray()
        };
    }

    private static string GetFieldDisplay(IEnumerable<OneCFieldValue> fields, string fieldName)
    {
        return fields.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue
            ?? string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static BindingList<OperationalPurchasingLineRecord> CloneLines(IEnumerable<OperationalPurchasingLineRecord> lines)
    {
        return new BindingList<OperationalPurchasingLineRecord>(lines.Select(item => item.Clone()).ToList());
    }

    private static void ReplaceBindingList<T>(BindingList<T> target, IEnumerable<T> source, Func<T, T> clone)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(clone(item));
        }
    }

    private static int ParseNumericSuffix(string value)
    {
        var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }
}

public sealed class OperationalPurchasingSupplierRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string TaxId { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Contract { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; set; } = Array.Empty<OneCFieldValue>();

    public OperationalPurchasingSupplierRecord Clone()
    {
        return new OperationalPurchasingSupplierRecord
        {
            Id = Id,
            Name = Name,
            Code = Code,
            Status = Status,
            TaxId = TaxId,
            Phone = Phone,
            Email = Email,
            Contract = Contract,
            SourceLabel = SourceLabel,
            Fields = Fields.ToArray()
        };
    }

    public void CopyFrom(OperationalPurchasingSupplierRecord source)
    {
        Name = source.Name;
        Code = source.Code;
        Status = source.Status;
        TaxId = source.TaxId;
        Phone = source.Phone;
        Email = source.Email;
        Contract = source.Contract;
        SourceLabel = source.SourceLabel;
        Fields = source.Fields.ToArray();
    }
}

public sealed class OperationalPurchasingDocumentRecord
{
    public Guid Id { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public DateTime DocumentDate { get; set; }

    public DateTime? DueDate { get; set; }

    public Guid SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Contract { get; set; } = string.Empty;

    public string Warehouse { get; set; } = string.Empty;

    public Guid RelatedOrderId { get; set; }

    public string RelatedOrderNumber { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; set; } = Array.Empty<OneCFieldValue>();

    public BindingList<OperationalPurchasingLineRecord> Lines { get; set; } = new();

    public decimal TotalAmount => Lines.Sum(item => item.Amount);

    public int PositionCount => Lines.Count;

    public OperationalPurchasingDocumentRecord Clone()
    {
        return new OperationalPurchasingDocumentRecord
        {
            Id = Id,
            DocumentType = DocumentType,
            Number = Number,
            DocumentDate = DocumentDate,
            DueDate = DueDate,
            SupplierId = SupplierId,
            SupplierName = SupplierName,
            Status = Status,
            Contract = Contract,
            Warehouse = Warehouse,
            RelatedOrderId = RelatedOrderId,
            RelatedOrderNumber = RelatedOrderNumber,
            Comment = Comment,
            SourceLabel = SourceLabel,
            Fields = Fields.ToArray(),
            Lines = new BindingList<OperationalPurchasingLineRecord>(Lines.Select(item => item.Clone()).ToList())
        };
    }

    public void CopyFrom(OperationalPurchasingDocumentRecord source)
    {
        DocumentType = source.DocumentType;
        Number = source.Number;
        DocumentDate = source.DocumentDate;
        DueDate = source.DueDate;
        SupplierId = source.SupplierId;
        SupplierName = source.SupplierName;
        Status = source.Status;
        Contract = source.Contract;
        Warehouse = source.Warehouse;
        RelatedOrderId = source.RelatedOrderId;
        RelatedOrderNumber = source.RelatedOrderNumber;
        Comment = source.Comment;
        SourceLabel = source.SourceLabel;
        Fields = source.Fields.ToArray();
        Lines = new BindingList<OperationalPurchasingLineRecord>(source.Lines.Select(item => item.Clone()).ToList());
    }
}

public sealed class OperationalPurchasingLineRecord
{
    public Guid Id { get; set; }

    public string SectionName { get; set; } = string.Empty;

    public string ItemCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public DateTime? PlannedDate { get; set; }

    public string TargetLocation { get; set; } = string.Empty;

    public string RelatedDocument { get; set; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; set; } = Array.Empty<OneCFieldValue>();

    public decimal Amount => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);

    public OperationalPurchasingLineRecord Clone()
    {
        return new OperationalPurchasingLineRecord
        {
            Id = Id,
            SectionName = SectionName,
            ItemCode = ItemCode,
            ItemName = ItemName,
            Quantity = Quantity,
            Unit = Unit,
            Price = Price,
            PlannedDate = PlannedDate,
            TargetLocation = TargetLocation,
            RelatedDocument = RelatedDocument,
            Fields = Fields.ToArray()
        };
    }
}

public sealed class PurchasingOperationLogEntry
{
    public Guid Id { get; set; }

    public DateTime LoggedAt { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string EntityNumber { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public PurchasingOperationLogEntry Clone()
    {
        return new PurchasingOperationLogEntry
        {
            Id = Id,
            LoggedAt = LoggedAt,
            Actor = Actor,
            EntityType = EntityType,
            EntityId = EntityId,
            EntityNumber = EntityNumber,
            Action = Action,
            Result = Result,
            Message = Message
        };
    }
}

public sealed record PurchasingWorkflowActionResult(bool Succeeded, string Message, string Detail);
