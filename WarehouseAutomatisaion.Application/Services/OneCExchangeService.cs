using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Application.Abstractions.Integrations;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Responses;
using WarehouseAutomatisaion.Domain.Entities;

namespace WarehouseAutomatisaion.Application.Services;

public interface IOneCExchangeService
{
    Task<OneCSyncStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
    Task<OneCSyncStatusResponse> SynchronizeAsync(CancellationToken cancellationToken);
}

public sealed class OneCExchangeService(
    IOneCExchangeGateway exchangeGateway,
    IIntegrationCheckpointRepository checkpointRepository,
    ILogger<OneCExchangeService> logger) : IOneCExchangeService
{
    public async Task<OneCSyncStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var checkpoint = await checkpointRepository.GetAsync(cancellationToken);
        return Map(checkpoint);
    }

    public async Task<OneCSyncStatusResponse> SynchronizeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = await exchangeGateway.PullWarehouseSnapshotAsync(cancellationToken);
            var checkpoint = new IntegrationCheckpoint(
                payload.SourceSystem,
                payload.PulledAtUtc,
                true,
                payload.IsSimulated,
                payload.ImportedProducts,
                payload.ImportedBalances,
                payload.ImportedOrders,
                payload.ExternalBatchId,
                $"Warehouse snapshot batch '{payload.ExternalBatchId}' was processed.");

            await checkpointRepository.SaveAsync(checkpoint, cancellationToken);

            logger.LogInformation(
                "1C synchronization completed successfully with batch {BatchId}.",
                checkpoint.LastBatchId);

            return Map(checkpoint);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var previousCheckpoint = await checkpointRepository.GetAsync(cancellationToken);
            var failedCheckpoint = previousCheckpoint with
            {
                IsHealthy = false,
                LastMessage = $"Synchronization failed: {exception.Message}"
            };

            await checkpointRepository.SaveAsync(failedCheckpoint, cancellationToken);

            logger.LogError(exception, "1C synchronization failed.");

            return Map(failedCheckpoint);
        }
    }

    private static OneCSyncStatusResponse Map(IntegrationCheckpoint checkpoint) =>
        new(
            checkpoint.ExternalSystem,
            checkpoint.IsHealthy,
            checkpoint.LastRunWasSimulated,
            checkpoint.LastSuccessfulSyncUtc,
            DateTimeOffset.UtcNow,
            checkpoint.ImportedProducts,
            checkpoint.ImportedBalances,
            checkpoint.ImportedOrders,
            checkpoint.LastBatchId,
            checkpoint.LastMessage);
}
