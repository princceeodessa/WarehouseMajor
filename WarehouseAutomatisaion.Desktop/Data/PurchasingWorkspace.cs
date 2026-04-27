using System.Globalization;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class PurchasingWorkspace
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string SummaryNote { get; init; } = string.Empty;

    public IReadOnlyList<PurchasingSupplierRecord> Suppliers { get; init; } = Array.Empty<PurchasingSupplierRecord>();

    public IReadOnlyList<PurchasingDocumentRecord> PurchaseOrders { get; init; } = Array.Empty<PurchasingDocumentRecord>();

    public IReadOnlyList<PurchasingDocumentRecord> SupplierInvoices { get; init; } = Array.Empty<PurchasingDocumentRecord>();

    public IReadOnlyList<PurchasingDocumentRecord> PurchaseReceipts { get; init; } = Array.Empty<PurchasingDocumentRecord>();

    public static PurchasingWorkspace Create(SalesWorkspace salesWorkspace)
    {
        if (salesWorkspace.OperationalSnapshot?.HasPurchasingData == true)
        {
            return new PurchasingWorkspace
            {
                SummaryNote =
                    "Закупки читаются из operational MySQL: поставщики, заказы, приемка и производные счета больше не зависят от CSV-снимка 1С.",
                Suppliers = salesWorkspace.OperationalSnapshot.Suppliers,
                PurchaseOrders = salesWorkspace.OperationalSnapshot.PurchaseOrders,
                SupplierInvoices = salesWorkspace.OperationalSnapshot.SupplierInvoices,
                PurchaseReceipts = salesWorkspace.OperationalSnapshot.PurchaseReceipts
            };
        }

        var snapshot = salesWorkspace.OneCImport ?? new OneCImportSnapshot();

        var purchaseOrders = MapDocuments(
            snapshot.PurchaseOrders,
            "Заказ поставщику",
            record => new PurchasingDocumentRecord
            {
                DocumentType = "Заказ поставщику",
                Number = FirstNonEmpty(record.Number, GetFieldDisplay(record.Fields, "Номер")),
                Date = record.Date,
                Status = record.Status,
                SupplierName = GetFieldDisplay(record.Fields, "Контрагент"),
                Contract = GetFieldDisplay(record.Fields, "Договор"),
                Warehouse = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "СтруктурнаяЕдиница"),
                    GetFieldDisplay(record.Fields, "СтруктурнаяЕдиницаРезерв")),
                RelatedDocument = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "ЗаказПокупателя"),
                    GetFieldDisplay(record.Fields, "ДокументОснование")),
                Comment = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "Комментарий"),
                    GetFieldDisplay(record.Fields, "Заметки")),
                TotalAmount = FirstDecimal(record.Fields, "СуммаДокумента"),
                SourceLabel = BuildSourceLabel(snapshot.PurchaseOrders),
                Title = record.Title,
                Subtitle = record.Subtitle,
                Fields = record.Fields,
                Lines = BuildDocumentLines(record)
            });

        var purchaseReceipts = MapDocuments(
            snapshot.PurchaseReceipts,
            "Приемка",
            record => new PurchasingDocumentRecord
            {
                DocumentType = "Приемка",
                Number = FirstNonEmpty(record.Number, GetFieldDisplay(record.Fields, "Номер")),
                Date = record.Date,
                Status = record.Status,
                SupplierName = GetFieldDisplay(record.Fields, "Контрагент"),
                Contract = GetFieldDisplay(record.Fields, "Договор"),
                Warehouse = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "СтруктурнаяЕдиница"),
                    GetFieldDisplay(record.Fields, "Подразделение")),
                RelatedDocument = FirstNonEmpty(
                    GetFieldDisplay(record.Fields, "Заказ"),
                    GetFieldDisplay(record.Fields, "ДокументОснование")),
                Comment = GetFieldDisplay(record.Fields, "Комментарий"),
                TotalAmount = FirstDecimal(record.Fields, "СуммаДокумента"),
                SourceLabel = BuildSourceLabel(snapshot.PurchaseReceipts),
                Title = record.Title,
                Subtitle = record.Subtitle,
                Fields = record.Fields,
                Lines = BuildDocumentLines(record)
            });

        var supplierInvoices = snapshot.SupplierInvoices.Records.Count > 0
            ? MapDocuments(
                snapshot.SupplierInvoices,
                "Счет поставщика",
                record => new PurchasingDocumentRecord
                {
                    DocumentType = "Счет поставщика",
                    Number = FirstNonEmpty(record.Number, GetFieldDisplay(record.Fields, "Номер")),
                    Date = record.Date,
                    Status = record.Status,
                    SupplierName = GetFieldDisplay(record.Fields, "Контрагент"),
                    Contract = GetFieldDisplay(record.Fields, "Договор"),
                    Warehouse = GetFieldDisplay(record.Fields, "Подразделение"),
                    RelatedDocument = GetFieldDisplay(record.Fields, "ДокументОснование"),
                    Comment = GetFieldDisplay(record.Fields, "Комментарий"),
                    TotalAmount = FirstDecimal(record.Fields, "СуммаДокумента"),
                    SourceLabel = BuildSourceLabel(snapshot.SupplierInvoices),
                    Title = record.Title,
                    Subtitle = record.Subtitle,
                    Fields = record.Fields,
                    Lines = BuildDocumentLines(record)
                })
            : BuildDerivedSupplierInvoices(purchaseOrders, purchaseReceipts);

        var suppliers = BuildSuppliers(snapshot.Customers, purchaseOrders, supplierInvoices, purchaseReceipts);

        if (salesWorkspace.OperationalSnapshot?.HasPurchasingData == true)
        {
            return new PurchasingWorkspace
            {
                SummaryNote =
                    "Закупки собраны из operational MySQL и live-выгрузки 1С. Если один источник неполный, модуль берет более насыщенные документы и связи из второго.",
                Suppliers = MergeSuppliers(salesWorkspace.OperationalSnapshot.Suppliers, suppliers),
                PurchaseOrders = MergeDocuments(salesWorkspace.OperationalSnapshot.PurchaseOrders, purchaseOrders),
                SupplierInvoices = MergeDocuments(salesWorkspace.OperationalSnapshot.SupplierInvoices, supplierInvoices),
                PurchaseReceipts = MergeDocuments(salesWorkspace.OperationalSnapshot.PurchaseReceipts, purchaseReceipts)
            };
        }

        return new PurchasingWorkspace
        {
            SummaryNote =
                "Поставщики берутся из контрагентов 1С и связываются с заказами, счетами и приемкой. " +
                "Если по документу пока нет полного CSV, модуль использует schema probe, чтобы не терять поля и связи.",
            Suppliers = suppliers,
            PurchaseOrders = purchaseOrders,
            SupplierInvoices = supplierInvoices,
            PurchaseReceipts = purchaseReceipts
        };
    }

    private static IReadOnlyList<PurchasingSupplierRecord> MergeSuppliers(
        IReadOnlyList<PurchasingSupplierRecord> operationalSuppliers,
        IReadOnlyList<PurchasingSupplierRecord> importSuppliers)
    {
        var merged = new Dictionary<string, PurchasingSupplierRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var supplier in operationalSuppliers.Concat(importSuppliers))
        {
            var key = BuildSupplierMergeKey(supplier);
            if (merged.TryGetValue(key, out var existing))
            {
                merged[key] = ScoreSupplier(supplier) > ScoreSupplier(existing) ? supplier : existing;
                continue;
            }

            merged[key] = supplier;
        }

        return merged.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PurchasingDocumentRecord> MergeDocuments(
        IReadOnlyList<PurchasingDocumentRecord> operationalDocuments,
        IReadOnlyList<PurchasingDocumentRecord> importDocuments)
    {
        var merged = new Dictionary<string, PurchasingDocumentRecord>(StringComparer.OrdinalIgnoreCase);

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

    private static string BuildSupplierMergeKey(PurchasingSupplierRecord supplier)
    {
        return !string.IsNullOrWhiteSpace(supplier.Code)
            ? $"code:{supplier.Code}"
            : $"name:{supplier.Name}";
    }

    private static string BuildDocumentMergeKey(PurchasingDocumentRecord document)
    {
        return !string.IsNullOrWhiteSpace(document.Number)
            ? $"{document.DocumentType}|{document.Number}"
            : $"{document.DocumentType}|{document.Title}|{document.SupplierName}";
    }

    private static int ScoreSupplier(PurchasingSupplierRecord record)
    {
        var score = 0;
        score += !string.IsNullOrWhiteSpace(record.Name) ? 2 : 0;
        score += !string.IsNullOrWhiteSpace(record.Code) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.TaxId) ? 2 : 0;
        score += !string.IsNullOrWhiteSpace(record.Phone) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.Email) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.Contract) ? 1 : 0;
        score += record.Fields.Count;
        return score;
    }

    private static int ScoreDocument(PurchasingDocumentRecord record)
    {
        var score = 0;
        score += !string.IsNullOrWhiteSpace(record.Number) ? 2 : 0;
        score += record.Date.HasValue ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.SupplierName) ? 2 : 0;
        score += !string.IsNullOrWhiteSpace(record.Contract) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.Warehouse) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.RelatedDocument) ? 1 : 0;
        score += !string.IsNullOrWhiteSpace(record.Comment) ? 1 : 0;
        score += record.Fields.Count;
        score += record.Lines.Count * 2;
        return score;
    }

    private static IReadOnlyList<PurchasingSupplierRecord> BuildSuppliers(
        OneCEntityDataset customers,
        IReadOnlyList<PurchasingDocumentRecord> purchaseOrders,
        IReadOnlyList<PurchasingDocumentRecord> supplierInvoices,
        IReadOnlyList<PurchasingDocumentRecord> purchaseReceipts)
    {
        var docMap = purchaseOrders
            .Concat(supplierInvoices)
            .Concat(purchaseReceipts)
            .Where(item => !string.IsNullOrWhiteSpace(item.SupplierName))
            .GroupBy(item => item.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var supplierRecords = customers.Records
            .Where(record =>
                record.Status.Contains("Поставщик", StringComparison.OrdinalIgnoreCase)
                || docMap.ContainsKey(record.Title))
            .Select(record =>
            {
                docMap.TryGetValue(record.Title, out var linkedDocument);
                return new PurchasingSupplierRecord
                {
                    Name = record.Title,
                    Code = record.Code,
                    Status = record.Status,
                    TaxId = FirstNonEmpty(
                        GetFieldDisplay(record.Fields, "ИНН"),
                        GetFieldDisplay(record.Fields, "КПП")),
                    Phone = ExtractContactValue(record.TabularSections, "НомерТелефона", "Представление"),
                    Email = ExtractContactValue(record.TabularSections, "АдресЭП", "Представление"),
                    Contract = linkedDocument?.Contract ?? string.Empty,
                    SourceLabel = "Контрагент 1С",
                    Fields = record.Fields
                };
            })
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var document in docMap.Values)
        {
            if (supplierRecords.Any(item => item.Name.Equals(document.SupplierName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            supplierRecords.Add(new PurchasingSupplierRecord
            {
                Name = document.SupplierName,
                Code = string.Empty,
                Status = "Поставщик из документа",
                TaxId = string.Empty,
                Phone = string.Empty,
                Email = string.Empty,
                Contract = document.Contract,
                SourceLabel = document.SourceLabel,
                Fields = Array.Empty<OneCFieldValue>()
            });
        }

        return supplierRecords
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PurchasingDocumentRecord> BuildDerivedSupplierInvoices(
        IReadOnlyList<PurchasingDocumentRecord> purchaseOrders,
        IReadOnlyList<PurchasingDocumentRecord> purchaseReceipts)
    {
        var fromReceipts = purchaseReceipts.Select(receipt => new PurchasingDocumentRecord
        {
            DocumentType = "Счет поставщика",
            Number = $"AP-{receipt.Number}",
            Date = receipt.Date,
            Status = "Производный счет к приемке",
            SupplierName = receipt.SupplierName,
            Contract = receipt.Contract,
            Warehouse = receipt.Warehouse,
            RelatedDocument = receipt.Number,
            Comment = receipt.Comment,
            TotalAmount = receipt.TotalAmount,
            SourceLabel = "Производный документ по приемке",
            Title = $"Счет поставщика к приемке {receipt.Number}",
            Subtitle = receipt.SupplierName,
            Fields =
            [
                new OneCFieldValue("ДокументОснование", receipt.Number, receipt.Number),
                new OneCFieldValue("Контрагент", receipt.SupplierName, receipt.SupplierName),
                new OneCFieldValue("Договор", receipt.Contract, receipt.Contract),
                new OneCFieldValue("СуммаДокумента", receipt.TotalAmount.ToString("N2", RuCulture), receipt.TotalAmount.ToString("N2", RuCulture))
            ],
            Lines = receipt.Lines
        });

        if (fromReceipts.Any())
        {
            return fromReceipts
                .OrderByDescending(item => item.Date ?? DateTime.MinValue)
                .ToArray();
        }

        return purchaseOrders
            .Where(order => order.TotalAmount > 0)
            .Select(order => new PurchasingDocumentRecord
            {
                DocumentType = "Счет поставщика",
                Number = $"AP-{order.Number}",
                Date = order.Date,
                Status = "Производный счет к заказу",
                SupplierName = order.SupplierName,
                Contract = order.Contract,
                Warehouse = order.Warehouse,
                RelatedDocument = order.Number,
                Comment = order.Comment,
                TotalAmount = order.TotalAmount,
                SourceLabel = "Производный документ по заказу поставщику",
                Title = $"Счет поставщика к заказу {order.Number}",
                Subtitle = order.SupplierName,
                Fields =
                [
                    new OneCFieldValue("ДокументОснование", order.Number, order.Number),
                    new OneCFieldValue("Контрагент", order.SupplierName, order.SupplierName),
                    new OneCFieldValue("Договор", order.Contract, order.Contract),
                    new OneCFieldValue("СуммаДокумента", order.TotalAmount.ToString("N2", RuCulture), order.TotalAmount.ToString("N2", RuCulture))
                ],
                Lines = order.Lines
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ToArray();
    }

    private static IReadOnlyList<PurchasingDocumentRecord> MapDocuments(
        OneCEntityDataset dataset,
        string documentType,
        Func<OneCRecordSnapshot, PurchasingDocumentRecord> mapper)
    {
        _ = documentType;
        return dataset.Records
            .Select(mapper)
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PurchasingDocumentLineRecord> BuildDocumentLines(OneCRecordSnapshot record)
    {
        if (record.TabularSections.Count == 0)
        {
            return Array.Empty<PurchasingDocumentLineRecord>();
        }

        return record.TabularSections
            .Where(section => section.Rows.Count > 0)
            .SelectMany(section => section.Rows.Select(row => new PurchasingDocumentLineRecord
            {
                SectionName = section.Name,
                RowNumber = row.RowNumber,
                Item = FirstNonEmpty(
                    GetFieldDisplay(row.Fields, "Номенклатура"),
                    GetFieldDisplay(row.Fields, "Содержание"),
                    section.Name.Equals("ПлатежныйКалендарь", StringComparison.OrdinalIgnoreCase)
                        ? "Платеж по графику"
                        : section.Name),
                Quantity = FirstDecimal(row.Fields, "Количество", "ПроцентОплаты"),
                Unit = FirstNonEmpty(
                    GetFieldDisplay(row.Fields, "ЕдиницаИзмерения"),
                    section.Name.Equals("ПлатежныйКалендарь", StringComparison.OrdinalIgnoreCase) ? "%" : string.Empty),
                Price = FirstDecimal(row.Fields, "Цена"),
                Amount = FirstDecimal(row.Fields, "Сумма", "Всего", "СуммаОплаты"),
                PlannedDate = ParseOneCDate(FirstNonEmpty(
                    GetFieldRaw(row.Fields, "ДатаПоступления"),
                    GetFieldRaw(row.Fields, "ДатаОтгрузки"),
                    GetFieldRaw(row.Fields, "ДатаОплаты"))),
                RelatedDocument = FirstNonEmpty(
                    GetFieldDisplay(row.Fields, "ЗаказПокупателя"),
                    GetFieldDisplay(row.Fields, "ЗаказПоставщику"),
                    GetFieldDisplay(row.Fields, "Заказ")),
                Fields = row.Fields
            }))
            .OrderBy(item => item.SectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RowNumber)
            .ToArray();
    }

    private static string BuildSourceLabel(OneCEntityDataset dataset)
    {
        return dataset.Schema?.SourceFileName is { Length: > 0 } fileName
            ? $"1С / {fileName}"
            : $"1С / {dataset.ObjectName}";
    }

    private static string ExtractContactValue(
        IReadOnlyList<OneCTabularSectionSnapshot> sections,
        params string[] preferredFields)
    {
        var section = sections.FirstOrDefault(
            item => string.Equals(item.Name, "КонтактнаяИнформация", StringComparison.OrdinalIgnoreCase));
        if (section is null)
        {
            return string.Empty;
        }

        foreach (var fieldName in preferredFields)
        {
            var value = section.Rows
                .Select(row => GetFieldDisplay(row.Fields, fieldName))
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string GetFieldDisplay(IEnumerable<OneCFieldValue> fields, string fieldName)
    {
        return fields.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue
            ?? string.Empty;
    }

    private static string GetFieldRaw(IEnumerable<OneCFieldValue> fields, string fieldName)
    {
        return fields.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.RawValue
            ?? string.Empty;
    }

    private static decimal FirstDecimal(IEnumerable<OneCFieldValue> fields, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (TryParseDecimal(GetFieldRaw(fields, fieldName), out var result))
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

    private static DateTime? ParseOneCDate(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var formats = new[]
        {
            "dd.MM.yyyy",
            "dd.MM.yyyy H:mm:ss",
            "dd.MM.yyyy HH:mm:ss",
            "dd.MM.yyyy H:mm",
            "dd.MM.yyyy HH:mm"
        };

        return DateTime.TryParseExact(rawValue, formats, RuCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}

public sealed class PurchasingSupplierRecord
{
    public string Name { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string TaxId { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Contract { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; init; } = Array.Empty<OneCFieldValue>();
}

public sealed class PurchasingDocumentRecord
{
    public string DocumentType { get; init; } = string.Empty;

    public string Number { get; init; } = string.Empty;

    public DateTime? Date { get; init; }

    public string Status { get; init; } = string.Empty;

    public string SupplierName { get; init; } = string.Empty;

    public string Contract { get; init; } = string.Empty;

    public string Warehouse { get; init; } = string.Empty;

    public string RelatedDocument { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public string SourceLabel { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; init; } = Array.Empty<OneCFieldValue>();

    public IReadOnlyList<PurchasingDocumentLineRecord> Lines { get; init; } = Array.Empty<PurchasingDocumentLineRecord>();
}

public sealed class PurchasingDocumentLineRecord
{
    public string SectionName { get; init; } = string.Empty;

    public int RowNumber { get; init; }

    public string Item { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public string Unit { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public decimal Amount { get; init; }

    public DateTime? PlannedDate { get; init; }

    public string RelatedDocument { get; init; } = string.Empty;

    public IReadOnlyList<OneCFieldValue> Fields { get; init; } = Array.Empty<OneCFieldValue>();
}
