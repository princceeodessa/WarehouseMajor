using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Integrations;

public sealed class OneCSynchronizationBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    IOptionsMonitor<OneCIntegrationOptions> optionsMonitor,
    ILogger<OneCSynchronizationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("1C synchronization background worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            var interval = TimeSpan.FromMinutes(Math.Max(1, options.SyncIntervalMinutes));

            if (options.AutoSyncEnabled)
            {
                await RunSynchronizationAsync(stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunSynchronizationAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var exchangeService = scope.ServiceProvider.GetRequiredService<IOneCExchangeService>();
            var status = await exchangeService.SynchronizeAsync(cancellationToken);

            logger.LogInformation(
                "Background 1C synchronization finished. Healthy: {Healthy}, batch: {BatchId}.",
                status.Healthy,
                status.LastBatchId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Background 1C synchronization failed.");
        }
    }
}
