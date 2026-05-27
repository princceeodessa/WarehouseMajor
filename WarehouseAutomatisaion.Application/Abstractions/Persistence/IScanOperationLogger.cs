using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Запись в app_warehouse_operation_log (TSD scan events, sales completions, и т.д.).
// Tsd сейчас пишет напрямую — поднимаем в общий контракт чтобы Desktop при
// необходимости использовал то же место (один формат лога — одна точка анализа).
public interface IScanOperationLogger
{
    Task WriteAsync(ScanLogEntry entry, CancellationToken cancellationToken);
}
