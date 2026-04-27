using System.Diagnostics;
using System.Globalization;
using System.Text;
using WarehouseAutomatisaion.Infrastructure.Persistence.Sql;

namespace WarehouseAutomatisaion.Infrastructure.Projection.MySql;

public sealed class OneCOperationalProjectionService
{
    private static readonly string[] OperationalTablesToClear =
    [
        "discount_policy_item_categories",
        "discount_policy_partners",
        "discount_policy_price_groups",
        "discount_policy_warehouse_nodes",
        "purchase_receipt_additional_charges",
        "purchase_receipt_lines",
        "purchase_receipts",
        "purchase_order_payment_schedule",
        "purchase_order_lines",
        "purchase_orders",
        "sales_invoice_payment_schedule",
        "sales_invoice_lines",
        "sales_invoices",
        "partner_contacts",
        "partner_contracts",
        "bank_accounts",
        "nomenclature_items",
        "price_registration_lines",
        "price_registration_documents",
        "discount_policies",
        "price_type_rounding_rules",
        "price_types",
        "price_groups",
        "stock_balances",
        "stock_reservation_lines",
        "stock_reservations",
        "stock_transfer_lines",
        "stock_transfers",
        "stock_write_off_lines",
        "stock_write_offs",
        "inventory_count_lines",
        "inventory_counts",
        "supplier_invoice_payment_schedule",
        "supplier_invoice_lines",
        "supplier_invoices",
        "sales_shipment_lines",
        "sales_shipments",
        "sales_order_payment_schedule",
        "sales_order_lines",
        "sales_orders",
        "transfer_order_lines",
        "transfer_orders",
        "storage_bins",
        "warehouse_nodes",
        "item_categories",
        "units_of_measure",
        "employees",
        "business_partners",
        "organizations"
    ];

    private readonly OneCOperationalProjectionOptions _options;

    public OneCOperationalProjectionService(OneCOperationalProjectionOptions options)
    {
        _options = options;
    }

    public OneCOperationalProjectionResult ProjectLatestRawBatch()
    {
        ValidateDatabaseName(_options.DatabaseName);

        var mysqlExecutablePath = ResolveMysqlExecutablePath(_options.MysqlExecutablePath);
        var schemaPath = ResolveSchemaPath();
        var generatedScriptPath = ResolveGeneratedScriptPath();
        var schemaSql = File.ReadAllText(schemaPath, Encoding.UTF8);
        var script = BuildSqlScript(schemaSql);

        Directory.CreateDirectory(Path.GetDirectoryName(generatedScriptPath)!);
        File.WriteAllText(generatedScriptPath, script, new UTF8Encoding(false));

        var output = ExecuteSql(mysqlExecutablePath, generatedScriptPath);
        var resultLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("PROJECT_RESULT|", StringComparison.Ordinal));

        var counts = ParseResultCounts(resultLine);

        return new OneCOperationalProjectionResult(
            _options.DatabaseName,
            mysqlExecutablePath,
            generatedScriptPath,
            counts.OrganizationCount,
            counts.PartnerCount,
            counts.ItemCount,
            counts.SalesInvoiceCount,
            counts.PurchaseOrderCount,
            counts.PurchaseReceiptCount,
            output);
    }

    private string ResolveSchemaPath()
    {
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

        return Path.Combine(
            _options.WorkspaceRoot,
            "app_data",
            "one-c-operational-projection",
            "onec-operational-projection-latest.sql");
    }

    private string BuildSqlScript(string schemaSql)
    {
        var builder = new StringBuilder(220_000);

        builder.AppendLine($"CREATE DATABASE IF NOT EXISTS {_options.DatabaseName} CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");
        builder.AppendLine($"USE {_options.DatabaseName};");
        builder.AppendLine("SET NAMES utf8mb4;");
        builder.AppendLine("SET @latest_batch_id = (SELECT MAX(id) FROM onec_import_batches);");
        builder.AppendLine();

        if (_options.RebuildOperationalTables)
        {
            AppendOperationalReset(builder);
        }

        AppendLatestBatchTempTables(builder);
        AppendReferenceCatalogProjection(builder);
        AppendBusinessPartnerProjection(builder);
        AppendNomenclatureProjection(builder);
        AppendPartnerContractProjection(builder);
        AppendSalesOrderProjection(builder);
        AppendSalesInvoiceProjection(builder);
        AppendSalesShipmentProjection(builder);
        AppendPurchaseOrderProjection(builder);
        AppendPurchaseReceiptProjection(builder);
        AppendTransferOrderProjection(builder);
        AppendStockReservationProjection(builder);
        AppendInventoryCountProjection(builder);
        AppendStockWriteOffProjection(builder);
        AppendStockBalanceProjection(builder);
        AppendProjectionResult(builder);

        return builder.ToString();
    }

    private void AppendOperationalReset(StringBuilder builder)
    {
        builder.AppendLine("SET FOREIGN_KEY_CHECKS = 0;");
        foreach (var tableName in OperationalTablesToClear)
        {
            builder.AppendLine($"DELETE FROM {tableName};");
        }

        builder.AppendLine("SET FOREIGN_KEY_CHECKS = 1;");
        builder.AppendLine();
    }

    private void AppendLatestBatchTempTables(StringBuilder builder)
    {
        builder.AppendLine("DROP TABLE IF EXISTS onec_projection_latest_objects;");
        builder.AppendLine("DROP TABLE IF EXISTS onec_projection_field_values;");
        builder.AppendLine("DROP TABLE IF EXISTS onec_projection_tabular_field_values;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_sales_order_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_sales_order_payment_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_sales_invoice_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_sales_shipment_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_purchase_order_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_purchase_receipt_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_purchase_receipt_charge_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_transfer_order_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_stock_reservation_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_inventory_count_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_stock_write_off_line_source;");
        builder.AppendLine("DROP TEMPORARY TABLE IF EXISTS tmp_stock_balance_source;");
        builder.AppendLine();
        builder.AppendLine("CREATE TABLE onec_projection_latest_objects AS");
        builder.AppendLine("SELECT *");
        builder.AppendLine("FROM onec_object_snapshots");
        builder.AppendLine("WHERE batch_id = @latest_batch_id;");
        builder.AppendLine("ALTER TABLE onec_projection_latest_objects ADD PRIMARY KEY (id), ADD INDEX ix_onec_projection_latest_objects_name_ref (object_name, reference_code);");
        builder.AppendLine();
        builder.AppendLine("CREATE TABLE onec_projection_field_values AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    o.id AS object_snapshot_id,");
        builder.AppendLine("    o.object_name,");
        builder.AppendLine("    o.reference_code,");
        builder.AppendLine("    o.code_value,");
        builder.AppendLine("    o.number_value,");
        builder.AppendLine("    o.title_text,");
        builder.AppendLine("    o.record_date,");
        builder.AppendLine("    f.field_name,");
        builder.AppendLine("    f.raw_value,");
        builder.AppendLine("    f.display_value");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("INNER JOIN onec_field_snapshots f ON f.object_snapshot_id = o.id;");
        builder.AppendLine("ALTER TABLE onec_projection_field_values");
        builder.AppendLine("    ADD INDEX ix_onec_projection_field_values_snapshot_field (object_snapshot_id, field_name),");
        builder.AppendLine("    ADD INDEX ix_onec_projection_field_values_object_field (object_name, field_name);");
        builder.AppendLine();
        builder.AppendLine("CREATE TABLE onec_projection_tabular_field_values AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    o.id AS object_snapshot_id,");
        builder.AppendLine("    o.object_name,");
        builder.AppendLine("    o.reference_code,");
        builder.AppendLine("    s.section_name,");
        builder.AppendLine("    r.row_number,");
        builder.AppendLine("    f.field_name,");
        builder.AppendLine("    f.raw_value,");
        builder.AppendLine("    f.display_value");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("INNER JOIN onec_tabular_section_snapshots s ON s.object_snapshot_id = o.id");
        builder.AppendLine("INNER JOIN onec_tabular_section_rows r ON r.tabular_section_snapshot_id = s.id");
        builder.AppendLine("INNER JOIN onec_tabular_section_fields f ON f.tabular_section_row_id = r.id;");
        builder.AppendLine("ALTER TABLE onec_projection_tabular_field_values");
        builder.AppendLine("    ADD INDEX ix_onec_projection_tabular_field_values_object_section (object_name, section_name),");
        builder.AppendLine("    ADD INDEX ix_onec_projection_tabular_field_values_row_field (object_snapshot_id, section_name, `row_number`, field_name);");
        builder.AppendLine();
    }

    private void AppendReferenceCatalogProjection(StringBuilder builder)
    {
        var documentObjects = "'СчетНаОплату','ЗаказПоставщику','ПриходнаяНакладная','ЗаказПокупателя','РасходнаяНакладная','РезервированиеЗапасов'";
        var organizationRef = NormalizeReferenceExpression("src.organization_ref");
        var employeeRef = NormalizeReferenceExpression("src.employee_ref");
        var warehouseRef = NormalizeReferenceExpression("src.warehouse_ref");
        var unitRef = NormalizeReferenceExpression("src.unit_ref");
        var categoryRef = NormalizeReferenceExpression("src.category_ref");
        var priceGroupRef = NormalizeReferenceExpression("src.price_group_ref");
        var priceTypeRef = NormalizeReferenceExpression("src.price_type_ref");

        builder.AppendLine("INSERT IGNORE INTO organizations (id, code, name, tax_id)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} AS id,");
        builder.AppendLine($"    CONCAT('ORG-', {ShortHashExpression($"CONCAT('org|', {organizationRef})", 10)}) AS code,");
        builder.AppendLine($"    COALESCE(src.organization_name, CONCAT('Организация ', {ShortHashExpression($"CONCAT('org|', {organizationRef})", 8)})) AS name,");
        builder.AppendLine("    NULL AS tax_id");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("reference_code")} AS organization_ref,");
        builder.AppendLine($"        COALESCE({HumanTextExpression("title_text")}, {HumanTextExpression("code_value")}) AS organization_name");
        builder.AppendLine("    FROM onec_projection_latest_objects");
        builder.AppendLine("    WHERE object_name = 'Организации'");
        builder.AppendLine("    UNION DISTINCT");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS organization_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS organization_name");
        builder.AppendLine("    FROM onec_projection_field_values");
        builder.AppendLine($"    WHERE field_name = 'Организация' AND object_name IN ({documentObjects})");
        builder.AppendLine(") src");
        builder.AppendLine($"WHERE {organizationRef} IS NOT NULL;");
        builder.AppendLine();
        builder.AppendLine($"INSERT IGNORE INTO organizations (id, code, name, tax_id) VALUES ({DeterministicGuidExpression("'system|organization|default'")}, 'ORG-DEFAULT', 'Основная организация', NULL);");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO employees (id, code, full_name, email)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('employee|', {employeeRef})")} AS id,");
        builder.AppendLine($"    CONCAT('EMP-', {ShortHashExpression($"CONCAT('employee|', {employeeRef})", 10)}) AS code,");
        builder.AppendLine($"    COALESCE(src.employee_name, CONCAT('Сотрудник ', {ShortHashExpression($"CONCAT('employee|', {employeeRef})", 8)})) AS full_name,");
        builder.AppendLine("    NULL AS email");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("reference_code")} AS employee_ref,");
        builder.AppendLine($"        COALESCE({HumanTextExpression("title_text")}, {HumanTextExpression("code_value")}) AS employee_name");
        builder.AppendLine("    FROM onec_projection_latest_objects");
        builder.AppendLine("    WHERE object_name = 'Сотрудники'");
        builder.AppendLine("    UNION DISTINCT");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS employee_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS employee_name");
        builder.AppendLine("    FROM onec_projection_field_values");
        builder.AppendLine($"    WHERE field_name IN ('Автор', 'Ответственный') AND object_name IN ({documentObjects}, 'Контрагенты')");
        builder.AppendLine(") src");
        builder.AppendLine($"WHERE {employeeRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO warehouse_nodes (id, parent_id, code, name, type, is_reserve_area)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('warehouse|', {warehouseRef})")} AS id,");
        builder.AppendLine("    NULL AS parent_id,");
        builder.AppendLine($"    CONCAT('WH-', {ShortHashExpression($"CONCAT('warehouse|', {warehouseRef})", 10)}) AS code,");
        builder.AppendLine($"    COALESCE(MAX(src.warehouse_name), CONCAT(CASE WHEN MAX(src.is_reserve_area) = 1 THEN 'Резервный склад ' ELSE 'Склад ' END, {ShortHashExpression($"CONCAT('warehouse|', {warehouseRef})", 8)})) AS name,");
        builder.AppendLine("    1 AS type,");
        builder.AppendLine("    MAX(src.is_reserve_area) AS is_reserve_area");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("reference_code")} AS warehouse_ref,");
        builder.AppendLine($"        COALESCE({HumanTextExpression("title_text")}, {HumanTextExpression("code_value")}) AS warehouse_name,");
        builder.AppendLine("        0 AS is_reserve_area");
        builder.AppendLine("    FROM onec_projection_latest_objects");
        builder.AppendLine("    WHERE object_name = 'СкладыИМагазины'");
        builder.AppendLine("    UNION ALL");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS warehouse_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS warehouse_name,");
        builder.AppendLine("        CASE WHEN field_name LIKE '%Резерв%' THEN 1 ELSE 0 END AS is_reserve_area");
        builder.AppendLine("    FROM onec_projection_field_values");
        builder.AppendLine("    WHERE field_name IN ('Склад', 'СтруктурнаяЕдиница', 'СтруктурнаяЕдиницаПродажи', 'СтруктурнаяЕдиницаРезерв', 'СтруктурнаяЕдиницаПолучатель', 'ИсходноеМестоРезерва', 'НовоеМестоРезерва')");
        builder.AppendLine("    UNION ALL");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS warehouse_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS warehouse_name,");
        builder.AppendLine("        CASE WHEN field_name LIKE '%Резерв%' THEN 1 ELSE 0 END AS is_reserve_area");
        builder.AppendLine("    FROM onec_projection_tabular_field_values");
        builder.AppendLine("    WHERE field_name IN ('Склад', 'СтруктурнаяЕдиница', 'СтруктурнаяЕдиницаПродажи', 'СтруктурнаяЕдиницаРезерв', 'СтруктурнаяЕдиницаПолучатель', 'ИсходноеМестоРезерва', 'НовоеМестоРезерва')");
        builder.AppendLine(") src");
        builder.AppendLine($"WHERE {warehouseRef} IS NOT NULL");
        builder.AppendLine("GROUP BY src.warehouse_ref;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO units_of_measure (id, code, name, symbol)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('uom|', {unitRef})")} AS id,");
        builder.AppendLine($"    CONCAT('UOM-', {ShortHashExpression($"CONCAT('uom|', {unitRef})", 10)}) AS code,");
        builder.AppendLine($"    COALESCE(src.unit_name, CONCAT('Единица ', {ShortHashExpression($"CONCAT('uom|', {unitRef})", 8)})) AS name,");
        builder.AppendLine("    NULL AS symbol");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("reference_code")} AS unit_ref,");
        builder.AppendLine($"        COALESCE({HumanTextExpression("title_text")}, {HumanTextExpression("code_value")}) AS unit_name");
        builder.AppendLine("    FROM onec_projection_latest_objects");
        builder.AppendLine("    WHERE object_name = 'ЕдиницыИзмерения'");
        builder.AppendLine("    UNION DISTINCT");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS unit_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS unit_name");
        builder.AppendLine("    FROM onec_projection_field_values");
        builder.AppendLine("    WHERE field_name = 'ЕдиницаИзмерения'");
        builder.AppendLine("    UNION DISTINCT");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS unit_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS unit_name");
        builder.AppendLine("    FROM onec_projection_tabular_field_values");
        builder.AppendLine("    WHERE field_name = 'ЕдиницаИзмерения'");
        builder.AppendLine(") src");
        builder.AppendLine($"WHERE {unitRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO item_categories (id, parent_id, code, name)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('category|', {categoryRef})")} AS id,");
        builder.AppendLine("    NULL AS parent_id,");
        builder.AppendLine($"    CONCAT('CAT-', {ShortHashExpression($"CONCAT('category|', {categoryRef})", 10)}) AS code,");
        builder.AppendLine($"    COALESCE(src.category_name, CONCAT('Категория ', {ShortHashExpression($"CONCAT('category|', {categoryRef})", 8)})) AS name");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("reference_code")} AS category_ref,");
        builder.AppendLine($"        COALESCE({HumanTextExpression("title_text")}, {HumanTextExpression("code_value")}) AS category_name");
        builder.AppendLine("    FROM onec_projection_latest_objects");
        builder.AppendLine("    WHERE object_name = 'КатегорииНоменклатуры'");
        builder.AppendLine("    UNION DISTINCT");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS category_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS category_name");
        builder.AppendLine("    FROM onec_projection_field_values");
        builder.AppendLine("    WHERE object_name = 'Номенклатура' AND field_name = 'КатегорияНоменклатуры'");
        builder.AppendLine(") src");
        builder.AppendLine($"WHERE {categoryRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO price_groups (id, code, name)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('price-group|', {priceGroupRef})")} AS id,");
        builder.AppendLine($"    CONCAT('PG-', {ShortHashExpression($"CONCAT('price-group|', {priceGroupRef})", 10)}) AS code,");
        builder.AppendLine($"    COALESCE(src.price_group_name, CONCAT('Ценовая группа ', {ShortHashExpression($"CONCAT('price-group|', {priceGroupRef})", 8)})) AS name");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS price_group_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS price_group_name");
        builder.AppendLine("    FROM onec_projection_field_values");
        builder.AppendLine("    WHERE object_name = 'Номенклатура' AND field_name = 'ЦеноваяГруппа'");
        builder.AppendLine(") src");
        builder.AppendLine($"WHERE {priceGroupRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO price_types (id, code, name, currency_code, base_price_type_id, is_manual_entry_only, uses_psychological_rounding)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('price-type|', {priceTypeRef})")} AS id,");
        builder.AppendLine($"    CONCAT('PT-', {ShortHashExpression($"CONCAT('price-type|', {priceTypeRef})", 10)}) AS code,");
        builder.AppendLine($"    COALESCE(src.price_type_name, CONCAT('Вид цены ', {ShortHashExpression($"CONCAT('price-type|', {priceTypeRef})", 8)})) AS name,");
        builder.AppendLine("    'RUB' AS currency_code,");
        builder.AppendLine("    NULL AS base_price_type_id,");
        builder.AppendLine("    0 AS is_manual_entry_only,");
        builder.AppendLine("    0 AS uses_psychological_rounding");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("reference_code")} AS price_type_ref,");
        builder.AppendLine($"        COALESCE({HumanTextExpression("title_text")}, {HumanTextExpression("code_value")}) AS price_type_name");
        builder.AppendLine("    FROM onec_projection_latest_objects");
        builder.AppendLine("    WHERE object_name = 'ВидыЦен'");
        builder.AppendLine("    UNION DISTINCT");
        builder.AppendLine("    SELECT");
        builder.AppendLine($"        {NormalizeReferenceExpression("raw_value")} AS price_type_ref,");
        builder.AppendLine($"        {HumanTextExpression("display_value")} AS price_type_name");
        builder.AppendLine("    FROM onec_projection_field_values");
        builder.AppendLine($"    WHERE field_name IN ('ВидЦен', 'ВидЦенКонтрагента', 'ВидЦенВозврата') AND object_name IN ({documentObjects})");
        builder.AppendLine(") src");
        builder.AppendLine($"WHERE {priceTypeRef} IS NOT NULL;");
        builder.AppendLine();

    }

    private void AppendBusinessPartnerProjection(StringBuilder builder)
    {
        var partnerRef = NormalizeReferenceExpression("o.reference_code");
        var codeValue = NullIfBlankExpression("o.code_value");
        var titleText = NullIfBlankExpression("o.title_text");
        var nameField = BestTextExpression("nameField.display_value", "nameField.raw_value");
        var parentRef = NormalizeReferenceExpression("parentField.raw_value");
        var headRef = NormalizeReferenceExpression("headField.raw_value");
        var bankRef = NormalizeReferenceExpression("bankField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var buyerFlag = BooleanExpression("buyerField.raw_value");
        var supplierFlag = BooleanExpression("supplierField.raw_value");
        var archivedFlag = $"CASE WHEN {BooleanExpression("deleteField.raw_value")} = 1 OR {BooleanExpression("invalidField.raw_value")} = 1 THEN 1 ELSE 0 END";
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));

        builder.AppendLine("INSERT IGNORE INTO business_partners (");
        builder.AppendLine("    id, code, name, roles, parent_id, head_partner_id, default_bank_account_id,");
        builder.AppendLine("    settlement_currency_code, country_id, responsible_employee_id, primary_contact_id, is_archived)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('bp|', {partnerRef})")} AS id,");
        builder.AppendLine($"    COALESCE({codeValue}, CONCAT('BP-', {ShortHashExpression($"CONCAT('bp|', {partnerRef})", 10)})) AS code,");
        builder.AppendLine($"    COALESCE({titleText}, {nameField}, CONCAT('Контрагент ', {ShortHashExpression($"CONCAT('bp|', {partnerRef})", 8)})) AS name,");
        builder.AppendLine($"    ({buyerFlag} + ({supplierFlag} * 2)) AS roles,");
        builder.AppendLine("    NULL AS parent_id,");
        builder.AppendLine("    NULL AS head_partner_id,");
        builder.AppendLine("    NULL AS default_bank_account_id,");
        builder.AppendLine($"    {currencyCode} AS settlement_currency_code,");
        builder.AppendLine("    NULL AS country_id,");
        builder.AppendLine("    NULL AS responsible_employee_id,");
        builder.AppendLine("    NULL AS primary_contact_id,");
        builder.AppendLine($"    {archivedFlag} AS is_archived");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values nameField ON nameField.object_snapshot_id = o.id AND nameField.field_name = 'Наименование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values parentField ON parentField.object_snapshot_id = o.id AND parentField.field_name = 'Родитель'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values headField ON headField.object_snapshot_id = o.id AND headField.field_name = 'ГоловнойКонтрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values bankField ON bankField.object_snapshot_id = o.id AND bankField.field_name = 'БанковскийСчетПоУмолчанию'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values buyerField ON buyerField.object_snapshot_id = o.id AND buyerField.field_name = 'Покупатель'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values supplierField ON supplierField.object_snapshot_id = o.id AND supplierField.field_name = 'Поставщик'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values invalidField ON invalidField.object_snapshot_id = o.id AND invalidField.field_name = 'Недействителен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаРасчетовПоУмолчанию'");
        builder.AppendLine("WHERE o.object_name = 'Контрагенты';");
        builder.AppendLine();

        builder.AppendLine("UPDATE business_partners bp");
        builder.AppendLine("INNER JOIN onec_projection_latest_objects o ON o.object_name = 'Контрагенты' AND bp.id = " + DeterministicGuidExpression($"CONCAT('bp|', {partnerRef})"));
        builder.AppendLine("LEFT JOIN onec_projection_field_values parentField ON parentField.object_snapshot_id = o.id AND parentField.field_name = 'Родитель'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values headField ON headField.object_snapshot_id = o.id AND headField.field_name = 'ГоловнойКонтрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values bankField ON bankField.object_snapshot_id = o.id AND bankField.field_name = 'БанковскийСчетПоУмолчанию'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("SET");
        builder.AppendLine($"    bp.parent_id = CASE WHEN {parentRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('bp|', {parentRef})")} END,");
        builder.AppendLine($"    bp.head_partner_id = CASE WHEN {headRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('bp|', {headRef})")} END,");
        builder.AppendLine("    bp.default_bank_account_id = NULL,");
        builder.AppendLine($"    bp.responsible_employee_id = CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END;");
        builder.AppendLine();
    }

    private void AppendNomenclatureProjection(StringBuilder builder)
    {
        var itemRef = NormalizeReferenceExpression("o.reference_code");
        var codeValue = NullIfBlankExpression("o.code_value");
        var titleText = NullIfBlankExpression("o.title_text");
        var skuValue = BestTextExpression("skuField.display_value", "skuField.raw_value");
        var unitRef = NormalizeReferenceExpression("unitField.raw_value");
        var categoryRef = NormalizeReferenceExpression("categoryField.raw_value");
        var supplierRef = NormalizeReferenceExpression("supplierField.raw_value");
        var warehouseRef = NormalizeReferenceExpression("warehouseField.raw_value");
        var priceGroupRef = NormalizeReferenceExpression("priceGroupField.raw_value");
        var itemKind = BestTextExpression("kindField.display_value", "kindField.raw_value");
        var vatCode = BestTextExpression("vatField.display_value", "vatField.raw_value");
        var tracksBatches = BooleanExpression("batchField.raw_value");
        var tracksSerials = BooleanExpression("serialField.raw_value");
        var parentRef = NormalizeReferenceExpression("parentField.raw_value");
        var isGroup = BooleanExpression("groupField.raw_value");

        builder.AppendLine("INSERT IGNORE INTO nomenclature_items (");
        builder.AppendLine("    id, parent_id, code, sku, name, unit_of_measure_id, category_id, default_supplier_id,");
        builder.AppendLine("    default_warehouse_node_id, default_storage_bin_id, price_group_id, item_kind, vat_rate_code, tracks_batches, tracks_serials)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('item|', {itemRef})")} AS id,");
        builder.AppendLine("    NULL AS parent_id,");
        builder.AppendLine($"    COALESCE({codeValue}, CONCAT('ITEM-', {ShortHashExpression($"CONCAT('item|', {itemRef})", 10)})) AS code,");
        builder.AppendLine($"    CONCAT(LEFT(COALESCE({skuValue}, {codeValue}, 'SKU'), 119), '-', {ShortHashExpression($"CONCAT('item|', {itemRef})", 8)}) AS sku,");
        builder.AppendLine($"    COALESCE({titleText}, CONCAT('Номенклатура ', {ShortHashExpression($"CONCAT('item|', {itemRef})", 8)})) AS name,");
        builder.AppendLine($"    CASE WHEN {unitRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('uom|', {unitRef})")} END AS unit_of_measure_id,");
        builder.AppendLine($"    CASE WHEN {categoryRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('category|', {categoryRef})")} END AS category_id,");
        builder.AppendLine($"    CASE WHEN {supplierRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('bp|', {supplierRef})")} END AS default_supplier_id,");
        builder.AppendLine($"    CASE WHEN {warehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {warehouseRef})")} END AS default_warehouse_node_id,");
        builder.AppendLine("    NULL AS default_storage_bin_id,");
        builder.AppendLine($"    CASE WHEN {priceGroupRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('price-group|', {priceGroupRef})")} END AS price_group_id,");
        builder.AppendLine($"    COALESCE({itemKind}, CASE WHEN {isGroup} = 1 THEN 'Group' ELSE NULL END) AS item_kind,");
        builder.AppendLine($"    {vatCode} AS vat_rate_code,");
        builder.AppendLine($"    {tracksBatches} AS tracks_batches,");
        builder.AppendLine($"    {tracksSerials} AS tracks_serials");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values skuField ON skuField.object_snapshot_id = o.id AND skuField.field_name = 'Артикул'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values unitField ON unitField.object_snapshot_id = o.id AND unitField.field_name = 'ЕдиницаИзмерения'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values categoryField ON categoryField.object_snapshot_id = o.id AND categoryField.field_name = 'КатегорияНоменклатуры'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values supplierField ON supplierField.object_snapshot_id = o.id AND supplierField.field_name = 'Поставщик'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values warehouseField ON warehouseField.object_snapshot_id = o.id AND warehouseField.field_name = 'Склад'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values priceGroupField ON priceGroupField.object_snapshot_id = o.id AND priceGroupField.field_name = 'ЦеноваяГруппа'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values kindField ON kindField.object_snapshot_id = o.id AND kindField.field_name = 'ТипНоменклатуры'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values vatField ON vatField.object_snapshot_id = o.id AND vatField.field_name = 'ВидСтавкиНДС'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values batchField ON batchField.object_snapshot_id = o.id AND batchField.field_name = 'ИспользоватьПартии'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values serialField ON serialField.object_snapshot_id = o.id AND serialField.field_name = 'ИспользоватьСерииНоменклатуры'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values parentField ON parentField.object_snapshot_id = o.id AND parentField.field_name = 'Родитель'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values groupField ON groupField.object_snapshot_id = o.id AND groupField.field_name = 'ЭтоГруппа'");
        builder.AppendLine("WHERE o.object_name = 'Номенклатура';");
        builder.AppendLine();

        builder.AppendLine("UPDATE nomenclature_items ni");
        builder.AppendLine("INNER JOIN onec_projection_latest_objects o ON o.object_name = 'Номенклатура' AND ni.id = " + DeterministicGuidExpression($"CONCAT('item|', {itemRef})"));
        builder.AppendLine("LEFT JOIN onec_projection_field_values parentField ON parentField.object_snapshot_id = o.id AND parentField.field_name = 'Родитель'");
        builder.AppendLine("SET");
        builder.AppendLine($"    ni.parent_id = CASE WHEN {parentRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('item|', {parentRef})")} END;");
        builder.AppendLine();
    }

    private void AppendPartnerContractProjection(StringBuilder builder)
    {
        var contractRef = NormalizeReferenceExpression("contractField.raw_value");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var partnerRef = NormalizeReferenceExpression("partnerField.raw_value");
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));
        var contractText = BestTextExpression("contractField.display_value", "contractField.raw_value");
        var contractNumber = $"CONCAT('CTR-', {ShortHashExpression($"CONCAT('contract|', {contractRef})", 12)})";
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");

        builder.AppendLine("INSERT IGNORE INTO partner_contracts (id, number, business_partner_id, organization_id, settlement_currency_code, requires_prepayment)");
        builder.AppendLine("SELECT DISTINCT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('contract|', {contractRef})")} AS id,");
        builder.AppendLine($"    CASE WHEN {contractText} IS NULL THEN {contractNumber} ELSE CONCAT(LEFT({contractText}, 48), '-', {ShortHashExpression($"CONCAT('contract|', {contractRef})", 4)}) END AS number,");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('bp|', {partnerRef})")} AS business_partner_id,");
        builder.AppendLine($"    COALESCE(CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine($"    {currencyCode} AS settlement_currency_code,");
        builder.AppendLine("    0 AS requires_prepayment");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values contractField ON contractField.object_snapshot_id = o.id AND contractField.field_name = 'Договор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values partnerField ON partnerField.object_snapshot_id = o.id AND partnerField.field_name = 'Контрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаДокумента'");
        builder.AppendLine("WHERE o.object_name IN ('СчетНаОплату', 'ЗаказПоставщику', 'ПриходнаяНакладная', 'ЗаказПокупателя', 'РасходнаяНакладная')");
        builder.AppendLine($"  AND {contractRef} IS NOT NULL");
        builder.AppendLine($"  AND {partnerRef} IS NOT NULL;");
        builder.AppendLine();
    }

    private void AppendSalesOrderProjection(StringBuilder builder)
    {
        var orderRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var customerRef = NormalizeReferenceExpression("customerField.raw_value");
        var contractRef = NormalizeReferenceExpression("contractField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var priceTypeRef = NormalizeReferenceExpression("priceTypeField.raw_value");
        var salesWarehouseRef = NormalizeReferenceExpression("COALESCE(salesWarehouseField.raw_value, warehouseField.raw_value)");
        var reserveWarehouseRef = NormalizeReferenceExpression("reserveWarehouseField.raw_value");
        var postingState = PostingStateExpression("postedField.raw_value", "deleteField.raw_value");
        var lifecycleStatus = LifecycleStatusExpression("postedField.raw_value", "deleteField.raw_value");
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));
        var documentNumber = $"COALESCE({NullIfBlankExpression("o.number_value")}, {BestTextExpression("numberField.display_value", "numberField.raw_value")}, CONCAT('SO-', {ShortHashExpression($"CONCAT('sales-order|', {orderRef})", 10)}))";
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");

        builder.AppendLine("INSERT IGNORE INTO sales_orders (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, currency_code, customer_id, contract_id, price_type_id,");
        builder.AppendLine("    warehouse_node_id, reserve_warehouse_node_id, storage_bin_id, lifecycle_status)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('sales-order|', {orderRef})")} AS id,");
        builder.AppendLine($"    {documentNumber} AS number,");
        builder.AppendLine("    COALESCE(o.record_date, CURRENT_TIMESTAMP(6)) AS document_date,");
        builder.AppendLine($"    {postingState} AS posting_state,");
        builder.AppendLine($"    COALESCE(CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine($"    CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END AS author_id,");
        builder.AppendLine($"    CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END AS responsible_employee_id,");
        builder.AppendLine($"    COALESCE({BestTextExpression("commentField.display_value", "commentField.raw_value")}, {BestTextExpression("notesField.display_value", "notesField.raw_value")}) AS comment_text,");
        builder.AppendLine($"    CASE WHEN {NormalizeReferenceExpression("baseDocumentField.raw_value")} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('sales-order|', {NormalizeReferenceExpression("baseDocumentField.raw_value")})")} END AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine($"    {currencyCode} AS currency_code,");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('bp|', {customerRef})")} AS customer_id,");
        builder.AppendLine($"    CASE WHEN {contractRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('contract|', {contractRef})")} END AS contract_id,");
        builder.AppendLine($"    CASE WHEN {priceTypeRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('price-type|', {priceTypeRef})")} END AS price_type_id,");
        builder.AppendLine($"    CASE WHEN {salesWarehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {salesWarehouseRef})")} END AS warehouse_node_id,");
        builder.AppendLine($"    CASE WHEN {reserveWarehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {reserveWarehouseRef})")} END AS reserve_warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine($"    {lifecycleStatus} AS lifecycle_status");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values customerField ON customerField.object_snapshot_id = o.id AND customerField.field_name = 'Контрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values contractField ON contractField.object_snapshot_id = o.id AND contractField.field_name = 'Договор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values notesField ON notesField.object_snapshot_id = o.id AND notesField.field_name = 'Заметки'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values baseDocumentField ON baseDocumentField.object_snapshot_id = o.id AND baseDocumentField.field_name = 'ДокументОснование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values priceTypeField ON priceTypeField.object_snapshot_id = o.id AND priceTypeField.field_name = 'ВидЦен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values salesWarehouseField ON salesWarehouseField.object_snapshot_id = o.id AND salesWarehouseField.field_name = 'СтруктурнаяЕдиницаПродажи'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values warehouseField ON warehouseField.object_snapshot_id = o.id AND warehouseField.field_name = 'СтруктурнаяЕдиница'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values reserveWarehouseField ON reserveWarehouseField.object_snapshot_id = o.id AND reserveWarehouseField.field_name = 'СтруктурнаяЕдиницаРезерв'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine("WHERE o.object_name = 'ЗаказПокупателя'");
        builder.AppendLine($"  AND {customerRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_sales_order_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    ROW_NUMBER() OVER (PARTITION BY src.reference_code ORDER BY CASE WHEN src.section_name IN ('Запасы', 'БонусныеБаллыКНачислению') THEN 1 WHEN src.section_name = 'Работы' THEN 2 ELSE 3 END, COALESCE(src.line_no, src.row_number + 1), src.row_number) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.price,");
        builder.AppendLine("    src.discount_percent,");
        builder.AppendLine("    src.discount_amount,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.vat_rate_code,");
        builder.AppendLine("    src.tax_amount,");
        builder.AppendLine("    COALESCE(src.total, src.amount) AS total,");
        builder.AppendLine("    src.content_text");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.section_name,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Цена' THEN {DecimalExpression("t.raw_value")} END) AS price,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ПроцентСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_percent,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Сумма' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СтавкаНДС' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS vat_rate_code,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаНДС' THEN {DecimalExpression("t.raw_value")} END) AS tax_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Всего' THEN {DecimalExpression("t.raw_value")} END) AS total,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Содержание' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS content_text");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'ЗаказПокупателя' AND t.section_name IN ('Запасы', 'БонусныеБаллыКНачислению', 'Работы', 'Подарки')");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.section_name, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO sales_order_lines (");
        builder.AppendLine("    id, sales_order_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    price, discount_percent, discount_amount, amount, vat_rate_code, tax_amount, total, content_text)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-order-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-order|', s.reference_code)")} AS sales_order_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine("    COALESCE(s.price, 0) AS price,");
        builder.AppendLine("    COALESCE(s.discount_percent, 0) AS discount_percent,");
        builder.AppendLine("    COALESCE(s.discount_amount, 0) AS discount_amount,");
        builder.AppendLine("    COALESCE(s.amount, 0) AS amount,");
        builder.AppendLine("    s.vat_rate_code,");
        builder.AppendLine("    COALESCE(s.tax_amount, 0) AS tax_amount,");
        builder.AppendLine("    COALESCE(s.total, COALESCE(s.amount, 0)) AS total,");
        builder.AppendLine("    s.content_text");
        builder.AppendLine("FROM tmp_sales_order_line_source s;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_sales_order_payment_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.due_date,");
        builder.AppendLine("    src.payment_percent,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.tax_amount");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ДатаОплаты' THEN {DateExpression("t.raw_value")} END) AS due_date,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ПроцентОплаты' THEN {DecimalExpression("t.raw_value")} END) AS payment_percent,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаОплаты' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаНДСОплаты' THEN {DecimalExpression("t.raw_value")} END) AS tax_amount");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'ЗаказПокупателя' AND t.section_name = 'ПлатежныйКалендарь'");
        builder.AppendLine("    GROUP BY t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.due_date IS NOT NULL OR src.amount IS NOT NULL OR src.payment_percent IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO sales_order_payment_schedule (id, sales_order_id, line_no, due_date, payment_percent, amount, tax_amount)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-order-payment|', p.reference_code, '|', p.line_no)")} AS id,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-order|', p.reference_code)")} AS sales_order_id,");
        builder.AppendLine("    p.line_no,");
        builder.AppendLine("    COALESCE(p.due_date, CURRENT_DATE()) AS due_date,");
        builder.AppendLine("    COALESCE(p.payment_percent, 0) AS payment_percent,");
        builder.AppendLine("    COALESCE(p.amount, 0) AS amount,");
        builder.AppendLine("    COALESCE(p.tax_amount, 0) AS tax_amount");
        builder.AppendLine("FROM tmp_sales_order_payment_source p;");
        builder.AppendLine();
    }

    private void AppendSalesInvoiceProjection(StringBuilder builder)
    {
        var invoiceRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var customerRef = NormalizeReferenceExpression("customerField.raw_value");
        var contractRef = NormalizeReferenceExpression("contractField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var priceTypeRef = NormalizeReferenceExpression("priceTypeField.raw_value");
        var bankRef = NormalizeReferenceExpression("bankField.raw_value");
        var totalAmount = DecimalExpression("sumField.raw_value");
        var postingState = PostingStateExpression("postedField.raw_value", "deleteField.raw_value");
        var lifecycleStatus = LifecycleStatusExpression("postedField.raw_value", "deleteField.raw_value");
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));
        var documentNumber = $"COALESCE({NullIfBlankExpression("o.number_value")}, {BestTextExpression("numberField.display_value", "numberField.raw_value")}, CONCAT('INV-', {ShortHashExpression($"CONCAT('sales-invoice|', {invoiceRef})", 10)}))";
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");

        builder.AppendLine("INSERT IGNORE INTO sales_invoices (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, currency_code, customer_id, contract_id, price_type_id,");
        builder.AppendLine("    company_bank_account_id, cashbox_id, lifecycle_status, total_amount)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('sales-invoice|', {invoiceRef})")} AS id,");
        builder.AppendLine($"    {documentNumber} AS number,");
        builder.AppendLine("    COALESCE(o.record_date, CURRENT_TIMESTAMP(6)) AS document_date,");
        builder.AppendLine($"    {postingState} AS posting_state,");
        builder.AppendLine($"    COALESCE(CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine($"    CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END AS author_id,");
        builder.AppendLine($"    CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END AS responsible_employee_id,");
        builder.AppendLine($"    {BestTextExpression("commentField.display_value", "commentField.raw_value")} AS comment_text,");
        builder.AppendLine($"    CASE WHEN {NormalizeReferenceExpression("baseDocumentField.raw_value")} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('base-document|', {NormalizeReferenceExpression("baseDocumentField.raw_value")})")} END AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine($"    {currencyCode} AS currency_code,");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('bp|', {customerRef})")} AS customer_id,");
        builder.AppendLine($"    CASE WHEN {contractRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('contract|', {contractRef})")} END AS contract_id,");
        builder.AppendLine($"    CASE WHEN {priceTypeRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('price-type|', {priceTypeRef})")} END AS price_type_id,");
        builder.AppendLine("    NULL AS company_bank_account_id,");
        builder.AppendLine("    NULL AS cashbox_id,");
        builder.AppendLine($"    {lifecycleStatus} AS lifecycle_status,");
        builder.AppendLine($"    COALESCE({totalAmount}, 0) AS total_amount");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values customerField ON customerField.object_snapshot_id = o.id AND customerField.field_name = 'Контрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values contractField ON contractField.object_snapshot_id = o.id AND contractField.field_name = 'Договор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values baseDocumentField ON baseDocumentField.object_snapshot_id = o.id AND baseDocumentField.field_name = 'ДокументОснование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values priceTypeField ON priceTypeField.object_snapshot_id = o.id AND priceTypeField.field_name = 'ВидЦен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values bankField ON bankField.object_snapshot_id = o.id AND bankField.field_name = 'БанковскийСчет'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values sumField ON sumField.object_snapshot_id = o.id AND sumField.field_name = 'СуммаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine("WHERE o.object_name = 'СчетНаОплату'");
        builder.AppendLine($"  AND {customerRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_sales_invoice_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.price,");
        builder.AppendLine("    src.discount_percent,");
        builder.AppendLine("    src.discount_amount,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.vat_rate_code,");
        builder.AppendLine("    src.tax_amount,");
        builder.AppendLine("    COALESCE(src.total, src.amount) AS total,");
        builder.AppendLine("    src.content_text");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Цена' THEN {DecimalExpression("t.raw_value")} END) AS price,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ПроцентСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_percent,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Сумма' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СтавкаНДС' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS vat_rate_code,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаНДС' THEN {DecimalExpression("t.raw_value")} END) AS tax_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Всего' THEN {DecimalExpression("t.raw_value")} END) AS total,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Содержание' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS content_text");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'СчетНаОплату' AND t.section_name = 'Запасы'");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO sales_invoice_lines (");
        builder.AppendLine("    id, sales_invoice_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    price, discount_percent, discount_amount, amount, vat_rate_code, tax_amount, total, content_text)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-invoice-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-invoice|', s.reference_code)")} AS sales_invoice_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine("    COALESCE(s.price, 0) AS price,");
        builder.AppendLine("    COALESCE(s.discount_percent, 0) AS discount_percent,");
        builder.AppendLine("    COALESCE(s.discount_amount, 0) AS discount_amount,");
        builder.AppendLine("    COALESCE(s.amount, 0) AS amount,");
        builder.AppendLine("    s.vat_rate_code,");
        builder.AppendLine("    COALESCE(s.tax_amount, 0) AS tax_amount,");
        builder.AppendLine("    COALESCE(s.total, COALESCE(s.amount, 0)) AS total,");
        builder.AppendLine("    s.content_text");
        builder.AppendLine("FROM tmp_sales_invoice_line_source s;");
        builder.AppendLine();
    }

    private void AppendSalesShipmentProjection(StringBuilder builder)
    {
        var shipmentRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var customerRef = NormalizeReferenceExpression("customerField.raw_value");
        var contractRef = NormalizeReferenceExpression("contractField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var priceTypeRef = NormalizeReferenceExpression("priceTypeField.raw_value");
        var carrierRef = NormalizeReferenceExpression("carrierField.raw_value");
        var salesOrderRef = NormalizeReferenceExpression("orderField.raw_value");
        var baseDocumentRef = NormalizeReferenceExpression("baseDocumentField.raw_value");
        var warehouseRef = NormalizeReferenceExpression("warehouseField.raw_value");
        var totalAmount = DecimalExpression("sumField.raw_value");
        var postingState = PostingStateExpression("postedField.raw_value", "deleteField.raw_value");
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));
        var documentNumber = $"COALESCE({NullIfBlankExpression("o.number_value")}, {BestTextExpression("numberField.display_value", "numberField.raw_value")}, CONCAT('SHIP-', {ShortHashExpression($"CONCAT('sales-shipment|', {shipmentRef})", 10)}))";
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");

        builder.AppendLine("INSERT IGNORE INTO sales_shipments (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, currency_code, customer_id, contract_id, sales_order_id,");
        builder.AppendLine("    price_type_id, warehouse_node_id, storage_bin_id, carrier_id, total_amount)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('sales-shipment|', {shipmentRef})")} AS id,");
        builder.AppendLine($"    {documentNumber} AS number,");
        builder.AppendLine("    COALESCE(o.record_date, CURRENT_TIMESTAMP(6)) AS document_date,");
        builder.AppendLine($"    {postingState} AS posting_state,");
        builder.AppendLine($"    COALESCE(CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine($"    CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END AS author_id,");
        builder.AppendLine($"    CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END AS responsible_employee_id,");
        builder.AppendLine($"    {BestTextExpression("commentField.display_value", "commentField.raw_value")} AS comment_text,");
        builder.AppendLine($"    CASE WHEN {salesOrderRef} IS NOT NULL THEN {DeterministicGuidExpression($"CONCAT('sales-order|', {salesOrderRef})")} WHEN {baseDocumentRef} IS NOT NULL THEN {DeterministicGuidExpression($"CONCAT('base-document|', {baseDocumentRef})")} ELSE NULL END AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine($"    {currencyCode} AS currency_code,");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('bp|', {customerRef})")} AS customer_id,");
        builder.AppendLine($"    CASE WHEN {contractRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('contract|', {contractRef})")} END AS contract_id,");
        builder.AppendLine($"    CASE WHEN {salesOrderRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('sales-order|', {salesOrderRef})")} END AS sales_order_id,");
        builder.AppendLine($"    CASE WHEN {priceTypeRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('price-type|', {priceTypeRef})")} END AS price_type_id,");
        builder.AppendLine($"    CASE WHEN {warehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {warehouseRef})")} END AS warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine($"    CASE WHEN {carrierRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('bp|', {carrierRef})")} END AS carrier_id,");
        builder.AppendLine($"    COALESCE({totalAmount}, 0) AS total_amount");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values customerField ON customerField.object_snapshot_id = o.id AND customerField.field_name = 'Контрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values contractField ON contractField.object_snapshot_id = o.id AND contractField.field_name = 'Договор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values orderField ON orderField.object_snapshot_id = o.id AND orderField.field_name = 'Заказ'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values baseDocumentField ON baseDocumentField.object_snapshot_id = o.id AND baseDocumentField.field_name = 'ДокументОснование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values priceTypeField ON priceTypeField.object_snapshot_id = o.id AND priceTypeField.field_name = 'ВидЦен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values carrierField ON carrierField.object_snapshot_id = o.id AND carrierField.field_name = 'Перевозчик'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values warehouseField ON warehouseField.object_snapshot_id = o.id AND warehouseField.field_name = 'СтруктурнаяЕдиница'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values sumField ON sumField.object_snapshot_id = o.id AND sumField.field_name = 'СуммаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine("WHERE o.object_name = 'РасходнаяНакладная'");
        builder.AppendLine($"  AND {customerRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_sales_shipment_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.price,");
        builder.AppendLine("    src.discount_percent,");
        builder.AppendLine("    src.discount_amount,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.vat_rate_code,");
        builder.AppendLine("    src.tax_amount,");
        builder.AppendLine("    COALESCE(src.total, src.amount) AS total,");
        builder.AppendLine("    src.content_text");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Цена' THEN {DecimalExpression("t.raw_value")} END) AS price,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ПроцентСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_percent,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Сумма' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СтавкаНДС' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS vat_rate_code,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаНДС' THEN {DecimalExpression("t.raw_value")} END) AS tax_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Всего' THEN {DecimalExpression("t.raw_value")} END) AS total,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Содержание' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS content_text");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'РасходнаяНакладная' AND t.section_name = 'Запасы'");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO sales_shipment_lines (");
        builder.AppendLine("    id, sales_shipment_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    price, discount_percent, discount_amount, amount, vat_rate_code, tax_amount, total, content_text)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-shipment-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('sales-shipment|', s.reference_code)")} AS sales_shipment_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine("    COALESCE(s.price, 0) AS price,");
        builder.AppendLine("    COALESCE(s.discount_percent, 0) AS discount_percent,");
        builder.AppendLine("    COALESCE(s.discount_amount, 0) AS discount_amount,");
        builder.AppendLine("    COALESCE(s.amount, 0) AS amount,");
        builder.AppendLine("    s.vat_rate_code,");
        builder.AppendLine("    COALESCE(s.tax_amount, 0) AS tax_amount,");
        builder.AppendLine("    COALESCE(s.total, COALESCE(s.amount, 0)) AS total,");
        builder.AppendLine("    s.content_text");
        builder.AppendLine("FROM tmp_sales_shipment_line_source s;");
        builder.AppendLine();
    }

    private void AppendPurchaseOrderProjection(StringBuilder builder)
    {
        var orderRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var supplierRef = NormalizeReferenceExpression("supplierField.raw_value");
        var contractRef = NormalizeReferenceExpression("contractField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var priceTypeRef = NormalizeReferenceExpression("priceTypeField.raw_value");
        var warehouseRef = NormalizeReferenceExpression("warehouseField.raw_value");
        var reserveWarehouseRef = NormalizeReferenceExpression("reserveWarehouseField.raw_value");
        var postingState = PostingStateExpression("postedField.raw_value", "deleteField.raw_value");
        var lifecycleStatus = LifecycleStatusExpression("postedField.raw_value", "deleteField.raw_value");
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));
        var documentNumber = $"COALESCE({NullIfBlankExpression("o.number_value")}, {BestTextExpression("numberField.display_value", "numberField.raw_value")}, CONCAT('PO-', {ShortHashExpression($"CONCAT('purchase-order|', {orderRef})", 10)}))";
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");

        builder.AppendLine("INSERT IGNORE INTO purchase_orders (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, currency_code, supplier_id, contract_id, partner_price_type_id,");
        builder.AppendLine("    linked_sales_order_id, warehouse_node_id, reserve_warehouse_node_id, lifecycle_status)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('purchase-order|', {orderRef})")} AS id,");
        builder.AppendLine($"    {documentNumber} AS number,");
        builder.AppendLine("    COALESCE(o.record_date, CURRENT_TIMESTAMP(6)) AS document_date,");
        builder.AppendLine($"    {postingState} AS posting_state,");
        builder.AppendLine($"    COALESCE(CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine($"    CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END AS author_id,");
        builder.AppendLine($"    CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END AS responsible_employee_id,");
        builder.AppendLine($"    {BestTextExpression("commentField.display_value", "commentField.raw_value")} AS comment_text,");
        builder.AppendLine($"    CASE WHEN {NormalizeReferenceExpression("baseDocumentField.raw_value")} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('base-document|', {NormalizeReferenceExpression("baseDocumentField.raw_value")})")} END AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine($"    {currencyCode} AS currency_code,");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('bp|', {supplierRef})")} AS supplier_id,");
        builder.AppendLine($"    CASE WHEN {contractRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('contract|', {contractRef})")} END AS contract_id,");
        builder.AppendLine($"    CASE WHEN {priceTypeRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('price-type|', {priceTypeRef})")} END AS partner_price_type_id,");
        builder.AppendLine("    NULL AS linked_sales_order_id,");
        builder.AppendLine($"    CASE WHEN {warehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {warehouseRef})")} END AS warehouse_node_id,");
        builder.AppendLine($"    CASE WHEN {reserveWarehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {reserveWarehouseRef})")} END AS reserve_warehouse_node_id,");
        builder.AppendLine($"    {lifecycleStatus} AS lifecycle_status");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values supplierField ON supplierField.object_snapshot_id = o.id AND supplierField.field_name = 'Контрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values contractField ON contractField.object_snapshot_id = o.id AND contractField.field_name = 'Договор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values baseDocumentField ON baseDocumentField.object_snapshot_id = o.id AND baseDocumentField.field_name = 'ДокументОснование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values priceTypeField ON priceTypeField.object_snapshot_id = o.id AND priceTypeField.field_name = 'ВидЦенКонтрагента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values warehouseField ON warehouseField.object_snapshot_id = o.id AND warehouseField.field_name = 'СтруктурнаяЕдиница'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values reserveWarehouseField ON reserveWarehouseField.object_snapshot_id = o.id AND reserveWarehouseField.field_name = 'СтруктурнаяЕдиницаРезерв'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine("WHERE o.object_name = 'ЗаказПоставщику'");
        builder.AppendLine($"  AND {supplierRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_purchase_order_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    ROW_NUMBER() OVER (PARTITION BY src.reference_code ORDER BY CASE WHEN src.section_name = 'Запасы' THEN 1 ELSE 2 END, COALESCE(src.line_no, src.row_number + 1), src.row_number) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.price,");
        builder.AppendLine("    src.discount_percent,");
        builder.AppendLine("    src.discount_amount,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.vat_rate_code,");
        builder.AppendLine("    src.tax_amount,");
        builder.AppendLine("    COALESCE(src.total, src.amount) AS total,");
        builder.AppendLine("    src.content_text");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.section_name,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Цена' THEN {DecimalExpression("t.raw_value")} END) AS price,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ПроцентСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_percent,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Сумма' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СтавкаНДС' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS vat_rate_code,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаНДС' THEN {DecimalExpression("t.raw_value")} END) AS tax_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Всего' THEN {DecimalExpression("t.raw_value")} END) AS total,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Содержание' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS content_text");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'ЗаказПоставщику' AND t.section_name IN ('Запасы', 'Материалы')");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.section_name, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO purchase_order_lines (");
        builder.AppendLine("    id, purchase_order_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    price, discount_percent, discount_amount, amount, vat_rate_code, tax_amount, total, content_text)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('purchase-order-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('purchase-order|', s.reference_code)")} AS purchase_order_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine("    COALESCE(s.price, 0) AS price,");
        builder.AppendLine("    COALESCE(s.discount_percent, 0) AS discount_percent,");
        builder.AppendLine("    COALESCE(s.discount_amount, 0) AS discount_amount,");
        builder.AppendLine("    COALESCE(s.amount, 0) AS amount,");
        builder.AppendLine("    s.vat_rate_code,");
        builder.AppendLine("    COALESCE(s.tax_amount, 0) AS tax_amount,");
        builder.AppendLine("    COALESCE(s.total, COALESCE(s.amount, 0)) AS total,");
        builder.AppendLine("    s.content_text");
        builder.AppendLine("FROM tmp_purchase_order_line_source s;");
        builder.AppendLine();
    }

    private void AppendPurchaseReceiptProjection(StringBuilder builder)
    {
        var receiptRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var supplierRef = NormalizeReferenceExpression("supplierField.raw_value");
        var contractRef = NormalizeReferenceExpression("contractField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var priceTypeRef = NormalizeReferenceExpression("priceTypeField.raw_value");
        var warehouseRef = NormalizeReferenceExpression("warehouseField.raw_value");
        var purchaseOrderRef = NormalizeReferenceExpression("purchaseOrderField.raw_value");
        var postingState = PostingStateExpression("postedField.raw_value", "deleteField.raw_value");
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));
        var totalAmount = DecimalExpression("sumField.raw_value");
        var documentNumber = $"COALESCE({NullIfBlankExpression("o.number_value")}, {BestTextExpression("numberField.display_value", "numberField.raw_value")}, CONCAT('PR-', {ShortHashExpression($"CONCAT('purchase-receipt|', {receiptRef})", 10)}))";
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");

        builder.AppendLine("INSERT IGNORE INTO purchase_receipts (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, currency_code, supplier_id, contract_id, purchase_order_id,");
        builder.AppendLine("    warehouse_node_id, storage_bin_id, partner_price_type_id, total_amount)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('purchase-receipt|', {receiptRef})")} AS id,");
        builder.AppendLine($"    {documentNumber} AS number,");
        builder.AppendLine("    COALESCE(o.record_date, CURRENT_TIMESTAMP(6)) AS document_date,");
        builder.AppendLine($"    {postingState} AS posting_state,");
        builder.AppendLine($"    COALESCE(CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine($"    CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END AS author_id,");
        builder.AppendLine($"    CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END AS responsible_employee_id,");
        builder.AppendLine($"    {BestTextExpression("commentField.display_value", "commentField.raw_value")} AS comment_text,");
        builder.AppendLine($"    CASE WHEN {NormalizeReferenceExpression("baseDocumentField.raw_value")} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('base-document|', {NormalizeReferenceExpression("baseDocumentField.raw_value")})")} END AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine($"    {currencyCode} AS currency_code,");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('bp|', {supplierRef})")} AS supplier_id,");
        builder.AppendLine($"    CASE WHEN {contractRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('contract|', {contractRef})")} END AS contract_id,");
        builder.AppendLine($"    CASE WHEN {purchaseOrderRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('purchase-order|', {purchaseOrderRef})")} END AS purchase_order_id,");
        builder.AppendLine($"    CASE WHEN {warehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {warehouseRef})")} END AS warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine($"    CASE WHEN {priceTypeRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('price-type|', {priceTypeRef})")} END AS partner_price_type_id,");
        builder.AppendLine($"    COALESCE({totalAmount}, 0) AS total_amount");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values supplierField ON supplierField.object_snapshot_id = o.id AND supplierField.field_name = 'Контрагент'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values contractField ON contractField.object_snapshot_id = o.id AND contractField.field_name = 'Договор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values baseDocumentField ON baseDocumentField.object_snapshot_id = o.id AND baseDocumentField.field_name = 'ДокументОснование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values priceTypeField ON priceTypeField.object_snapshot_id = o.id AND priceTypeField.field_name = 'ВидЦенКонтрагента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values warehouseField ON warehouseField.object_snapshot_id = o.id AND warehouseField.field_name = 'СтруктурнаяЕдиница'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values purchaseOrderField ON purchaseOrderField.object_snapshot_id = o.id AND purchaseOrderField.field_name = 'Заказ'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values sumField ON sumField.object_snapshot_id = o.id AND sumField.field_name = 'СуммаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine("WHERE o.object_name = 'ПриходнаяНакладная'");
        builder.AppendLine($"  AND {supplierRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_purchase_receipt_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.price,");
        builder.AppendLine("    src.discount_percent,");
        builder.AppendLine("    src.discount_amount,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.vat_rate_code,");
        builder.AppendLine("    src.tax_amount,");
        builder.AppendLine("    COALESCE(src.total, src.amount) AS total,");
        builder.AppendLine("    src.content_text");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Цена' THEN {DecimalExpression("t.raw_value")} END) AS price,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ПроцентСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_percent,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Сумма' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СтавкаНДС' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS vat_rate_code,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаНДС' THEN {DecimalExpression("t.raw_value")} END) AS tax_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Всего' THEN {DecimalExpression("t.raw_value")} END) AS total,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Содержание' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS content_text");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'ПриходнаяНакладная' AND t.section_name = 'Запасы'");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO purchase_receipt_lines (");
        builder.AppendLine("    id, purchase_receipt_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    price, discount_percent, discount_amount, amount, vat_rate_code, tax_amount, total, content_text)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('purchase-receipt-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('purchase-receipt|', s.reference_code)")} AS purchase_receipt_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine("    COALESCE(s.price, 0) AS price,");
        builder.AppendLine("    COALESCE(s.discount_percent, 0) AS discount_percent,");
        builder.AppendLine("    COALESCE(s.discount_amount, 0) AS discount_amount,");
        builder.AppendLine("    COALESCE(s.amount, 0) AS amount,");
        builder.AppendLine("    s.vat_rate_code,");
        builder.AppendLine("    COALESCE(s.tax_amount, 0) AS tax_amount,");
        builder.AppendLine("    COALESCE(s.total, COALESCE(s.amount, 0)) AS total,");
        builder.AppendLine("    s.content_text");
        builder.AppendLine("FROM tmp_purchase_receipt_line_source s;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_purchase_receipt_charge_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.content_text");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Сумма' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Содержание' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS content_text");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'ПриходнаяНакладная' AND t.section_name = 'Расходы'");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE COALESCE(src.amount, 0) <> 0;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO purchase_receipt_additional_charges (id, purchase_receipt_id, line_no, charge_name, amount, allocation_rule)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('purchase-receipt-charge|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('purchase-receipt|', s.reference_code)")} AS purchase_receipt_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    COALESCE(s.content_text, CASE WHEN s.item_ref IS NULL THEN NULL ELSE (SELECT ni.name FROM nomenclature_items ni WHERE ni.id = {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")}) END, CONCAT('Доп. расход ', s.line_no)) AS charge_name,");
        builder.AppendLine("    COALESCE(s.amount, 0) AS amount,");
        builder.AppendLine("    NULL AS allocation_rule");
        builder.AppendLine("FROM tmp_purchase_receipt_charge_source s;");
        builder.AppendLine();
    }

    private void AppendTransferOrderProjection(StringBuilder builder)
    {
        var transferRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var baseDocumentRef = NormalizeReferenceExpression("baseDocumentField.raw_value");
        var customerOrderRef = NormalizeReferenceExpression("customerOrderField.raw_value");
        var sourceWarehouseRef = NormalizeReferenceExpression("sourceWarehouseField.raw_value");
        var targetWarehouseRef = NormalizeReferenceExpression("targetWarehouseField.raw_value");
        var requestedTransferDate = DateExpression("transferDateField.raw_value");
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");
        const string fallbackWarehouseId = "(SELECT id FROM warehouse_nodes ORDER BY name LIMIT 1)";
        var commentText = $"COALESCE({BestTextExpression("commentField.display_value", "commentField.raw_value")}, {BestTextExpression("notesField.display_value", "notesField.raw_value")})";

        builder.AppendLine("INSERT IGNORE INTO transfer_orders (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, customer_order_id, source_warehouse_node_id, target_warehouse_node_id,");
        builder.AppendLine("    requested_transfer_date, lifecycle_status)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('transfer-order|', {transferRef})")} AS id,");
        builder.AppendLine($"    COALESCE({BestTextExpression("numberField.display_value", "numberField.raw_value")}, o.number_value, CONCAT('TO-', {ShortHashExpression($"CONCAT('transfer-order|', {transferRef})", 8)})) AS number,");
        builder.AppendLine("    o.record_date AS document_date,");
        builder.AppendLine($"    {PostingStateExpression("postedField.raw_value", "deleteField.raw_value")} AS posting_state,");
        builder.AppendLine($"    COALESCE(organization.id, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine("    author.id AS author_id,");
        builder.AppendLine("    COALESCE(responsible.id, author.id) AS responsible_employee_id,");
        builder.AppendLine($"    {commentText} AS comment_text,");
        builder.AppendLine($"    CASE WHEN {baseDocumentRef} IS NOT NULL THEN {DeterministicGuidExpression($"CONCAT('base-document|', {baseDocumentRef})")} WHEN {customerOrderRef} IS NOT NULL THEN {DeterministicGuidExpression($"CONCAT('sales-order|', {customerOrderRef})")} ELSE NULL END AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine("    salesOrder.id AS customer_order_id,");
        builder.AppendLine($"    COALESCE(sourceWarehouse.id, {fallbackWarehouseId}) AS source_warehouse_node_id,");
        builder.AppendLine($"    COALESCE(targetWarehouse.id, {fallbackWarehouseId}) AS target_warehouse_node_id,");
        builder.AppendLine($"    COALESCE({requestedTransferDate}, DATE(o.record_date)) AS requested_transfer_date,");
        builder.AppendLine($"    {LifecycleStatusExpression("postedField.raw_value", "deleteField.raw_value")} AS lifecycle_status");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values notesField ON notesField.object_snapshot_id = o.id AND notesField.field_name = 'Заметки'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values baseDocumentField ON baseDocumentField.object_snapshot_id = o.id AND baseDocumentField.field_name = 'ДокументОснование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values customerOrderField ON customerOrderField.object_snapshot_id = o.id AND customerOrderField.field_name = 'ЗаказПокупателя'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values sourceWarehouseField ON sourceWarehouseField.object_snapshot_id = o.id AND sourceWarehouseField.field_name = 'СтруктурнаяЕдиницаРезерв'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values targetWarehouseField ON targetWarehouseField.object_snapshot_id = o.id AND targetWarehouseField.field_name = 'СтруктурнаяЕдиницаПолучатель'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values transferDateField ON transferDateField.object_snapshot_id = o.id AND transferDateField.field_name = 'ДатаПеремещения'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine($"LEFT JOIN organizations organization ON organization.id = CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END");
        builder.AppendLine($"LEFT JOIN employees author ON author.id = CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END");
        builder.AppendLine($"LEFT JOIN employees responsible ON responsible.id = CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END");
        builder.AppendLine($"LEFT JOIN warehouse_nodes sourceWarehouse ON sourceWarehouse.id = CASE WHEN {sourceWarehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {sourceWarehouseRef})")} END");
        builder.AppendLine($"LEFT JOIN warehouse_nodes targetWarehouse ON targetWarehouse.id = CASE WHEN {targetWarehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {targetWarehouseRef})")} END");
        builder.AppendLine($"LEFT JOIN sales_orders salesOrder ON salesOrder.id = CASE WHEN {customerOrderRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('sales-order|', {customerOrderRef})")} END");
        builder.AppendLine("WHERE o.object_name = 'ЗаказНаПеремещение'");
        builder.AppendLine($"  AND {transferRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_transfer_order_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.reserved_quantity,");
        builder.AppendLine("    src.collected_quantity");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Резерв' THEN {DecimalExpression("t.raw_value")} END) AS reserved_quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'КоличествоСобрано' THEN {DecimalExpression("t.raw_value")} END) AS collected_quantity");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'ЗаказНаПеремещение' AND t.section_name = 'Запасы'");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO transfer_order_lines (");
        builder.AppendLine("    id, transfer_order_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    source_warehouse_node_id, source_storage_bin_id, target_warehouse_node_id, target_storage_bin_id,");
        builder.AppendLine("    reserved_quantity, collected_quantity)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('transfer-order-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine("    o.id AS transfer_order_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine("    o.source_warehouse_node_id AS source_warehouse_node_id,");
        builder.AppendLine("    NULL AS source_storage_bin_id,");
        builder.AppendLine("    o.target_warehouse_node_id AS target_warehouse_node_id,");
        builder.AppendLine("    NULL AS target_storage_bin_id,");
        builder.AppendLine("    COALESCE(s.reserved_quantity, 0) AS reserved_quantity,");
        builder.AppendLine("    COALESCE(s.collected_quantity, 0) AS collected_quantity");
        builder.AppendLine("FROM tmp_transfer_order_line_source s");
        builder.AppendLine($"INNER JOIN transfer_orders o ON o.id = {DeterministicGuidExpression("CONCAT('transfer-order|', s.reference_code)")};");
        builder.AppendLine();
    }

    private void AppendStockReservationProjection(StringBuilder builder)
    {
        var reservationRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var salesOrderRef = NormalizeReferenceExpression("salesOrderField.raw_value");
        var sourcePlaceRef = NormalizeReferenceExpression("sourcePlaceField.raw_value");
        var targetPlaceRef = NormalizeReferenceExpression("targetPlaceField.raw_value");
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");

        builder.AppendLine("INSERT IGNORE INTO stock_reservations (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, sales_order_id, source_place, target_place)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('stock-reservation|', {reservationRef})")} AS id,");
        builder.AppendLine($"    COALESCE({BestTextExpression("numberField.display_value", "numberField.raw_value")}, o.number_value, CONCAT('RES-', {ShortHashExpression($"CONCAT('stock-reservation|', {reservationRef})", 8)})) AS number,");
        builder.AppendLine("    COALESCE(o.record_date, CURRENT_TIMESTAMP(6)) AS document_date,");
        builder.AppendLine($"    {PostingStateExpression("postedField.raw_value", "deleteField.raw_value")} AS posting_state,");
        builder.AppendLine($"    COALESCE(organization.id, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine("    author.id AS author_id,");
        builder.AppendLine("    COALESCE(responsible.id, author.id) AS responsible_employee_id,");
        builder.AppendLine($"    {BestTextExpression("commentField.display_value", "commentField.raw_value")} AS comment_text,");
        builder.AppendLine("    salesOrder.id AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine("    salesOrder.id AS sales_order_id,");
        builder.AppendLine("    1 AS source_place,");
        builder.AppendLine($"    CASE WHEN {targetPlaceRef} IS NULL OR {targetPlaceRef} = {sourcePlaceRef} THEN 1 ELSE 2 END AS target_place");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values salesOrderField ON salesOrderField.object_snapshot_id = o.id AND salesOrderField.field_name = 'ЗаказПокупателя'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values sourcePlaceField ON sourcePlaceField.object_snapshot_id = o.id AND sourcePlaceField.field_name = 'ИсходноеМестоРезерва'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values targetPlaceField ON targetPlaceField.object_snapshot_id = o.id AND targetPlaceField.field_name = 'НовоеМестоРезерва'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine($"LEFT JOIN organizations organization ON organization.id = CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END");
        builder.AppendLine($"LEFT JOIN employees author ON author.id = CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END");
        builder.AppendLine($"LEFT JOIN employees responsible ON responsible.id = CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END");
        builder.AppendLine($"LEFT JOIN sales_orders salesOrder ON salesOrder.id = CASE WHEN {salesOrderRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('sales-order|', {salesOrderRef})")} END");
        builder.AppendLine("WHERE o.object_name = 'РезервированиеЗапасов'");
        builder.AppendLine($"  AND {reservationRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_stock_reservation_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.source_place_ref,");
        builder.AppendLine("    src.target_place_ref");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ИсходноеМестоРезерва' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS source_place_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НовоеМестоРезерва' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS target_place_ref");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'РезервированиеЗапасов' AND t.section_name = 'Запасы'");
        builder.AppendLine("    GROUP BY t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO stock_reservation_lines (");
        builder.AppendLine("    id, stock_reservation_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    source_warehouse_node_id, source_storage_bin_id, target_warehouse_node_id, target_storage_bin_id,");
        builder.AppendLine("    reserved_quantity, collected_quantity)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('stock-reservation-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine("    r.id AS stock_reservation_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine($"    CASE WHEN COALESCE(s.source_place_ref, {sourcePlaceRef}) IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', COALESCE(s.source_place_ref, {sourcePlaceRef}))")} END AS source_warehouse_node_id,");
        builder.AppendLine("    NULL AS source_storage_bin_id,");
        builder.AppendLine($"    CASE WHEN COALESCE(s.target_place_ref, {targetPlaceRef}) IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', COALESCE(s.target_place_ref, {targetPlaceRef}))")} END AS target_warehouse_node_id,");
        builder.AppendLine("    NULL AS target_storage_bin_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS reserved_quantity,");
        builder.AppendLine("    0 AS collected_quantity");
        builder.AppendLine("FROM tmp_stock_reservation_line_source s");
        builder.AppendLine($"INNER JOIN stock_reservations r ON r.id = {DeterministicGuidExpression("CONCAT('stock-reservation|', s.reference_code)")}");
        builder.AppendLine("LEFT JOIN onec_projection_latest_objects o ON o.reference_code = s.reference_code AND o.object_name = 'РезервированиеЗапасов'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values sourcePlaceField ON sourcePlaceField.object_snapshot_id = o.id AND sourcePlaceField.field_name = 'ИсходноеМестоРезерва'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values targetPlaceField ON targetPlaceField.object_snapshot_id = o.id AND targetPlaceField.field_name = 'НовоеМестоРезерва';");
        builder.AppendLine();
    }

    private void AppendInventoryCountProjection(StringBuilder builder)
    {
        var inventoryRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var warehouseRef = NormalizeReferenceExpression("warehouseField.raw_value");
        var finishedOn = DateExpression("finishedOnField.raw_value");
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");
        const string fallbackWarehouseId = "(SELECT id FROM warehouse_nodes ORDER BY name LIMIT 1)";

        builder.AppendLine("INSERT IGNORE INTO inventory_counts (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, warehouse_node_id, storage_bin_id, finished_on)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('inventory-count|', {inventoryRef})")} AS id,");
        builder.AppendLine($"    COALESCE({BestTextExpression("numberField.display_value", "numberField.raw_value")}, o.number_value, CONCAT('INV-', {ShortHashExpression($"CONCAT('inventory-count|', {inventoryRef})", 8)})) AS number,");
        builder.AppendLine("    o.record_date AS document_date,");
        builder.AppendLine($"    {PostingStateExpression("postedField.raw_value", "deleteField.raw_value")} AS posting_state,");
        builder.AppendLine($"    COALESCE(organization.id, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine("    author.id AS author_id,");
        builder.AppendLine("    COALESCE(responsible.id, author.id) AS responsible_employee_id,");
        builder.AppendLine($"    {BestTextExpression("commentField.display_value", "commentField.raw_value")} AS comment_text,");
        builder.AppendLine("    NULL AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine($"    COALESCE(warehouse.id, {fallbackWarehouseId}) AS warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine($"    CASE WHEN {finishedOn} IS NULL OR {finishedOn} < '1901-01-01' THEN NULL ELSE {finishedOn} END AS finished_on");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values warehouseField ON warehouseField.object_snapshot_id = o.id AND warehouseField.field_name = 'СтруктурнаяЕдиница'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values finishedOnField ON finishedOnField.object_snapshot_id = o.id AND finishedOnField.field_name = 'ДатаОкончания'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine($"LEFT JOIN organizations organization ON organization.id = CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END");
        builder.AppendLine($"LEFT JOIN employees author ON author.id = CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END");
        builder.AppendLine($"LEFT JOIN employees responsible ON responsible.id = CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END");
        builder.AppendLine($"LEFT JOIN warehouse_nodes warehouse ON warehouse.id = CASE WHEN {warehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {warehouseRef})")} END");
        builder.AppendLine("WHERE o.object_name = 'ИнвентаризацияЗапасов'");
        builder.AppendLine($"  AND {inventoryRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_inventory_count_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.book_quantity,");
        builder.AppendLine("    src.actual_quantity,");
        builder.AppendLine("    COALESCE(src.difference_quantity, COALESCE(src.actual_quantity, 0) - COALESCE(src.book_quantity, 0)) AS difference_quantity");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'КоличествоУчет' THEN {DecimalExpression("t.raw_value")} END) AS book_quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS actual_quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Отклонение' THEN {DecimalExpression("t.raw_value")} END) AS difference_quantity");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'ИнвентаризацияЗапасов' AND t.section_name = 'Запасы'");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO inventory_count_lines (");
        builder.AppendLine("    id, inventory_count_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id,");
        builder.AppendLine("    book_quantity, actual_quantity, difference_quantity)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('inventory-count-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine("    c.id AS inventory_count_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.book_quantity, 0) AS book_quantity,");
        builder.AppendLine("    COALESCE(s.actual_quantity, 0) AS actual_quantity,");
        builder.AppendLine("    COALESCE(s.difference_quantity, 0) AS difference_quantity");
        builder.AppendLine("FROM tmp_inventory_count_line_source s");
        builder.AppendLine($"INNER JOIN inventory_counts c ON c.id = {DeterministicGuidExpression("CONCAT('inventory-count|', s.reference_code)")};");
        builder.AppendLine();
    }

    private void AppendStockWriteOffProjection(StringBuilder builder)
    {
        var writeOffRef = NormalizeReferenceExpression("o.reference_code");
        var organizationRef = NormalizeReferenceExpression("organizationField.raw_value");
        var authorRef = NormalizeReferenceExpression("authorField.raw_value");
        var responsibleRef = NormalizeReferenceExpression("responsibleField.raw_value");
        var warehouseRef = NormalizeReferenceExpression("warehouseField.raw_value");
        var priceTypeRef = NormalizeReferenceExpression("priceTypeField.raw_value");
        var baseDocumentRef = NormalizeReferenceExpression("baseDocumentField.raw_value");
        var receiptDocumentRef = NormalizeReferenceExpression("receiptDocumentField.raw_value");
        var defaultOrganizationId = DeterministicGuidExpression("'system|organization|default'");
        const string fallbackWarehouseId = "(SELECT id FROM warehouse_nodes ORDER BY name LIMIT 1)";
        var currencyCode = CurrencyCodeExpression(BestTextExpression("currencyField.display_value", "currencyField.raw_value"));
        var reasonText = $"COALESCE({BestTextExpression("reasonField.display_value", "reasonField.raw_value")}, {BestTextExpression("commentField.display_value", "commentField.raw_value")})";

        builder.AppendLine("INSERT IGNORE INTO stock_write_offs (");
        builder.AppendLine("    id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id,");
        builder.AppendLine("    comment_text, base_document_id, project_id, currency_code, warehouse_node_id, storage_bin_id, inventory_count_id,");
        builder.AppendLine("    price_type_id, reason_text)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression($"CONCAT('stock-writeoff|', {writeOffRef})")} AS id,");
        builder.AppendLine($"    COALESCE({BestTextExpression("numberField.display_value", "numberField.raw_value")}, o.number_value, CONCAT('WO-', {ShortHashExpression($"CONCAT('stock-writeoff|', {writeOffRef})", 8)})) AS number,");
        builder.AppendLine("    o.record_date AS document_date,");
        builder.AppendLine($"    {PostingStateExpression("postedField.raw_value", "deleteField.raw_value")} AS posting_state,");
        builder.AppendLine($"    COALESCE(organization.id, {defaultOrganizationId}) AS organization_id,");
        builder.AppendLine("    author.id AS author_id,");
        builder.AppendLine("    COALESCE(responsible.id, author.id) AS responsible_employee_id,");
        builder.AppendLine($"    {BestTextExpression("commentField.display_value", "commentField.raw_value")} AS comment_text,");
        builder.AppendLine($"    CASE WHEN {baseDocumentRef} IS NOT NULL THEN {DeterministicGuidExpression($"CONCAT('base-document|', {baseDocumentRef})")} WHEN {receiptDocumentRef} IS NOT NULL THEN {DeterministicGuidExpression($"CONCAT('base-document|', {receiptDocumentRef})")} ELSE NULL END AS base_document_id,");
        builder.AppendLine("    NULL AS project_id,");
        builder.AppendLine($"    {currencyCode} AS currency_code,");
        builder.AppendLine($"    COALESCE(warehouse.id, {fallbackWarehouseId}) AS warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine("    inventoryCount.id AS inventory_count_id,");
        builder.AppendLine("    priceType.id AS price_type_id,");
        builder.AppendLine($"    {reasonText} AS reason_text");
        builder.AppendLine("FROM onec_projection_latest_objects o");
        builder.AppendLine("LEFT JOIN onec_projection_field_values numberField ON numberField.object_snapshot_id = o.id AND numberField.field_name = 'Номер'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values organizationField ON organizationField.object_snapshot_id = o.id AND organizationField.field_name = 'Организация'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values authorField ON authorField.object_snapshot_id = o.id AND authorField.field_name = 'Автор'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values responsibleField ON responsibleField.object_snapshot_id = o.id AND responsibleField.field_name = 'Ответственный'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values commentField ON commentField.object_snapshot_id = o.id AND commentField.field_name = 'Комментарий'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values reasonField ON reasonField.object_snapshot_id = o.id AND reasonField.field_name = 'ПричинаСписания'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values warehouseField ON warehouseField.object_snapshot_id = o.id AND warehouseField.field_name = 'СтруктурнаяЕдиница'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values priceTypeField ON priceTypeField.object_snapshot_id = o.id AND priceTypeField.field_name = 'ВидЦен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values currencyField ON currencyField.object_snapshot_id = o.id AND currencyField.field_name = 'ВалютаДокумента'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values baseDocumentField ON baseDocumentField.object_snapshot_id = o.id AND baseDocumentField.field_name = 'ДокументОснование'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values receiptDocumentField ON receiptDocumentField.object_snapshot_id = o.id AND receiptDocumentField.field_name = 'ДокументПоступления'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values postedField ON postedField.object_snapshot_id = o.id AND postedField.field_name = 'Проведен'");
        builder.AppendLine("LEFT JOIN onec_projection_field_values deleteField ON deleteField.object_snapshot_id = o.id AND deleteField.field_name = 'ПометкаУдаления'");
        builder.AppendLine($"LEFT JOIN organizations organization ON organization.id = CASE WHEN {organizationRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('org|', {organizationRef})")} END");
        builder.AppendLine($"LEFT JOIN employees author ON author.id = CASE WHEN {authorRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {authorRef})")} END");
        builder.AppendLine($"LEFT JOIN employees responsible ON responsible.id = CASE WHEN {responsibleRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('employee|', {responsibleRef})")} END");
        builder.AppendLine($"LEFT JOIN warehouse_nodes warehouse ON warehouse.id = CASE WHEN {warehouseRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('warehouse|', {warehouseRef})")} END");
        builder.AppendLine($"LEFT JOIN inventory_counts inventoryCount ON inventoryCount.id = CASE WHEN {baseDocumentRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('inventory-count|', {baseDocumentRef})")} END");
        builder.AppendLine($"LEFT JOIN price_types priceType ON priceType.id = CASE WHEN {priceTypeRef} IS NULL THEN NULL ELSE {DeterministicGuidExpression($"CONCAT('price-type|', {priceTypeRef})")} END");
        builder.AppendLine("WHERE o.object_name = 'СписаниеЗапасов'");
        builder.AppendLine($"  AND {writeOffRef} IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("CREATE TEMPORARY TABLE tmp_stock_write_off_line_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    src.object_snapshot_id,");
        builder.AppendLine("    src.reference_code,");
        builder.AppendLine("    COALESCE(src.line_no, src.row_number + 1) AS line_no,");
        builder.AppendLine("    src.item_ref,");
        builder.AppendLine("    src.unit_ref,");
        builder.AppendLine("    src.quantity,");
        builder.AppendLine("    src.price,");
        builder.AppendLine("    src.discount_percent,");
        builder.AppendLine("    src.discount_amount,");
        builder.AppendLine("    src.amount,");
        builder.AppendLine("    src.vat_rate_code,");
        builder.AppendLine("    src.tax_amount,");
        builder.AppendLine("    COALESCE(src.total, src.amount) AS total,");
        builder.AppendLine("    src.content_text");
        builder.AppendLine("FROM (");
        builder.AppendLine("    SELECT");
        builder.AppendLine("        t.object_snapshot_id,");
        builder.AppendLine("        t.reference_code,");
        builder.AppendLine("        t.row_number,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'НомерСтроки' THEN {UnsignedIntegerExpression("t.raw_value")} END) AS line_no,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Номенклатура' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS item_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ЕдиницаИзмерения' THEN {NormalizeReferenceExpression("t.raw_value")} END) AS unit_ref,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Количество' THEN {DecimalExpression("t.raw_value")} END) AS quantity,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Цена' THEN {DecimalExpression("t.raw_value")} END) AS price,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'ПроцентСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_percent,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаСкидкиНаценки' THEN {DecimalExpression("t.raw_value")} END) AS discount_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Сумма' THEN {DecimalExpression("t.raw_value")} END) AS amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СтавкаНДС' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS vat_rate_code,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'СуммаНДС' THEN {DecimalExpression("t.raw_value")} END) AS tax_amount,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Всего' THEN {DecimalExpression("t.raw_value")} END) AS total,");
        builder.AppendLine($"        MAX(CASE WHEN t.field_name = 'Содержание' THEN {BestTextExpression("t.display_value", "t.raw_value")} END) AS content_text");
        builder.AppendLine("    FROM onec_projection_tabular_field_values t");
        builder.AppendLine("    WHERE t.object_name = 'СписаниеЗапасов' AND t.section_name = 'Запасы'");
        builder.AppendLine("    GROUP BY t.object_snapshot_id, t.reference_code, t.row_number");
        builder.AppendLine(") src");
        builder.AppendLine("WHERE src.item_ref IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO stock_write_off_lines (");
        builder.AppendLine("    id, stock_write_off_id, line_no, item_id, characteristic_id, batch_id, unit_of_measure_id, quantity,");
        builder.AppendLine("    price, discount_percent, discount_amount, amount, vat_rate_code, tax_amount, total, content_text)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('stock-writeoff-line|', s.reference_code, '|', s.line_no)")} AS id,");
        builder.AppendLine("    w.id AS stock_write_off_id,");
        builder.AppendLine("    s.line_no,");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('item|', s.item_ref)")} AS item_id,");
        builder.AppendLine("    NULL AS characteristic_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine($"    CASE WHEN s.unit_ref IS NULL THEN NULL ELSE {DeterministicGuidExpression("CONCAT('uom|', s.unit_ref)")} END AS unit_of_measure_id,");
        builder.AppendLine("    COALESCE(s.quantity, 0) AS quantity,");
        builder.AppendLine("    COALESCE(s.price, 0) AS price,");
        builder.AppendLine("    COALESCE(s.discount_percent, 0) AS discount_percent,");
        builder.AppendLine("    COALESCE(s.discount_amount, 0) AS discount_amount,");
        builder.AppendLine("    COALESCE(s.amount, 0) AS amount,");
        builder.AppendLine("    s.vat_rate_code,");
        builder.AppendLine("    COALESCE(s.tax_amount, 0) AS tax_amount,");
        builder.AppendLine("    COALESCE(s.total, COALESCE(s.amount, 0)) AS total,");
        builder.AppendLine("    s.content_text");
        builder.AppendLine("FROM tmp_stock_write_off_line_source s");
        builder.AppendLine($"INNER JOIN stock_write_offs w ON w.id = {DeterministicGuidExpression("CONCAT('stock-writeoff|', s.reference_code)")};");
        builder.AppendLine();
    }

    private void AppendStockBalanceProjection(StringBuilder builder)
    {
        builder.AppendLine("CREATE TEMPORARY TABLE tmp_stock_balance_source AS");
        builder.AppendLine("SELECT");
        builder.AppendLine("    prl.item_id,");
        builder.AppendLine("    pr.warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine("    prl.quantity AS quantity_delta,");
        builder.AppendLine("    0 AS reserved_delta,");
        builder.AppendLine("    pr.document_date AS movement_at_utc");
        builder.AppendLine("FROM purchase_receipt_lines prl");
        builder.AppendLine("INNER JOIN purchase_receipts pr ON pr.id = prl.purchase_receipt_id");
        builder.AppendLine("WHERE prl.item_id IS NOT NULL AND pr.warehouse_node_id IS NOT NULL");
        builder.AppendLine();
        builder.AppendLine("UNION ALL");
        builder.AppendLine();
        builder.AppendLine("SELECT");
        builder.AppendLine("    swl.item_id,");
        builder.AppendLine("    sw.warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine("    -swl.quantity AS quantity_delta,");
        builder.AppendLine("    0 AS reserved_delta,");
        builder.AppendLine("    sw.document_date AS movement_at_utc");
        builder.AppendLine("FROM stock_write_off_lines swl");
        builder.AppendLine("INNER JOIN stock_write_offs sw ON sw.id = swl.stock_write_off_id");
        builder.AppendLine("WHERE swl.item_id IS NOT NULL AND sw.warehouse_node_id IS NOT NULL");
        builder.AppendLine();
        builder.AppendLine("UNION ALL");
        builder.AppendLine();
        builder.AppendLine("SELECT");
        builder.AppendLine("    shipLine.item_id,");
        builder.AppendLine("    ship.warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine("    -shipLine.quantity AS quantity_delta,");
        builder.AppendLine("    0 AS reserved_delta,");
        builder.AppendLine("    ship.document_date AS movement_at_utc");
        builder.AppendLine("FROM sales_shipment_lines shipLine");
        builder.AppendLine("INNER JOIN sales_shipments ship ON ship.id = shipLine.sales_shipment_id");
        builder.AppendLine("WHERE shipLine.item_id IS NOT NULL AND ship.warehouse_node_id IS NOT NULL");
        builder.AppendLine();
        builder.AppendLine("UNION ALL");
        builder.AppendLine();
        builder.AppendLine("SELECT");
        builder.AppendLine("    srl.item_id,");
        builder.AppendLine("    COALESCE(srl.source_warehouse_node_id, srl.target_warehouse_node_id) AS warehouse_node_id,");
        builder.AppendLine("    NULL AS storage_bin_id,");
        builder.AppendLine("    NULL AS batch_id,");
        builder.AppendLine("    0 AS quantity_delta,");
        builder.AppendLine("    COALESCE(NULLIF(srl.reserved_quantity, 0), srl.quantity) AS reserved_delta,");
        builder.AppendLine("    sr.document_date AS movement_at_utc");
        builder.AppendLine("FROM stock_reservation_lines srl");
        builder.AppendLine("INNER JOIN stock_reservations sr ON sr.id = srl.stock_reservation_id");
        builder.AppendLine("WHERE srl.item_id IS NOT NULL AND COALESCE(srl.source_warehouse_node_id, srl.target_warehouse_node_id) IS NOT NULL;");
        builder.AppendLine();

        builder.AppendLine("INSERT IGNORE INTO stock_balances (id, item_id, warehouse_node_id, storage_bin_id, batch_id, quantity, reserved_quantity, last_movement_at_utc)");
        builder.AppendLine("SELECT");
        builder.AppendLine($"    {DeterministicGuidExpression("CONCAT('stock-balance|', item_id, '|', warehouse_node_id, '|', COALESCE(storage_bin_id, ''), '|', COALESCE(batch_id, ''))")} AS id,");
        builder.AppendLine("    item_id,");
        builder.AppendLine("    warehouse_node_id,");
        builder.AppendLine("    storage_bin_id,");
        builder.AppendLine("    batch_id,");
        builder.AppendLine("    SUM(quantity_delta) AS quantity,");
        builder.AppendLine("    SUM(reserved_delta) AS reserved_quantity,");
        builder.AppendLine("    MAX(movement_at_utc) AS last_movement_at_utc");
        builder.AppendLine("FROM tmp_stock_balance_source");
        builder.AppendLine("GROUP BY item_id, warehouse_node_id, storage_bin_id, batch_id");
        builder.AppendLine("HAVING SUM(quantity_delta) <> 0 OR SUM(reserved_delta) <> 0;");
        builder.AppendLine();
    }

    private void AppendProjectionResult(StringBuilder builder)
    {
        builder.AppendLine("SELECT CONCAT(");
        builder.AppendLine("    'PROJECT_RESULT|',");
        builder.AppendLine("    (SELECT COUNT(*) FROM organizations), '|',");
        builder.AppendLine("    (SELECT COUNT(*) FROM business_partners), '|',");
        builder.AppendLine("    (SELECT COUNT(*) FROM nomenclature_items), '|',");
        builder.AppendLine("    (SELECT COUNT(*) FROM sales_invoices), '|',");
        builder.AppendLine("    (SELECT COUNT(*) FROM purchase_orders), '|',");
        builder.AppendLine("    (SELECT COUNT(*) FROM purchase_receipts)");
        builder.AppendLine(") AS projection_result;");
    }

    private static (int OrganizationCount, int PartnerCount, int ItemCount, int SalesInvoiceCount, int PurchaseOrderCount, int PurchaseReceiptCount) ParseResultCounts(string? resultLine)
    {
        if (string.IsNullOrWhiteSpace(resultLine))
        {
            return (0, 0, 0, 0, 0, 0);
        }

        var parts = resultLine.Split('|');
        if (parts.Length < 7)
        {
            return (0, 0, 0, 0, 0, 0);
        }

        return (
            ParseCount(parts[1]),
            ParseCount(parts[2]),
            ParseCount(parts[3]),
            ParseCount(parts[4]),
            ParseCount(parts[5]),
            ParseCount(parts[6]));
    }

    private static int ParseCount(string rawValue)
    {
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private string ResolveMysqlExecutablePath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        var mysqlFromPath = Environment.GetEnvironmentVariable("PATH")
            ?.Split(Path.PathSeparator)
            .Select(path => Path.Combine(path.Trim(), "mysql.exe"))
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(mysqlFromPath))
        {
            return mysqlFromPath;
        }

        throw new FileNotFoundException("mysql.exe was not found. Pass --mysql-exe with the full path to the MySQL client.");
    }

    private string ExecuteSql(string mysqlExecutablePath, string scriptPath)
    {
        var mysqlArguments = new StringBuilder();
        mysqlArguments.Append($"-h {_options.Host} -P {_options.Port.ToString(CultureInfo.InvariantCulture)} -u {_options.User} ");
        if (!string.IsNullOrEmpty(_options.Password))
        {
            mysqlArguments.Append($"-p{_options.Password} ");
        }

        mysqlArguments.Append("--default-character-set=utf8mb4 ");
        mysqlArguments.Append($"--database={_options.DatabaseName}");
        var fullScriptPath = Path.GetFullPath(scriptPath);
        var commandLine = $"\"\"{mysqlExecutablePath}\" {mysqlArguments} < \"{fullScriptPath}\"\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {commandLine}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"MySQL projection failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }

        return string.Concat(output, Environment.NewLine, error);
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Target database name is required.");
        }

        if (databaseName.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
        {
            throw new InvalidOperationException("Target database name can contain only letters, digits and underscore.");
        }
    }

    private static string DeterministicGuidExpression(string seedSql)
    {
        var hash = $"SHA2(COALESCE({seedSql}, ''), 256)";
        return $"LOWER(CONCAT(SUBSTRING({hash}, 1, 8), '-', SUBSTRING({hash}, 9, 4), '-', SUBSTRING({hash}, 13, 4), '-', SUBSTRING({hash}, 17, 4), '-', SUBSTRING({hash}, 21, 12)))";
    }

    private static string ShortHashExpression(string seedSql, int length)
    {
        return $"UPPER(SUBSTRING(SHA2(COALESCE({seedSql}, ''), 256), 1, {length.ToString(CultureInfo.InvariantCulture)}))";
    }

    private static string NormalizeReferenceExpression(string sql)
    {
        return $"CASE WHEN {sql} IS NULL OR LOWER(TRIM({sql})) = 'null' OR TRIM({sql}) = '' OR TRIM({sql}) LIKE '%00000000000000000000000000000000%' THEN NULL ELSE TRIM({sql}) END";
    }

    private static string HumanTextExpression(string sql)
    {
        return $"CASE WHEN {sql} IS NULL OR LOWER(TRIM({sql})) = 'null' OR TRIM({sql}) = '' OR LEFT(TRIM({sql}), 3) = '{{\"#' THEN NULL ELSE TRIM({sql}) END";
    }

    private static string BestTextExpression(string displaySql, string rawSql)
    {
        return $"COALESCE({HumanTextExpression(displaySql)}, {HumanTextExpression(rawSql)})";
    }

    private static string NullIfBlankExpression(string sql)
    {
        return $"CASE WHEN {sql} IS NULL OR LOWER(TRIM({sql})) = 'null' OR TRIM({sql}) = '' THEN NULL ELSE TRIM({sql}) END";
    }

    private static string DecimalExpression(string sql)
    {
        var normalized = $"REPLACE(REPLACE(TRIM(COALESCE({sql}, '')), ' ', ''), ',', '.')";
        return $"CASE WHEN {normalized} REGEXP '^-?[0-9]+(\\\\.[0-9]+)?$' THEN CAST({normalized} AS DECIMAL(18,4)) ELSE NULL END";
    }

    private static string UnsignedIntegerExpression(string sql)
    {
        var normalized = $"REPLACE(REPLACE(TRIM(COALESCE({sql}, '')), ' ', ''), ',', '.')";
        return $"CASE WHEN {normalized} REGEXP '^[0-9]+$' THEN CAST({normalized} AS UNSIGNED) ELSE NULL END";
    }

    private static string BooleanExpression(string sql)
    {
        return $"CASE WHEN LOWER(TRIM(COALESCE({sql}, ''))) IN ('истина', 'true', '1', 'yes') THEN 1 ELSE 0 END";
    }

    private static string CurrencyCodeExpression(string sql)
    {
        return $"CASE WHEN {sql} IS NULL THEN 'RUB' WHEN LOWER({sql}) LIKE '%руб%' OR {sql} LIKE '643%' THEN 'RUB' WHEN UPPER({sql}) REGEXP '^[A-Z]{{3}}$' THEN UPPER({sql}) ELSE 'RUB' END";
    }

    private static string DateExpression(string sql)
    {
        var normalized = $"TRIM(COALESCE({sql}, ''))";
        return $"CASE WHEN {normalized} = '' OR LOWER({normalized}) = 'null' OR {normalized} IN ('01.01.100', '01.01.0001', '0001-01-01', '0001-01-01 00:00:00') THEN NULL WHEN {normalized} REGEXP '^[0-9]{{2}}\\\\.[0-9]{{2}}\\\\.[0-9]{{4}}$' THEN STR_TO_DATE({normalized}, '%d.%m.%Y') WHEN {normalized} REGEXP '^[0-9]{{2}}\\\\.[0-9]{{2}}\\\\.[0-9]{{4}} [0-9]{{2}}:[0-9]{{2}}:[0-9]{{2}}$' THEN DATE(STR_TO_DATE({normalized}, '%d.%m.%Y %H:%i:%s')) WHEN {normalized} REGEXP '^[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}$' THEN CAST({normalized} AS DATE) ELSE NULL END";
    }

    private static string PostingStateExpression(string postedSql, string deletedSql)
    {
        var deleted = BooleanExpression(deletedSql);
        var posted = BooleanExpression(postedSql);
        return $"CASE WHEN {deleted} = 1 THEN 3 WHEN {posted} = 1 THEN 2 ELSE 1 END";
    }

    private static string LifecycleStatusExpression(string postedSql, string deletedSql)
    {
        var deleted = BooleanExpression(deletedSql);
        var posted = BooleanExpression(postedSql);
        return $"CASE WHEN {deleted} = 1 THEN 7 WHEN {posted} = 1 THEN 3 ELSE 1 END";
    }
}


