namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class AuditWorkspaceSnapshot
{
    public IReadOnlyList<AuditWorkspaceEntry> Entries { get; init; } = Array.Empty<AuditWorkspaceEntry>();

    public int SalesCount { get; init; }

    public int PurchasingCount { get; init; }

    public int WarehouseCount { get; init; }

    public int TotalCount => Entries.Count;

    public static AuditWorkspaceSnapshot Create(SalesWorkspace salesWorkspace)
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        var backplaneEntries = backplane?.TryLoadAuditEvents();
        if (backplaneEntries is { Count: > 0 })
        {
            return CreateFromEntries(backplaneEntries.Select(item => new AuditWorkspaceEntry(
                item.LoggedAtLocal,
                item.ModuleCaption,
                item.Actor,
                item.EntityType,
                item.EntityNumber,
                item.Action,
                item.Result,
                item.Message)));
        }

        var entries = new List<AuditWorkspaceEntry>();

        entries.AddRange(salesWorkspace.OperationLog.Select(item => new AuditWorkspaceEntry(
            item.LoggedAt,
            "Продажи",
            item.Actor,
            item.EntityType,
            item.EntityNumber,
            item.Action,
            item.Result,
            item.Message)));

        var currentOperator = string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator)
            ? Environment.UserName
            : salesWorkspace.CurrentOperator;

        var purchasingWorkspace = PurchasingOperationalWorkspaceStore.CreateDefault()
            .TryLoadExisting(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
        if (purchasingWorkspace is not null)
        {
            entries.AddRange(purchasingWorkspace.OperationLog.Select(item => new AuditWorkspaceEntry(
                item.LoggedAt,
                "Закупки",
                item.Actor,
                item.EntityType,
                item.EntityNumber,
                item.Action,
                item.Result,
                item.Message)));
        }

        var warehouseWorkspace = WarehouseOperationalWorkspaceStore.CreateDefault()
            .TryLoadExisting(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
        if (warehouseWorkspace is not null)
        {
            entries.AddRange(warehouseWorkspace.OperationLog.Select(item => new AuditWorkspaceEntry(
                item.LoggedAt,
                "Склад",
                item.Actor,
                item.EntityType,
                item.EntityNumber,
                item.Action,
                item.Result,
                item.Message)));
        }

        return CreateFromEntries(entries);
    }

    private static AuditWorkspaceSnapshot CreateFromEntries(IEnumerable<AuditWorkspaceEntry> entries)
    {
        var ordered = entries
            .OrderByDescending(item => item.LoggedAt)
            .ThenBy(item => item.Module, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AuditWorkspaceSnapshot
        {
            Entries = ordered,
            SalesCount = ordered.Count(item => IsModule(item.Module, "Продажи", "Sales")),
            PurchasingCount = ordered.Count(item => IsModule(item.Module, "Закупки", "Purchasing")),
            WarehouseCount = ordered.Count(item => IsModule(item.Module, "Склад", "Warehouse"))
        };
    }

    private static bool IsModule(string value, params string[] names)
    {
        return names.Any(name => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record AuditWorkspaceEntry(
    DateTime LoggedAt,
    string Module,
    string Actor,
    string EntityType,
    string EntityNumber,
    string Action,
    string Result,
    string Message);
