using System.ComponentModel;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class OperationalWarehouseWorkspace
{
    private OperationalWarehouseWorkspace(
        BindingList<OperationalWarehouseDocumentRecord> transferOrders,
        BindingList<OperationalWarehouseDocumentRecord> inventoryCounts,
        BindingList<OperationalWarehouseDocumentRecord> writeOffs,
        BindingList<WarehouseOperationLogEntry> operationLog,
        IReadOnlyList<string> transferStatuses,
        IReadOnlyList<string> inventoryStatuses,
        IReadOnlyList<string> writeOffStatuses,
        IReadOnlyList<string> warehouses,
        IReadOnlyList<SalesCatalogItemOption> catalogItems)
    {
        TransferOrders = transferOrders;
        InventoryCounts = inventoryCounts;
        WriteOffs = writeOffs;
        OperationLog = operationLog;
        TransferStatuses = transferStatuses;
        InventoryStatuses = inventoryStatuses;
        WriteOffStatuses = writeOffStatuses;
        Warehouses = warehouses;
        CatalogItems = catalogItems;
    }

    public BindingList<OperationalWarehouseDocumentRecord> TransferOrders { get; }

    public BindingList<OperationalWarehouseDocumentRecord> InventoryCounts { get; }

    public BindingList<OperationalWarehouseDocumentRecord> WriteOffs { get; }

    public BindingList<WarehouseOperationLogEntry> OperationLog { get; }

    public IReadOnlyList<string> TransferStatuses { get; }

    public IReadOnlyList<string> InventoryStatuses { get; }

    public IReadOnlyList<string> WriteOffStatuses { get; }

    public IReadOnlyList<string> Warehouses { get; internal set; }

    public IReadOnlyList<SalesCatalogItemOption> CatalogItems { get; internal set; }

    public string CurrentOperator { get; internal set; } = string.Empty;

    public event EventHandler? Changed;

    public static OperationalWarehouseWorkspace Create(string currentOperator, SalesWorkspace salesWorkspace)
    {
        var imported = WarehouseWorkspace.Create(salesWorkspace);
        var workspace = CreateEmpty(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);

        foreach (var transfer in imported.TransferOrders)
        {
            workspace.TransferOrders.Add(MapImportedDocument(
                transfer,
                "Перемещение",
                NormalizeTransferStatus(transfer.Status),
                workspace.CatalogItems));
        }

        foreach (var inventory in imported.InventoryCounts)
        {
            workspace.InventoryCounts.Add(MapImportedDocument(
                inventory,
                "Инвентаризация",
                NormalizeInventoryStatus(inventory.Status),
                workspace.CatalogItems));
        }

        foreach (var writeOff in imported.WriteOffs)
        {
            workspace.WriteOffs.Add(MapImportedDocument(
                writeOff,
                "Списание",
                NormalizeWriteOffStatus(writeOff.Status),
                workspace.CatalogItems));
        }

        return workspace;
    }

    internal static OperationalWarehouseWorkspace CreateEmpty(
        string currentOperator,
        IReadOnlyList<SalesCatalogItemOption>? catalogItems = null,
        IReadOnlyList<string>? warehouses = null)
    {
        return new OperationalWarehouseWorkspace(
            new BindingList<OperationalWarehouseDocumentRecord>(),
            new BindingList<OperationalWarehouseDocumentRecord>(),
            new BindingList<OperationalWarehouseDocumentRecord>(),
            new BindingList<WarehouseOperationLogEntry>(),
            new[] { "Черновик", "К перемещению", "Перемещен" },
            new[] { "Черновик", "Проведена" },
            new[] { "Черновик", "Списано" },
            (warehouses is { Count: > 0 } ? warehouses : new[] { "Главный склад", "Шоурум", "Монтажный склад" }).ToArray(),
            (catalogItems ?? Array.Empty<SalesCatalogItemOption>()).ToArray())
        {
            CurrentOperator = string.IsNullOrWhiteSpace(currentOperator) ? Environment.UserName : currentOperator
        };
    }

    public void ReplaceFrom(OperationalWarehouseWorkspace source)
    {
        ReplaceBindingList(TransferOrders, source.TransferOrders, item => item.Clone());
        ReplaceBindingList(InventoryCounts, source.InventoryCounts, item => item.Clone());
        ReplaceBindingList(WriteOffs, source.WriteOffs, item => item.Clone());
        ReplaceBindingList(OperationLog, source.OperationLog, item => item.Clone());
        CurrentOperator = source.CurrentOperator;
        Warehouses = source.Warehouses.ToArray();
        CatalogItems = source.CatalogItems.Select(item => item with { }).ToArray();
        OnChanged();
    }

    public void RefreshReferenceData(SalesWorkspace salesWorkspace)
    {
        CurrentOperator = string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator)
            ? CurrentOperator
            : salesWorkspace.CurrentOperator;
        Warehouses = salesWorkspace.Warehouses.ToArray();
        CatalogItems = salesWorkspace.CatalogItems.Select(item => item with { }).ToArray();
    }

    public OperationalWarehouseDocumentRecord CreateTransferDraft(string? sourceWarehouse = null)
    {
        var source = NormalizeWarehouse(sourceWarehouse);
        var target = Warehouses.FirstOrDefault(item => !item.Equals(source, StringComparison.OrdinalIgnoreCase)) ?? source;
        return new OperationalWarehouseDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = "Перемещение",
            Number = GetNextNumber("TRF", TransferOrders),
            DocumentDate = DateTime.Today,
            SourceWarehouse = source,
            TargetWarehouse = target,
            Status = TransferStatuses.First(),
            SourceLabel = "Локальный контур",
            Lines = new BindingList<OperationalWarehouseLineRecord>()
        };
    }

    public OperationalWarehouseDocumentRecord CreateInventoryDraft(string? warehouse = null)
    {
        return new OperationalWarehouseDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = "Инвентаризация",
            Number = GetNextNumber("INV", InventoryCounts),
            DocumentDate = DateTime.Today,
            SourceWarehouse = NormalizeWarehouse(warehouse),
            Status = InventoryStatuses.First(),
            SourceLabel = "Локальный контур",
            Lines = new BindingList<OperationalWarehouseLineRecord>()
        };
    }

    public OperationalWarehouseDocumentRecord CreateWriteOffDraft(string? warehouse = null)
    {
        return new OperationalWarehouseDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = "Списание",
            Number = GetNextNumber("WOF", WriteOffs),
            DocumentDate = DateTime.Today,
            SourceWarehouse = NormalizeWarehouse(warehouse),
            Status = WriteOffStatuses.First(),
            SourceLabel = "Локальный контур",
            Lines = new BindingList<OperationalWarehouseLineRecord>()
        };
    }

    public void AddTransferOrder(OperationalWarehouseDocumentRecord document)
    {
        AddDocument(TransferOrders, document, "Создание перемещения");
    }

    public void UpdateTransferOrder(OperationalWarehouseDocumentRecord document)
    {
        UpdateDocument(TransferOrders, document, "Изменение перемещения");
    }

    public WarehouseWorkflowActionResult MarkTransferReady(Guid documentId)
    {
        return UpdateStatus(
            TransferOrders,
            documentId,
            TransferStatuses[1],
            "Подготовка перемещения",
            "Документ переведен в статус перемещения.");
    }

    public WarehouseWorkflowActionResult CompleteTransfer(Guid documentId)
    {
        var document = TransferOrders.First(item => item.Id == documentId);
        if (document.Lines.Count == 0)
        {
            return CreateWorkflowResult(false, "Перемещение не завершено.", "Добавьте хотя бы одну позицию.");
        }

        if (string.IsNullOrWhiteSpace(document.SourceWarehouse) || string.IsNullOrWhiteSpace(document.TargetWarehouse))
        {
            return CreateWorkflowResult(false, "Перемещение не завершено.", "Укажите склад-источник и склад-получатель.");
        }

        if (document.SourceWarehouse.Equals(document.TargetWarehouse, StringComparison.OrdinalIgnoreCase))
        {
            return CreateWorkflowResult(false, "Перемещение не завершено.", "Склад-источник и склад-получатель должны отличаться.");
        }

        return UpdateStatus(
            TransferOrders,
            documentId,
            TransferStatuses[2],
            "Завершение перемещения",
            $"Перемещение {document.Number} выполнено.");
    }

    public void AddInventoryCount(OperationalWarehouseDocumentRecord document)
    {
        AddDocument(InventoryCounts, document, "Создание инвентаризации");
    }

    public void UpdateInventoryCount(OperationalWarehouseDocumentRecord document)
    {
        UpdateDocument(InventoryCounts, document, "Изменение инвентаризации");
    }

    public WarehouseWorkflowActionResult PostInventoryCount(Guid documentId)
    {
        var document = InventoryCounts.First(item => item.Id == documentId);
        if (document.Lines.Count == 0)
        {
            return CreateWorkflowResult(false, "Инвентаризация не проведена.", "Добавьте строки корректировки.");
        }

        if (string.IsNullOrWhiteSpace(document.SourceWarehouse))
        {
            return CreateWorkflowResult(false, "Инвентаризация не проведена.", "Укажите склад.");
        }

        return UpdateStatus(
            InventoryCounts,
            documentId,
            InventoryStatuses[1],
            "Проведение инвентаризации",
            $"Инвентаризация {document.Number} проведена.");
    }

    public void AddWriteOff(OperationalWarehouseDocumentRecord document)
    {
        AddDocument(WriteOffs, document, "Создание списания");
    }

    public void UpdateWriteOff(OperationalWarehouseDocumentRecord document)
    {
        UpdateDocument(WriteOffs, document, "Изменение списания");
    }

    public WarehouseWorkflowActionResult PostWriteOff(Guid documentId)
    {
        var document = WriteOffs.First(item => item.Id == documentId);
        if (document.Lines.Count == 0)
        {
            return CreateWorkflowResult(false, "Списание не проведено.", "Добавьте хотя бы одну позицию.");
        }

        if (string.IsNullOrWhiteSpace(document.SourceWarehouse))
        {
            return CreateWorkflowResult(false, "Списание не проведено.", "Укажите склад списания.");
        }

        return UpdateStatus(
            WriteOffs,
            documentId,
            WriteOffStatuses[1],
            "Проведение списания",
            $"Списание {document.Number} проведено.");
    }

    private void AddDocument(
        BindingList<OperationalWarehouseDocumentRecord> target,
        OperationalWarehouseDocumentRecord document,
        string action)
    {
        var copy = document.Clone();
        target.Add(copy);
        WriteOperationLog(copy.DocumentType, copy.Id, copy.Number, action, "Успех", $"{copy.DocumentType} {copy.Number} сохранен.");
        OnChanged();
    }

    private void UpdateDocument(
        BindingList<OperationalWarehouseDocumentRecord> target,
        OperationalWarehouseDocumentRecord document,
        string action)
    {
        var existing = target.First(item => item.Id == document.Id);
        existing.CopyFrom(document);
        WriteOperationLog(existing.DocumentType, existing.Id, existing.Number, action, "Успех", $"{existing.DocumentType} {existing.Number} обновлен.");
        OnChanged();
    }

    private WarehouseWorkflowActionResult UpdateStatus(
        BindingList<OperationalWarehouseDocumentRecord> target,
        Guid documentId,
        string targetStatus,
        string action,
        string detail)
    {
        var document = target.First(item => item.Id == documentId);
        if (document.Status.Equals(targetStatus, StringComparison.OrdinalIgnoreCase))
        {
            return CreateWorkflowResult(true, $"Статус уже '{targetStatus}'.", detail);
        }

        document.Status = targetStatus;
        WriteOperationLog(document.DocumentType, document.Id, document.Number, action, "Успех", detail);
        OnChanged();
        return CreateWorkflowResult(true, $"Статус изменен: {targetStatus}.", detail);
    }

    private void WriteOperationLog(
        string entityType,
        Guid entityId,
        string entityNumber,
        string action,
        string result,
        string message)
    {
        OperationLog.Insert(0, new WarehouseOperationLogEntry
        {
            Id = Guid.NewGuid(),
            LoggedAt = DateTime.Now,
            Actor = string.IsNullOrWhiteSpace(CurrentOperator) ? Environment.UserName : CurrentOperator,
            EntityType = entityType,
            EntityId = entityId,
            EntityNumber = entityNumber,
            Action = action,
            Result = result,
            Message = message
        });
    }

    private WarehouseWorkflowActionResult CreateWorkflowResult(bool succeeded, string message, string detail)
    {
        return new WarehouseWorkflowActionResult(succeeded, message, detail);
    }

    private string GetNextNumber(string prefix, IEnumerable<OperationalWarehouseDocumentRecord> documents)
    {
        var next = documents
            .Select(item => item.Number)
            .Where(number => number.StartsWith($"{prefix}-", StringComparison.OrdinalIgnoreCase))
            .Select(number => number[(prefix.Length + 1)..])
            .Select(number => int.TryParse(number, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{prefix}-{next:0000}";
    }

    private string NormalizeWarehouse(string? warehouse)
    {
        if (!string.IsNullOrWhiteSpace(warehouse))
        {
            return warehouse;
        }

        return Warehouses.FirstOrDefault() ?? "Главный склад";
    }

    private static OperationalWarehouseDocumentRecord MapImportedDocument(
        WarehouseDocumentRecord document,
        string documentType,
        string normalizedStatus,
        IReadOnlyList<SalesCatalogItemOption> catalogItems)
    {
        return new OperationalWarehouseDocumentRecord
        {
            Id = Guid.NewGuid(),
            DocumentType = documentType,
            Number = string.IsNullOrWhiteSpace(document.Number) ? Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() : document.Number,
            DocumentDate = document.Date ?? DateTime.Today,
            Status = normalizedStatus,
            SourceWarehouse = document.SourceWarehouse,
            TargetWarehouse = document.TargetWarehouse,
            RelatedDocument = document.RelatedDocument,
            Comment = document.Comment,
            SourceLabel = string.IsNullOrWhiteSpace(document.SourceLabel) ? "Миграция 1С" : document.SourceLabel,
            Fields = document.Fields.ToArray(),
            Lines = new BindingList<OperationalWarehouseLineRecord>(document.Lines.Select(line => MapImportedLine(line, catalogItems)).ToList())
        };
    }

    private static OperationalWarehouseLineRecord MapImportedLine(
        WarehouseDocumentLineRecord line,
        IReadOnlyList<SalesCatalogItemOption> catalogItems)
    {
        var matchedCatalogItem = catalogItems.FirstOrDefault(item =>
            item.Code.Equals(line.Item, StringComparison.OrdinalIgnoreCase)
            || item.Name.Equals(line.Item, StringComparison.OrdinalIgnoreCase));

        return new OperationalWarehouseLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = matchedCatalogItem?.Code ?? string.Empty,
            ItemName = string.IsNullOrWhiteSpace(line.Item) ? matchedCatalogItem?.Name ?? string.Empty : line.Item,
            Quantity = line.Quantity,
            Unit = string.IsNullOrWhiteSpace(line.Unit) ? matchedCatalogItem?.Unit ?? string.Empty : line.Unit,
            SourceLocation = line.SourceLocation,
            TargetLocation = line.TargetLocation,
            RelatedDocument = line.RelatedDocument,
            Fields = line.Fields.ToArray()
        };
    }

    private static string NormalizeTransferStatus(string status)
    {
        if (ContainsAny(status, "перемещ", "заверш", "выполн"))
        {
            return "Перемещен";
        }

        if (ContainsAny(status, "план", "в резерве", "в работе", "сбор"))
        {
            return "К перемещению";
        }

        return "Черновик";
    }

    private static string NormalizeInventoryStatus(string status)
    {
        return ContainsAny(status, "провед", "заверш", "исполн")
            ? "Проведена"
            : "Черновик";
    }

    private static string NormalizeWriteOffStatus(string status)
    {
        return ContainsAny(status, "спис", "провед", "закрыт")
            ? "Списано"
            : "Черновик";
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReplaceBindingList<T>(
        BindingList<T> target,
        IEnumerable<T> source,
        Func<T, T> clone)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(clone(item));
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class OperationalWarehouseDocumentRecord
{
    public Guid Id { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public DateTime DocumentDate { get; set; }

    public string Status { get; set; } = string.Empty;

    public string SourceWarehouse { get; set; } = string.Empty;

    public string TargetWarehouse { get; set; } = string.Empty;

    public string RelatedDocument { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; set; } = Array.Empty<OneCFieldValue>();

    public BindingList<OperationalWarehouseLineRecord> Lines { get; set; } = new();

    public decimal TotalQuantity => Lines.Sum(item => item.Quantity);

    public int PositionCount => Lines.Count;

    public OperationalWarehouseDocumentRecord Clone()
    {
        return new OperationalWarehouseDocumentRecord
        {
            Id = Id,
            DocumentType = DocumentType,
            Number = Number,
            DocumentDate = DocumentDate,
            Status = Status,
            SourceWarehouse = SourceWarehouse,
            TargetWarehouse = TargetWarehouse,
            RelatedDocument = RelatedDocument,
            Comment = Comment,
            SourceLabel = SourceLabel,
            Fields = Fields.ToArray(),
            Lines = new BindingList<OperationalWarehouseLineRecord>(Lines.Select(item => item.Clone()).ToList())
        };
    }

    public void CopyFrom(OperationalWarehouseDocumentRecord source)
    {
        DocumentType = source.DocumentType;
        Number = source.Number;
        DocumentDate = source.DocumentDate;
        Status = source.Status;
        SourceWarehouse = source.SourceWarehouse;
        TargetWarehouse = source.TargetWarehouse;
        RelatedDocument = source.RelatedDocument;
        Comment = source.Comment;
        SourceLabel = source.SourceLabel;
        Fields = source.Fields.ToArray();
        Lines = new BindingList<OperationalWarehouseLineRecord>(source.Lines.Select(item => item.Clone()).ToList());
    }
}

public sealed class OperationalWarehouseLineRecord
{
    public Guid Id { get; set; }

    public string ItemCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = string.Empty;

    public string SourceLocation { get; set; } = string.Empty;

    public string TargetLocation { get; set; } = string.Empty;

    public string RelatedDocument { get; set; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; set; } = Array.Empty<OneCFieldValue>();

    public OperationalWarehouseLineRecord Clone()
    {
        return new OperationalWarehouseLineRecord
        {
            Id = Id,
            ItemCode = ItemCode,
            ItemName = ItemName,
            Quantity = Quantity,
            Unit = Unit,
            SourceLocation = SourceLocation,
            TargetLocation = TargetLocation,
            RelatedDocument = RelatedDocument,
            Fields = Fields.ToArray()
        };
    }
}

public sealed class WarehouseOperationLogEntry
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

    public WarehouseOperationLogEntry Clone()
    {
        return new WarehouseOperationLogEntry
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

public sealed record WarehouseWorkflowActionResult(bool Succeeded, string Message, string Detail);
