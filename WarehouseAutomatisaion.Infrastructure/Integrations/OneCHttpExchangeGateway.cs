using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Integrations;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Integrations;

public sealed class OneCHttpExchangeGateway(
    HttpClient httpClient,
    IOptionsMonitor<OneCIntegrationOptions> optionsMonitor,
    ILogger<OneCHttpExchangeGateway> logger) : IOneCExchangeGateway
{
    public async Task<OneCSyncPayload> PullWarehouseSnapshotAsync(CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (options.SimulateResponses)
        {
            var batchId = $"mock-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

            logger.LogInformation(
                "Running simulated 1C synchronization batch {BatchId}.",
                batchId);

            return new OneCSyncPayload(
                18,
                42,
                6,
                "1C",
                batchId,
                DateTimeOffset.UtcNow,
                true);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "exchange/warehouse-snapshot");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Add("X-API-Key", options.ApiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OneCWarehouseSnapshotDto>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("1C returned an empty synchronization payload.");
        }

        return new OneCSyncPayload(
            payload.ImportedProducts,
            payload.ImportedBalances,
            payload.ImportedOrders,
            "1C",
            payload.BatchId ?? $"remote-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            DateTimeOffset.UtcNow,
            false);
    }

    private sealed record OneCWarehouseSnapshotDto(
        int ImportedProducts,
        int ImportedBalances,
        int ImportedOrders,
        string? BatchId);
}
