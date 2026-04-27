namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class SalesInventoryService
{
    private readonly SalesWorkspace _workspace;
    private readonly IReadOnlyList<SalesStockBalanceSeed> _baseline;

    public SalesInventoryService(SalesWorkspace workspace)
    {
        _workspace = workspace;
        _baseline = CreateBaseline();
    }

    public SalesInventoryCheck AnalyzeOrder(SalesOrderRecord order)
    {
        return AnalyzeDraft(order.Warehouse, order.Lines, order.Id, null);
    }

    public SalesInventoryCheck AnalyzeShipment(SalesShipmentRecord shipment)
    {
        return AnalyzeDraft(shipment.Warehouse, shipment.Lines, shipment.SalesOrderId, shipment.Id);
    }

    public IReadOnlyList<SalesWarehouseStockSnapshot> GetStockSnapshot()
    {
        var committedShipments = BuildCommittedShipmentUsage(null);
        var reservedOrders = BuildReservedOrderUsage(null, committedShipments);

        return _baseline
            .GroupBy(item => new { item.ItemCode, item.ItemName, item.Warehouse, item.Unit })
            .Select(group =>
            {
                var key = (group.Key.ItemCode, group.Key.Warehouse);
                var baselineQuantity = group.Sum(item => item.AvailableQuantity);
                var reservedQuantity = reservedOrders.TryGetValue(key, out var reserved) ? reserved : 0m;
                var shippedQuantity = committedShipments.TryGetValue(key, out var shipped) ? shipped : 0m;
                var freeQuantity = Math.Max(0m, baselineQuantity - reservedQuantity - shippedQuantity);

                return new SalesWarehouseStockSnapshot(
                    group.Key.ItemCode,
                    group.Key.ItemName,
                    group.Key.Warehouse,
                    group.Key.Unit,
                    baselineQuantity,
                    reservedQuantity,
                    shippedQuantity,
                    freeQuantity);
            })
            .OrderBy(item => item.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public SalesInventoryCheck AnalyzeDraft(
        string warehouse,
        IEnumerable<SalesOrderLineRecord> lines,
        Guid? ignoreOrderId = null,
        Guid? ignoreShipmentId = null)
    {
        var committedShipments = BuildCommittedShipmentUsage(ignoreShipmentId);
        var reservedOrders = BuildReservedOrderUsage(ignoreOrderId, committedShipments);

        var requestedLines = lines
            .GroupBy(line => line.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new SalesRequestedLine(
                    first.ItemCode,
                    first.ItemName,
                    first.Unit,
                    group.Sum(line => line.Quantity));
            })
            .OrderBy(item => item.ItemName)
            .ToList();

        var resultLines = new List<SalesInventoryLineStatus>();
        foreach (var line in requestedLines)
        {
            var available = GetAvailableAtWarehouse(line.ItemCode, warehouse, reservedOrders, committedShipments);
            var shortage = Math.Max(0m, line.RequiredQuantity - available);
            var alternatives = _baseline
                .Where(item => item.ItemCode.Equals(line.ItemCode, StringComparison.OrdinalIgnoreCase)
                    && !item.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase))
                .Select(item => new SalesInventoryAlternative(
                    item.Warehouse,
                    GetAvailableAtWarehouse(line.ItemCode, item.Warehouse, reservedOrders, committedShipments)))
                .Where(item => item.AvailableQuantity > 0)
                .OrderByDescending(item => item.AvailableQuantity)
                .ToList();

            resultLines.Add(new SalesInventoryLineStatus(
                line.ItemCode,
                line.ItemName,
                line.Unit,
                warehouse,
                line.RequiredQuantity,
                available,
                shortage,
                shortage <= 0 ? "В наличии" : "Нужен перенос / закупка",
                alternatives));
        }

        var shortageCount = resultLines.Count(item => item.ShortageQuantity > 0);
        var totalRequested = resultLines.Sum(item => item.RequiredQuantity);
        var totalCovered = resultLines.Sum(item => Math.Min(item.RequiredQuantity, item.AvailableQuantity));
        var statusText = shortageCount == 0
            ? "Все позиции покрыты остатком выбранного склада."
            : $"Не хватает по {shortageCount} позициям.";
        var hintText = shortageCount == 0
            ? $"Можно резервировать {totalCovered:N2} ед. и проводить дальнейшие документы."
            : $"Покрыто {totalCovered:N2} из {totalRequested:N2} ед. Проверьте перенос между складами или закупку.";

        return new SalesInventoryCheck(
            shortageCount == 0,
            statusText,
            hintText,
            resultLines);
    }

    private Dictionary<(string ItemCode, string Warehouse), decimal> BuildCommittedShipmentUsage(Guid? ignoreShipmentId)
    {
        var map = new Dictionary<(string ItemCode, string Warehouse), decimal>();

        foreach (var shipment in _workspace.Shipments)
        {
            if (ignoreShipmentId is not null && shipment.Id == ignoreShipmentId.Value)
            {
                continue;
            }

            if (shipment.Status.Equals("Черновик", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var line in shipment.Lines)
            {
                var key = (line.ItemCode, shipment.Warehouse);
                map[key] = map.TryGetValue(key, out var current) ? current + line.Quantity : line.Quantity;
            }
        }

        return map;
    }

    private Dictionary<(string ItemCode, string Warehouse), decimal> BuildReservedOrderUsage(
        Guid? ignoreOrderId,
        IReadOnlyDictionary<(string ItemCode, string Warehouse), decimal> committedShipments)
    {
        _ = committedShipments;
        var ordersWithCommittedShipment = _workspace.Shipments
            .Where(item => !item.Status.Equals("Черновик", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.SalesOrderId)
            .ToHashSet();

        var map = new Dictionary<(string ItemCode, string Warehouse), decimal>();
        foreach (var order in _workspace.Orders)
        {
            if (ignoreOrderId is not null && order.Id == ignoreOrderId.Value)
            {
                continue;
            }

            if (!order.Status.Equals("В резерве", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ordersWithCommittedShipment.Contains(order.Id))
            {
                continue;
            }

            foreach (var line in order.Lines)
            {
                var key = (line.ItemCode, order.Warehouse);
                map[key] = map.TryGetValue(key, out var current) ? current + line.Quantity : line.Quantity;
            }
        }

        return map;
    }

    private decimal GetAvailableAtWarehouse(
        string itemCode,
        string warehouse,
        IReadOnlyDictionary<(string ItemCode, string Warehouse), decimal> reservedOrders,
        IReadOnlyDictionary<(string ItemCode, string Warehouse), decimal> committedShipments)
    {
        var baseline = _baseline
            .Where(item => item.ItemCode.Equals(itemCode, StringComparison.OrdinalIgnoreCase)
                && item.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.AvailableQuantity)
            .DefaultIfEmpty(0m)
            .Sum();

        var orderReserve = reservedOrders.TryGetValue((itemCode, warehouse), out var reserved) ? reserved : 0m;
        var shipmentUsage = committedShipments.TryGetValue((itemCode, warehouse), out var shipped) ? shipped : 0m;
        return Math.Max(0m, baseline - orderReserve - shipmentUsage);
    }

    private static IReadOnlyList<SalesStockBalanceSeed> CreateBaseline()
    {
        var baseline = new List<SalesStockBalanceSeed>
        {
            new("ALTEZA-P50-BL", "ALTEZA профиль P-50 гардина черный мат", "Главный склад", "м", 320m),
            new("LUM-CLAMP-50", "Профиль LumFer Clamp Level 50", "Главный склад", "м", 72m),
            new("SCREEN-30", "Экран световой SCREEN 30 белый", "Шоурум", "м", 48m),
            new("GX53-BASE", "Платформа GX-53 белая", "Монтажный склад", "шт", 100m),
            new("KLEM-2X", "Клеммы 2-контактные", "Главный склад", "шт", 1500m)
        };

        var purchasingWorkspace = PurchasingOperationalWorkspaceStore.CreateDefault()
            .TryLoadExisting(Environment.UserName);
        if (purchasingWorkspace is not null)
        {

        foreach (var receipt in purchasingWorkspace.PurchaseReceipts.Where(item =>
                     item.Status.Equals("Принята", StringComparison.OrdinalIgnoreCase)
                     || item.Status.Equals("Размещена", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in receipt.Lines)
            {
                baseline.Add(new SalesStockBalanceSeed(
                    string.IsNullOrWhiteSpace(line.ItemCode) ? line.ItemName : line.ItemCode,
                    line.ItemName,
                    string.IsNullOrWhiteSpace(receipt.Warehouse) ? "Главный склад" : receipt.Warehouse,
                    line.Unit,
                    line.Quantity));
            }
        }
        }

        var warehouseWorkspace = WarehouseOperationalWorkspaceStore.CreateDefault()
            .TryLoadExisting(Environment.UserName);
        if (warehouseWorkspace is not null)
        {
            foreach (var transfer in warehouseWorkspace.TransferOrders.Where(item =>
                         item.Status.Equals("Перемещен", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var line in transfer.Lines.Where(item => !string.IsNullOrWhiteSpace(item.ItemCode) && item.Quantity != 0m))
                {
                    var unit = string.IsNullOrWhiteSpace(line.Unit) ? "шт" : line.Unit;
                    if (!string.IsNullOrWhiteSpace(transfer.SourceWarehouse))
                    {
                        baseline.Add(new SalesStockBalanceSeed(
                            line.ItemCode,
                            line.ItemName,
                            transfer.SourceWarehouse,
                            unit,
                            -line.Quantity));
                    }

                    if (!string.IsNullOrWhiteSpace(transfer.TargetWarehouse))
                    {
                        baseline.Add(new SalesStockBalanceSeed(
                            line.ItemCode,
                            line.ItemName,
                            transfer.TargetWarehouse,
                            unit,
                            line.Quantity));
                    }
                }
            }

            foreach (var inventory in warehouseWorkspace.InventoryCounts.Where(item =>
                         item.Status.Equals("Проведена", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var line in inventory.Lines.Where(item => !string.IsNullOrWhiteSpace(item.ItemCode) && item.Quantity != 0m))
                {
                    baseline.Add(new SalesStockBalanceSeed(
                        line.ItemCode,
                        line.ItemName,
                        string.IsNullOrWhiteSpace(inventory.SourceWarehouse) ? "Главный склад" : inventory.SourceWarehouse,
                        string.IsNullOrWhiteSpace(line.Unit) ? "шт" : line.Unit,
                        line.Quantity));
                }
            }

            foreach (var writeOff in warehouseWorkspace.WriteOffs.Where(item =>
                         item.Status.Equals("Списано", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var line in writeOff.Lines.Where(item => !string.IsNullOrWhiteSpace(item.ItemCode) && item.Quantity != 0m))
                {
                    baseline.Add(new SalesStockBalanceSeed(
                        line.ItemCode,
                        line.ItemName,
                        string.IsNullOrWhiteSpace(writeOff.SourceWarehouse) ? "Главный склад" : writeOff.SourceWarehouse,
                        string.IsNullOrWhiteSpace(line.Unit) ? "шт" : line.Unit,
                        -line.Quantity));
                }
            }
        }

        return baseline;
    }
}

public sealed record SalesInventoryCheck(
    bool IsFullyCovered,
    string StatusText,
    string HintText,
    IReadOnlyList<SalesInventoryLineStatus> Lines);

public sealed record SalesInventoryLineStatus(
    string ItemCode,
    string ItemName,
    string Unit,
    string Warehouse,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    decimal ShortageQuantity,
    string Status,
    IReadOnlyList<SalesInventoryAlternative> Alternatives);

public sealed record SalesInventoryAlternative(string Warehouse, decimal AvailableQuantity);

public sealed record SalesWarehouseStockSnapshot(
    string ItemCode,
    string ItemName,
    string Warehouse,
    string Unit,
    decimal BaselineQuantity,
    decimal ReservedQuantity,
    decimal ShippedQuantity,
    decimal FreeQuantity);

public sealed record SalesStockBalanceSeed(
    string ItemCode,
    string ItemName,
    string Warehouse,
    string Unit,
    decimal AvailableQuantity);

file sealed record SalesRequestedLine(string ItemCode, string ItemName, string Unit, decimal RequiredQuantity);
