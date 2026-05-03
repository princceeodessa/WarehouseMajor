using System.Globalization;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class WarehouseWorkspace
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string SummaryNote { get; init; } = string.Empty;

    public IReadOnlyList<WarehouseStockBalanceRecord> StockBalances { get; init; } = Array.Empty<WarehouseStockBalanceRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> TransferOrders { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> Reservations { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> InventoryCounts { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> WriteOffs { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public static WarehouseWorkspace Create(SalesWorkspace salesWorkspace)
    {
        var snapshot = salesWorkspace.OneCImport ?? new OneCImportSnapshot();
        var inventoryService = new SalesInventoryService(salesWorkspace);

        var stockBalances = inventoryService
            .GetStockSnapshot()
            .Select(item => new WarehouseStockBalanceRecord
            {
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                Warehouse = item.Warehouse,
                Unit = item.Unit,
                BaselineQuantity = item.BaselineQuantity,
                ReservedQuantity = item.ReservedQuantity,
                ShippedQuantity = item.ShippedQuantity,
                FreeQuantity = item.FreeQuantity,
                Status = BuildStockStatus(item),
                SourceLabel = "Операционный складской срез"
            })
            .ToArray();

        var transferOrders = MapDocuments(
            snapshot.TransferOrders,
            record => new WarehouseDocumentRecord
            {
                DocumentType = "Заказ на перемещение",
                Number = FirstNonEmpty(record.Number, GetFieldDisplay(record.Fields, "Номер")),
                Date = record.Date,
                Status = record.Status,
                SourceWarehouse = GetFieldDisplay(record.Fields, "СтруктурнаяЕдиницаРезерв"),
                TargetWarehouse = GetFieldDisplay(record.Fields, "СтруктурнаяЕдиницаПолучатель"),
                RelatedDocument = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "ЗаказПокупателя"),
                    GetFieldDisplay(record.Fields, "ДокументОснование")),
                Comment = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "Комментарий"),
                    GetFieldDisplay(record.Fields, "Заметки")),
                SourceLabel = BuildSourceLabel(snapshot.TransferOrders),
                Title = record.Title,
                Subtitle = record.Subtitle,
                Fields = record.Fields,
                Lines = BuildDocumentLines(record)
            });

        var reservations = snapshot.StockReservations.Records.Count > 0
            ? MapDocuments(
                snapshot.StockReservations,
                record => new WarehouseDocumentRecord
                {
                    DocumentType = "Резервирование",
                    Number = FirstNonEmpty(record.Number, GetFieldDisplay(record.Fields, "Номер")),
                    Date = record.Date,
                    Status = record.Status,
                    SourceWarehouse = FirstNonEmpty(
                        GetFieldDisplay(record.Fields, "ИсходноеМестоРезерва"),
                        GetFieldDisplay(record.Fields, "ПоложениеМестаРезерва")),
                    TargetWarehouse = GetFieldDisplay(record.Fields, "НовоеМестоРезерва"),
                    RelatedDocument = GetFieldDisplay(record.Fields, "ЗаказПокупателя"),
                    Comment = GetFieldDisplay(record.Fields, "Комментарий"),
                    SourceLabel = BuildSourceLabel(snapshot.StockReservations),
                    Title = record.Title,
                    Subtitle = record.Subtitle,
                    Fields = record.Fields,
                    Lines = BuildDocumentLines(record)
                })
            : BuildDerivedReservations(salesWorkspace);

        var inventoryCounts = MapDocuments(
            snapshot.InventoryCounts,
            record => new WarehouseDocumentRecord
            {
                DocumentType = "Инвентаризация",
                Number = FirstNonEmpty(record.Number, GetFieldDisplay(record.Fields, "Номер")),
                Date = record.Date,
                Status = record.Status,
                SourceWarehouse = GetFieldDisplay(record.Fields, "СтруктурнаяЕдиница"),
                TargetWarehouse = GetFieldDisplay(record.Fields, "Ячейка"),
                RelatedDocument = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "ДокументОснованиеНомер"),
                    GetFieldDisplay(record.Fields, "ДокументОснованиеДата")),
                Comment = GetFieldDisplay(record.Fields, "Комментарий"),
                SourceLabel = BuildSourceLabel(snapshot.InventoryCounts),
                Title = record.Title,
                Subtitle = record.Subtitle,
                Fields = record.Fields,
                Lines = BuildDocumentLines(record)
            });

        var writeOffs = MapDocuments(
            snapshot.StockWriteOffs,
            record => new WarehouseDocumentRecord
            {
                DocumentType = "Списание",
                Number = FirstNonEmpty(record.Number, GetFieldDisplay(record.Fields, "Номер")),
                Date = record.Date,
                Status = record.Status,
                SourceWarehouse = GetFieldDisplay(record.Fields, "СтруктурнаяЕдиница"),
                TargetWarehouse = GetFieldDisplay(record.Fields, "Ячейка"),
                RelatedDocument = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "ДокументОснование"),
                    GetFieldDisplay(record.Fields, "ДокументПоступления")),
                Comment = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "ПричинаСписания"),
                    GetFieldDisplay(record.Fields, "Комментарий")),
                SourceLabel = BuildSourceLabel(snapshot.StockWriteOffs),
                Title = record.Title,
                Subtitle = record.Subtitle,
                Fields = record.Fields,
                Lines = BuildDocumentLines(record)
            });

        if (salesWorkspace.OperationalSnapshot?.HasWarehouseData == true)
        {
            return new WarehouseWorkspace
            {
                SummaryNote = "Склад собран из live-выгрузки 1С и operational MySQL. Если один источник неполный, модуль берет более насыщенную версию документа из другого.",
                StockBalances = MergeStockBalances(salesWorkspace.OperationalSnapshot.StockBalances, stockBalances),
                TransferOrders = MergeDocuments(salesWorkspace.OperationalSnapshot.TransferOrders, transferOrders),
                Reservations = MergeDocuments(salesWorkspace.OperationalSnapshot.Reservations, reservations),
                InventoryCounts = MergeDocuments(salesWorkspace.OperationalSnapshot.InventoryCounts, inventoryCounts),
                WriteOffs = MergeDocuments(salesWorkspace.OperationalSnapshot.WriteOffs, writeOffs)
            };
        }

        return new WarehouseWorkspace
        {
            SummaryNote =
                "Остатки считаются из текущего рабочего контура, а складские документы подтягиваются из выгрузок и model_schema 1С. " +
                "Это уже позволяет видеть связи, поля и табличные части без интерфейса 1С.",
            StockBalances = stockBalances,
            TransferOrders = transferOrders,
            Reservations = reservations,
            InventoryCounts = inventoryCounts,
            WriteOffs = writeOffs
        };
    }

    private static IReadOnlyList<WarehouseDocumentRecord> MapDocuments(
        OneCEntityDataset dataset,
        Func<OneCRecordSnapshot, WarehouseDocumentRecord> factory)
    {
        return dataset.Records
            .Select(factory)
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<WarehouseStockBalanceRecord> MergeStockBalances(
        IReadOnlyList<WarehouseStockBalanceRecord> operationalBalances,
        IReadOnlyList<WarehouseStockBalanceRecord> importBalances)
    {
        var merged = new Dictionary<string, WarehouseStockBalanceRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var balance in operationalBalances.Concat(importBalances))
        {
            var key = !string.IsNullOrWhiteSpace(balance.ItemCode)
                ? $"{balance.ItemCode}|{balance.Warehouse}"
                : $"{balance.ItemName}|{balance.Warehouse}";

            if (merged.TryGetValue(key, out var existing))
            {
                merged[key] = MergeStockBalance(existing, balance);
                continue;
            }

            merged[key] = balance;
        }

        return merged.Values
            .OrderBy(item => item.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<WarehouseDocumentRecord> MergeDocuments(
        IReadOnlyList<WarehouseDocumentRecord> operationalDocuments,
        IReadOnlyList<WarehouseDocumentRecord> importDocuments)
    {
        var merged = new Dictionary<string, WarehouseDocumentRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in operationalDocuments.Concat(importDocuments))
        {
            var key = BuildDocumentMergeKey(document);
            if (merged.TryGetValue(key, out var existing))
            {
                merged[key] = ScoreDocument(document) > ScoreDocument(existing) ? document : existing;
                continue;
            }

            merged[key] = document;
        }

        return merged.Values
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildDocumentMergeKey(WarehouseDocumentRecord document)
    {
        return !string.IsNullOrWhiteSpace(document.Number)
            ? $"{document.DocumentType}|{document.Number}"
            : $"{document.DocumentType}|{document.Title}|{document.SourceWarehouse}|{document.TargetWarehouse}";
    }

    private static int ScoreBalance(WarehouseStockBalanceRecord record)
    {
        var score = 0;
        score += !string.IsNullOrWhiteSpace(record.ItemCode) ? 2 : 0;
        score += !string.IsNullOrWhiteSpace(record.ItemName) ? 2 : 0;
        score += !string.IsNullOrWhiteSpace(record.Warehouse) ? 2 : 0;
        score += !string.IsNullOrWhiteSpace(record.Unit) ? 1 : 0;
        score += record.BaselineQuantity != 0m ? 2 : 0;
        score += record.ReservedQuantity != 0m ? 1 : 0;
        score += record.ShippedQuantity != 0m ? 1 : 0;
        return score;
    }

    private static WarehouseStockBalanceRecord MergeStockBalance(
        WarehouseStockBalanceRecord existing,
        WarehouseStockBalanceRecord incoming)
    {
        var preferred = ScoreBalance(incoming) > ScoreBalance(existing) ? incoming : existing;
        var baseline = Math.Max(existing.BaselineQuantity, incoming.BaselineQuantity);
        var reserved = Math.Max(existing.ReservedQuantity, incoming.ReservedQuantity);
        var shipped = Math.Max(existing.ShippedQuantity, incoming.ShippedQuantity);
        var free = Math.Max(0m, baseline - reserved - shipped);

        return new WarehouseStockBalanceRecord
        {
            ItemCode = FirstNonEmpty(existing.ItemCode, incoming.ItemCode),
            ItemName = FirstNonEmpty(existing.ItemName, incoming.ItemName),
            Warehouse = FirstNonEmpty(existing.Warehouse, incoming.Warehouse),
            Unit = FirstNonEmpty(existing.Unit, incoming.Unit),
            BaselineQuantity = baseline,
            ReservedQuantity = reserved,
            ShippedQuantity = shipped,
            FreeQuantity = free,
            Status = BuildStockStatus(free, reserved, shipped),
            SourceLabel = FirstNonEmpty(preferred.SourceLabel, existing.SourceLabel, incoming.SourceLabel)
        };
    }

    private static int ScoreDocument(WarehouseDocumentRecord record)
    {
        var score = 0;
        score += !string.IsNullOrWhiteSpace(record.Number) ? 2 : 0;
        score += record.Date.HasValue ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.Status) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.SourceWarehouse) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.TargetWarehouse) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.RelatedDocument) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.Comment) ? 1 : 0;
        score += record.Fields.Count;
        score += record.Lines.Count * 2;
        return score;
    }

    private static IReadOnlyList<WarehouseDocumentRecord> BuildDerivedReservations(SalesWorkspace salesWorkspace)
    {
        return salesWorkspace.Orders
            .Where(order =>
                order.Status.Contains("резерв", StringComparison.OrdinalIgnoreCase)
                || order.Status.Contains("отгруз", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new WarehouseDocumentRecord
            {
                DocumentType = "Резервирование",
                Number = $"RES-{order.Number}",
                Date = order.OrderDate,
                Status = order.Status,
                SourceWarehouse = order.Warehouse,
                TargetWarehouse = string.Empty,
                RelatedDocument = order.Number,
                Comment = order.Comment,
                SourceLabel = "Операционный резерв из заказа покупателя",
                Title = $"Резерв по заказу {order.Number}",
                Subtitle = $"{order.CustomerName} | {order.Manager}",
                Fields =
                [
                    new OneCFieldValue("ЗаказПокупателя", order.Number, order.Number),
                    new OneCFieldValue("Контрагент", order.CustomerName, order.CustomerName),
                    new OneCFieldValue("Склад", order.Warehouse, order.Warehouse),
                    new OneCFieldValue("Статус", order.Status, order.Status),
                    new OneCFieldValue("Менеджер", order.Manager, order.Manager),
                    new OneCFieldValue("Комментарий", order.Comment, order.Comment)
                ],
                Lines = order.Lines
                    .Select((line, index) => new WarehouseDocumentLineRecord
                    {
                        RowNumber = index + 1,
                        Item = line.ItemName,
                        Quantity = line.Quantity,
                        Unit = line.Unit,
                        Reserve = line.Quantity,
                        PickedQuantity = 0m,
                        Price = line.Price,
                        Amount = line.Amount,
                        SourceLocation = order.Warehouse,
                        TargetLocation = string.Empty,
                        RelatedDocument = order.Number,
                        Fields =
                        [
                            new OneCFieldValue("Номенклатура", line.ItemName, line.ItemName),
                            new OneCFieldValue("Количество", line.Quantity.ToString("N2", RuCulture), line.Quantity.ToString("N2", RuCulture)),
                            new OneCFieldValue("ЕдиницаИзмерения", line.Unit, line.Unit),
                            new OneCFieldValue("Цена", line.Price.ToString("N2", RuCulture), line.Price.ToString("N2", RuCulture)),
                            new OneCFieldValue("Сумма", line.Amount.ToString("N2", RuCulture), line.Amount.ToString("N2", RuCulture))
                        ]
                    })
                    .ToArray()
            })
            .ToArray();
    }

    private static IReadOnlyList<WarehouseDocumentLineRecord> BuildDocumentLines(OneCRecordSnapshot record)
    {
        var section = record.TabularSections.FirstOrDefault(
                          item => string.Equals(item.Name, "Запасы", StringComparison.OrdinalIgnoreCase))
                      ?? record.TabularSections.FirstOrDefault();
        if (section is null)
        {
            return Array.Empty<WarehouseDocumentLineRecord>();
        }

        return section.Rows
            .Select(row => new WarehouseDocumentLineRecord
            {
                RowNumber = row.RowNumber,
                Item = GetFieldDisplay(row.Fields, "Номенклатура"),
                Quantity = FirstDecimal(row.Fields, "Количество", "КоличествоУчет", "Излишки", "Недостача"),
                Unit = GetFieldDisplay(row.Fields, "ЕдиницаИзмерения"),
                Reserve = FirstDecimal(row.Fields, "Резерв"),
                PickedQuantity = FirstDecimal(row.Fields, "КоличествоСобрано"),
                Price = FirstDecimal(row.Fields, "Цена"),
                Amount = FirstDecimal(row.Fields, "Сумма", "СуммаУчет"),
                SourceLocation = FirstNonEmpty(
                    GetFieldDisplay(row.Fields, "ИсходноеМестоРезерва"),
                    GetFieldDisplay(row.Fields, "Ячейка")),
                TargetLocation = GetFieldDisplay(row.Fields, "НовоеМестоРезерва"),
                RelatedDocument = FirstNonEmpty(
                    GetFieldDisplay(row.Fields, "ЗаказПокупателя"),
                    GetFieldDisplay(row.Fields, "ДокументОснование")),
                Fields = row.Fields
            })
            .OrderBy(item => item.RowNumber)
            .ToArray();
    }

    private static string BuildSourceLabel(OneCEntityDataset dataset)
    {
        return dataset.Schema?.SourceFileName is { Length: > 0 } fileName
            ? $"1С / {fileName}"
            : $"1С / {dataset.ObjectName}";
    }

    private static string BuildStockStatus(SalesWarehouseStockSnapshot item)
    {
        return BuildStockStatus(item.FreeQuantity, item.ReservedQuantity, item.ShippedQuantity);
    }

    private static string BuildStockStatus(decimal freeQuantity, decimal reservedQuantity, decimal shippedQuantity)
    {
        if (freeQuantity <= 0m)
        {
            return "Критично";
        }

        if (reservedQuantity > 0m || shippedQuantity > 0m)
        {
            return "Под контроль";
        }

        return "Норма";
    }

    private static string GetFieldDisplay(IEnumerable<OneCFieldValue> fields, string fieldName)
    {
        return fields.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue
            ?? string.Empty;
    }

    private static decimal FirstDecimal(IEnumerable<OneCFieldValue> fields, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            var rawValue = fields.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                ?.RawValue;
            if (TryParseDecimal(rawValue, out var result))
            {
                return result;
            }
        }

        return 0m;
    }

    private static bool TryParseDecimal(string? rawValue, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue
            .Replace('\u00A0', ' ')
            .Replace(" ", string.Empty);
        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands,
            RuCulture,
            out result);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}

public sealed class WarehouseStockBalanceRecord
{
    public string ItemCode { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string Warehouse { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public decimal BaselineQuantity { get; init; }

    public decimal ReservedQuantity { get; init; }

    public decimal ShippedQuantity { get; init; }

    public decimal FreeQuantity { get; init; }

    public string Status { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;
}

public sealed class WarehouseDocumentRecord
{
    public string DocumentType { get; init; } = string.Empty;

    public string Number { get; init; } = string.Empty;

    public DateTime? Date { get; init; }

    public string Status { get; init; } = string.Empty;

    public string SourceWarehouse { get; init; } = string.Empty;

    public string TargetWarehouse { get; init; } = string.Empty;

    public string RelatedDocument { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; init; } = Array.Empty<OneCFieldValue>();

    public IReadOnlyList<WarehouseDocumentLineRecord> Lines { get; init; } = Array.Empty<WarehouseDocumentLineRecord>();
}

public sealed class WarehouseDocumentLineRecord
{
    public int RowNumber { get; init; }

    public string Item { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public string Unit { get; init; } = string.Empty;

    public decimal Reserve { get; init; }

    public decimal PickedQuantity { get; init; }

    public decimal Price { get; init; }

    public decimal Amount { get; init; }

    public string SourceLocation { get; init; } = string.Empty;

    public string TargetLocation { get; init; } = string.Empty;

    public string RelatedDocument { get; init; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; init; } = Array.Empty<OneCFieldValue>();
}
