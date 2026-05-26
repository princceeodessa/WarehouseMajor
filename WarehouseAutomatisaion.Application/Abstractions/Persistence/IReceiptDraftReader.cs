using WarehouseAutomatisaion.Application.Contracts.Receiving;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Sprint 8: чтение AI-черновиков для UI «Черновики приёмок».
// Отделён от IReceiptDraftWriter потому что read-only path сильно отличается
// (агрегаты, JOIN на строки, парсинг fields_json).
public interface IReceiptDraftReader
{
    /// <summary>Список черновиков с агрегатами (lines count, total qty).</summary>
    Task<IReadOnlyList<ReceiptDraftSummary>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Детализация одного черновика (header + строки).</summary>
    Task<ReceiptDraftDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Помечает черновик как принятый — меняет status_text + добавляет в fields_json
    /// id ячейки приёма и timestamp применения. После этого черновик не появится в списке drafts.</summary>
    Task MarkReceivedAsync(
        Guid draftId,
        Guid receivingCellId,
        string receivingCellCode,
        int linesReceived,
        CancellationToken cancellationToken = default);
}
