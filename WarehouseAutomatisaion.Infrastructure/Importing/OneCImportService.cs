using System.Globalization;
using Microsoft.VisualBasic.FileIO;

namespace WarehouseAutomatisaion.Infrastructure.Importing;

public sealed class OneCImportService
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly string _workspaceRoot;
    private readonly IReadOnlyList<string>? _preferredExportRoots;

    public OneCImportService(string workspaceRoot, IReadOnlyList<string>? preferredExportRoots = null)
    {
        _workspaceRoot = workspaceRoot;
        _preferredExportRoots = preferredExportRoots;
    }

    public static OneCImportService CreateDefault()
    {
        return new OneCImportService(WorkspacePathResolver.ResolveWorkspaceRoot());
    }

    public OneCImportSnapshot LoadSnapshot()
    {
        var exportRoots = FindExportRoots();
        var manifestEntries = LoadManifestEntries(exportRoots);
        var schemaMap = LoadSchemaDefinitions();
        var referenceLookup = BuildReferenceLookup(manifestEntries);

        return new OneCImportSnapshot
        {
            SourceFolders = exportRoots,
            Customers = LoadDataset(
                manifestEntries,
                "Catalog",
                "Контрагенты",
                "Контрагенты 1С",
                "Реальные карточки покупателей и поставщиков из базы 1С.",
                schemaMap,
                referenceLookup,
                BuildCustomerRecord),
            Items = LoadDataset(
                manifestEntries,
                "Catalog",
                "Номенклатура",
                "Номенклатура 1С",
                "Номенклатура из 1С со всеми выгруженными полями карточки товара.",
                schemaMap,
                referenceLookup,
                BuildItemRecord),
            SalesOrders = LoadDataset(
                manifestEntries,
                "Document",
                "ЗаказПокупателя",
                "Заказы покупателей 1С",
                "Заказы покупателей и их табличные части из 1С.",
                schemaMap,
                referenceLookup,
                BuildSalesOrderRecord),
            SalesInvoices = LoadDataset(
                manifestEntries,
                "Document",
                "СчетНаОплату",
                "Счета на оплату 1С",
                "Счета на оплату покупателей и их табличные части из 1С.",
                schemaMap,
                referenceLookup,
                BuildSalesInvoiceRecord),
            SalesShipments = LoadDataset(
                manifestEntries,
                "Document",
                "РасходнаяНакладная",
                "Расходные накладные 1С",
                "Отгрузки и расходные накладные из 1С.",
                schemaMap,
                referenceLookup,
                BuildSalesShipmentRecord),
            PurchaseOrders = LoadDataset(
                manifestEntries,
                "Document",
                "ЗаказПоставщику",
                "Заказы поставщику 1С",
                "Заказы поставщикам, закупочные строки и материалы из 1С.",
                schemaMap,
                referenceLookup,
                BuildPurchaseOrderRecord,
                allowSchemaProbeFallback: true),
            SupplierInvoices = LoadDataset(
                manifestEntries,
                "Document",
                "СчетНаОплатуПоставщика",
                "Счета поставщиков 1С",
                "Входящие счета поставщиков и графики оплат из 1С.",
                schemaMap,
                referenceLookup,
                BuildSupplierInvoiceRecord,
                allowSchemaProbeFallback: true),
            PurchaseReceipts = LoadDataset(
                manifestEntries,
                "Document",
                "ПриходнаяНакладная",
                "Приемка 1С",
                "Приходные накладные, расходы и приемка поставок из 1С.",
                schemaMap,
                referenceLookup,
                BuildPurchaseReceiptRecord,
                allowSchemaProbeFallback: true),
            TransferOrders = LoadDataset(
                manifestEntries,
                "Document",
                "ЗаказНаПеремещение",
                "Перемещения 1С",
                "Заказы на перемещение, сборка и внутренняя логистика из 1С.",
                schemaMap,
                referenceLookup,
                BuildTransferOrderRecord,
                allowSchemaProbeFallback: true),
            StockReservations = LoadDataset(
                manifestEntries,
                "Document",
                "РезервированиеЗапасов",
                "Резервы 1С",
                "Документы резервирования запасов из 1С.",
                schemaMap,
                referenceLookup,
                BuildReservationRecord,
                allowSchemaProbeFallback: true),
            InventoryCounts = LoadDataset(
                manifestEntries,
                "Document",
                "ИнвентаризацияЗапасов",
                "Инвентаризации 1С",
                "Документы инвентаризации и пересчета запасов из 1С.",
                schemaMap,
                referenceLookup,
                BuildInventoryCountRecord,
                allowSchemaProbeFallback: true),
            StockWriteOffs = LoadDataset(
                manifestEntries,
                "Document",
                "СписаниеЗапасов",
                "Списания 1С",
                "Документы списания запасов и потерь из 1С.",
                schemaMap,
                referenceLookup,
                BuildWriteOffRecord,
                allowSchemaProbeFallback: true),
            Schemas = schemaMap.Values.OrderBy(schema => schema.ObjectName, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private List<string> FindExportRoots()
    {
        if (_preferredExportRoots is { Count: > 0 })
        {
            return _preferredExportRoots
                .Where(path => Directory.Exists(path) && File.Exists(Path.Combine(path, "manifest.csv")))
                .Where(path => !IsIgnoredImportRoot(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .ToList();
        }

        if (!Directory.Exists(_workspaceRoot))
        {
            return [];
        }

        var exportRoots = Directory
            .GetDirectories(_workspaceRoot, "exports*")
            .Where(path => File.Exists(Path.Combine(path, "manifest.csv")))
            .ToList();

        var liveImportRoot = Path.Combine(_workspaceRoot, "app_data", "one-c-live");
        if (Directory.Exists(liveImportRoot))
        {
            exportRoots.AddRange(
                Directory
                    .GetDirectories(liveImportRoot)
                    .Where(path => !IsIgnoredImportRoot(path))
                    .Where(path => File.Exists(Path.Combine(path, "manifest.csv"))));
        }

        return exportRoots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToList();
    }

    private static bool IsIgnoredImportRoot(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("smoke-", StringComparison.OrdinalIgnoreCase);
    }

    private List<ManifestEntry> LoadManifestEntries(IEnumerable<string> exportRoots)
    {
        var entries = new List<ManifestEntry>();

        foreach (var exportRoot in exportRoots)
        {
            var manifestPath = Path.Combine(exportRoot, "manifest.csv");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                foreach (var row in ReadCsvRows(manifestPath))
                {
                    var status = GetValue(row, "status");
                    var filePath = GetValue(row, "file_path");
                    if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(filePath))
                    {
                        continue;
                    }

                    entries.Add(new ManifestEntry(
                        exportRoot,
                        GetValue(row, "object_type"),
                        GetValue(row, "object_name"),
                        GetValue(row, "subobject_name"),
                        filePath,
                        ParseInt(GetValue(row, "row_count"))));
                }
            }
            catch
            {
                // Ignore broken manifests and continue with healthy export folders.
            }
        }

        return entries;
    }

    private Dictionary<string, OneCSchemaDefinition> LoadSchemaDefinitions()
    {
        var schemaDirectory = Path.Combine(_workspaceRoot, "model_schema");
        var result = new Dictionary<string, OneCSchemaDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(schemaDirectory))
        {
            return result;
        }

        foreach (var filePath in Directory.GetFiles(schemaDirectory, "*.txt"))
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0)
            {
                continue;
            }

            var headerParts = lines[0].Split('|');
            if (headerParts.Length < 4 || !string.Equals(headerParts[0], "OBJECT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var columns = new List<string>();
            var sectionColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines.Skip(1))
            {
                if (line.StartsWith("COL|", StringComparison.Ordinal))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        columns.Add(parts[2]);
                    }
                }
                else if (line.StartsWith("TS|", StringComparison.Ordinal))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        sectionColumns.TryAdd(parts[2], []);
                    }
                }
                else if (line.StartsWith("TS_COL|", StringComparison.Ordinal))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        if (!sectionColumns.TryGetValue(parts[1], out var values))
                        {
                            values = [];
                            sectionColumns[parts[1]] = values;
                        }

                        values.Add(parts[3]);
                    }
                }
            }

            result[headerParts[3]] = new OneCSchemaDefinition
            {
                Kind = headerParts[1],
                ObjectName = headerParts[3],
                SourceFileName = Path.GetFileName(filePath),
                Columns = columns,
                TabularSections = sectionColumns
                    .Select(item => new OneCSchemaTabularSectionDefinition
                    {
                        Name = item.Key,
                        Columns = item.Value
                    })
                    .OrderBy(section => section.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        return result;
    }

    private Dictionary<string, string> BuildReferenceLookup(IReadOnlyList<ManifestEntry> manifestEntries)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        AddReferenceLookup(lookup, manifestEntries, "Catalog", "Валюты", ["Код", "Наименование"]);
        AddReferenceLookup(lookup, manifestEntries, "Catalog", "ВидыЦен", ["Наименование", "Код"]);
        AddReferenceLookup(lookup, manifestEntries, "Catalog", "БанковскиеСчета", ["НомерСчета", "Наименование"]);
        AddReferenceLookup(lookup, manifestEntries, "Catalog", "Контрагенты", ["Наименование", "Код"]);
        AddReferenceLookup(lookup, manifestEntries, "Catalog", "ДоговорыКонтрагентов", ["Наименование", "Код"]);
        AddReferenceLookup(lookup, manifestEntries, "Catalog", "Номенклатура", ["Наименование", "Код", "Артикул"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "ЗаказПокупателя", ["Номер"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "СчетНаОплату", ["Номер"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "РасходнаяНакладная", ["Номер"]);

        AddReferenceLookup(lookup, manifestEntries, "Catalog", "ЕдиницыИзмерения", ["Наименование", "Код", "МеждународноеСокращение"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "ЗаказПоставщику", ["Номер"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "СчетНаОплатуПоставщика", ["Номер"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "ПриходнаяНакладная", ["Номер"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "ЗаказНаПеремещение", ["Номер"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "ИнвентаризацияЗапасов", ["Номер"]);
        AddReferenceLookup(lookup, manifestEntries, "Document", "СписаниеЗапасов", ["Номер"]);

        return lookup;
    }

    private void AddReferenceLookup(
        IDictionary<string, string> referenceLookup,
        IReadOnlyList<ManifestEntry> manifestEntries,
        string objectType,
        string objectName,
        IReadOnlyList<string> titleFields)
    {
        var mainEntry = SelectLatestEntry(manifestEntries, objectType, objectName, string.Empty);
        if (mainEntry is null || !File.Exists(mainEntry.FilePath))
        {
            return;
        }

        try
        {
            foreach (var row in ReadCsvRows(mainEntry.FilePath))
            {
                var reference = NormalizeOneCValue(GetValue(row, "Ссылка"));
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                var title = BuildCompositeValue(row, titleFields);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    referenceLookup[reference] = title;
                }
            }
        }
        catch
        {
            // Ignore broken lookup files and fall back to raw reference values.
        }
    }

    private OneCEntityDataset LoadDataset(
        IReadOnlyList<ManifestEntry> manifestEntries,
        string objectType,
        string objectName,
        string displayName,
        string emptySummary,
        IReadOnlyDictionary<string, OneCSchemaDefinition> schemaMap,
        IReadOnlyDictionary<string, string> referenceLookup,
        Func<RecordBuildContext, OneCRecordSnapshot> builder,
        bool allowSchemaProbeFallback = false)
    {
        TryGetSchema(schemaMap, objectName, out var schema);
        var mainEntry = SelectLatestEntry(manifestEntries, objectType, objectName, string.Empty);
        var manifestSectionEntries = manifestEntries
            .Where(entry =>
                string.Equals(entry.ObjectType, objectType, StringComparison.OrdinalIgnoreCase)
                && OneCTextNormalizer.TextEquals(entry.ObjectName, objectName)
                && !string.IsNullOrWhiteSpace(entry.SubobjectName))
            .ToList();

        if (mainEntry is null || !File.Exists(mainEntry.FilePath))
        {
            if (allowSchemaProbeFallback && schema is not null)
            {
                return LoadSchemaProbeDataset(
                    objectName,
                    displayName,
                    emptySummary,
                    schema,
                    referenceLookup,
                    builder);
            }

            return new OneCEntityDataset
            {
                ObjectName = objectName,
                DisplayName = displayName,
                Summary = $"{emptySummary} Выгруженный CSV пока не найден, поэтому доступны только схема и карта полей.",
                Schema = schema,
                Records = Array.Empty<OneCRecordSnapshot>()
            };
        }

        var sectionEntries = ResolveSectionEntries(
            manifestSectionEntries,
            mainEntry,
            objectType,
            objectName,
            schema);
        var excludedMainFields = BuildExcludedMainFields(schema, sectionEntries);

        try
        {
            var mainRows = ReadCsvRows(mainEntry.FilePath);
            var sectionsByReference = LoadTabularSections(sectionEntries, referenceLookup);
            var records = new List<OneCRecordSnapshot>(mainRows.Count);

            foreach (var row in mainRows)
            {
                var fields = CreateFieldValues(row, referenceLookup, schema?.Columns, excludedMainFields);
                var reference = GetFieldRawValue(fields, "Ссылка");
                sectionsByReference.TryGetValue(reference, out var sections);
                records.Add(builder(new RecordBuildContext(objectName, fields, sections ?? [], schema)));
            }

            return new OneCEntityDataset
            {
                ObjectName = objectName,
                DisplayName = displayName,
                Summary = records.Count == 0
                    ? $"{emptySummary} Строки еще не выгружены."
                    : $"{emptySummary} Загружено {records.Count:N0} строк из {Path.GetFileName(mainEntry.FilePath)}.",
                Schema = schema,
                Records = records
            };
        }
        catch (IOException)
        {
            return new OneCEntityDataset
            {
                ObjectName = objectName,
                DisplayName = displayName,
                Summary = $"{emptySummary} Файл выгрузки еще записывается, поэтому карточки временно недоступны.",
                Schema = schema,
                Records = Array.Empty<OneCRecordSnapshot>()
            };
        }
        catch
        {
            return new OneCEntityDataset
            {
                ObjectName = objectName,
                DisplayName = displayName,
                Summary = $"{emptySummary} Файл найден, но не удалось разобрать его содержимое.",
                Schema = schema,
                Records = Array.Empty<OneCRecordSnapshot>()
            };
        }
    }

    private Dictionary<string, List<OneCTabularSectionSnapshot>> LoadTabularSections(
        IEnumerable<ManifestEntry> sectionEntries,
        IReadOnlyDictionary<string, string> referenceLookup)
    {
        var result = new Dictionary<string, List<OneCTabularSectionSnapshot>>(StringComparer.Ordinal);

        foreach (var entry in sectionEntries.OrderBy(item => item.SubobjectName, StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(entry.FilePath))
            {
                continue;
            }

            var rows = ReadCsvRows(entry.FilePath);
            if (rows.Count == 0)
            {
                continue;
            }

            foreach (var group in rows.GroupBy(row => NormalizeOneCValue(GetValue(row, "Ссылка")), StringComparer.Ordinal))
            {
                var sectionRows = group
                    .Select(row =>
                    {
                        var fields = CreateFieldValues(row, referenceLookup, row.Keys.ToArray());
                        return new OneCTabularSectionRowSnapshot
                        {
                            RowNumber = ParseInt(GetFieldRawValue(fields, "НомерСтроки")),
                            Fields = fields
                        };
                    })
                    .OrderBy(row => row.RowNumber)
                    .ToArray();

                if (!result.TryGetValue(group.Key, out var sections))
                {
                    sections = [];
                    result[group.Key] = sections;
                }

                sections.Add(new OneCTabularSectionSnapshot
                {
                    Name = entry.SubobjectName,
                    Columns = rows[0].Keys.ToArray(),
                    Rows = sectionRows
                });
            }
        }

        foreach (var sectionList in result.Values)
        {
            sectionList.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    private static HashSet<string> BuildExcludedMainFields(
        OneCSchemaDefinition? schema,
        IReadOnlyList<ManifestEntry> sectionEntries)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in sectionEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.SubobjectName))
            {
                excluded.Add(entry.SubobjectName);
            }
        }

        if (schema is null)
        {
            return excluded;
        }

        foreach (var section in schema.TabularSections)
        {
            if (!string.IsNullOrWhiteSpace(section.Name))
            {
                excluded.Add(section.Name);
            }
        }

        return excluded;
    }

    private static List<ManifestEntry> ResolveSectionEntries(
        IReadOnlyList<ManifestEntry> manifestSectionEntries,
        ManifestEntry mainEntry,
        string objectType,
        string objectName,
        OneCSchemaDefinition? schema)
    {
        var resolved = manifestSectionEntries
            .GroupBy(entry => entry.SubobjectName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => GetEntryTimestamp(item.FilePath)).First())
            .ToList();

        if (schema is null)
        {
            return resolved;
        }

        var knownSections = new HashSet<string>(
            resolved.Select(entry => entry.SubobjectName),
            StringComparer.OrdinalIgnoreCase);

        var mainDirectory = Path.GetDirectoryName(mainEntry.FilePath);
        if (string.IsNullOrWhiteSpace(mainDirectory) || !Directory.Exists(mainDirectory))
        {
            return resolved;
        }

        var mainFileName = Path.GetFileNameWithoutExtension(mainEntry.FilePath);
        if (string.IsNullOrWhiteSpace(mainFileName))
        {
            return resolved;
        }

        var fallbackFiles = Directory
            .GetFiles(mainDirectory, $"{mainFileName}_ts_*.csv")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var filePath in fallbackFiles)
        {
            var suffix = Path.GetFileNameWithoutExtension(filePath);
            var token = suffix[(mainFileName.Length + 4)..];
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oneBasedIndex))
            {
                continue;
            }

            var schemaIndex = oneBasedIndex - 1;
            if (schemaIndex < 0 || schemaIndex >= schema.TabularSections.Count)
            {
                continue;
            }

            var sectionName = schema.TabularSections[schemaIndex].Name;
            if (string.IsNullOrWhiteSpace(sectionName)
                || knownSections.Any(value => OneCTextNormalizer.TextEquals(value, sectionName)))
            {
                continue;
            }

            resolved.Add(new ManifestEntry(
                mainEntry.ExportRoot,
                objectType,
                objectName,
                sectionName,
                filePath,
                0));
            knownSections.Add(sectionName);
        }

        return resolved
            .OrderBy(entry => entry.SubobjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private OneCEntityDataset LoadSchemaProbeDataset(
        string objectName,
        string displayName,
        string emptySummary,
        OneCSchemaDefinition schema,
        IReadOnlyDictionary<string, string> referenceLookup,
        Func<RecordBuildContext, OneCRecordSnapshot> builder)
    {
        var probePath = Path.Combine(_workspaceRoot, "model_schema", schema.SourceFileName);
        if (!File.Exists(probePath))
        {
            return new OneCEntityDataset
            {
                ObjectName = objectName,
                DisplayName = displayName,
                Summary = $"{emptySummary} Не найден ни CSV, ни probe-файл {schema.SourceFileName}.",
                Schema = schema,
                Records = Array.Empty<OneCRecordSnapshot>()
            };
        }

        try
        {
            var records = ReadSchemaProbeRecords(objectName, schema, probePath, referenceLookup, builder);
            return new OneCEntityDataset
            {
                ObjectName = objectName,
                DisplayName = displayName,
                Summary = records.Count == 0
                    ? $"{emptySummary} CSV еще не выгружен, поэтому доступна только схема из {schema.SourceFileName}."
                    : $"{emptySummary} Загружено {records.Count:N0} примерных строк из {schema.SourceFileName}.",
                Schema = schema,
                Records = records
            };
        }
        catch
        {
            return new OneCEntityDataset
            {
                ObjectName = objectName,
                DisplayName = displayName,
                Summary = $"{emptySummary} Probe-файл найден, но не удалось разобрать его содержимое.",
                Schema = schema,
                Records = Array.Empty<OneCRecordSnapshot>()
            };
        }
    }

    private List<OneCRecordSnapshot> ReadSchemaProbeRecords(
        string objectName,
        OneCSchemaDefinition schema,
        string filePath,
        IReadOnlyDictionary<string, string> referenceLookup,
        Func<RecordBuildContext, OneCRecordSnapshot> builder)
    {
        var records = new List<OneCRecordSnapshot>();
        var lines = File.ReadAllLines(filePath);
        var currentFields = new List<OneCFieldValue>();
        var sectionsByName = new Dictionary<string, MutableProbeSection>(StringComparer.OrdinalIgnoreCase);
        MutableFieldTarget? lastTarget = null;
        var hasRow = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("ROW|", StringComparison.Ordinal))
            {
                CompleteRecord();
                currentFields = [];
                sectionsByName = new Dictionary<string, MutableProbeSection>(StringComparer.OrdinalIgnoreCase);
                lastTarget = null;
                hasRow = true;
                continue;
            }

            if (line.StartsWith("VAL|", StringComparison.Ordinal))
            {
                if (!hasRow)
                {
                    continue;
                }

                var parts = line.Split('|', 3);
                var field = CreateProbeField(
                    parts.Length > 1 ? parts[1] : string.Empty,
                    parts.Length > 2 ? parts[2] : string.Empty,
                    referenceLookup);
                currentFields.Add(field);
                lastTarget = new MutableFieldTarget(currentFields, currentFields.Count - 1);
                continue;
            }

            if (line.StartsWith("TS_ROW|", StringComparison.Ordinal))
            {
                if (!hasRow)
                {
                    continue;
                }

                var parts = line.Split('|', 3);
                var section = GetOrCreateSection(parts.Length > 1 ? parts[1] : string.Empty);
                section.Rows.Add(new MutableProbeRow
                {
                    RowNumber = parts.Length > 2 ? ParseInt(parts[2]) : 0
                });
                lastTarget = null;
                continue;
            }

            if (line.StartsWith("TS_VAL|", StringComparison.Ordinal))
            {
                if (!hasRow)
                {
                    continue;
                }

                var parts = line.Split('|', 4);
                var section = GetOrCreateSection(parts.Length > 1 ? parts[1] : string.Empty);
                if (section.Rows.Count == 0)
                {
                    section.Rows.Add(new MutableProbeRow());
                }

                var row = section.Rows[^1];
                row.Fields.Add(CreateProbeField(
                    parts.Length > 2 ? parts[2] : string.Empty,
                    parts.Length > 3 ? parts[3] : string.Empty,
                    referenceLookup));
                lastTarget = new MutableFieldTarget(row.Fields, row.Fields.Count - 1);
                continue;
            }

            if (!IsProbeMetadataLine(line) && lastTarget is not null)
            {
                var field = lastTarget.Fields[lastTarget.Index];
                var rawValue = string.IsNullOrEmpty(field.RawValue)
                    ? line
                    : $"{field.RawValue}{Environment.NewLine}{line}";
                var displayValue = string.IsNullOrEmpty(field.DisplayValue)
                    ? line
                    : $"{field.DisplayValue}{Environment.NewLine}{line}";
                lastTarget.Fields[lastTarget.Index] = field with
                {
                    RawValue = rawValue,
                    DisplayValue = displayValue
                };
            }
        }

        CompleteRecord();
        return records;

        MutableProbeSection GetOrCreateSection(string sectionName)
        {
            if (!sectionsByName.TryGetValue(sectionName, out var section))
            {
                section = new MutableProbeSection
                {
                    Name = sectionName,
                    Columns = schema.TabularSections
                        .FirstOrDefault(item => string.Equals(item.Name, sectionName, StringComparison.OrdinalIgnoreCase))
                        ?.Columns
                        ?? Array.Empty<string>()
                };
                sectionsByName[sectionName] = section;
            }

            return section;
        }

        void CompleteRecord()
        {
            if (!hasRow || (currentFields.Count == 0 && sectionsByName.Count == 0))
            {
                hasRow = false;
                return;
            }

            var sections = sectionsByName.Values
                .Select(section => new OneCTabularSectionSnapshot
                {
                    Name = section.Name,
                    Columns = section.Columns,
                    Rows = section.Rows
                        .Select(row => new OneCTabularSectionRowSnapshot
                        {
                            RowNumber = row.RowNumber,
                            Fields = row.Fields.ToArray()
                        })
                        .OrderBy(row => row.RowNumber)
                        .ToArray()
                })
                .OrderBy(section => section.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            records.Add(builder(new RecordBuildContext(objectName, currentFields.ToArray(), sections, schema)));
            hasRow = false;
            lastTarget = null;
        }
    }

    private static OneCFieldValue CreateProbeField(
        string fieldName,
        string rawValue,
        IReadOnlyDictionary<string, string> referenceLookup)
    {
        var normalizedRaw = NormalizeOneCValue(rawValue);
        return new OneCFieldValue(
            fieldName,
            normalizedRaw,
            ResolveDisplayValue(normalizedRaw, referenceLookup));
    }

    private static bool IsProbeMetadataLine(string line)
    {
        return line.StartsWith("OBJECT|", StringComparison.Ordinal)
            || line.StartsWith("COLUMNS|", StringComparison.Ordinal)
            || line.StartsWith("COL|", StringComparison.Ordinal)
            || line.StartsWith("ROW|", StringComparison.Ordinal)
            || string.Equals(line, "NO_ROWS", StringComparison.Ordinal)
            || line.StartsWith("TABULAR|", StringComparison.Ordinal)
            || line.StartsWith("TS|", StringComparison.Ordinal)
            || line.StartsWith("TS_COLUMNS|", StringComparison.Ordinal)
            || line.StartsWith("TS_COL|", StringComparison.Ordinal)
            || line.StartsWith("TS_ROW|", StringComparison.Ordinal)
            || line.StartsWith("TS_VAL|", StringComparison.Ordinal)
            || line.StartsWith("TS_NO_ROWS|", StringComparison.Ordinal);
    }

    private static OneCRecordSnapshot BuildCustomerRecord(RecordBuildContext context)
    {
        var phone = ExtractContactValue(context.TabularSections, "НомерТелефона", "Представление");
        var email = ExtractContactValue(context.TabularSections, "АдресЭП", "Представление");
        var statusParts = new List<string>();
        if (IsTrue(context.Fields, "Покупатель"))
        {
            statusParts.Add("Покупатель");
        }

        if (IsTrue(context.Fields, "Поставщик"))
        {
            statusParts.Add("Поставщик");
        }

        if (IsTrue(context.Fields, "Недействителен"))
        {
            statusParts.Add("Недействителен");
        }

        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Code = GetFieldDisplayValue(context.Fields, "Код"),
            Title = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "Наименование"),
                GetFieldDisplayValue(context.Fields, "НаименованиеПолное"),
                GetFieldDisplayValue(context.Fields, "ФИО")),
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "ИНН"),
                GetFieldDisplayValue(context.Fields, "КПП"),
                phone,
                email),
            Status = statusParts.Count == 0 ? "Карточка контрагента" : string.Join(", ", statusParts),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "ДатаСоздания")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildItemRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Code = GetFieldDisplayValue(context.Fields, "Код"),
            Title = GetFieldDisplayValue(context.Fields, "Наименование"),
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "Артикул"),
                GetFieldDisplayValue(context.Fields, "ТипНоменклатуры"),
                GetFieldDisplayValue(context.Fields, "ВидСтавкиНДС")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "КатегорияНоменклатуры"),
                GetFieldDisplayValue(context.Fields, "ЦеноваяГруппа"),
                "Карточка номенклатуры"),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "ДатаИзменения")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildSalesOrderRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Заказ {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "Контрагент"),
                GetFieldDisplayValue(context.Fields, "Организация"),
                GetFieldDisplayValue(context.Fields, "ВидЦен")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "СостояниеЗаказа"),
                GetFieldDisplayValue(context.Fields, "СтатусСборки"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildSalesInvoiceRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Счет {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "Контрагент"),
                GetFieldDisplayValue(context.Fields, "ДокументОснование"),
                GetFieldDisplayValue(context.Fields, "ВидЦен")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "ОплатаДо"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildSalesShipmentRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Отгрузка {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "Контрагент"),
                GetFieldDisplayValue(context.Fields, "Заказ"),
                GetFieldDisplayValue(context.Fields, "Перевозчик")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "ВидОперации"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildPurchaseOrderRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Заказ поставщику {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "Контрагент"),
                GetFieldDisplayValue(context.Fields, "Договор"),
                GetFieldDisplayValue(context.Fields, "СтруктурнаяЕдиница")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "СостояниеЗаказа"),
                GetFieldDisplayValue(context.Fields, "ВидОперации"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildSupplierInvoiceRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Счет поставщика {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "Контрагент"),
                GetFieldDisplayValue(context.Fields, "ДокументОснование"),
                GetFieldDisplayValue(context.Fields, "БанковскийСчетПоставщика")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "Комментарий"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildPurchaseReceiptRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Приемка {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "Контрагент"),
                GetFieldDisplayValue(context.Fields, "Заказ"),
                GetFieldDisplayValue(context.Fields, "СтруктурнаяЕдиница")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "ВидОперации"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildTransferOrderRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Перемещение {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "СтруктурнаяЕдиницаРезерв"),
                GetFieldDisplayValue(context.Fields, "СтруктурнаяЕдиницаПолучатель"),
                GetFieldDisplayValue(context.Fields, "ЗаказПокупателя")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "СостояниеЗаказа"),
                GetFieldDisplayValue(context.Fields, "СтатусСборки"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildReservationRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Резерв {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "ЗаказПокупателя"),
                GetFieldDisplayValue(context.Fields, "ИсходноеМестоРезерва"),
                GetFieldDisplayValue(context.Fields, "НовоеМестоРезерва")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "ВидОперации"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildInventoryCountRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Инвентаризация {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "СтруктурнаяЕдиница"),
                GetFieldDisplayValue(context.Fields, "Ячейка")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "Комментарий"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static OneCRecordSnapshot BuildWriteOffRecord(RecordBuildContext context)
    {
        return new OneCRecordSnapshot
        {
            ObjectName = context.ObjectName,
            Reference = GetFieldRawValue(context.Fields, "Ссылка"),
            Number = GetFieldDisplayValue(context.Fields, "Номер"),
            Title = $"Списание {GetFieldDisplayValue(context.Fields, "Номер")}",
            Subtitle = JoinNonEmpty(" | ",
                GetFieldDisplayValue(context.Fields, "СтруктурнаяЕдиница"),
                GetFieldDisplayValue(context.Fields, "ДокументОснование"),
                GetFieldDisplayValue(context.Fields, "ПричинаСписания")),
            Status = FirstNonEmpty(
                GetFieldDisplayValue(context.Fields, "ХозяйственнаяОперация"),
                BoolToStatus(GetFieldRawValue(context.Fields, "Проведен"))),
            Date = ParseOneCDate(GetFieldRawValue(context.Fields, "Дата")),
            Fields = context.Fields,
            TabularSections = context.TabularSections
        };
    }

    private static string ExtractContactValue(
        IReadOnlyList<OneCTabularSectionSnapshot> sections,
        params string[] preferredFields)
    {
        var contactSection = sections.FirstOrDefault(
            section => string.Equals(section.Name, "КонтактнаяИнформация", StringComparison.OrdinalIgnoreCase));

        if (contactSection is null)
        {
            return string.Empty;
        }

        foreach (var fieldName in preferredFields)
        {
            var value = contactSection.Rows
                .Select(row => row.FindField(fieldName)?.DisplayValue)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static List<OneCFieldValue> CreateFieldValues(
        IReadOnlyDictionary<string, string> row,
        IReadOnlyDictionary<string, string> referenceLookup,
        IReadOnlyList<string>? preferredOrder,
        IReadOnlySet<string>? excludedFields = null)
    {
        var orderedKeys = preferredOrder is null || preferredOrder.Count == 0
            ? row.Keys.ToArray()
            : preferredOrder.Where(row.ContainsKey).Concat(row.Keys.Where(key => !preferredOrder.Contains(key))).ToArray();

        var fields = new List<OneCFieldValue>(orderedKeys.Length);
        foreach (var key in orderedKeys)
        {
            if (excludedFields is not null && excludedFields.Contains(key))
            {
                continue;
            }

            var raw = NormalizeOneCValue(GetValue(row, key));
            var display = ResolveDisplayValue(raw, referenceLookup);
            fields.Add(new OneCFieldValue(key, raw, display));
        }

        return fields;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> row, string key)
    {
        if (row.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var item in row)
        {
            if (OneCTextNormalizer.TextEquals(item.Key, key))
            {
                return item.Value;
            }
        }

        return string.Empty;
    }

    private static string GetFieldRawValue(IEnumerable<OneCFieldValue> fields, string fieldName)
    {
        return fields.FirstOrDefault(field => OneCTextNormalizer.TextEquals(field.Name, fieldName))?.RawValue ?? string.Empty;
    }

    private static string GetFieldDisplayValue(IEnumerable<OneCFieldValue> fields, string fieldName)
    {
        return fields.FirstOrDefault(field => OneCTextNormalizer.TextEquals(field.Name, fieldName))?.DisplayValue ?? string.Empty;
    }

    private static bool IsTrue(IEnumerable<OneCFieldValue> fields, string fieldName)
    {
        return OneCTextNormalizer.TextEquals(GetFieldRawValue(fields, fieldName), "Истина");
    }

    private static string NormalizeOneCValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var value = rawValue.Trim();
        return value is "{\"L\"}" or "{\"\"L\"\"}"
            ? string.Empty
            : value;
    }

    private static string ResolveDisplayValue(string rawValue, IReadOnlyDictionary<string, string> referenceLookup)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        return referenceLookup.TryGetValue(rawValue, out var resolved)
            ? resolved
            : rawValue;
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

    private static string JoinNonEmpty(string separator, params string[] values)
    {
        return string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildCompositeValue(IReadOnlyDictionary<string, string> row, IReadOnlyList<string> titleFields)
    {
        var values = new List<string>();
        foreach (var fieldName in titleFields)
        {
            var value = NormalizeOneCValue(GetValue(row, fieldName));
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return string.Join(" | ", values);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string BoolToStatus(string rawValue)
    {
        return OneCTextNormalizer.TextEquals(rawValue, "Истина")
            ? "Проведен"
            : "Черновик";
    }

    private static int ParseInt(string rawValue)
    {
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static DateTime GetEntryTimestamp(string filePath)
    {
        return File.Exists(filePath)
            ? File.GetLastWriteTimeUtc(filePath)
            : DateTime.MinValue;
    }

    private static ManifestEntry? SelectLatestEntry(
        IReadOnlyList<ManifestEntry> manifestEntries,
        string objectType,
        string objectName,
        string subobjectName)
    {
        return manifestEntries
            .Where(entry =>
                string.Equals(entry.ObjectType, objectType, StringComparison.OrdinalIgnoreCase)
                && OneCTextNormalizer.TextEquals(entry.ObjectName, objectName)
                && OneCTextNormalizer.TextEquals(entry.SubobjectName, subobjectName))
            .OrderByDescending(entry => entry.RowCount > 0)
            .ThenByDescending(entry => entry.RowCount)
            .ThenByDescending(entry => GetEntryTimestamp(entry.FilePath))
            .FirstOrDefault();
    }

    private static bool TryGetSchema(
        IReadOnlyDictionary<string, OneCSchemaDefinition> schemaMap,
        string objectName,
        out OneCSchemaDefinition? schema)
    {
        if (schemaMap.TryGetValue(objectName, out schema))
        {
            return true;
        }

        foreach (var item in schemaMap)
        {
            if (OneCTextNormalizer.TextEquals(item.Key, objectName))
            {
                schema = item.Value;
                return true;
            }
        }

        schema = null;
        return false;
    }

    private static List<Dictionary<string, string>> ReadCsvRows(string filePath)
    {
        using var parser = new TextFieldParser(filePath);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(";");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        if (parser.EndOfData)
        {
            return [];
        }

        var headers = parser.ReadFields() ?? [];
        var rows = new List<Dictionary<string, string>>();

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null)
            {
                continue;
            }

            var row = new Dictionary<string, string>(headers.Length, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                row[headers[index]] = index < fields.Length ? fields[index] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private sealed class MutableProbeSection
    {
        public string Name { get; init; } = string.Empty;

        public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

        public List<MutableProbeRow> Rows { get; } = [];
    }

    private sealed class MutableProbeRow
    {
        public int RowNumber { get; init; }

        public List<OneCFieldValue> Fields { get; } = [];
    }

    private sealed record MutableFieldTarget(List<OneCFieldValue> Fields, int Index);

    private sealed record ManifestEntry(
        string ExportRoot,
        string ObjectType,
        string ObjectName,
        string SubobjectName,
        string FilePath,
        int RowCount);

    private sealed record RecordBuildContext(
        string ObjectName,
        IReadOnlyList<OneCFieldValue> Fields,
        IReadOnlyList<OneCTabularSectionSnapshot> TabularSections,
        OneCSchemaDefinition? Schema);
}
