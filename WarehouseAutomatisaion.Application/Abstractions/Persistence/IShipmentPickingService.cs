using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Сборка расходных накладных (TSD picking flow).
// Хотя по форме это «service», транзакционная логика completion живёт в SQL
// (SELECT FOR UPDATE → UPDATE статусов → INSERT в operation_log в одной TX),
// поэтому интерфейс размещён в Persistence — это transactional repository.
//
// Реализация — Infrastructure/Persistence/MySqlShipmentPickingService.
public interface IShipmentPickingService
{
    /// <summary>Активные задания на сборку (kind='shipment', статус не Отгружена/Отменена).</summary>
    Task<IReadOnlyList<ShipmentPickingDocumentSummary>> GetDocumentsAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Детали задания с построчным прогрессом сборки.</summary>
    Task<ShipmentPickingDocumentDetails?> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    /// <summary>Завершить сборку (атомарно): UPDATE shipment.status → 'Готова к отгрузке',
    /// UPDATE связанных ТСД-документов → 'Готово ТСД', INSERT в sales/warehouse operation_log.
    /// Если RemainingQuantity > 0 — откат с информативным сообщением.</summary>
    Task<ShipmentPickingCompletionResult> CompleteDocumentAsync(
        Guid documentId,
        string worker,
        CancellationToken cancellationToken);
}
