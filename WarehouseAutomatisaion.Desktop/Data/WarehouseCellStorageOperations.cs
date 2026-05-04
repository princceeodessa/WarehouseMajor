using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Data;

public static class WarehouseCellStorageOperations
{
    public const string UnassignedCellName = "Ячейка не указана";

    public static WarehouseCellStorageSnapshot Build(
        SalesWorkspace salesWorkspace,
        WarehouseWorkspace warehouseView,
        OperationalWarehouseWorkspace warehouseWorkspace,
        OperationalPurchasingWorkspace? purchasingWorkspace,
        DateTime workDate)
    {
        var cellBalances = BuildCellBalances(warehouseView, warehouseWorkspace, purchasingWorkspace)
            .Where(item => item.Quantity > 0m)
            .OrderBy(item => item.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Cell, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dueShipments = salesWorkspace.Shipments
            .Where(item => IsShipmentDueForPicking(item, workDate.Date))
            .OrderBy(item => item.ShipmentDate.Date)
            .ThenBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pickLines = dueShipments
            .SelectMany(shipment => BuildPickLines(shipment, warehouseView.StockBalances, cellBalances))
            .ToArray();

        var shipmentRows = dueShipments
            .Select(shipment => BuildShipmentRecord(shipment, pickLines.Where(line => line.ShipmentId == shipment.Id)))
            .OrderBy(item => item.ReadinessWeight)
            .ThenBy(item => item.ShipmentDate)
            .ThenBy(item => item.ShipmentNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WarehouseCellStorageSnapshot
        {
            TodayShipments = shipmentRows,
            PickLines = pickLines,
            CellBalances = cellBalances,
            TodayShipmentCount = shipmentRows.Length,
            ReadyShipmentCount = shipmentRows.Count(item => item.IsStockCovered && item.IsCellCovered),
            ShortShipmentCount = shipmentRows.Count(item => !item.IsStockCovered),
            MissingCellLineCount = pickLines.Count(item => item.IsStockCovered && !item.IsCellCovered)
        };
    }

    private static IEnumerable<WarehouseCellBalanceRecord> BuildCellBalances(
        WarehouseWorkspace warehouseView,
        OperationalWarehouseWorkspace warehouseWorkspace,
        OperationalPurchasingWorkspace? purchasingWorkspace)
    {
        var balances = new Dictionary<string, MutableCellBalance>(StringComparer.OrdinalIgnoreCase);

        void Add(
            string itemCode,
            string itemName,
            string warehouse,
            string cell,
            string unit,
            decimal delta,
            string source,
            bool isAddressed)
        {
            if (delta == 0m || string.IsNullOrWhiteSpace(itemCode) && string.IsNullOrWhiteSpace(itemName))
            {
                return;
            }

            var normalizedWarehouse = Clean(warehouse);
            var normalizedCell = string.IsNullOrWhiteSpace(cell) ? UnassignedCellName : Clean(cell);
            var key = BuildBalanceKey(itemCode, itemName, normalizedWarehouse, normalizedCell, isAddressed);
            if (!balances.TryGetValue(key, out var balance))
            {
                balance = new MutableCellBalance
                {
                    ItemCode = Clean(itemCode),
                    ItemName = Clean(itemName),
                    Warehouse = string.IsNullOrWhiteSpace(normalizedWarehouse) ? "Главный склад" : normalizedWarehouse,
                    Cell = normalizedCell,
                    Unit = Clean(unit),
                    IsAddressed = isAddressed
                };
                balances[key] = balance;
            }

            balance.Quantity += delta;
            if (!string.IsNullOrWhiteSpace(source) && balance.Sources.All(item => !item.Equals(source, StringComparison.OrdinalIgnoreCase)))
            {
                balance.Sources.Add(source);
            }
        }

        foreach (var receipt in purchasingWorkspace?.PurchaseReceipts.AsEnumerable() ?? Enumerable.Empty<OperationalPurchasingDocumentRecord>())
        {
            if (IsDraftLike(receipt.Status))
            {
                continue;
            }

            foreach (var line in receipt.Lines)
            {
                var cell = Clean(line.TargetLocation);
                if (string.IsNullOrWhiteSpace(cell) || IsWarehouseName(cell, receipt.Warehouse, warehouseView))
                {
                    continue;
                }

                Add(
                    line.ItemCode,
                    line.ItemName,
                    receipt.Warehouse,
                    cell,
                    line.Unit,
                    line.Quantity,
                    $"Приемка {Clean(receipt.Number)}",
                    isAddressed: true);
            }
        }

        foreach (var transfer in warehouseWorkspace.TransferOrders)
        {
            if (IsDraftLike(transfer.Status))
            {
                continue;
            }

            foreach (var line in transfer.Lines)
            {
                var sourceCell = Clean(line.SourceLocation);
                if (!string.IsNullOrWhiteSpace(sourceCell) && !IsWarehouseName(sourceCell, transfer.SourceWarehouse, warehouseView))
                {
                    Add(
                        line.ItemCode,
                        line.ItemName,
                        transfer.SourceWarehouse,
                        sourceCell,
                        line.Unit,
                        -line.Quantity,
                        $"Перемещение {Clean(transfer.Number)}",
                        isAddressed: true);
                }

                var targetCell = Clean(line.TargetLocation);
                if (!string.IsNullOrWhiteSpace(targetCell) && !IsWarehouseName(targetCell, transfer.TargetWarehouse, warehouseView))
                {
                    Add(
                        line.ItemCode,
                        line.ItemName,
                        string.IsNullOrWhiteSpace(transfer.TargetWarehouse) ? transfer.SourceWarehouse : transfer.TargetWarehouse,
                        targetCell,
                        line.Unit,
                        line.Quantity,
                        $"Перемещение {Clean(transfer.Number)}",
                        isAddressed: true);
                }
            }
        }

        foreach (var inventory in warehouseWorkspace.InventoryCounts)
        {
            if (IsDraftLike(inventory.Status))
            {
                continue;
            }

            foreach (var line in inventory.Lines)
            {
                var cell = FirstNonEmpty(line.TargetLocation, line.SourceLocation);
                if (string.IsNullOrWhiteSpace(cell) || IsWarehouseName(cell, inventory.SourceWarehouse, warehouseView))
                {
                    continue;
                }

                Add(
                    line.ItemCode,
                    line.ItemName,
                    string.IsNullOrWhiteSpace(inventory.SourceWarehouse) ? inventory.TargetWarehouse : inventory.SourceWarehouse,
                    cell,
                    line.Unit,
                    line.Quantity,
                    $"Инвентаризация {Clean(inventory.Number)}",
                    isAddressed: true);
            }
        }

        foreach (var writeOff in warehouseWorkspace.WriteOffs)
        {
            if (IsDraftLike(writeOff.Status))
            {
                continue;
            }

            foreach (var line in writeOff.Lines)
            {
                var cell = Clean(line.SourceLocation);
                if (string.IsNullOrWhiteSpace(cell) || IsWarehouseName(cell, writeOff.SourceWarehouse, warehouseView))
                {
                    continue;
                }

                Add(
                    line.ItemCode,
                    line.ItemName,
                    writeOff.SourceWarehouse,
                    cell,
                    line.Unit,
                    -line.Quantity,
                    $"Списание {Clean(writeOff.Number)}",
                    isAddressed: true);
            }
        }

        var addressedBalances = balances.Values
            .Where(item => item.IsAddressed && item.Quantity > 0m)
            .ToArray();

        foreach (var stock in warehouseView.StockBalances)
        {
            if (stock.FreeQuantity <= 0m)
            {
                continue;
            }

            var addressedQuantity = addressedBalances
                .Where(item => WarehouseMatches(item.Warehouse, stock.Warehouse)
                               && MatchesItem(item.ItemCode, item.ItemName, stock.ItemCode, stock.ItemName))
                .Sum(item => item.Quantity);
            var unassignedQuantity = stock.FreeQuantity - addressedQuantity;
            if (unassignedQuantity <= 0m)
            {
                continue;
            }

            Add(
                stock.ItemCode,
                stock.ItemName,
                stock.Warehouse,
                UnassignedCellName,
                stock.Unit,
                unassignedQuantity,
                "Остаток без адреса",
                isAddressed: false);
        }

        return balances.Values.Select(item => item.ToRecord());
    }

    private static IEnumerable<WarehouseShipmentPickLineRecord> BuildPickLines(
        SalesShipmentRecord shipment,
        IReadOnlyList<WarehouseStockBalanceRecord> stockBalances,
        IReadOnlyList<WarehouseCellBalanceRecord> cellBalances)
    {
        foreach (var line in shipment.Lines)
        {
            var available = stockBalances
                .Where(item => WarehouseMatches(item.Warehouse, shipment.Warehouse)
                               && MatchesItem(item.ItemCode, item.ItemName, line.ItemCode, line.ItemName))
                .Sum(item => item.FreeQuantity);

            var cells = cellBalances
                .Where(item => item.Quantity > 0m
                               && WarehouseMatches(item.Warehouse, shipment.Warehouse)
                               && MatchesItem(item.ItemCode, item.ItemName, line.ItemCode, line.ItemName))
                .OrderByDescending(item => item.IsAddressed)
                .ThenBy(item => item.Cell, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var addressedQuantity = cells.Where(item => item.IsAddressed).Sum(item => item.Quantity);
            var shortage = Math.Max(0m, line.Quantity - available);
            var cellShortage = Math.Max(0m, line.Quantity - addressedQuantity);
            var status = shortage > 0m
                ? "Не хватает"
                : cellShortage > 0m
                    ? addressedQuantity > 0m ? "Ячейки частично" : "Ячейка не указана"
                    : "Готово";

            yield return new WarehouseShipmentPickLineRecord
            {
                ShipmentId = shipment.Id,
                ShipmentNumber = Clean(shipment.Number),
                SalesOrderNumber = Clean(shipment.SalesOrderNumber),
                Warehouse = Clean(shipment.Warehouse),
                ItemCode = Clean(line.ItemCode),
                ItemName = Clean(line.ItemName),
                Unit = Clean(line.Unit),
                RequiredQuantity = line.Quantity,
                AvailableQuantity = available,
                ShortageQuantity = shortage,
                AddressedCellQuantity = addressedQuantity,
                CellShortageQuantity = cellShortage,
                CellSummary = BuildCellSummary(cells),
                PickStatus = status,
                IsStockCovered = shortage <= 0m,
                IsCellCovered = cellShortage <= 0m,
                SearchText = BuildSearchText(shipment.Number, shipment.SalesOrderNumber, shipment.CustomerName, shipment.Warehouse, line.ItemCode, line.ItemName)
            };
        }
    }

    private static WarehouseTodayShipmentRecord BuildShipmentRecord(
        SalesShipmentRecord shipment,
        IEnumerable<WarehouseShipmentPickLineRecord> pickLines)
    {
        var lines = pickLines.ToArray();
        var totalRequired = lines.Sum(item => item.RequiredQuantity);
        var totalShortage = lines.Sum(item => item.ShortageQuantity);
        var missingCellLines = lines.Count(item => item.IsStockCovered && !item.IsCellCovered);
        var stockCovered = totalShortage <= 0m;
        var cellCovered = missingCellLines == 0;
        var readiness = !stockCovered
            ? "Не хватает товара"
            : !cellCovered
                ? "Нужны ячейки"
                : "Готово к сборке";

        return new WarehouseTodayShipmentRecord
        {
            ShipmentId = shipment.Id,
            ShipmentNumber = Clean(shipment.Number),
            ShipmentDate = shipment.ShipmentDate.Date,
            SalesOrderNumber = Clean(shipment.SalesOrderNumber),
            CustomerName = Clean(shipment.CustomerName),
            Warehouse = Clean(shipment.Warehouse),
            Status = Clean(shipment.Status),
            PositionCount = shipment.PositionCount,
            RequiredQuantity = totalRequired,
            ShortageQuantity = totalShortage,
            MissingCellLineCount = missingCellLines,
            Readiness = readiness,
            ReadinessWeight = !stockCovered ? 0 : !cellCovered ? 1 : 2,
            IsStockCovered = stockCovered,
            IsCellCovered = cellCovered,
            SearchText = BuildSearchText(shipment.Number, shipment.SalesOrderNumber, shipment.CustomerName, shipment.Warehouse, shipment.Status)
        };
    }

    private static string BuildCellSummary(IReadOnlyList<WarehouseCellBalanceRecord> cells)
    {
        var addressed = cells
            .Where(item => item.IsAddressed)
            .Take(4)
            .Select(item => $"{item.Cell}: {item.Quantity:N0} {item.Unit}")
            .ToArray();
        if (addressed.Length > 0)
        {
            var tail = cells.Count(item => item.IsAddressed) > addressed.Length ? "; ..." : string.Empty;
            return string.Join("; ", addressed) + tail;
        }

        var unassigned = cells.FirstOrDefault(item => !item.IsAddressed && item.Quantity > 0m);
        return unassigned is null
            ? "Нет данных по ячейкам"
            : $"{UnassignedCellName}: {unassigned.Quantity:N0} {unassigned.Unit}";
    }

    private static bool IsShipmentDueForPicking(SalesShipmentRecord shipment, DateTime workDate)
    {
        if (shipment.ShipmentDate == default)
        {
            return false;
        }

        var status = Clean(shipment.Status).ToLowerInvariant();
        return shipment.ShipmentDate.Date <= workDate
               && !status.Contains("отгруж", StringComparison.OrdinalIgnoreCase)
               && !status.Contains("закры", StringComparison.OrdinalIgnoreCase)
               && !status.Contains("отмен", StringComparison.OrdinalIgnoreCase)
               && !status.Contains("архив", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDraftLike(string status)
    {
        var value = Clean(status).ToLowerInvariant();
        return value.Contains("чернов", StringComparison.OrdinalIgnoreCase)
               || value.Contains("план", StringComparison.OrdinalIgnoreCase)
               || value.Contains("отмен", StringComparison.OrdinalIgnoreCase)
               || value.Contains("архив", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWarehouseName(string value, string documentWarehouse, WarehouseWorkspace warehouseView)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return true;
        }

        return (!string.IsNullOrWhiteSpace(documentWarehouse) && WarehouseMatches(cleaned, documentWarehouse))
               || warehouseView.StockBalances.Any(item => Clean(item.Warehouse).Equals(cleaned, StringComparison.OrdinalIgnoreCase));
    }

    private static bool WarehouseMatches(string left, string right)
    {
        var cleanLeft = Clean(left);
        var cleanRight = Clean(right);
        return string.IsNullOrWhiteSpace(cleanLeft)
               || string.IsNullOrWhiteSpace(cleanRight)
               || cleanLeft.Equals(cleanRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesItem(string leftCode, string leftName, string rightCode, string rightName)
    {
        var cleanLeftCode = Clean(leftCode);
        var cleanRightCode = Clean(rightCode);
        if (!string.IsNullOrWhiteSpace(cleanLeftCode)
            && !string.IsNullOrWhiteSpace(cleanRightCode)
            && cleanLeftCode.Equals(cleanRightCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var cleanLeftName = Clean(leftName);
        var cleanRightName = Clean(rightName);
        return !string.IsNullOrWhiteSpace(cleanLeftName)
               && !string.IsNullOrWhiteSpace(cleanRightName)
               && cleanLeftName.Equals(cleanRightName, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBalanceKey(string itemCode, string itemName, string warehouse, string cell, bool isAddressed)
    {
        var itemKey = !string.IsNullOrWhiteSpace(Clean(itemCode))
            ? $"C:{Clean(itemCode)}"
            : $"N:{Clean(itemName)}";
        return $"{itemKey}|W:{Clean(warehouse)}|L:{Clean(cell)}|A:{isAddressed}";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.Select(Clean).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string BuildSearchText(params string?[] parts)
    {
        return string.Join(' ', parts.Select(Clean).Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string Clean(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value).Trim();
    }

    private sealed class MutableCellBalance
    {
        public string ItemCode { get; init; } = string.Empty;

        public string ItemName { get; init; } = string.Empty;

        public string Warehouse { get; init; } = string.Empty;

        public string Cell { get; init; } = string.Empty;

        public string Unit { get; init; } = string.Empty;

        public decimal Quantity { get; set; }

        public bool IsAddressed { get; init; }

        public List<string> Sources { get; } = [];

        public WarehouseCellBalanceRecord ToRecord()
        {
            return new WarehouseCellBalanceRecord
            {
                ItemCode = ItemCode,
                ItemName = ItemName,
                Warehouse = Warehouse,
                Cell = Cell,
                Unit = string.IsNullOrWhiteSpace(Unit) ? "шт" : Unit,
                Quantity = Quantity,
                IsAddressed = IsAddressed,
                SourceLabel = Sources.Count == 0 ? string.Empty : string.Join(", ", Sources.Take(3)),
                SearchText = string.Join(' ', ItemCode, ItemName, Warehouse, Cell, string.Join(' ', Sources))
            };
        }
    }
}

public sealed class WarehouseCellStorageSnapshot
{
    public static WarehouseCellStorageSnapshot Empty { get; } = new();

    public IReadOnlyList<WarehouseTodayShipmentRecord> TodayShipments { get; init; } = Array.Empty<WarehouseTodayShipmentRecord>();

    public IReadOnlyList<WarehouseShipmentPickLineRecord> PickLines { get; init; } = Array.Empty<WarehouseShipmentPickLineRecord>();

    public IReadOnlyList<WarehouseCellBalanceRecord> CellBalances { get; init; } = Array.Empty<WarehouseCellBalanceRecord>();

    public int TodayShipmentCount { get; init; }

    public int ReadyShipmentCount { get; init; }

    public int ShortShipmentCount { get; init; }

    public int MissingCellLineCount { get; init; }
}

public sealed class WarehouseTodayShipmentRecord
{
    public Guid ShipmentId { get; init; }

    public string ShipmentNumber { get; init; } = string.Empty;

    public DateTime ShipmentDate { get; init; }

    public string SalesOrderNumber { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string Warehouse { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int PositionCount { get; init; }

    public decimal RequiredQuantity { get; init; }

    public decimal ShortageQuantity { get; init; }

    public int MissingCellLineCount { get; init; }

    public string Readiness { get; init; } = string.Empty;

    public int ReadinessWeight { get; init; }

    public bool IsStockCovered { get; init; }

    public bool IsCellCovered { get; init; }

    public string SearchText { get; init; } = string.Empty;
}

public sealed class WarehouseShipmentPickLineRecord
{
    public Guid ShipmentId { get; init; }

    public string ShipmentNumber { get; init; } = string.Empty;

    public string SalesOrderNumber { get; init; } = string.Empty;

    public string Warehouse { get; init; } = string.Empty;

    public string ItemCode { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public decimal RequiredQuantity { get; init; }

    public decimal AvailableQuantity { get; init; }

    public decimal ShortageQuantity { get; init; }

    public decimal AddressedCellQuantity { get; init; }

    public decimal CellShortageQuantity { get; init; }

    public string CellSummary { get; init; } = string.Empty;

    public string PickStatus { get; init; } = string.Empty;

    public bool IsStockCovered { get; init; }

    public bool IsCellCovered { get; init; }

    public string SearchText { get; init; } = string.Empty;
}

public sealed class WarehouseCellBalanceRecord
{
    public string ItemCode { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string Warehouse { get; init; } = string.Empty;

    public string Cell { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public bool IsAddressed { get; init; }

    public string SourceLabel { get; init; } = string.Empty;

    public string SearchText { get; init; } = string.Empty;
}
