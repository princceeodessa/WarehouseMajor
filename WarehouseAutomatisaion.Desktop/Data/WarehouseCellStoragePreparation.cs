namespace WarehouseAutomatisaion.Desktop.Data;

public static class WarehouseCellStoragePreparationPlan
{
    public const string QrScheme = "MWH";

    public const int QrVersion = 1;

    public static WarehouseCellStoragePreparationSnapshot Create(
        IReadOnlyList<string>? warehouses,
        bool sharedDatabaseEnabled,
        string currentRoleCode)
    {
        var normalizedWarehouses = (warehouses ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var templateCells = BuildTemplateCells(normalizedWarehouses);
        var qrPayloads = BuildQrPayloads(templateCells);

        return new WarehouseCellStoragePreparationSnapshot(
            Status: "Подготовка",
            AccessLevel: string.Equals(currentRoleCode, "admin", StringComparison.OrdinalIgnoreCase)
                ? "Администратор"
                : "Нет доступа к настройке",
            DataMode: sharedDatabaseEnabled ? "Общая база" : "Локальные данные",
            AddressMask: "Склад-Зона-Ряд-Стеллаж-Полка-Ячейка",
            QrMode: "Подготовлен, не обязателен",
            WarehouseCount: normalizedWarehouses.Length,
            WarehousesPreview: BuildWarehousesPreview(normalizedWarehouses),
            TemplateCells: templateCells,
            QrPayloads: qrPayloads,
            ScanFlow:
            [
                new("1", "Приемка", "Кладовщик указывает ячейку вручную в строке приемки.", "Скан товара -> скан ячейки -> строка получает адрес размещения."),
                new("2", "Перемещение", "Кладовщик заполняет источник и назначение вручную.", "Скан исходной ячейки -> скан товара -> скан целевой ячейки."),
                new("3", "Отбор", "Кладовщик видит ячейки в складе и собирает заказ вручную.", "Скан ячейки -> скан товара -> подтверждение количества.")
            ],
            References:
            [
                new("Склады", normalizedWarehouses.Length == 0 ? "Не найдены" : $"{normalizedWarehouses.Length:N0}", "Привязка к зонам хранения"),
                new("Зоны", "Подготовлено", "Приемка, хранение, отбор, брак"),
                new("Адреса ячеек", $"{templateCells.Count:N0}", "Единый код адреса и статус активности"),
                new("QR payload", $"{qrPayloads.Count:N0}", "Формат будущих QR-кодов без обязательной зависимости"),
                new("Товары", "Подготовлено", "Основная и резервная ячейка товара"),
                new("Остатки", "Подготовлено", "Разделение по складу и ячейке")
            ],
            Stages:
            [
                new("1", "Справочник ячеек", "Подготовлено", "Склад, зона, ряд, стеллаж, полка, ячейка, тип, вместимость"),
                new("2", "QR-коды", "Подготовлено без зависимости", "Payload для склада, ячейки и товара. Печать/сканер подключаются позже."),
                new("3", "Привязка товаров", "Готовится", "Основная ячейка, резервная ячейка, правила отбора"),
                new("4", "Миграция остатков", "Ожидает требований", "Перенос текущих остатков в адресные остатки"),
                new("5", "Документы склада", "Частично готово", "Приемки и перемещения уже могут указывать ячейки"),
                new("6", "Контроль ошибок", "Ожидает требований", "Пустые адреса, дубли, отрицательные остатки")
            ],
            Readiness:
            [
                new("Роль", "ОК", "Раздел находится в админских настройках"),
                new("База", sharedDatabaseEnabled ? "ОК" : "Внимание", sharedDatabaseEnabled ? "Можно готовить общий контур" : "Для общего склада потребуется серверная база"),
                new("Склады", normalizedWarehouses.Length > 0 ? "ОК" : "Внимание", normalizedWarehouses.Length > 0 ? "Есть базовый список складов" : "Нужно загрузить или создать склады"),
                new("QR", "Не обязателен", "Основной процесс работает без сканера; QR только подготовлен как будущий ускоритель")
            ]);
    }

    public static string BuildCellQrPayload(string warehouse, string cellCode)
    {
        return BuildQrPayload("cell", ("warehouse", warehouse), ("cell", cellCode));
    }

    public static string BuildItemQrPayload(string itemCode, string itemName)
    {
        return BuildQrPayload("item", ("code", itemCode), ("name", itemName));
    }

    public static bool TryParseQrPayload(string? payload, out WarehousePreparedQrPayload parsed)
    {
        parsed = new WarehousePreparedQrPayload(
            string.Empty,
            0,
            string.Empty,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !parts[0].Equals(QrScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts.Skip(1))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            values[part[..separator]] = Uri.UnescapeDataString(part[(separator + 1)..]);
        }

        if (!values.TryGetValue("v", out var versionText)
            || !int.TryParse(versionText, out var version)
            || version != QrVersion
            || !values.TryGetValue("type", out var objectType))
        {
            return false;
        }

        values.Remove("v");
        values.Remove("type");
        parsed = new WarehousePreparedQrPayload(QrScheme, version, objectType, values);
        return true;
    }

    private static string BuildWarehousesPreview(IReadOnlyList<string> warehouses)
    {
        if (warehouses.Count == 0)
        {
            return "Склады не найдены";
        }

        var preview = string.Join(", ", warehouses.Take(4));
        return warehouses.Count > 4
            ? $"{preview} и еще {warehouses.Count - 4:N0}"
            : preview;
    }

    private static IReadOnlyList<WarehouseCellStorageTemplateCell> BuildTemplateCells(IReadOnlyList<string> warehouses)
    {
        var sourceWarehouses = warehouses.Count == 0 ? new[] { "Главный склад" } : warehouses;
        var result = new List<WarehouseCellStorageTemplateCell>();
        var warehouseIndex = 1;

        foreach (var warehouse in sourceWarehouses.Take(6))
        {
            AddCell(result, warehouse, warehouseIndex, "RCV", "Приемка", 1, 1, 1, 1, "Временная", 100m);
            AddCell(result, warehouse, warehouseIndex, "PCK", "Отбор", 1, 1, 1, 1, "Штучная", 40m);
            AddCell(result, warehouse, warehouseIndex, "PCK", "Отбор", 1, 1, 1, 2, "Штучная", 40m);
            AddCell(result, warehouse, warehouseIndex, "STG", "Хранение", 1, 1, 2, 1, "Паллетная", 200m);
            AddCell(result, warehouse, warehouseIndex, "STG", "Хранение", 1, 2, 1, 1, "Длинномер", 120m);
            AddCell(result, warehouse, warehouseIndex, "DEF", "Брак", 1, 1, 1, 1, "Карантин", 30m);
            warehouseIndex++;
        }

        return result;
    }

    private static void AddCell(
        ICollection<WarehouseCellStorageTemplateCell> target,
        string warehouse,
        int warehouseIndex,
        string zoneCode,
        string zoneName,
        int row,
        int rack,
        int shelf,
        int cell,
        string cellType,
        decimal capacity)
    {
        var code = $"WH{warehouseIndex:00}-{zoneCode}-{row:00}-{rack:00}-{shelf:00}-{cell:00}";
        target.Add(new WarehouseCellStorageTemplateCell(
            Warehouse: warehouse,
            ZoneCode: zoneCode,
            ZoneName: zoneName,
            Row: row,
            Rack: rack,
            Shelf: shelf,
            Cell: cell,
            Code: code,
            CellType: cellType,
            Capacity: capacity,
            Status: "Активна",
            QrPayload: BuildCellQrPayload(warehouse, code)));
    }

    private static IReadOnlyList<WarehouseCellStorageQrPayload> BuildQrPayloads(IReadOnlyList<WarehouseCellStorageTemplateCell> templateCells)
    {
        var payloads = templateCells
            .Take(12)
            .Select(cell => new WarehouseCellStorageQrPayload(
                ObjectType: "Ячейка",
                ObjectCode: cell.Code,
                Payload: cell.QrPayload,
                Usage: "Будущее сканирование адреса размещения или отбора",
                RequiredState: "Не обязателен"))
            .ToList();

        payloads.Add(new WarehouseCellStorageQrPayload(
            ObjectType: "Товар",
            ObjectCode: "ITEM-CODE",
            Payload: BuildItemQrPayload("ITEM-CODE", "Название товара"),
            Usage: "Будущее сканирование товара в приемке, перемещении и отборе",
            RequiredState: "Не обязателен"));

        return payloads;
    }

    private static string BuildQrPayload(string objectType, params (string Key, string Value)[] values)
    {
        var parts = new List<string>
        {
            QrScheme,
            $"v={QrVersion}",
            $"type={Uri.EscapeDataString(objectType)}"
        };
        parts.AddRange(values.Select(value => $"{value.Key}={Uri.EscapeDataString(value.Value ?? string.Empty)}"));
        return string.Join('|', parts);
    }
}

public sealed record WarehouseCellStoragePreparationSnapshot(
    string Status,
    string AccessLevel,
    string DataMode,
    string AddressMask,
    string QrMode,
    int WarehouseCount,
    string WarehousesPreview,
    IReadOnlyList<WarehouseCellStorageTemplateCell> TemplateCells,
    IReadOnlyList<WarehouseCellStorageQrPayload> QrPayloads,
    IReadOnlyList<WarehouseCellStorageScanStep> ScanFlow,
    IReadOnlyList<WarehouseCellStorageReference> References,
    IReadOnlyList<WarehouseCellStorageStage> Stages,
    IReadOnlyList<WarehouseCellStorageReadinessItem> Readiness);

public sealed record WarehouseCellStorageTemplateCell(
    string Warehouse,
    string ZoneCode,
    string ZoneName,
    int Row,
    int Rack,
    int Shelf,
    int Cell,
    string Code,
    string CellType,
    decimal Capacity,
    string Status,
    string QrPayload);

public sealed record WarehouseCellStorageQrPayload(
    string ObjectType,
    string ObjectCode,
    string Payload,
    string Usage,
    string RequiredState);

public sealed record WarehouseCellStorageScanStep(
    string Step,
    string Name,
    string WithoutQr,
    string WithQr);

public sealed record WarehousePreparedQrPayload(
    string Scheme,
    int Version,
    string ObjectType,
    IReadOnlyDictionary<string, string> Values);

public sealed record WarehouseCellStorageReference(
    string Area,
    string CurrentState,
    string PreparedFor);

public sealed record WarehouseCellStorageStage(
    string Step,
    string Name,
    string Status,
    string Scope);

public sealed record WarehouseCellStorageReadinessItem(
    string Title,
    string State,
    string Detail);
