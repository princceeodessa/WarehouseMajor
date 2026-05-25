using WarehouseAutomatisaion.Application.Contracts.Receiving;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Sprint 5: запись черновика приёмочного документа (распознанная AI накладная)
// в app_warehouse_documents + app_warehouse_document_lines.
//
// Возвращает Id созданного документа. Идемпотентность — клиент сам решает
// (например через unique_constraint на (source_label + invoice_number)).
public interface IReceiptDraftWriter
{
    Task<Guid> CreateDraftAsync(ReceiptDraft draft, CancellationToken cancellationToken = default);
}
