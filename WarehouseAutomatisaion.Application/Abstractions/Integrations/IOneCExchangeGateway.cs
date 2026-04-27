namespace WarehouseAutomatisaion.Application.Abstractions.Integrations;

public interface IOneCExchangeGateway
{
    Task<OneCSyncPayload> PullWarehouseSnapshotAsync(CancellationToken cancellationToken);
}

public sealed record OneCSyncPayload(
    int ImportedProducts,
    int ImportedBalances,
    int ImportedOrders,
    string SourceSystem,
    string ExternalBatchId,
    DateTimeOffset PulledAtUtc,
    bool IsSimulated);
