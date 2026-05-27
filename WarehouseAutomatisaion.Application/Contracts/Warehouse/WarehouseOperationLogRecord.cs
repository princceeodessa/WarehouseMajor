namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

public sealed record WarehouseOperationLogRecord(
    Guid Id,
    DateTime LoggedAt,
    string ActorUserName,
    string EntityType,
    Guid? EntityId,
    string EntityNumber,
    string ActionText,
    string ResultText,
    string MessageText);
