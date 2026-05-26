using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 5 learning loop: wrapper IInvoiceMatchOverrideStore через BackplaneService.
public sealed class MySqlInvoiceMatchOverrideStore : IInvoiceMatchOverrideStore
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public MySqlInvoiceMatchOverrideStore(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane;
    }

    public Task<NomenclatureRef?> FindOverrideAsync(
        string recognizedText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = IInvoiceMatchOverrideStore.NormalizeText(recognizedText);
        return Task.FromResult(_backplane.FindInvoiceMatchOverride(normalized));
    }

    public Task SaveOverrideAsync(
        string recognizedText,
        NomenclatureRef matchedItem,
        string actor,
        string? supplierName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = IInvoiceMatchOverrideStore.NormalizeText(recognizedText);
        _backplane.SaveInvoiceMatchOverride(recognizedText, normalized, matchedItem, actor, supplierName);
        return Task.CompletedTask;
    }
}
