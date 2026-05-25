using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Receiving;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 5 Task 16: wrapper для IReceiptDraftWriter.
// Делегирует BackplaneService.CreateReceiptDraft() в Task для async API.
// Не использует Task.Run (запись короткая, ~50-200 мс) — синхронный путь
// на ThreadPool через Task.FromResult после блокирующего вызова.
public sealed class MySqlReceiptDraftWriter : IReceiptDraftWriter
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public MySqlReceiptDraftWriter(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane;
    }

    public Task<Guid> CreateDraftAsync(ReceiptDraft draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = _backplane.CreateReceiptDraft(draft);
        return Task.FromResult(id);
    }
}
