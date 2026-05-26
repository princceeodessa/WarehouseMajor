using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Receiving;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 8: async wrapper над sync backplane API для чтения AI-черновиков.
// Pattern идентичен MySqlStockLocationRepository, MySqlStorageCellCatalog и др.
public sealed class MySqlReceiptDraftReader : IReceiptDraftReader
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public MySqlReceiptDraftReader(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
    }

    public Task<IReadOnlyList<ReceiptDraftSummary>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var drafts = _backplane.LoadReceiptDrafts(statusFilter: "draft");
        return Task.FromResult(drafts);
    }

    public Task<ReceiptDraftDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var detail = _backplane.LoadReceiptDraftDetail(id);
        return Task.FromResult(detail);
    }

    public Task MarkReceivedAsync(
        Guid draftId,
        Guid receivingCellId,
        string receivingCellCode,
        int linesReceived,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backplane.MarkReceiptDraftReceived(draftId, receivingCellId, receivingCellCode, linesReceived);
        return Task.CompletedTask;
    }
}
