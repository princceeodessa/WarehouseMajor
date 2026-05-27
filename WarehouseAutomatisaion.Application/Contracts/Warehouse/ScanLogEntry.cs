namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

// Запись в app_warehouse_operation_log / app_sales_operation_log.
// Уровень — Application, чтобы logger мог работать без MySQL-зависимостей.
public sealed record ScanLogEntry(
    Guid Id,
    string ActorUserName,
    string EntityType,
    Guid? EntityId,
    string EntityNumber,
    string ActionText,
    string ResultText,
    string MessageText);
