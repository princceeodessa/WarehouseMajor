using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Persistence.Sql;

namespace WarehouseAutomatisaion.Infrastructure.Importing.MySql;

public sealed class OneCRawSnapshotMySqlSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly OneCRawMySqlSyncOptions _options;

    public OneCRawSnapshotMySqlSyncService(OneCRawMySqlSyncOptions options)
    {
        _options = options;
    }

    public OneCRawMySqlSyncResult SyncCurrentSnapshot()
    {
        var snapshot = new OneCImportService(_options.WorkspaceRoot).LoadSnapshot();
        return SyncSnapshot(snapshot);
    }

    public OneCRawMySqlSyncResult SyncSnapshot(OneCImportSnapshot snapshot)
    {
        ValidateDatabaseName(_options.DatabaseName);

        var mysqlExecutablePath = ResolveMysqlExecutablePath(_options.MysqlExecutablePath);
        var schemaPath = ResolveSchemaPath();
        var generatedScriptPath = ResolveGeneratedScriptPath();
        var schemaSql = File.ReadAllText(schemaPath, Encoding.UTF8);
        var canonicalRecords = BuildCanonicalRecords(snapshot);
        var fieldCount = canonicalRecords.Sum(record => record.Record.Fields.Count + record.Record.TabularSections.Sum(section => section.Rows.Sum(row => row.Fields.Count)));
        var tabularRowCount = canonicalRecords.Sum(record => record.Record.TabularSections.Sum(section => section.Rows.Count));

        Directory.CreateDirectory(Path.GetDirectoryName(generatedScriptPath)!);
        WriteSqlScript(snapshot, schemaSql, canonicalRecords, generatedScriptPath);

        var output = ExecuteSql(mysqlExecutablePath, generatedScriptPath);
        var resultLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("SYNC_RESULT|", StringComparison.Ordinal));

        long? batchId = null;
        if (!string.IsNullOrWhiteSpace(resultLine))
        {
            var parts = resultLine.Split('|');
            if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBatchId))
            {
                batchId = parsedBatchId;
            }
        }

        return new OneCRawMySqlSyncResult(
            _options.DatabaseName,
            mysqlExecutablePath,
            generatedScriptPath,
            batchId,
            canonicalRecords.Count,
            fieldCount,
            tabularRowCount,
            output);
    }

    private void WriteSqlScript(
        OneCImportSnapshot snapshot,
        string schemaSql,
        IReadOnlyList<CanonicalRecord> canonicalRecords,
        string generatedScriptPath)
    {
        var schemaRows = snapshot.Schemas.ToArray();
        var schemaColumns = snapshot.Schemas
            .SelectMany(schema => schema.Columns.Select((columnName, index) => new SchemaColumnRow(schema.ObjectName, schema.SourceFileName, index + 1, columnName)))
            .ToArray();
        var schemaSections = snapshot.Schemas
            .SelectMany(schema => schema.TabularSections.Select(section => new SchemaSectionRow(schema.ObjectName, schema.SourceFileName, section.Name)))
            .ToArray();
        var schemaSectionColumns = snapshot.Schemas
            .SelectMany(schema => schema.TabularSections.SelectMany(section => section.Columns.Select((columnName, index) => new SchemaSectionColumnRow(schema.ObjectName, schema.SourceFileName, section.Name, index + 1, columnName))))
            .ToArray();
        var referenceLookup = canonicalRecords
            .Where(record => !string.IsNullOrWhiteSpace(record.ReferenceCode))
            .GroupBy(record => record.ReferenceCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ObjectName, StringComparer.Ordinal);
        var fieldRowCount = canonicalRecords.Sum(record => record.Record.Fields.Count);
        var tabularRowCount = canonicalRecords.Sum(record => record.Record.TabularSections.Sum(section => section.Rows.Count));

        using var writer = new StreamWriter(generatedScriptPath, false, new UTF8Encoding(false));

        if (_options.RecreateDatabaseOnSync)
        {
            writer.WriteLine($"DROP DATABASE IF EXISTS {_options.DatabaseName};");
        }

        writer.WriteLine($"CREATE DATABASE IF NOT EXISTS {_options.DatabaseName} CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");
        writer.WriteLine($"USE {_options.DatabaseName};");
        writer.WriteLine(schemaSql.Trim());
        writer.WriteLine();
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_definitions;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_columns;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_sections;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_section_columns;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_object_snapshots;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_field_snapshots;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_tabular_sections;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_tabular_rows;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_tabular_fields;");
        writer.WriteLine("DROP TEMPORARY TABLE IF EXISTS tmp_reference_links;");
        writer.WriteLine();
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_schema_definitions (schema_kind VARCHAR(32) NOT NULL, object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_schema_columns (object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL, ordinal_position INT UNSIGNED NOT NULL, column_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_schema_sections (object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL, section_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_schema_section_columns (object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL, section_name VARCHAR(160) NOT NULL, ordinal_position INT UNSIGNED NOT NULL, column_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_object_snapshots (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, code_value VARCHAR(160) NULL, number_value VARCHAR(160) NULL, title_text VARCHAR(512) NULL, subtitle_text VARCHAR(512) NULL, status_text VARCHAR(160) NULL, record_date DATETIME(6) NULL, source_folder VARCHAR(512) NULL, record_hash CHAR(64) NULL, payload_json LONGTEXT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_field_snapshots (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, field_name VARCHAR(160) NOT NULL, raw_value LONGTEXT NULL, display_value LONGTEXT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_tabular_sections (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_tabular_rows (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NOT NULL, `row_number` INT UNSIGNED NOT NULL, payload_json LONGTEXT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_tabular_fields (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NOT NULL, `row_number` INT UNSIGNED NOT NULL, field_name VARCHAR(160) NOT NULL, raw_value LONGTEXT NULL, display_value LONGTEXT NULL) ENGINE=InnoDB;");
        writer.WriteLine("CREATE TEMPORARY TABLE tmp_reference_links (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NULL, `row_number` INT UNSIGNED NULL, field_name VARCHAR(160) NOT NULL, target_object_name VARCHAR(160) NULL, target_reference_code VARCHAR(160) NULL, target_display_value VARCHAR(512) NULL) ENGINE=InnoDB;");
        writer.WriteLine();

        AppendInsert(writer, "tmp_schema_definitions", ["schema_kind", "object_name", "source_file_name"], schemaRows.Select(schema => new[]
        {
            Sql(schema.Kind),
            Sql(schema.ObjectName),
            Sql(schema.SourceFileName)
        }));
        AppendInsert(writer, "tmp_schema_columns", ["object_name", "source_file_name", "ordinal_position", "column_name"], schemaColumns.Select(column => new[]
        {
            Sql(column.ObjectName),
            Sql(column.SourceFileName),
            Sql(column.OrdinalPosition),
            Sql(column.ColumnName)
        }));
        AppendInsert(writer, "tmp_schema_sections", ["object_name", "source_file_name", "section_name"], schemaSections.Select(section => new[]
        {
            Sql(section.ObjectName),
            Sql(section.SourceFileName),
            Sql(section.SectionName)
        }));
        AppendInsert(writer, "tmp_schema_section_columns", ["object_name", "source_file_name", "section_name", "ordinal_position", "column_name"], schemaSectionColumns.Select(column => new[]
        {
            Sql(column.ObjectName),
            Sql(column.SourceFileName),
            Sql(column.SectionName),
            Sql(column.OrdinalPosition),
            Sql(column.ColumnName)
        }));
        AppendInsert(writer, "tmp_object_snapshots", ["object_name", "reference_code", "code_value", "number_value", "title_text", "subtitle_text", "status_text", "record_date", "source_folder", "record_hash", "payload_json"], canonicalRecords.Select(record => new[]
        {
            Sql(record.ObjectName),
            Sql(record.ReferenceCode),
            Sql(NullIfEmpty(record.Record.Code)),
            Sql(NullIfEmpty(record.Record.Number)),
            Sql(NullIfEmpty(record.Record.Title)),
            Sql(NullIfEmpty(record.Record.Subtitle)),
            Sql(NullIfEmpty(record.Record.Status)),
            Sql(record.Record.Date),
            Sql(record.SourceFolder),
            Sql(record.RecordHash),
            Sql((string?)null)
        }));
        AppendInsert(writer, "tmp_field_snapshots", ["object_name", "reference_code", "field_name", "raw_value", "display_value"], canonicalRecords.SelectMany(record => record.Record.Fields.Select(field => new[]
        {
            Sql(record.ObjectName),
            Sql(record.ReferenceCode),
            Sql(field.Name),
            Sql(NullIfEmpty(field.RawValue)),
            Sql(NullIfEmpty(field.DisplayValue))
        })));
        AppendInsert(writer, "tmp_tabular_sections", ["object_name", "reference_code", "section_name"], canonicalRecords.SelectMany(record => record.Record.TabularSections.Select(section => new[]
        {
            Sql(record.ObjectName),
            Sql(record.ReferenceCode),
            Sql(section.Name)
        })));
        AppendInsert(writer, "tmp_tabular_rows", ["object_name", "reference_code", "section_name", "`row_number`", "payload_json"], canonicalRecords.SelectMany(record => record.Record.TabularSections.SelectMany(section => section.Rows.Select(row => new[]
        {
            Sql(record.ObjectName),
            Sql(record.ReferenceCode),
            Sql(section.Name),
            Sql(row.RowNumber),
            Sql((string?)null)
        }))));
        AppendInsert(writer, "tmp_tabular_fields", ["object_name", "reference_code", "section_name", "`row_number`", "field_name", "raw_value", "display_value"], canonicalRecords.SelectMany(record => record.Record.TabularSections.SelectMany(section => section.Rows.SelectMany(row => row.Fields.Select(field => new[]
        {
            Sql(record.ObjectName),
            Sql(record.ReferenceCode),
            Sql(section.Name),
            Sql(row.RowNumber),
            Sql(field.Name),
            Sql(NullIfEmpty(field.RawValue)),
            Sql(NullIfEmpty(field.DisplayValue))
        })))));
        AppendInsert(writer, "tmp_reference_links", ["object_name", "reference_code", "section_name", "`row_number`", "field_name", "target_object_name", "target_reference_code", "target_display_value"], canonicalRecords.SelectMany(record => ExtractLinks(record, referenceLookup).Select(link => new[]
        {
            Sql(link.ObjectName),
            Sql(link.ReferenceCode),
            Sql(link.SectionName),
            Sql(link.RowNumber),
            Sql(link.FieldName),
            Sql(link.TargetObjectName),
            Sql(link.TargetReferenceCode),
            Sql(link.TargetDisplayValue)
        })));

        writer.WriteLine("START TRANSACTION;");
        writer.WriteLine($"INSERT INTO onec_import_batches (source_folders_json, note_text, created_by) VALUES (CAST({Sql(JsonSerializer.Serialize(snapshot.SourceFolders, JsonOptions))} AS JSON), {Sql("Snapshot imported by WarehouseAutomatisaion raw MySQL sync.")}, {Sql(NullIfEmpty(_options.CreatedBy))});");
        writer.WriteLine("SET @batch_id = LAST_INSERT_ID();");
        writer.WriteLine();
        writer.WriteLine("INSERT INTO onec_schema_definitions (batch_id, schema_kind, object_name, source_file_name)");
        writer.WriteLine("SELECT @batch_id, schema_kind, object_name, source_file_name FROM tmp_schema_definitions");
        writer.WriteLine("ON DUPLICATE KEY UPDATE batch_id = VALUES(batch_id), schema_kind = VALUES(schema_kind), imported_at_utc = UTC_TIMESTAMP(6);");
        writer.WriteLine("DELETE c FROM onec_schema_columns c INNER JOIN onec_schema_definitions d ON d.id = c.schema_definition_id INNER JOIN tmp_schema_definitions t ON t.object_name = d.object_name AND t.source_file_name = d.source_file_name;");
        writer.WriteLine("DELETE sc FROM onec_schema_tabular_section_columns sc INNER JOIN onec_schema_tabular_sections s ON s.id = sc.schema_tabular_section_id INNER JOIN onec_schema_definitions d ON d.id = s.schema_definition_id INNER JOIN tmp_schema_definitions t ON t.object_name = d.object_name AND t.source_file_name = d.source_file_name;");
        writer.WriteLine("DELETE s FROM onec_schema_tabular_sections s INNER JOIN onec_schema_definitions d ON d.id = s.schema_definition_id INNER JOIN tmp_schema_definitions t ON t.object_name = d.object_name AND t.source_file_name = d.source_file_name;");
        writer.WriteLine("INSERT INTO onec_schema_columns (schema_definition_id, ordinal_position, column_name)");
        writer.WriteLine("SELECT d.id, c.ordinal_position, c.column_name FROM tmp_schema_columns c INNER JOIN onec_schema_definitions d ON d.object_name = c.object_name AND d.source_file_name = c.source_file_name;");
        writer.WriteLine("INSERT INTO onec_schema_tabular_sections (schema_definition_id, section_name)");
        writer.WriteLine("SELECT d.id, s.section_name FROM tmp_schema_sections s INNER JOIN onec_schema_definitions d ON d.object_name = s.object_name AND d.source_file_name = s.source_file_name;");
        writer.WriteLine("INSERT INTO onec_schema_tabular_section_columns (schema_tabular_section_id, ordinal_position, column_name)");
        writer.WriteLine("SELECT ts.id, sc.ordinal_position, sc.column_name FROM tmp_schema_section_columns sc INNER JOIN onec_schema_definitions d ON d.object_name = sc.object_name AND d.source_file_name = sc.source_file_name INNER JOIN onec_schema_tabular_sections ts ON ts.schema_definition_id = d.id AND ts.section_name = sc.section_name;");
        writer.WriteLine();
        writer.WriteLine("INSERT INTO onec_object_snapshots (batch_id, object_name, reference_code, code_value, number_value, title_text, subtitle_text, status_text, record_date, source_folder, record_hash, payload_json)");
        writer.WriteLine("SELECT @batch_id, object_name, reference_code, code_value, number_value, title_text, subtitle_text, status_text, record_date, source_folder, record_hash, CASE WHEN payload_json IS NULL THEN NULL ELSE CAST(payload_json AS JSON) END FROM tmp_object_snapshots;");
        writer.WriteLine("INSERT INTO onec_field_snapshots (object_snapshot_id, field_name, raw_value, display_value)");
        writer.WriteLine("SELECT o.id, f.field_name, f.raw_value, f.display_value FROM tmp_field_snapshots f INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = f.object_name AND o.reference_code = f.reference_code;");
        writer.WriteLine("INSERT INTO onec_tabular_section_snapshots (object_snapshot_id, section_name)");
        writer.WriteLine("SELECT o.id, s.section_name FROM tmp_tabular_sections s INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = s.object_name AND o.reference_code = s.reference_code;");
        writer.WriteLine("INSERT INTO onec_tabular_section_rows (tabular_section_snapshot_id, `row_number`, payload_json)");
        writer.WriteLine("SELECT ts.id, r.`row_number`, CASE WHEN r.payload_json IS NULL THEN NULL ELSE CAST(r.payload_json AS JSON) END FROM tmp_tabular_rows r INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = r.object_name AND o.reference_code = r.reference_code INNER JOIN onec_tabular_section_snapshots ts ON ts.object_snapshot_id = o.id AND ts.section_name = r.section_name;");
        writer.WriteLine("INSERT INTO onec_tabular_section_fields (tabular_section_row_id, field_name, raw_value, display_value)");
        writer.WriteLine("SELECT tr.id, f.field_name, f.raw_value, f.display_value FROM tmp_tabular_fields f INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = f.object_name AND o.reference_code = f.reference_code INNER JOIN onec_tabular_section_snapshots ts ON ts.object_snapshot_id = o.id AND ts.section_name = f.section_name INNER JOIN onec_tabular_section_rows tr ON tr.tabular_section_snapshot_id = ts.id AND tr.`row_number` = f.`row_number`;");
        writer.WriteLine("INSERT INTO onec_reference_links (object_snapshot_id, section_name, `row_number`, field_name, target_object_name, target_reference_code, target_display_value)");
        writer.WriteLine("SELECT o.id, l.section_name, l.`row_number`, l.field_name, l.target_object_name, l.target_reference_code, l.target_display_value FROM tmp_reference_links l INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = l.object_name AND o.reference_code = l.reference_code;");
        writer.WriteLine("COMMIT;");
        writer.WriteLine($"SELECT CONCAT('SYNC_RESULT|', @batch_id, '|', {canonicalRecords.Count}, '|', {fieldRowCount}, '|', {tabularRowCount}) AS sync_result;");
    }

    private string ResolveSchemaPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.SchemaPath) && File.Exists(_options.SchemaPath))
        {
            return Path.GetFullPath(_options.SchemaPath);
        }

        var directPath = Path.Combine(
            _options.WorkspaceRoot,
            "WarehouseAutomatisaion.Infrastructure",
            "Persistence",
            "Sql",
            SqlAssetCatalog.MySqlOperationalSchemaFileName);

        if (File.Exists(directPath))
        {
            return directPath;
        }

        var outputPath = SqlAssetCatalog.GetMySqlOperationalSchemaPath(AppContext.BaseDirectory);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        throw new FileNotFoundException("MySQL operational schema was not found.");
    }

    private string ResolveGeneratedScriptPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.GeneratedScriptPath))
        {
            return Path.GetFullPath(_options.GeneratedScriptPath);
        }

        return Path.Combine(_options.WorkspaceRoot, "app_data", "one-c-raw-sync", "onec-raw-sync-latest.sql");
    }

    private string BuildSqlScript(OneCImportSnapshot snapshot, string schemaSql)
    {
        var schemaRows = snapshot.Schemas.ToArray();
        var schemaColumns = snapshot.Schemas
            .SelectMany(schema => schema.Columns.Select((columnName, index) => new SchemaColumnRow(schema.ObjectName, schema.SourceFileName, index + 1, columnName)))
            .ToArray();
        var schemaSections = snapshot.Schemas
            .SelectMany(schema => schema.TabularSections.Select(section => new SchemaSectionRow(schema.ObjectName, schema.SourceFileName, section.Name)))
            .ToArray();
        var schemaSectionColumns = snapshot.Schemas
            .SelectMany(schema => schema.TabularSections.SelectMany(section => section.Columns.Select((columnName, index) => new SchemaSectionColumnRow(schema.ObjectName, schema.SourceFileName, section.Name, index + 1, columnName))))
            .ToArray();

        var canonicalRecords = BuildCanonicalRecords(snapshot);
        var referenceLookup = canonicalRecords
            .Where(record => !string.IsNullOrWhiteSpace(record.ReferenceCode))
            .GroupBy(record => record.ReferenceCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ObjectName, StringComparer.Ordinal);

        var objectRows = canonicalRecords
            .Select(record => new ObjectSnapshotRow(
                record.ObjectName,
                record.ReferenceCode,
                NullIfEmpty(record.Record.Code),
                NullIfEmpty(record.Record.Number),
                NullIfEmpty(record.Record.Title),
                NullIfEmpty(record.Record.Subtitle),
                NullIfEmpty(record.Record.Status),
                record.Record.Date,
                record.SourceFolder,
                record.RecordHash,
                null))
            .ToArray();

        var fieldRows = canonicalRecords
            .SelectMany(record => record.Record.Fields.Select(field => new FieldSnapshotRow(
                record.ObjectName,
                record.ReferenceCode,
                field.Name,
                NullIfEmpty(field.RawValue),
                NullIfEmpty(field.DisplayValue))))
            .ToArray();

        var sectionRows = canonicalRecords
            .SelectMany(record => record.Record.TabularSections.Select(section => new TabularSectionSnapshotRow(
                record.ObjectName,
                record.ReferenceCode,
                section.Name)))
            .ToArray();

        var tabularRows = canonicalRecords
            .SelectMany(record => record.Record.TabularSections.SelectMany(section => section.Rows.Select(row => new TabularSectionRowRow(
                record.ObjectName,
                record.ReferenceCode,
                section.Name,
                row.RowNumber,
                null))))
            .ToArray();

        var tabularFieldRows = canonicalRecords
            .SelectMany(record => record.Record.TabularSections.SelectMany(section => section.Rows.SelectMany(row => row.Fields.Select(field => new TabularSectionFieldRow(
                record.ObjectName,
                record.ReferenceCode,
                section.Name,
                row.RowNumber,
                field.Name,
                NullIfEmpty(field.RawValue),
                NullIfEmpty(field.DisplayValue))))))
            .ToArray();

        var linkRows = canonicalRecords
            .SelectMany(record => ExtractLinks(record, referenceLookup))
            .ToArray();

        return BuildSqlScriptCore(snapshot, schemaSql, schemaRows, schemaColumns, schemaSections, schemaSectionColumns, canonicalRecords, objectRows, fieldRows, sectionRows, tabularRows, tabularFieldRows, linkRows);
    }

    private string BuildSqlScriptCore(
        OneCImportSnapshot snapshot,
        string schemaSql,
        IReadOnlyList<OneCSchemaDefinition> schemaRows,
        IReadOnlyList<SchemaColumnRow> schemaColumns,
        IReadOnlyList<SchemaSectionRow> schemaSections,
        IReadOnlyList<SchemaSectionColumnRow> schemaSectionColumns,
        IReadOnlyList<CanonicalRecord> canonicalRecords,
        IReadOnlyList<ObjectSnapshotRow> objectRows,
        IReadOnlyList<FieldSnapshotRow> fieldRows,
        IReadOnlyList<TabularSectionSnapshotRow> sectionRows,
        IReadOnlyList<TabularSectionRowRow> tabularRows,
        IReadOnlyList<TabularSectionFieldRow> tabularFieldRows,
        IReadOnlyList<ReferenceLinkRow> linkRows)
    {
        var builder = new StringBuilder(2_000_000);
        if (_options.RecreateDatabaseOnSync)
        {
            builder.AppendLine($"DROP DATABASE IF EXISTS {_options.DatabaseName};");
        }

        builder.AppendLine($"CREATE DATABASE IF NOT EXISTS {_options.DatabaseName} CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");
        builder.AppendLine($"USE {_options.DatabaseName};");
        builder.AppendLine(schemaSql.Trim());
        builder.AppendLine();
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_definitions;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_columns;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_sections;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_schema_section_columns;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_object_snapshots;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_field_snapshots;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_tabular_sections;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_tabular_rows;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_tabular_fields;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_reference_links;");
        builder.AppendLine();
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_schema_definitions (schema_kind VARCHAR(32) NOT NULL, object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_schema_columns (object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL, ordinal_position INT UNSIGNED NOT NULL, column_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_schema_sections (object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL, section_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_schema_section_columns (object_name VARCHAR(160) NOT NULL, source_file_name VARCHAR(260) NOT NULL, section_name VARCHAR(160) NOT NULL, ordinal_position INT UNSIGNED NOT NULL, column_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_object_snapshots (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, code_value VARCHAR(160) NULL, number_value VARCHAR(160) NULL, title_text VARCHAR(512) NULL, subtitle_text VARCHAR(512) NULL, status_text VARCHAR(160) NULL, record_date DATETIME(6) NULL, source_folder VARCHAR(512) NULL, record_hash CHAR(64) NULL, payload_json LONGTEXT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_field_snapshots (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, field_name VARCHAR(160) NOT NULL, raw_value LONGTEXT NULL, display_value LONGTEXT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_tabular_sections (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NOT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_tabular_rows (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NOT NULL, `row_number` INT UNSIGNED NOT NULL, payload_json LONGTEXT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_tabular_fields (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NOT NULL, `row_number` INT UNSIGNED NOT NULL, field_name VARCHAR(160) NOT NULL, raw_value LONGTEXT NULL, display_value LONGTEXT NULL) ENGINE=InnoDB;");
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_reference_links (object_name VARCHAR(160) NOT NULL, reference_code VARCHAR(160) NOT NULL, section_name VARCHAR(160) NULL, `row_number` INT UNSIGNED NULL, field_name VARCHAR(160) NOT NULL, target_object_name VARCHAR(160) NULL, target_reference_code VARCHAR(160) NULL, target_display_value VARCHAR(512) NULL) ENGINE=InnoDB;");
        builder.AppendLine();

        AppendInsert(builder, "tmp_schema_definitions", ["schema_kind", "object_name", "source_file_name"], schemaRows.Select(schema => new[]
        {
            Sql(schema.Kind),
            Sql(schema.ObjectName),
            Sql(schema.SourceFileName)
        }));
        AppendInsert(builder, "tmp_schema_columns", ["object_name", "source_file_name", "ordinal_position", "column_name"], schemaColumns.Select(column => new[]
        {
            Sql(column.ObjectName),
            Sql(column.SourceFileName),
            Sql(column.OrdinalPosition),
            Sql(column.ColumnName)
        }));
        AppendInsert(builder, "tmp_schema_sections", ["object_name", "source_file_name", "section_name"], schemaSections.Select(section => new[]
        {
            Sql(section.ObjectName),
            Sql(section.SourceFileName),
            Sql(section.SectionName)
        }));
        AppendInsert(builder, "tmp_schema_section_columns", ["object_name", "source_file_name", "section_name", "ordinal_position", "column_name"], schemaSectionColumns.Select(column => new[]
        {
            Sql(column.ObjectName),
            Sql(column.SourceFileName),
            Sql(column.SectionName),
            Sql(column.OrdinalPosition),
            Sql(column.ColumnName)
        }));
        AppendInsert(builder, "tmp_object_snapshots", ["object_name", "reference_code", "code_value", "number_value", "title_text", "subtitle_text", "status_text", "record_date", "source_folder", "record_hash", "payload_json"], objectRows.Select(row => new[]
        {
            Sql(row.ObjectName),
            Sql(row.ReferenceCode),
            Sql(row.CodeValue),
            Sql(row.NumberValue),
            Sql(row.TitleText),
            Sql(row.SubtitleText),
            Sql(row.StatusText),
            Sql(row.RecordDate),
            Sql(row.SourceFolder),
            Sql(row.RecordHash),
            Sql(row.PayloadJson)
        }));
        AppendInsert(builder, "tmp_field_snapshots", ["object_name", "reference_code", "field_name", "raw_value", "display_value"], fieldRows.Select(row => new[]
        {
            Sql(row.ObjectName),
            Sql(row.ReferenceCode),
            Sql(row.FieldName),
            Sql(row.RawValue),
            Sql(row.DisplayValue)
        }));
        AppendInsert(builder, "tmp_tabular_sections", ["object_name", "reference_code", "section_name"], sectionRows.Select(row => new[]
        {
            Sql(row.ObjectName),
            Sql(row.ReferenceCode),
            Sql(row.SectionName)
        }));
        AppendInsert(builder, "tmp_tabular_rows", ["object_name", "reference_code", "section_name", "`row_number`", "payload_json"], tabularRows.Select(row => new[]
        {
            Sql(row.ObjectName),
            Sql(row.ReferenceCode),
            Sql(row.SectionName),
            Sql(row.RowNumber),
            Sql(row.PayloadJson)
        }));
        AppendInsert(builder, "tmp_tabular_fields", ["object_name", "reference_code", "section_name", "`row_number`", "field_name", "raw_value", "display_value"], tabularFieldRows.Select(row => new[]
        {
            Sql(row.ObjectName),
            Sql(row.ReferenceCode),
            Sql(row.SectionName),
            Sql(row.RowNumber),
            Sql(row.FieldName),
            Sql(row.RawValue),
            Sql(row.DisplayValue)
        }));
        AppendInsert(builder, "tmp_reference_links", ["object_name", "reference_code", "section_name", "`row_number`", "field_name", "target_object_name", "target_reference_code", "target_display_value"], linkRows.Select(row => new[]
        {
            Sql(row.ObjectName),
            Sql(row.ReferenceCode),
            Sql(row.SectionName),
            Sql(row.RowNumber),
            Sql(row.FieldName),
            Sql(row.TargetObjectName),
            Sql(row.TargetReferenceCode),
            Sql(row.TargetDisplayValue)
        }));

        builder.AppendLine("START TRANSACTION;");
        builder.AppendLine($"INSERT INTO onec_import_batches (source_folders_json, note_text, created_by) VALUES (CAST({Sql(JsonSerializer.Serialize(snapshot.SourceFolders, JsonOptions))} AS JSON), {Sql("Snapshot imported by WarehouseAutomatisaion raw MySQL sync.")}, {Sql(NullIfEmpty(_options.CreatedBy))});");
        builder.AppendLine("SET @batch_id = LAST_INSERT_ID();");
        builder.AppendLine();
        builder.AppendLine("INSERT INTO onec_schema_definitions (batch_id, schema_kind, object_name, source_file_name)");
        builder.AppendLine("SELECT @batch_id, schema_kind, object_name, source_file_name FROM tmp_schema_definitions");
        builder.AppendLine("ON DUPLICATE KEY UPDATE batch_id = VALUES(batch_id), schema_kind = VALUES(schema_kind), imported_at_utc = UTC_TIMESTAMP(6);");
        builder.AppendLine("DELETE c FROM onec_schema_columns c INNER JOIN onec_schema_definitions d ON d.id = c.schema_definition_id INNER JOIN tmp_schema_definitions t ON t.object_name = d.object_name AND t.source_file_name = d.source_file_name;");
        builder.AppendLine("DELETE sc FROM onec_schema_tabular_section_columns sc INNER JOIN onec_schema_tabular_sections s ON s.id = sc.schema_tabular_section_id INNER JOIN onec_schema_definitions d ON d.id = s.schema_definition_id INNER JOIN tmp_schema_definitions t ON t.object_name = d.object_name AND t.source_file_name = d.source_file_name;");
        builder.AppendLine("DELETE s FROM onec_schema_tabular_sections s INNER JOIN onec_schema_definitions d ON d.id = s.schema_definition_id INNER JOIN tmp_schema_definitions t ON t.object_name = d.object_name AND t.source_file_name = d.source_file_name;");
        builder.AppendLine("INSERT INTO onec_schema_columns (schema_definition_id, ordinal_position, column_name)");
        builder.AppendLine("SELECT d.id, c.ordinal_position, c.column_name FROM tmp_schema_columns c INNER JOIN onec_schema_definitions d ON d.object_name = c.object_name AND d.source_file_name = c.source_file_name;");
        builder.AppendLine("INSERT INTO onec_schema_tabular_sections (schema_definition_id, section_name)");
        builder.AppendLine("SELECT d.id, s.section_name FROM tmp_schema_sections s INNER JOIN onec_schema_definitions d ON d.object_name = s.object_name AND d.source_file_name = s.source_file_name;");
        builder.AppendLine("INSERT INTO onec_schema_tabular_section_columns (schema_tabular_section_id, ordinal_position, column_name)");
        builder.AppendLine("SELECT ts.id, sc.ordinal_position, sc.column_name FROM tmp_schema_section_columns sc INNER JOIN onec_schema_definitions d ON d.object_name = sc.object_name AND d.source_file_name = sc.source_file_name INNER JOIN onec_schema_tabular_sections ts ON ts.schema_definition_id = d.id AND ts.section_name = sc.section_name;");
        builder.AppendLine();
        builder.AppendLine("INSERT INTO onec_object_snapshots (batch_id, object_name, reference_code, code_value, number_value, title_text, subtitle_text, status_text, record_date, source_folder, record_hash, payload_json)");
        builder.AppendLine("SELECT @batch_id, object_name, reference_code, code_value, number_value, title_text, subtitle_text, status_text, record_date, source_folder, record_hash, CASE WHEN payload_json IS NULL THEN NULL ELSE CAST(payload_json AS JSON) END FROM tmp_object_snapshots;");
        builder.AppendLine("INSERT INTO onec_field_snapshots (object_snapshot_id, field_name, raw_value, display_value)");
        builder.AppendLine("SELECT o.id, f.field_name, f.raw_value, f.display_value FROM tmp_field_snapshots f INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = f.object_name AND o.reference_code = f.reference_code;");
        builder.AppendLine("INSERT INTO onec_tabular_section_snapshots (object_snapshot_id, section_name)");
        builder.AppendLine("SELECT o.id, s.section_name FROM tmp_tabular_sections s INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = s.object_name AND o.reference_code = s.reference_code;");
        builder.AppendLine("INSERT INTO onec_tabular_section_rows (tabular_section_snapshot_id, `row_number`, payload_json)");
        builder.AppendLine("SELECT ts.id, r.`row_number`, CASE WHEN r.payload_json IS NULL THEN NULL ELSE CAST(r.payload_json AS JSON) END FROM tmp_tabular_rows r INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = r.object_name AND o.reference_code = r.reference_code INNER JOIN onec_tabular_section_snapshots ts ON ts.object_snapshot_id = o.id AND ts.section_name = r.section_name;");
        builder.AppendLine("INSERT INTO onec_tabular_section_fields (tabular_section_row_id, field_name, raw_value, display_value)");
        builder.AppendLine("SELECT tr.id, f.field_name, f.raw_value, f.display_value FROM tmp_tabular_fields f INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = f.object_name AND o.reference_code = f.reference_code INNER JOIN onec_tabular_section_snapshots ts ON ts.object_snapshot_id = o.id AND ts.section_name = f.section_name INNER JOIN onec_tabular_section_rows tr ON tr.tabular_section_snapshot_id = ts.id AND tr.`row_number` = f.`row_number`;");
        builder.AppendLine("INSERT INTO onec_reference_links (object_snapshot_id, section_name, `row_number`, field_name, target_object_name, target_reference_code, target_display_value)");
        builder.AppendLine("SELECT o.id, l.section_name, l.`row_number`, l.field_name, l.target_object_name, l.target_reference_code, l.target_display_value FROM tmp_reference_links l INNER JOIN onec_object_snapshots o ON o.batch_id = @batch_id AND o.object_name = l.object_name AND o.reference_code = l.reference_code;");
        builder.AppendLine("COMMIT;");
        builder.AppendLine($"SELECT CONCAT('SYNC_RESULT|', @batch_id, '|', {canonicalRecords.Count}, '|', {fieldRows.Count}, '|', {tabularRows.Count}) AS sync_result;");

        return builder.ToString();
    }

    private static List<CanonicalRecord> BuildCanonicalRecords(OneCImportSnapshot snapshot)
    {
        var sourceFolder = snapshot.SourceFolders.FirstOrDefault() ?? string.Empty;
        var result = new List<CanonicalRecord>();

        foreach (var dataset in EnumerateDatasets(snapshot))
        {
            for (var index = 0; index < dataset.Records.Count; index++)
            {
                var record = dataset.Records[index];
                var objectName = string.IsNullOrWhiteSpace(record.ObjectName) ? dataset.ObjectName : record.ObjectName;
                var referenceCode = string.IsNullOrWhiteSpace(record.Reference)
                    ? $"{objectName}#{index + 1}"
                    : record.Reference;
                var hashSeed = string.Join("|", new[]
                {
                    objectName,
                    referenceCode,
                    record.Code,
                    record.Number,
                    record.Title,
                    record.Status,
                    record.Date?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
                });

                result.Add(new CanonicalRecord(
                    objectName,
                    referenceCode,
                    sourceFolder,
                    ComputeSha256(hashSeed),
                    record));
            }
        }

        return result;
    }

    private static IEnumerable<OneCEntityDataset> EnumerateDatasets(OneCImportSnapshot snapshot)
    {
        yield return snapshot.Customers;
        yield return snapshot.Items;
        yield return snapshot.SalesOrders;
        yield return snapshot.SalesInvoices;
        yield return snapshot.SalesShipments;
        yield return snapshot.PurchaseOrders;
        yield return snapshot.SupplierInvoices;
        yield return snapshot.PurchaseReceipts;
        yield return snapshot.TransferOrders;
        yield return snapshot.StockReservations;
        yield return snapshot.InventoryCounts;
        yield return snapshot.StockWriteOffs;
    }

    private static IEnumerable<ReferenceLinkRow> ExtractLinks(
        CanonicalRecord record,
        IReadOnlyDictionary<string, string> referenceLookup)
    {
        foreach (var field in record.Record.Fields)
        {
            if (!IsReferenceValue(field))
            {
                continue;
            }

            referenceLookup.TryGetValue(field.RawValue, out var targetObjectName);
            yield return new ReferenceLinkRow(record.ObjectName, record.ReferenceCode, null, null, field.Name, targetObjectName, field.RawValue, field.DisplayValue);
        }

        foreach (var section in record.Record.TabularSections)
        {
            foreach (var row in section.Rows)
            {
                foreach (var field in row.Fields)
                {
                    if (!IsReferenceValue(field))
                    {
                        continue;
                    }

                    referenceLookup.TryGetValue(field.RawValue, out var targetObjectName);
                    yield return new ReferenceLinkRow(record.ObjectName, record.ReferenceCode, section.Name, row.RowNumber, field.Name, targetObjectName, field.RawValue, field.DisplayValue);
                }
            }
        }
    }

    private static bool IsReferenceValue(OneCFieldValue field)
    {
        return !string.IsNullOrWhiteSpace(field.RawValue)
            && !string.Equals(field.RawValue, field.DisplayValue, StringComparison.Ordinal);
    }

    private static string ComputeSha256(string payloadJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string ExecuteSql(string mysqlExecutablePath, string scriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = mysqlExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add($"--host={_options.Host}");
        startInfo.ArgumentList.Add($"--port={_options.Port.ToString(CultureInfo.InvariantCulture)}");
        startInfo.ArgumentList.Add($"--user={_options.User}");
        startInfo.ArgumentList.Add("--default-character-set=utf8mb4");
        startInfo.ArgumentList.Add("--batch");
        startInfo.ArgumentList.Add("--skip-column-names");

        if (!string.IsNullOrEmpty(_options.Password))
        {
            startInfo.ArgumentList.Add($"--password={_options.Password}");
        }

        startInfo.ArgumentList.Add($"--execute=source {QuoteMySqlSourcePath(scriptPath)}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start mysql.exe.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"mysql.exe exited with code {process.ExitCode}:{Environment.NewLine}{error}{Environment.NewLine}{output}");
        }

        return string.IsNullOrWhiteSpace(error)
            ? output
            : $"{output}{Environment.NewLine}{error}";
    }

    private static string QuoteMySqlSourcePath(string scriptPath)
    {
        return scriptPath.Replace("\\", "/", StringComparison.Ordinal);
    }

    private static string ResolveMysqlExecutablePath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var pathCommand = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.Combine(path.Trim(), "mysql.exe"))
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(pathCommand))
        {
            return pathCommand;
        }

        var knownPaths = new[]
        {
            @"C:\laragon\bin\mysql\mysql-8.4.3-winx64\bin\mysql.exe",
            @"C:\laragon\bin\mysql\mysql-8.3.0-winx64\bin\mysql.exe",
            @"C:\laragon\bin\mysql\mysql-8.0.30-winx64\bin\mysql.exe"
        };

        var resolved = knownPaths.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        throw new FileNotFoundException("mysql.exe was not found.");
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || !databaseName.All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            throw new InvalidOperationException("Database name may contain only letters, digits and underscore.");
        }
    }

    private static void AppendInsert(
        StringBuilder builder,
        string tableName,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        int batchSize = 250)
    {
        var batch = new List<IReadOnlyList<string>>(batchSize);
        foreach (var row in rows)
        {
            batch.Add(row);
            if (batch.Count >= batchSize)
            {
                FlushInsert(builder, tableName, columns, batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            FlushInsert(builder, tableName, columns, batch);
        }
    }

    private static void AppendInsert(
        TextWriter writer,
        string tableName,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        int batchSize = 250)
    {
        var batch = new List<IReadOnlyList<string>>(batchSize);
        foreach (var row in rows)
        {
            batch.Add(row);
            if (batch.Count >= batchSize)
            {
                FlushInsert(writer, tableName, columns, batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            FlushInsert(writer, tableName, columns, batch);
        }
    }

    private static void FlushInsert(
        StringBuilder builder,
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        builder.Append("INSERT INTO ")
            .Append(tableName)
            .Append(" (")
            .Append(string.Join(", ", columns))
            .AppendLine(") VALUES");

        for (var index = 0; index < rows.Count; index++)
        {
            builder.Append("    (")
                .Append(string.Join(", ", rows[index]))
                .Append(')');

            builder.AppendLine(index == rows.Count - 1 ? ";" : ",");
        }
    }

    private static void FlushInsert(
        TextWriter writer,
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        writer.Write("INSERT INTO ");
        writer.Write(tableName);
        writer.Write(" (");
        writer.Write(string.Join(", ", columns));
        writer.WriteLine(") VALUES");

        for (var index = 0; index < rows.Count; index++)
        {
            writer.Write("    (");
            writer.Write(string.Join(", ", rows[index]));
            writer.Write(')');
            writer.WriteLine(index == rows.Count - 1 ? ";" : ",");
        }
    }

    private static string Sql(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "NULL";
        }

        return $"'{EscapeSql(value)}'";
    }

    private static string Sql(DateTime? value)
    {
        return value.HasValue
            ? $"'{value.Value:yyyy-MM-dd HH:mm:ss.ffffff}'"
            : "NULL";
    }

    private static string Sql(int? value)
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "NULL";
    }

    private static string EscapeSql(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record CanonicalRecord(string ObjectName, string ReferenceCode, string SourceFolder, string RecordHash, OneCRecordSnapshot Record);
    private sealed record SchemaColumnRow(string ObjectName, string SourceFileName, int OrdinalPosition, string ColumnName);
    private sealed record SchemaSectionRow(string ObjectName, string SourceFileName, string SectionName);
    private sealed record SchemaSectionColumnRow(string ObjectName, string SourceFileName, string SectionName, int OrdinalPosition, string ColumnName);
    private sealed record ObjectSnapshotRow(string ObjectName, string ReferenceCode, string? CodeValue, string? NumberValue, string? TitleText, string? SubtitleText, string? StatusText, DateTime? RecordDate, string? SourceFolder, string RecordHash, string? PayloadJson);
    private sealed record FieldSnapshotRow(string ObjectName, string ReferenceCode, string FieldName, string? RawValue, string? DisplayValue);
    private sealed record TabularSectionSnapshotRow(string ObjectName, string ReferenceCode, string SectionName);
    private sealed record TabularSectionRowRow(string ObjectName, string ReferenceCode, string SectionName, int RowNumber, string? PayloadJson);
    private sealed record TabularSectionFieldRow(string ObjectName, string ReferenceCode, string SectionName, int RowNumber, string FieldName, string? RawValue, string? DisplayValue);
    private sealed record ReferenceLinkRow(string ObjectName, string ReferenceCode, string? SectionName, int? RowNumber, string FieldName, string? TargetObjectName, string? TargetReferenceCode, string? TargetDisplayValue);
}
