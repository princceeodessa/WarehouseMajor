using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class OperationalMySqlDesktopService
{
    private const int MysqlConnectTimeoutSeconds = 2;
    private const int ConnectionBackoffSeconds = 45;

    private static readonly object DefaultServiceSync = new();
    private static OperationalMySqlDesktopService? s_defaultService;
    private static readonly object ConnectionStateSync = new();
    private static DateTime s_connectionBackoffUntilUtc;
    private static readonly object ErrorLogSync = new();
    private static DateTime s_lastErrorLogUtc;
    private static string s_lastErrorSignature = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OperationalMySqlDesktopOptions _options;

    public OperationalMySqlDesktopService(OperationalMySqlDesktopOptions options)
    {
        _options = options;
    }

    public static OperationalMySqlDesktopService CreateDefault()
    {
        lock (DefaultServiceSync)
        {
            if (s_defaultService is not null)
            {
                return s_defaultService;
            }

            s_defaultService = new OperationalMySqlDesktopService(new OperationalMySqlDesktopOptions
            {
                Host = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_HOST") ?? "127.0.0.1",
                Port = int.TryParse(Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_PORT"), out var port) ? port : 3306,
                DatabaseName = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_DATABASE") ?? "warehouse_automation_raw_dev",
                User = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_USER") ?? "root",
                Password = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_PASSWORD") ?? string.Empty
            });

            return s_defaultService;
        }
    }

    public static OperationalMySqlDesktopService? TryCreateConfigured()
    {
        lock (DefaultServiceSync)
        {
            if (s_defaultService is not null)
            {
                return s_defaultService;
            }
        }

        var configuredOptions = DesktopRemoteDatabaseSettings.TryBuildOptions();
        if (configuredOptions is null)
        {
            return null;
        }

        lock (DefaultServiceSync)
        {
            if (s_defaultService is not null)
            {
                return s_defaultService;
            }

            s_defaultService = new OperationalMySqlDesktopService(configuredOptions);

            return s_defaultService;
        }
    }

    public void EnsureOperationalSchemaAccessible()
    {
        ValidateDatabaseName(_options.DatabaseName);
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = DATABASE()
              AND table_name IN (
                  'business_partners',
                  'nomenclature_items',
                  'warehouse_nodes',
                  'sales_orders',
                  'purchase_orders'
              );
            """;

        var output = ExecuteSqlScalar(sql).Trim();
        if (!string.Equals(output, "5", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Операционная схема MySQL не инициализирована или заполнена не полностью.");
        }
    }

    public DesktopOperationalSnapshot? TryLoadSnapshot()
    {
        try
        {
            return LoadSnapshot();
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public DesktopOperationalSnapshot LoadSnapshot()
    {
        ValidateDatabaseName(_options.DatabaseName);

        var customers = LoadCustomers();
        var catalogItems = LoadCatalogItems();
        var managers = LoadManagers();
        var warehouses = LoadWarehouses();
        var currencies = customers
            .Select(item => item.CurrencyCode)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var salesOrders = LoadSalesOrders();
        var salesInvoices = LoadSalesInvoices();
        var salesShipments = LoadSalesShipments();
        var purchaseOrders = LoadPurchaseOrders();
        var purchaseReceipts = LoadPurchaseReceipts();
        var supplierInvoices = LoadSupplierInvoices();
        var stockBalances = LoadStockBalances();
        var transferOrders = LoadTransferOrders();
        var reservations = LoadReservations();
        var inventoryCounts = LoadInventoryCounts();
        var writeOffs = LoadWriteOffs();
        if (supplierInvoices.Count == 0)
        {
            supplierInvoices = BuildDerivedSupplierInvoices(purchaseOrders, purchaseReceipts);
        }

        var suppliers = BuildSuppliers(customers, purchaseOrders, supplierInvoices, purchaseReceipts);

        return new DesktopOperationalSnapshot
        {
            Customers = customers,
            CatalogItems = catalogItems,
            Managers = managers,
            Currencies = currencies.Length == 0 ? ["RUB"] : currencies,
            Warehouses = warehouses,
            Orders = salesOrders,
            Invoices = salesInvoices,
            Shipments = salesShipments,
            Suppliers = suppliers,
            PurchaseOrders = purchaseOrders,
            SupplierInvoices = supplierInvoices,
            PurchaseReceipts = purchaseReceipts,
            StockBalances = stockBalances,
            TransferOrders = transferOrders,
            Reservations = reservations,
            InventoryCounts = inventoryCounts,
            WriteOffs = writeOffs
        };
    }

    public IReadOnlyList<OperationalCatalogPriceTypeSeed> TryLoadCatalogPriceTypes()
    {
        try
        {
            ValidateDatabaseName(_options.DatabaseName);
            return LoadCatalogPriceTypes();
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<OperationalCatalogPriceTypeSeed>();
        }
    }

    private IReadOnlyList<SalesCustomerRecord> LoadCustomers()
    {
        const string sql = """\\n            SELECT COALESCE(\\n                CAST(\\n                    JSON_ARRAYAGG(\\n                        JSON_OBJECT(\\n                            'Id', data.id,\\n                            'Code', data.code,\\n                            'Name', data.name,\\n                            'ContractNumber', data.contract_number,\\n                            'CurrencyCode', data.currency_code,\\n                            'Manager', data.manager_name,\\n                            'Status', data.status_text,\\n                            'Phone', data.phone,\\n                            'Email', data.email,\\n                            'Notes', data.notes\\n                        )\\n                    ) AS CHAR CHARACTER SET utf8mb4\\n                ),\\n                '[]'\\n            )\\n            FROM (\\n                SELECT\\n                    bp.id,\\n                    bp.code,\\n                    bp.name,\\n                    COALESCE(pc.number, '') AS contract_number,\\n                    COALESCE(NULLIF(pc.settlement_currency_code, ''), NULLIF(bp.settlement_currency_code, ''), 'RUB') AS currency_code,\\n                    COALESCE(emp.full_name, '') AS manager_name,\\n                    CASE\\n                        WHEN bp.is_archived = 1 THEN 'Архив'\\n                        WHEN bp.roles > 0 THEN 'Активен'\\n                        ELSE 'Активен'\\n                    END AS status_text,\\n                    COALESCE(cnt.phone, '') AS phone,\\n                    COALESCE(cnt.email, '') AS email,\\n                    CONCAT('Operational MySQL / business_partners / ', bp.code) AS notes\\n                FROM business_partners bp\\n                LEFT JOIN (\\n                    SELECT\\n                        business_partner_id,\\n                        SUBSTRING_INDEX(GROUP_CONCAT(number ORDER BY number SEPARATOR '||'), '||', 1) AS number,\\n                        SUBSTRING_INDEX(GROUP_CONCAT(COALESCE(settlement_currency_code, 'RUB') ORDER BY number SEPARATOR '||'), '||', 1) AS settlement_currency_code\\n                    FROM partner_contracts\\n                    GROUP BY business_partner_id\\n                ) pc ON pc.business_partner_id = bp.id\\n                LEFT JOIN employees emp ON emp.id = bp.responsible_employee_id\\n                LEFT JOIN (\\n                    SELECT\\n                        business_partner_id,\\n                        SUBSTRING_INDEX(GROUP_CONCAT(COALESCE(phone, '') ORDER BY is_primary DESC, full_name SEPARATOR '||'), '||', 1) AS phone,\\n                        SUBSTRING_INDEX(GROUP_CONCAT(COALESCE(email, '') ORDER BY is_primary DESC, full_name SEPARATOR '||'), '||', 1) AS email\\n                    FROM partner_contacts\\n                    GROUP BY business_partner_id\\n                ) cnt ON cnt.business_partner_id = bp.id\\n                ORDER BY bp.name\\n            ) AS data;\\n            """;

        return QueryJsonArray<PartnerRow>(sql)
            .Select(row => new SalesCustomerRecord
            {
                Id = ParseGuid(row.Id, row.Code),
                Code = row.Code ?? string.Empty,
                Name = row.Name ?? string.Empty,
                ContractNumber = row.ContractNumber ?? string.Empty,
                CurrencyCode = string.IsNullOrWhiteSpace(row.CurrencyCode) ? "RUB" : row.CurrencyCode,
                Manager = row.Manager ?? string.Empty,
                Status = row.Status ?? "Активен",
                Phone = row.Phone ?? string.Empty,
                Email = row.Email ?? string.Empty,
                Notes = row.Notes ?? string.Empty
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<SalesCatalogItemOption> LoadCatalogItems()
    {
        const string sql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Code', data.code,
                            'Name', data.name,
                            'Unit', data.unit_name,
                            'DefaultPrice', data.default_price
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    COALESCE(NULLIF(ni.sku, ''), ni.code) AS code,
                    ni.name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    CAST(0 AS DECIMAL(18,4)) AS default_price
                FROM nomenclature_items ni
                LEFT JOIN units_of_measure u ON u.id = ni.unit_of_measure_id
                ORDER BY ni.name, ni.sku
            ) AS data;
            """;

        return QueryJsonArray<CatalogRow>(sql)
            .Select(row => new SalesCatalogItemOption(
                row.Code ?? string.Empty,
                row.Name ?? string.Empty,
                string.IsNullOrWhiteSpace(row.Unit) ? "шт" : row.Unit,
                row.DefaultPrice))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<OperationalCatalogPriceTypeSeed> LoadCatalogPriceTypes()
    {
        const string sql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Code', data.code,
                            'Name', data.name,
                            'CurrencyCode', data.currency_code,
                            'BasePriceTypeName', data.base_price_type_name,
                            'IsManualEntryOnly', data.is_manual_entry_only,
                            'UsesPsychologicalRounding', data.uses_psychological_rounding
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    pt.code,
                    pt.name,
                    pt.currency_code,
                    COALESCE(baseType.name, '') AS base_price_type_name,
                    pt.is_manual_entry_only,
                    pt.uses_psychological_rounding
                FROM price_types pt
                LEFT JOIN price_types baseType ON baseType.id = pt.base_price_type_id
                ORDER BY pt.name, pt.code
            ) AS data;
            """;

        return QueryJsonArray<OperationalCatalogPriceTypeRow>(sql)
            .Select(row => new OperationalCatalogPriceTypeSeed(
                row.Code ?? string.Empty,
                row.Name ?? string.Empty,
                string.IsNullOrWhiteSpace(row.CurrencyCode) ? "RUB" : row.CurrencyCode,
                row.BasePriceTypeName ?? string.Empty,
                row.IsManualEntryOnly != 0,
                row.UsesPsychologicalRounding != 0))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    private IReadOnlyList<string> LoadManagers()
    {
        const string sql = """
            SELECT COALESCE(
                CAST(JSON_ARRAYAGG(data.value) AS CHAR CHARACTER SET utf8mb4),
                '[]'
            )
            FROM (
                SELECT DISTINCT full_name AS value
                FROM employees
                WHERE full_name IS NOT NULL AND full_name <> ''
                ORDER BY full_name
            ) AS data;
            """;

        return QueryJsonArray<string>(sql)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<string> LoadWarehouses()
    {
        const string sql = """
            SELECT COALESCE(
                CAST(JSON_ARRAYAGG(data.value) AS CHAR CHARACTER SET utf8mb4),
                '[]'
            )
            FROM (
                SELECT DISTINCT name AS value
                FROM warehouse_nodes
                WHERE name IS NOT NULL AND name <> ''
                ORDER BY name
            ) AS data;
            """;

        return QueryJsonArray<string>(sql)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<WarehouseStockBalanceRecord> LoadStockBalances()
    {
        const string sql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'WarehouseName', data.warehouse_name,
                            'UnitName', data.unit_name,
                            'Quantity', data.quantity,
                            'ReservedQuantity', data.reserved_quantity,
                            'ShippedQuantity', data.shipped_quantity
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, '') AS item_name,
                    COALESCE(wn.name, '') AS warehouse_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    sb.quantity,
                    sb.reserved_quantity,
                    COALESCE(shipments.shipped_quantity, 0) AS shipped_quantity
                FROM stock_balances sb
                LEFT JOIN nomenclature_items ni ON ni.id = sb.item_id
                LEFT JOIN warehouse_nodes wn ON wn.id = sb.warehouse_node_id
                LEFT JOIN units_of_measure u ON u.id = ni.unit_of_measure_id
                LEFT JOIN (
                    SELECT ship_line.item_id, ship.warehouse_node_id, SUM(ship_line.quantity) AS shipped_quantity
                    FROM sales_shipment_lines ship_line
                    INNER JOIN sales_shipments ship ON ship.id = ship_line.sales_shipment_id
                    WHERE ship.warehouse_node_id IS NOT NULL
                    GROUP BY ship_line.item_id, ship.warehouse_node_id
                ) shipments ON shipments.item_id = sb.item_id AND shipments.warehouse_node_id = sb.warehouse_node_id
                ORDER BY wn.name, ni.name, ni.code
            ) AS data;
            """;

        return QueryJsonArray<WarehouseBalanceRow>(sql)
            .Select(row =>
            {
                var freeQuantity = row.Quantity - row.ReservedQuantity;
                return new WarehouseStockBalanceRecord
                {
                    ItemCode = row.ItemCode ?? string.Empty,
                    ItemName = row.ItemName ?? string.Empty,
                    Warehouse = row.WarehouseName ?? string.Empty,
                    Unit = string.IsNullOrWhiteSpace(row.UnitName) ? "шт" : row.UnitName,
                    BaselineQuantity = row.Quantity,
                    ReservedQuantity = row.ReservedQuantity,
                    ShippedQuantity = row.ShippedQuantity,
                    FreeQuantity = freeQuantity,
                    Status = freeQuantity <= 0m ? "Критично" : row.ReservedQuantity > 0m ? "Под контроль" : "ОК",
                    SourceLabel = "Operational MySQL / stock_balances"
                };
            })
            .ToArray();
    }

    private IReadOnlyList<WarehouseDocumentRecord> LoadTransferOrders()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'DocumentDate', data.document_date,
                            'PostingState', data.posting_state,
                            'LifecycleStatus', data.lifecycle_status,
                            'SourceWarehouseName', data.source_warehouse_name,
                            'TargetWarehouseName', data.target_warehouse_name,
                            'RelatedDocument', data.related_document,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    t.id,
                    t.number,
                    DATE_FORMAT(t.document_date, '%Y-%m-%dT%H:%i:%s') AS document_date,
                    t.posting_state,
                    t.lifecycle_status,
                    COALESCE(src.name, '') AS source_warehouse_name,
                    COALESCE(dst.name, '') AS target_warehouse_name,
                    COALESCE(so.number, '') AS related_document,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(t.comment_text, '') AS comment_text
                FROM transfer_orders t
                LEFT JOIN warehouse_nodes src ON src.id = t.source_warehouse_node_id
                LEFT JOIN warehouse_nodes dst ON dst.id = t.target_warehouse_node_id
                LEFT JOIN sales_orders so ON so.id = t.customer_order_id
                LEFT JOIN employees emp ON emp.id = COALESCE(t.responsible_employee_id, t.author_id)
                ORDER BY t.document_date DESC, t.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'UnitName', data.unit_name,
                            'Quantity', data.quantity,
                            'ReservedQuantity', data.reserved_quantity,
                            'CollectedQuantity', data.collected_quantity,
                            'SourceLocation', data.source_location,
                            'TargetLocation', data.target_location
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    tol.transfer_order_id AS document_id,
                    tol.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    tol.quantity,
                    tol.reserved_quantity,
                    tol.collected_quantity,
                    COALESCE(src.name, '') AS source_location,
                    COALESCE(dst.name, '') AS target_location
                FROM transfer_order_lines tol
                LEFT JOIN nomenclature_items ni ON ni.id = tol.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(tol.unit_of_measure_id, ni.unit_of_measure_id)
                LEFT JOIN warehouse_nodes src ON src.id = tol.source_warehouse_node_id
                LEFT JOIN warehouse_nodes dst ON dst.id = tol.target_warehouse_node_id
            ) AS data;
            """;

        var headers = QueryJsonArray<WarehouseDocumentHeaderRow>(headerSql);
        var lines = QueryJsonArray<WarehouseDocumentLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WarehouseDocumentLineRecord>)group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new WarehouseDocumentLineRecord
                    {
                        RowNumber = item.LineNo,
                        Item = FirstNonEmpty(item.ItemName, item.ItemCode),
                        Quantity = item.Quantity,
                        Unit = string.IsNullOrWhiteSpace(item.UnitName) ? "шт" : item.UnitName,
                        Reserve = item.ReservedQuantity,
                        PickedQuantity = item.CollectedQuantity,
                        Price = 0m,
                        Amount = 0m,
                        SourceLocation = item.SourceLocation ?? string.Empty,
                        TargetLocation = item.TargetLocation ?? string.Empty,
                        RelatedDocument = string.Empty,
                        Fields = BuildFields(
                            ("Номенклатура", FirstNonEmpty(item.ItemName, item.ItemCode)),
                            ("Код", item.ItemCode),
                            ("Количество", item.Quantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ЕдиницаИзмерения", item.UnitName),
                            ("Резерв", item.ReservedQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("КоличествоСобрано", item.CollectedQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ИсходноеМесто", item.SourceLocation),
                            ("НовоеМесто", item.TargetLocation))
                    })
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new WarehouseDocumentRecord
            {
                DocumentType = "Заказ на перемещение",
                Number = row.Number ?? string.Empty,
                Date = ParseDate(row.DocumentDate),
                Status = MapOperationalDocumentStatus(row.PostingState, row.LifecycleStatus),
                SourceWarehouse = row.SourceWarehouseName ?? string.Empty,
                TargetWarehouse = row.TargetWarehouseName ?? string.Empty,
                RelatedDocument = row.RelatedDocument ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                SourceLabel = "Operational MySQL / transfer_orders",
                Title = $"Заказ на перемещение {row.Number}",
                Subtitle = FirstNonEmpty(row.SourceWarehouseName, row.TargetWarehouseName),
                Fields = BuildFields(
                    ("Номер", row.Number),
                    ("Дата", row.DocumentDate),
                    ("СкладИсточник", row.SourceWarehouseName),
                    ("СкладПолучатель", row.TargetWarehouseName),
                    ("ДокументОснование", row.RelatedDocument),
                    ("Комментарий", row.Comment),
                    ("Ответственный", row.Manager)),
                Lines = lines.GetValueOrDefault(row.Id ?? string.Empty, Array.Empty<WarehouseDocumentLineRecord>())
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<WarehouseDocumentRecord> LoadReservations()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'DocumentDate', data.document_date,
                            'PostingState', data.posting_state,
                            'LifecycleStatus', data.lifecycle_status,
                            'SourceWarehouseName', data.source_warehouse_name,
                            'TargetWarehouseName', data.target_warehouse_name,
                            'RelatedDocument', data.related_document,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    sr.id,
                    sr.number,
                    DATE_FORMAT(sr.document_date, '%Y-%m-%dT%H:%i:%s') AS document_date,
                    sr.posting_state,
                    0 AS lifecycle_status,
                    COALESCE(src.name, '') AS source_warehouse_name,
                    COALESCE(dst.name, '') AS target_warehouse_name,
                    COALESCE(so.number, '') AS related_document,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(sr.comment_text, '') AS comment_text
                FROM stock_reservations sr
                LEFT JOIN sales_orders so ON so.id = sr.sales_order_id
                LEFT JOIN employees emp ON emp.id = COALESCE(sr.responsible_employee_id, sr.author_id)
                LEFT JOIN (
                    SELECT stock_reservation_id, MIN(source_warehouse_node_id) AS source_warehouse_node_id, MIN(target_warehouse_node_id) AS target_warehouse_node_id
                    FROM stock_reservation_lines
                    GROUP BY stock_reservation_id
                ) line_ref ON line_ref.stock_reservation_id = sr.id
                LEFT JOIN warehouse_nodes src ON src.id = line_ref.source_warehouse_node_id
                LEFT JOIN warehouse_nodes dst ON dst.id = line_ref.target_warehouse_node_id
                ORDER BY sr.document_date DESC, sr.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'UnitName', data.unit_name,
                            'Quantity', data.quantity,
                            'ReservedQuantity', data.reserved_quantity,
                            'CollectedQuantity', data.collected_quantity,
                            'SourceLocation', data.source_location,
                            'TargetLocation', data.target_location
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    srl.stock_reservation_id AS document_id,
                    srl.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    srl.quantity,
                    srl.reserved_quantity,
                    srl.collected_quantity,
                    COALESCE(src.name, '') AS source_location,
                    COALESCE(dst.name, '') AS target_location
                FROM stock_reservation_lines srl
                LEFT JOIN nomenclature_items ni ON ni.id = srl.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(srl.unit_of_measure_id, ni.unit_of_measure_id)
                LEFT JOIN warehouse_nodes src ON src.id = srl.source_warehouse_node_id
                LEFT JOIN warehouse_nodes dst ON dst.id = srl.target_warehouse_node_id
            ) AS data;
            """;

        var headers = QueryJsonArray<WarehouseDocumentHeaderRow>(headerSql);
        var lines = QueryJsonArray<WarehouseDocumentLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WarehouseDocumentLineRecord>)group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new WarehouseDocumentLineRecord
                    {
                        RowNumber = item.LineNo,
                        Item = FirstNonEmpty(item.ItemName, item.ItemCode),
                        Quantity = item.Quantity,
                        Unit = string.IsNullOrWhiteSpace(item.UnitName) ? "шт" : item.UnitName,
                        Reserve = item.ReservedQuantity,
                        PickedQuantity = item.CollectedQuantity,
                        Price = 0m,
                        Amount = 0m,
                        SourceLocation = item.SourceLocation ?? string.Empty,
                        TargetLocation = item.TargetLocation ?? string.Empty,
                        RelatedDocument = string.Empty,
                        Fields = BuildFields(
                            ("Номенклатура", FirstNonEmpty(item.ItemName, item.ItemCode)),
                            ("Код", item.ItemCode),
                            ("Количество", item.Quantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ЕдиницаИзмерения", item.UnitName),
                            ("Резерв", item.ReservedQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("КоличествоСобрано", item.CollectedQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ИсходноеМесто", item.SourceLocation),
                            ("НовоеМесто", item.TargetLocation))
                    })
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new WarehouseDocumentRecord
            {
                DocumentType = "Резервирование",
                Number = row.Number ?? string.Empty,
                Date = ParseDate(row.DocumentDate),
                Status = MapOperationalDocumentStatus(row.PostingState, row.LifecycleStatus),
                SourceWarehouse = row.SourceWarehouseName ?? string.Empty,
                TargetWarehouse = row.TargetWarehouseName ?? string.Empty,
                RelatedDocument = row.RelatedDocument ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                SourceLabel = "Operational MySQL / stock_reservations",
                Title = $"Резервирование {row.Number}",
                Subtitle = FirstNonEmpty(row.SourceWarehouseName, row.TargetWarehouseName),
                Fields = BuildFields(
                    ("Номер", row.Number),
                    ("Дата", row.DocumentDate),
                    ("ИсходноеМесто", row.SourceWarehouseName),
                    ("НовоеМесто", row.TargetWarehouseName),
                    ("ЗаказПокупателя", row.RelatedDocument),
                    ("Комментарий", row.Comment),
                    ("Ответственный", row.Manager)),
                Lines = lines.GetValueOrDefault(row.Id ?? string.Empty, Array.Empty<WarehouseDocumentLineRecord>())
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<WarehouseDocumentRecord> LoadInventoryCounts()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'DocumentDate', data.document_date,
                            'PostingState', data.posting_state,
                            'LifecycleStatus', data.lifecycle_status,
                            'SourceWarehouseName', data.source_warehouse_name,
                            'TargetWarehouseName', data.target_warehouse_name,
                            'RelatedDocument', data.related_document,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    ic.id,
                    ic.number,
                    DATE_FORMAT(ic.document_date, '%Y-%m-%dT%H:%i:%s') AS document_date,
                    ic.posting_state,
                    0 AS lifecycle_status,
                    COALESCE(wn.name, '') AS source_warehouse_name,
                    '' AS target_warehouse_name,
                    '' AS related_document,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(ic.comment_text, '') AS comment_text
                FROM inventory_counts ic
                LEFT JOIN warehouse_nodes wn ON wn.id = ic.warehouse_node_id
                LEFT JOIN employees emp ON emp.id = COALESCE(ic.responsible_employee_id, ic.author_id)
                ORDER BY ic.document_date DESC, ic.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'UnitName', data.unit_name,
                            'BookQuantity', data.book_quantity,
                            'ActualQuantity', data.actual_quantity,
                            'DifferenceQuantity', data.difference_quantity
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    icl.inventory_count_id AS document_id,
                    icl.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    icl.book_quantity,
                    icl.actual_quantity,
                    icl.difference_quantity
                FROM inventory_count_lines icl
                LEFT JOIN nomenclature_items ni ON ni.id = icl.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(icl.unit_of_measure_id, ni.unit_of_measure_id)
            ) AS data;
            """;

        var headers = QueryJsonArray<WarehouseDocumentHeaderRow>(headerSql);
        var lines = QueryJsonArray<WarehouseDocumentLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WarehouseDocumentLineRecord>)group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new WarehouseDocumentLineRecord
                    {
                        RowNumber = item.LineNo,
                        Item = FirstNonEmpty(item.ItemName, item.ItemCode),
                        Quantity = item.ActualQuantity,
                        Unit = string.IsNullOrWhiteSpace(item.UnitName) ? "шт" : item.UnitName,
                        Reserve = 0m,
                        PickedQuantity = 0m,
                        Price = 0m,
                        Amount = 0m,
                        SourceLocation = string.Empty,
                        TargetLocation = string.Empty,
                        RelatedDocument = string.Empty,
                        Fields = BuildFields(
                            ("Номенклатура", FirstNonEmpty(item.ItemName, item.ItemCode)),
                            ("Код", item.ItemCode),
                            ("КоличествоУчет", item.BookQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("Количество", item.ActualQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("Отклонение", item.DifferenceQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ЕдиницаИзмерения", item.UnitName))
                    })
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new WarehouseDocumentRecord
            {
                DocumentType = "Инвентаризация",
                Number = row.Number ?? string.Empty,
                Date = ParseDate(row.DocumentDate),
                Status = MapOperationalDocumentStatus(row.PostingState, row.LifecycleStatus),
                SourceWarehouse = row.SourceWarehouseName ?? string.Empty,
                TargetWarehouse = row.TargetWarehouseName ?? string.Empty,
                RelatedDocument = row.RelatedDocument ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                SourceLabel = "Operational MySQL / inventory_counts",
                Title = $"Инвентаризация {row.Number}",
                Subtitle = row.SourceWarehouseName ?? string.Empty,
                Fields = BuildFields(
                    ("Номер", row.Number),
                    ("Дата", row.DocumentDate),
                    ("Склад", row.SourceWarehouseName),
                    ("Комментарий", row.Comment),
                    ("Ответственный", row.Manager)),
                Lines = lines.GetValueOrDefault(row.Id ?? string.Empty, Array.Empty<WarehouseDocumentLineRecord>())
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<WarehouseDocumentRecord> LoadWriteOffs()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'DocumentDate', data.document_date,
                            'PostingState', data.posting_state,
                            'LifecycleStatus', data.lifecycle_status,
                            'SourceWarehouseName', data.source_warehouse_name,
                            'TargetWarehouseName', data.target_warehouse_name,
                            'RelatedDocument', data.related_document,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    sw.id,
                    sw.number,
                    DATE_FORMAT(sw.document_date, '%Y-%m-%dT%H:%i:%s') AS document_date,
                    sw.posting_state,
                    0 AS lifecycle_status,
                    COALESCE(wn.name, '') AS source_warehouse_name,
                    '' AS target_warehouse_name,
                    COALESCE(ic.number, '') AS related_document,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(sw.reason_text, sw.comment_text, '') AS comment_text
                FROM stock_write_offs sw
                LEFT JOIN warehouse_nodes wn ON wn.id = sw.warehouse_node_id
                LEFT JOIN inventory_counts ic ON ic.id = sw.inventory_count_id
                LEFT JOIN employees emp ON emp.id = COALESCE(sw.responsible_employee_id, sw.author_id)
                ORDER BY sw.document_date DESC, sw.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'UnitName', data.unit_name,
                            'Quantity', data.quantity,
                            'Price', data.price,
                            'Amount', data.amount
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    swl.stock_write_off_id AS document_id,
                    swl.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, swl.content_text, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    swl.quantity,
                    swl.price,
                    swl.total AS amount
                FROM stock_write_off_lines swl
                LEFT JOIN nomenclature_items ni ON ni.id = swl.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(swl.unit_of_measure_id, ni.unit_of_measure_id)
            ) AS data;
            """;

        var headers = QueryJsonArray<WarehouseDocumentHeaderRow>(headerSql);
        var lines = QueryJsonArray<WarehouseDocumentLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WarehouseDocumentLineRecord>)group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new WarehouseDocumentLineRecord
                    {
                        RowNumber = item.LineNo,
                        Item = FirstNonEmpty(item.ItemName, item.ItemCode),
                        Quantity = item.Quantity,
                        Unit = string.IsNullOrWhiteSpace(item.UnitName) ? "шт" : item.UnitName,
                        Reserve = 0m,
                        PickedQuantity = 0m,
                        Price = item.Price,
                        Amount = item.Amount,
                        SourceLocation = string.Empty,
                        TargetLocation = string.Empty,
                        RelatedDocument = string.Empty,
                        Fields = BuildFields(
                            ("Номенклатура", FirstNonEmpty(item.ItemName, item.ItemCode)),
                            ("Код", item.ItemCode),
                            ("Количество", item.Quantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ЕдиницаИзмерения", item.UnitName),
                            ("Цена", item.Price.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("Сумма", item.Amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))))
                    })
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new WarehouseDocumentRecord
            {
                DocumentType = "Списание",
                Number = row.Number ?? string.Empty,
                Date = ParseDate(row.DocumentDate),
                Status = MapOperationalDocumentStatus(row.PostingState, row.LifecycleStatus),
                SourceWarehouse = row.SourceWarehouseName ?? string.Empty,
                TargetWarehouse = row.TargetWarehouseName ?? string.Empty,
                RelatedDocument = row.RelatedDocument ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                SourceLabel = "Operational MySQL / stock_write_offs",
                Title = $"Списание {row.Number}",
                Subtitle = row.SourceWarehouseName ?? string.Empty,
                Fields = BuildFields(
                    ("Номер", row.Number),
                    ("Дата", row.DocumentDate),
                    ("Склад", row.SourceWarehouseName),
                    ("ДокументОснование", row.RelatedDocument),
                    ("Комментарий", row.Comment),
                    ("Ответственный", row.Manager)),
                Lines = lines.GetValueOrDefault(row.Id ?? string.Empty, Array.Empty<WarehouseDocumentLineRecord>())
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<SalesOrderRecord> LoadSalesOrders()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'OrderDate', data.order_date,
                            'CustomerId', data.customer_id,
                            'CustomerCode', data.customer_code,
                            'CustomerName', data.customer_name,
                            'ContractNumber', data.contract_number,
                            'CurrencyCode', data.currency_code,
                            'WarehouseName', data.warehouse_name,
                            'PostingState', data.posting_state,
                            'LifecycleStatus', data.lifecycle_status,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text,
                            'OrderedQuantity', data.ordered_quantity,
                            'ReservedQuantity', data.reserved_quantity
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    so.id,
                    so.number,
                    DATE_FORMAT(so.document_date, '%Y-%m-%dT%H:%i:%s') AS order_date,
                    so.customer_id,
                    COALESCE(bp.code, '') AS customer_code,
                    COALESCE(bp.name, '') AS customer_name,
                    COALESCE(pc.number, '') AS contract_number,
                    COALESCE(so.currency_code, 'RUB') AS currency_code,
                    COALESCE(wh.name, reserve_wh.name, '') AS warehouse_name,
                    so.posting_state,
                    so.lifecycle_status,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(so.comment_text, '') AS comment_text,
                    COALESCE(line_totals.ordered_quantity, 0) AS ordered_quantity,
                    COALESCE(reservations.reserved_quantity, 0) AS reserved_quantity
                FROM sales_orders so
                LEFT JOIN business_partners bp ON bp.id = so.customer_id
                LEFT JOIN partner_contracts pc ON pc.id = so.contract_id
                LEFT JOIN employees emp ON emp.id = COALESCE(so.responsible_employee_id, so.author_id)
                LEFT JOIN warehouse_nodes wh ON wh.id = so.warehouse_node_id
                LEFT JOIN warehouse_nodes reserve_wh ON reserve_wh.id = so.reserve_warehouse_node_id
                LEFT JOIN (
                    SELECT sales_order_id, SUM(quantity) AS ordered_quantity
                    FROM sales_order_lines
                    GROUP BY sales_order_id
                ) line_totals ON line_totals.sales_order_id = so.id
                LEFT JOIN (
                    SELECT sr.sales_order_id, SUM(COALESCE(NULLIF(srl.reserved_quantity, 0), srl.quantity)) AS reserved_quantity
                    FROM stock_reservations sr
                    INNER JOIN stock_reservation_lines srl ON srl.stock_reservation_id = sr.id
                    WHERE sr.sales_order_id IS NOT NULL
                    GROUP BY sr.sales_order_id
                ) reservations ON reservations.sales_order_id = so.id
                ORDER BY so.document_date DESC, so.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'Unit', data.unit_name,
                            'Quantity', data.quantity,
                            'Price', data.price
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    sol.sales_order_id AS document_id,
                    sol.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, sol.content_text, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    sol.quantity,
                    sol.price
                FROM sales_order_lines sol
                LEFT JOIN nomenclature_items ni ON ni.id = sol.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(sol.unit_of_measure_id, ni.unit_of_measure_id)
            ) AS data;
            """;

        var headers = QueryJsonArray<SalesOrderHeaderRow>(headerSql);
        var lineLookup = QueryJsonArray<DocumentLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new BindingList<SalesOrderLineRecord>(group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new SalesOrderLineRecord
                    {
                        Id = CreateDeterministicGuid($"{group.Key}|{item.LineNo}"),
                        ItemCode = item.ItemCode ?? string.Empty,
                        ItemName = item.ItemName ?? string.Empty,
                        Unit = string.IsNullOrWhiteSpace(item.Unit) ? "шт" : item.Unit,
                        Quantity = item.Quantity,
                        Price = item.Price
                    })
                    .ToList()),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new SalesOrderRecord
            {
                Id = ParseGuid(row.Id, row.Number),
                Number = row.Number ?? string.Empty,
                OrderDate = ParseDate(row.OrderDate) ?? DateTime.Today,
                CustomerId = ParseGuid(row.CustomerId, $"customer|{row.CustomerCode}|{row.CustomerName}"),
                CustomerCode = row.CustomerCode ?? string.Empty,
                CustomerName = row.CustomerName ?? string.Empty,
                ContractNumber = row.ContractNumber ?? string.Empty,
                CurrencyCode = string.IsNullOrWhiteSpace(row.CurrencyCode) ? "RUB" : row.CurrencyCode,
                Warehouse = row.WarehouseName ?? string.Empty,
                Status = MapSalesOrderStatus(row.PostingState, row.LifecycleStatus, row.OrderedQuantity, row.ReservedQuantity),
                Manager = row.Manager ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                Lines = lineLookup.GetValueOrDefault(row.Id ?? string.Empty, new BindingList<SalesOrderLineRecord>())
            })
            .OrderByDescending(item => item.OrderDate)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<SalesInvoiceRecord> LoadSalesInvoices()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'InvoiceDate', data.invoice_date,
                            'DueDate', data.due_date,
                            'SalesOrderId', data.sales_order_id,
                            'SalesOrderNumber', data.sales_order_number,
                            'CustomerId', data.customer_id,
                            'CustomerCode', data.customer_code,
                            'CustomerName', data.customer_name,
                            'ContractNumber', data.contract_number,
                            'CurrencyCode', data.currency_code,
                            'PostingState', data.posting_state,
                            'LifecycleStatus', data.lifecycle_status,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    si.id,
                    si.number,
                    DATE_FORMAT(si.document_date, '%Y-%m-%dT%H:%i:%s') AS invoice_date,
                    DATE_FORMAT(COALESCE(ps.due_date, DATE(si.document_date)), '%Y-%m-%dT00:00:00') AS due_date,
                    COALESCE(so.id, '') AS sales_order_id,
                    COALESCE(so.number, '') AS sales_order_number,
                    si.customer_id,
                    COALESCE(bp.code, '') AS customer_code,
                    COALESCE(bp.name, '') AS customer_name,
                    COALESCE(pc.number, '') AS contract_number,
                    COALESCE(si.currency_code, 'RUB') AS currency_code,
                    si.posting_state,
                    si.lifecycle_status,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(si.comment_text, '') AS comment_text
                FROM sales_invoices si
                LEFT JOIN business_partners bp ON bp.id = si.customer_id
                LEFT JOIN partner_contracts pc ON pc.id = si.contract_id
                LEFT JOIN employees emp ON emp.id = COALESCE(si.responsible_employee_id, si.author_id)
                LEFT JOIN sales_orders so ON so.id = si.base_document_id
                LEFT JOIN (
                    SELECT sales_invoice_id, MIN(due_date) AS due_date
                    FROM sales_invoice_payment_schedule
                    GROUP BY sales_invoice_id
                ) ps ON ps.sales_invoice_id = si.id
                ORDER BY si.document_date DESC, si.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'Unit', data.unit_name,
                            'Quantity', data.quantity,
                            'Price', data.price
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    sil.sales_invoice_id AS document_id,
                    sil.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, sil.content_text, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    sil.quantity,
                    sil.price
                FROM sales_invoice_lines sil
                LEFT JOIN nomenclature_items ni ON ni.id = sil.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(sil.unit_of_measure_id, ni.unit_of_measure_id)
            ) AS data;
            """;

        var headers = QueryJsonArray<SalesInvoiceHeaderRow>(headerSql);
        var lineLookup = QueryJsonArray<DocumentLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new BindingList<SalesOrderLineRecord>(group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new SalesOrderLineRecord
                    {
                        Id = CreateDeterministicGuid($"{group.Key}|{item.LineNo}"),
                        ItemCode = item.ItemCode ?? string.Empty,
                        ItemName = item.ItemName ?? string.Empty,
                        Unit = string.IsNullOrWhiteSpace(item.Unit) ? "шт" : item.Unit,
                        Quantity = item.Quantity,
                        Price = item.Price
                    })
                    .ToList()),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new SalesInvoiceRecord
            {
                Id = ParseGuid(row.Id, row.Number),
                Number = row.Number ?? string.Empty,
                InvoiceDate = ParseDate(row.InvoiceDate) ?? DateTime.Today,
                DueDate = ParseDate(row.DueDate) ?? ParseDate(row.InvoiceDate) ?? DateTime.Today,
                SalesOrderId = ParseGuid(row.SalesOrderId, $"sales-order|{row.Number}"),
                SalesOrderNumber = row.SalesOrderNumber ?? string.Empty,
                CustomerId = ParseGuid(row.CustomerId, $"customer|{row.CustomerCode}|{row.CustomerName}"),
                CustomerCode = row.CustomerCode ?? string.Empty,
                CustomerName = row.CustomerName ?? string.Empty,
                ContractNumber = row.ContractNumber ?? string.Empty,
                CurrencyCode = string.IsNullOrWhiteSpace(row.CurrencyCode) ? "RUB" : row.CurrencyCode,
                Status = MapFinancialDocumentStatus(row.PostingState, row.LifecycleStatus, ParseDate(row.DueDate)),
                Manager = row.Manager ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                Lines = lineLookup.GetValueOrDefault(row.Id ?? string.Empty, new BindingList<SalesOrderLineRecord>())
            })
            .OrderByDescending(item => item.InvoiceDate)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<SalesShipmentRecord> LoadSalesShipments()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'ShipmentDate', data.shipment_date,
                            'SalesOrderId', data.sales_order_id,
                            'SalesOrderNumber', data.sales_order_number,
                            'CustomerId', data.customer_id,
                            'CustomerCode', data.customer_code,
                            'CustomerName', data.customer_name,
                            'ContractNumber', data.contract_number,
                            'CurrencyCode', data.currency_code,
                            'WarehouseName', data.warehouse_name,
                            'CarrierName', data.carrier_name,
                            'PostingState', data.posting_state,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    ss.id,
                    ss.number,
                    DATE_FORMAT(ss.document_date, '%Y-%m-%dT%H:%i:%s') AS shipment_date,
                    COALESCE(so.id, '') AS sales_order_id,
                    COALESCE(so.number, '') AS sales_order_number,
                    ss.customer_id,
                    COALESCE(bp.code, '') AS customer_code,
                    COALESCE(bp.name, '') AS customer_name,
                    COALESCE(pc.number, '') AS contract_number,
                    COALESCE(ss.currency_code, 'RUB') AS currency_code,
                    COALESCE(wn.name, '') AS warehouse_name,
                    COALESCE(carrier.name, '') AS carrier_name,
                    ss.posting_state,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(ss.comment_text, '') AS comment_text
                FROM sales_shipments ss
                LEFT JOIN sales_orders so ON so.id = ss.sales_order_id
                LEFT JOIN business_partners bp ON bp.id = ss.customer_id
                LEFT JOIN business_partners carrier ON carrier.id = ss.carrier_id
                LEFT JOIN partner_contracts pc ON pc.id = ss.contract_id
                LEFT JOIN warehouse_nodes wn ON wn.id = ss.warehouse_node_id
                LEFT JOIN employees emp ON emp.id = COALESCE(ss.responsible_employee_id, ss.author_id)
                ORDER BY ss.document_date DESC, ss.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'Unit', data.unit_name,
                            'Quantity', data.quantity,
                            'Price', data.price
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    ship_line.sales_shipment_id AS document_id,
                    ship_line.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, ship_line.content_text, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    ship_line.quantity,
                    ship_line.price
                FROM sales_shipment_lines ship_line
                LEFT JOIN nomenclature_items ni ON ni.id = ship_line.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(ship_line.unit_of_measure_id, ni.unit_of_measure_id)
            ) AS data;
            """;

        var headers = QueryJsonArray<SalesShipmentHeaderRow>(headerSql);
        var lineLookup = QueryJsonArray<DocumentLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new BindingList<SalesOrderLineRecord>(group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new SalesOrderLineRecord
                    {
                        Id = CreateDeterministicGuid($"{group.Key}|{item.LineNo}"),
                        ItemCode = item.ItemCode ?? string.Empty,
                        ItemName = item.ItemName ?? string.Empty,
                        Unit = string.IsNullOrWhiteSpace(item.Unit) ? "шт" : item.Unit,
                        Quantity = item.Quantity,
                        Price = item.Price
                    })
                    .ToList()),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new SalesShipmentRecord
            {
                Id = ParseGuid(row.Id, row.Number),
                Number = row.Number ?? string.Empty,
                ShipmentDate = ParseDate(row.ShipmentDate) ?? DateTime.Today,
                SalesOrderId = ParseGuid(row.SalesOrderId, $"sales-order|{row.SalesOrderNumber}"),
                SalesOrderNumber = row.SalesOrderNumber ?? string.Empty,
                CustomerId = ParseGuid(row.CustomerId, $"customer|{row.CustomerCode}|{row.CustomerName}"),
                CustomerCode = row.CustomerCode ?? string.Empty,
                CustomerName = row.CustomerName ?? string.Empty,
                ContractNumber = row.ContractNumber ?? string.Empty,
                CurrencyCode = string.IsNullOrWhiteSpace(row.CurrencyCode) ? "RUB" : row.CurrencyCode,
                Warehouse = row.WarehouseName ?? string.Empty,
                Status = MapSalesShipmentStatus(row.PostingState),
                Carrier = row.CarrierName ?? string.Empty,
                Manager = row.Manager ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                Lines = lineLookup.GetValueOrDefault(row.Id ?? string.Empty, new BindingList<SalesOrderLineRecord>())
            })
            .OrderByDescending(item => item.ShipmentDate)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<PurchasingDocumentRecord> LoadPurchaseOrders()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'DocumentDate', data.document_date,
                            'PostingState', data.posting_state,
                            'LifecycleStatus', data.lifecycle_status,
                            'SupplierName', data.supplier_name,
                            'SupplierCode', data.supplier_code,
                            'ContractNumber', data.contract_number,
                            'WarehouseName', data.warehouse_name,
                            'RelatedDocument', data.related_document,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text,
                            'TotalAmount', data.total_amount
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    po.id,
                    po.number,
                    DATE_FORMAT(po.document_date, '%Y-%m-%dT%H:%i:%s') AS document_date,
                    po.posting_state,
                    po.lifecycle_status,
                    COALESCE(bp.name, '') AS supplier_name,
                    COALESCE(bp.code, '') AS supplier_code,
                    COALESCE(pc.number, '') AS contract_number,
                    COALESCE(wn.name, reserve_wn.name, '') AS warehouse_name,
                    COALESCE(so.number, '') AS related_document,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(po.comment_text, '') AS comment_text,
                    COALESCE(po_lines.total_amount, 0) AS total_amount
                FROM purchase_orders po
                LEFT JOIN business_partners bp ON bp.id = po.supplier_id
                LEFT JOIN partner_contracts pc ON pc.id = po.contract_id
                LEFT JOIN warehouse_nodes wn ON wn.id = po.warehouse_node_id
                LEFT JOIN warehouse_nodes reserve_wn ON reserve_wn.id = po.reserve_warehouse_node_id
                LEFT JOIN sales_orders so ON so.id = po.linked_sales_order_id
                LEFT JOIN employees emp ON emp.id = COALESCE(po.responsible_employee_id, po.author_id)
                LEFT JOIN (
                    SELECT purchase_order_id, SUM(total) AS total_amount
                    FROM purchase_order_lines
                    GROUP BY purchase_order_id
                ) po_lines ON po_lines.purchase_order_id = po.id
                ORDER BY po.document_date DESC, po.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'Unit', data.unit_name,
                            'Quantity', data.quantity,
                            'Price', data.price,
                            'Amount', data.total_amount
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    pol.purchase_order_id AS document_id,
                    pol.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, pol.content_text, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    pol.quantity,
                    pol.price,
                    pol.total AS total_amount
                FROM purchase_order_lines pol
                LEFT JOIN nomenclature_items ni ON ni.id = pol.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(pol.unit_of_measure_id, ni.unit_of_measure_id)
            ) AS data;
            """;

        var headers = QueryJsonArray<PurchasingHeaderRow>(headerSql);
        var lines = QueryJsonArray<PurchasingLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PurchasingDocumentLineRecord>)group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new PurchasingDocumentLineRecord
                    {
                        SectionName = "Товары",
                        RowNumber = item.LineNo,
                        Item = FirstNonEmpty(item.ItemName, item.ItemCode),
                        Quantity = item.Quantity,
                        Unit = string.IsNullOrWhiteSpace(item.Unit) ? "шт" : item.Unit,
                        Price = item.Price,
                        Amount = item.Amount,
                        Fields = BuildFields(
                            ("Номенклатура", FirstNonEmpty(item.ItemName, item.ItemCode)),
                            ("Код", item.ItemCode),
                            ("Количество", item.Quantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ЕдиницаИзмерения", item.Unit),
                            ("Цена", item.Price.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("Сумма", item.Amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))))
                    })
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new PurchasingDocumentRecord
            {
                DocumentType = "Заказ поставщику",
                Number = row.Number ?? string.Empty,
                Date = ParseDate(row.DocumentDate),
                Status = MapOperationalDocumentStatus(row.PostingState, row.LifecycleStatus),
                SupplierName = row.SupplierName ?? string.Empty,
                Contract = row.ContractNumber ?? string.Empty,
                Warehouse = row.WarehouseName ?? string.Empty,
                RelatedDocument = row.RelatedDocument ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                TotalAmount = row.TotalAmount,
                SourceLabel = "Operational MySQL / purchase_orders",
                Title = $"Заказ поставщику {row.Number}",
                Subtitle = row.SupplierName ?? string.Empty,
                Fields = BuildFields(
                    ("Номер", row.Number),
                    ("Дата", row.DocumentDate),
                    ("Контрагент", row.SupplierName),
                    ("Договор", row.ContractNumber),
                    ("Склад", row.WarehouseName),
                    ("ДокументОснование", row.RelatedDocument),
                    ("Комментарий", row.Comment),
                    ("Ответственный", row.Manager),
                    ("СуммаДокумента", row.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")))),
                Lines = lines.GetValueOrDefault(row.Id ?? string.Empty, Array.Empty<PurchasingDocumentLineRecord>())
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<PurchasingDocumentRecord> LoadPurchaseReceipts()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'DocumentDate', data.document_date,
                            'PostingState', data.posting_state,
                            'SupplierName', data.supplier_name,
                            'SupplierCode', data.supplier_code,
                            'ContractNumber', data.contract_number,
                            'WarehouseName', data.warehouse_name,
                            'RelatedDocument', data.related_document,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text,
                            'TotalAmount', data.total_amount
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    pr.id,
                    pr.number,
                    DATE_FORMAT(pr.document_date, '%Y-%m-%dT%H:%i:%s') AS document_date,
                    pr.posting_state,
                    COALESCE(bp.name, '') AS supplier_name,
                    COALESCE(bp.code, '') AS supplier_code,
                    COALESCE(pc.number, '') AS contract_number,
                    COALESCE(wn.name, '') AS warehouse_name,
                    COALESCE(po.number, '') AS related_document,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(pr.comment_text, '') AS comment_text,
                    pr.total_amount
                FROM purchase_receipts pr
                LEFT JOIN business_partners bp ON bp.id = pr.supplier_id
                LEFT JOIN partner_contracts pc ON pc.id = pr.contract_id
                LEFT JOIN warehouse_nodes wn ON wn.id = pr.warehouse_node_id
                LEFT JOIN purchase_orders po ON po.id = pr.purchase_order_id
                LEFT JOIN employees emp ON emp.id = COALESCE(pr.responsible_employee_id, pr.author_id)
                ORDER BY pr.document_date DESC, pr.number DESC
            ) AS data;
            """;

        const string lineSql = """\\n            SELECT COALESCE(\\n                CAST(\\n                    JSON_ARRAYAGG(\\n                        JSON_OBJECT(\\n                            'DocumentId', data.document_id,\\n                            'SectionName', data.section_name,\\n                            'LineNo', data.line_no,\\n                            'ItemCode', data.item_code,\\n                            'ItemName', data.item_name,\\n                            'Unit', data.unit_name,\\n                            'Quantity', data.quantity,\\n                            'Price', data.price,\\n                            'Amount', data.total_amount\\n                        )\\n                    ) AS CHAR CHARACTER SET utf8mb4\\n                ),\\n                '[]'\\n            )\\n            FROM (\\n                SELECT\\n                    prl.purchase_receipt_id AS document_id,\\n                    'Товары' AS section_name,\\n                    prl.line_no,\\n                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,\\n                    COALESCE(ni.name, prl.content_text, '') AS item_name,\\n                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,\\n                    prl.quantity,\\n                    prl.price,\\n                    prl.total AS total_amount\\n                FROM purchase_receipt_lines prl\\n                LEFT JOIN nomenclature_items ni ON ni.id = prl.item_id\\n                LEFT JOIN units_of_measure u ON u.id = COALESCE(prl.unit_of_measure_id, ni.unit_of_measure_id)\\n\\n                UNION ALL\\n\\n                SELECT\\n                    prc.purchase_receipt_id AS document_id,\\n                    'Доп. расходы' AS section_name,\\n                    prc.line_no,\\n                    '' AS item_code,\\n                    prc.charge_name AS item_name,\\n                    '' AS unit_name,\\n                    1 AS quantity,\\n                    prc.amount AS price,\\n                    prc.amount AS total_amount\\n                FROM purchase_receipt_additional_charges prc\\n            ) AS data;\\n            """;

        var headers = QueryJsonArray<PurchasingHeaderRow>(headerSql);
        var lines = QueryJsonArray<PurchasingLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PurchasingDocumentLineRecord>)group
                    .OrderBy(item => item.SectionName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.LineNo)
                    .Select(item => new PurchasingDocumentLineRecord
                    {
                        SectionName = item.SectionName ?? "Товары",
                        RowNumber = item.LineNo,
                        Item = FirstNonEmpty(item.ItemName, item.ItemCode),
                        Quantity = item.Quantity,
                        Unit = item.Unit ?? string.Empty,
                        Price = item.Price,
                        Amount = item.Amount,
                        Fields = BuildFields(
                            ("Раздел", item.SectionName),
                            ("Номенклатура", FirstNonEmpty(item.ItemName, item.ItemCode)),
                            ("Код", item.ItemCode),
                            ("Количество", item.Quantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ЕдиницаИзмерения", item.Unit),
                            ("Цена", item.Price.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("Сумма", item.Amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))))
                    })
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new PurchasingDocumentRecord
            {
                DocumentType = "Приемка",
                Number = row.Number ?? string.Empty,
                Date = ParseDate(row.DocumentDate),
                Status = row.PostingState >= 2 ? "Проведен" : "Черновик",
                SupplierName = row.SupplierName ?? string.Empty,
                Contract = row.ContractNumber ?? string.Empty,
                Warehouse = row.WarehouseName ?? string.Empty,
                RelatedDocument = row.RelatedDocument ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                TotalAmount = row.TotalAmount,
                SourceLabel = "Operational MySQL / purchase_receipts",
                Title = $"Приемка {row.Number}",
                Subtitle = row.SupplierName ?? string.Empty,
                Fields = BuildFields(
                    ("Номер", row.Number),
                    ("Дата", row.DocumentDate),
                    ("Контрагент", row.SupplierName),
                    ("Договор", row.ContractNumber),
                    ("Склад", row.WarehouseName),
                    ("ДокументОснование", row.RelatedDocument),
                    ("Комментарий", row.Comment),
                    ("Ответственный", row.Manager),
                    ("СуммаДокумента", row.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")))),
                Lines = lines.GetValueOrDefault(row.Id ?? string.Empty, Array.Empty<PurchasingDocumentLineRecord>())
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<PurchasingDocumentRecord> LoadSupplierInvoices()
    {
        const string headerSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'Number', data.number,
                            'DocumentDate', data.document_date,
                            'PostingState', data.posting_state,
                            'SupplierName', data.supplier_name,
                            'SupplierCode', data.supplier_code,
                            'ContractNumber', data.contract_number,
                            'WarehouseName', data.warehouse_name,
                            'RelatedDocument', data.related_document,
                            'Manager', data.manager_name,
                            'Comment', data.comment_text,
                            'TotalAmount', data.total_amount
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    si.id,
                    si.number,
                    DATE_FORMAT(si.document_date, '%Y-%m-%dT%H:%i:%s') AS document_date,
                    si.posting_state,
                    COALESCE(bp.name, '') AS supplier_name,
                    COALESCE(bp.code, '') AS supplier_code,
                    COALESCE(pc.number, '') AS contract_number,
                    '' AS warehouse_name,
                    COALESCE(po.number, pr.number, '') AS related_document,
                    COALESCE(emp.full_name, '') AS manager_name,
                    COALESCE(si.comment_text, '') AS comment_text,
                    COALESCE(si_lines.total_amount, 0) AS total_amount
                FROM supplier_invoices si
                LEFT JOIN business_partners bp ON bp.id = si.supplier_id
                LEFT JOIN partner_contracts pc ON pc.id = si.contract_id
                LEFT JOIN purchase_orders po ON po.id = si.purchase_order_id
                LEFT JOIN purchase_receipts pr ON pr.id = si.base_document_id
                LEFT JOIN employees emp ON emp.id = COALESCE(si.responsible_employee_id, si.author_id)
                LEFT JOIN (
                    SELECT supplier_invoice_id, SUM(total) AS total_amount
                    FROM supplier_invoice_lines
                    GROUP BY supplier_invoice_id
                ) si_lines ON si_lines.supplier_invoice_id = si.id
                ORDER BY si.document_date DESC, si.number DESC
            ) AS data;
            """;

        const string lineSql = """
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'DocumentId', data.document_id,
                            'LineNo', data.line_no,
                            'ItemCode', data.item_code,
                            'ItemName', data.item_name,
                            'Unit', data.unit_name,
                            'Quantity', data.quantity,
                            'Price', data.price,
                            'Amount', data.total_amount
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    sil.supplier_invoice_id AS document_id,
                    sil.line_no,
                    COALESCE(NULLIF(ni.sku, ''), ni.code, '') AS item_code,
                    COALESCE(ni.name, sil.content_text, '') AS item_name,
                    COALESCE(NULLIF(u.symbol, ''), u.name, 'шт') AS unit_name,
                    sil.quantity,
                    sil.price,
                    sil.total AS total_amount
                FROM supplier_invoice_lines sil
                LEFT JOIN nomenclature_items ni ON ni.id = sil.item_id
                LEFT JOIN units_of_measure u ON u.id = COALESCE(sil.unit_of_measure_id, ni.unit_of_measure_id)
            ) AS data;
            """;

        var headers = QueryJsonArray<PurchasingHeaderRow>(headerSql);
        if (headers.Count == 0)
        {
            return Array.Empty<PurchasingDocumentRecord>();
        }

        var lines = QueryJsonArray<PurchasingLineRow>(lineSql)
            .GroupBy(item => item.DocumentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PurchasingDocumentLineRecord>)group
                    .OrderBy(item => item.LineNo)
                    .Select(item => new PurchasingDocumentLineRecord
                    {
                        SectionName = "Товары",
                        RowNumber = item.LineNo,
                        Item = FirstNonEmpty(item.ItemName, item.ItemCode),
                        Quantity = item.Quantity,
                        Unit = string.IsNullOrWhiteSpace(item.Unit) ? "шт" : item.Unit,
                        Price = item.Price,
                        Amount = item.Amount,
                        Fields = BuildFields(
                            ("Номенклатура", FirstNonEmpty(item.ItemName, item.ItemCode)),
                            ("Код", item.ItemCode),
                            ("Количество", item.Quantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("ЕдиницаИзмерения", item.Unit),
                            ("Цена", item.Price.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))),
                            ("Сумма", item.Amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))))
                    })
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return headers
            .Select(row => new PurchasingDocumentRecord
            {
                DocumentType = "Счет поставщика",
                Number = row.Number ?? string.Empty,
                Date = ParseDate(row.DocumentDate),
                Status = row.PostingState >= 2 ? "Проведен" : "Черновик",
                SupplierName = row.SupplierName ?? string.Empty,
                Contract = row.ContractNumber ?? string.Empty,
                Warehouse = row.WarehouseName ?? string.Empty,
                RelatedDocument = row.RelatedDocument ?? string.Empty,
                Comment = row.Comment ?? string.Empty,
                TotalAmount = row.TotalAmount,
                SourceLabel = "Operational MySQL / supplier_invoices",
                Title = $"Счет поставщика {row.Number}",
                Subtitle = row.SupplierName ?? string.Empty,
                Fields = BuildFields(
                    ("Номер", row.Number),
                    ("Дата", row.DocumentDate),
                    ("Контрагент", row.SupplierName),
                    ("Договор", row.ContractNumber),
                    ("Склад", row.WarehouseName),
                    ("ДокументОснование", row.RelatedDocument),
                    ("Комментарий", row.Comment),
                    ("Ответственный", row.Manager),
                    ("СуммаДокумента", row.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")))),
                Lines = lines.GetValueOrDefault(row.Id ?? string.Empty, Array.Empty<PurchasingDocumentLineRecord>())
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PurchasingDocumentRecord> BuildDerivedSupplierInvoices(
        IReadOnlyList<PurchasingDocumentRecord> purchaseOrders,
        IReadOnlyList<PurchasingDocumentRecord> purchaseReceipts)
    {
        if (purchaseReceipts.Count > 0)
        {
            return purchaseReceipts
                .Select(receipt => new PurchasingDocumentRecord
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
                    SourceLabel = "Operational MySQL / derived from purchase_receipts",
                    Title = $"Счет поставщика к приемке {receipt.Number}",
                    Subtitle = receipt.SupplierName,
                    Fields =
                    [
                        new OneCFieldValue("ДокументОснование", receipt.Number, receipt.Number),
                        new OneCFieldValue("Контрагент", receipt.SupplierName, receipt.SupplierName),
                        new OneCFieldValue("Договор", receipt.Contract, receipt.Contract),
                        new OneCFieldValue("СуммаДокумента", receipt.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")), receipt.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")))
                    ],
                    Lines = receipt.Lines
                })
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
                SourceLabel = "Operational MySQL / derived from purchase_orders",
                Title = $"Счет поставщика к заказу {order.Number}",
                Subtitle = order.SupplierName,
                Fields =
                [
                    new OneCFieldValue("ДокументОснование", order.Number, order.Number),
                    new OneCFieldValue("Контрагент", order.SupplierName, order.SupplierName),
                    new OneCFieldValue("Договор", order.Contract, order.Contract),
                    new OneCFieldValue("СуммаДокумента", order.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")), order.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")))
                ],
                Lines = order.Lines
            })
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ToArray();
    }

    private static IReadOnlyList<PurchasingSupplierRecord> BuildSuppliers(
        IReadOnlyList<SalesCustomerRecord> customers,
        IReadOnlyList<PurchasingDocumentRecord> purchaseOrders,
        IReadOnlyList<PurchasingDocumentRecord> supplierInvoices,
        IReadOnlyList<PurchasingDocumentRecord> purchaseReceipts)
    {
        var customerByName = customers
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var docLookup = purchaseOrders
            .Concat(supplierInvoices)
            .Concat(purchaseReceipts)
            .Where(item => !string.IsNullOrWhiteSpace(item.SupplierName))
            .GroupBy(item => item.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var suppliers = new List<PurchasingSupplierRecord>();
        foreach (var pair in docLookup)
        {
            customerByName.TryGetValue(pair.Key, out var customer);
            suppliers.Add(new PurchasingSupplierRecord
            {
                Name = pair.Key,
                Code = customer?.Code ?? string.Empty,
                Status = customer?.Status ?? "Поставщик из документов",
                TaxId = string.Empty,
                Phone = customer?.Phone ?? string.Empty,
                Email = customer?.Email ?? string.Empty,
                Contract = FirstNonEmpty(pair.Value.Contract, customer?.ContractNumber),
                SourceLabel = "Operational MySQL",
                Fields =
                [
                    new OneCFieldValue("Контрагент", pair.Key, pair.Key),
                    new OneCFieldValue("Код", customer?.Code ?? string.Empty, customer?.Code ?? string.Empty),
                    new OneCFieldValue("Договор", FirstNonEmpty(pair.Value.Contract, customer?.ContractNumber), FirstNonEmpty(pair.Value.Contract, customer?.ContractNumber)),
                    new OneCFieldValue("Телефон", customer?.Phone ?? string.Empty, customer?.Phone ?? string.Empty),
                    new OneCFieldValue("Email", customer?.Email ?? string.Empty, customer?.Email ?? string.Empty)
                ]
            });
        }

        return suppliers
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private List<T> QueryJsonArray<T>(string sql)
    {
        var output = ExecuteSqlScalar(sql).Trim();
        if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<T>>(output, JsonOptions) ?? [];
    }

    private string ExecuteSqlScalar(string sql)
    {
        ThrowIfConnectionBackoffActive();

        try
        {
            var output = DesktopMySqlCommandRunner.ExecuteScalar(
                _options,
                sql,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                commandTimeoutSeconds: 30);
            ResetConnectionBackoff();
            return output;
        }
        catch (Exception exception)
        {
            if (IsConnectionFailure(exception.Message))
            {
                EnterConnectionBackoff();
            }

            throw new InvalidOperationException($"Operational MySQL query failed.{Environment.NewLine}{exception.Message}", exception);
        }
    }

    private static bool IsConnectionFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("ERROR 2003", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Can't connect to MySQL server", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Lost connection", StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowIfConnectionBackoffActive()
    {
        lock (ConnectionStateSync)
        {
            if (DateTime.UtcNow < s_connectionBackoffUntilUtc)
            {
                throw new InvalidOperationException("Operational MySQL loading is temporarily unavailable after connection failures.");
            }
        }
    }

    private static void EnterConnectionBackoff()
    {
        lock (ConnectionStateSync)
        {
            s_connectionBackoffUntilUtc = DateTime.UtcNow.AddSeconds(ConnectionBackoffSeconds);
        }
    }

    private static void ResetConnectionBackoff()
    {
        lock (ConnectionStateSync)
        {
            s_connectionBackoffUntilUtc = DateTime.MinValue;
        }
    }

    private static void TryWriteErrorLog(Exception exception)
    {
        if (!ShouldWriteError(exception))
        {
            return;
        }

        try
        {
            var root = WorkspacePathResolver.ResolveWorkspaceRoot();
            var path = Path.Combine(root, "app_data", "operational-desktop-load-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exception.ToString(), new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static bool ShouldWriteError(Exception exception)
    {
        var signature = $"{exception.GetType().FullName}:{exception.Message}";
        lock (ErrorLogSync)
        {
            var now = DateTime.UtcNow;
            if (string.Equals(signature, s_lastErrorSignature, StringComparison.Ordinal)
                && (now - s_lastErrorLogUtc).TotalSeconds < 30)
            {
                return false;
            }

            s_lastErrorSignature = signature;
            s_lastErrorLogUtc = now;
            return true;
        }
    }


    private static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Operational database name is required.");
        }

        if (databaseName.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
        {
            throw new InvalidOperationException("Operational database name can contain only letters, digits and underscore.");
        }
    }

    private static Guid ParseGuid(string? rawValue, string? fallbackSeed)
    {
        return Guid.TryParse(rawValue, out var parsed)
            ? parsed
            : CreateDeterministicGuid(FirstNonEmpty(rawValue, fallbackSeed, Guid.NewGuid().ToString("N")));
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result)
            ? result
            : null;
    }

    private static string MapFinancialDocumentStatus(int postingState, int lifecycleStatus, DateTime? dueDate)
    {
        if (lifecycleStatus >= 7)
        {
            return "Отменен";
        }

        if (postingState >= 2)
        {
            return dueDate is not null && dueDate.Value.Date < DateTime.Today
                ? "Ожидает оплату"
                : "Выставлен";
        }

        return "Черновик";
    }

    private static string MapSalesOrderStatus(int postingState, int lifecycleStatus, decimal orderedQuantity, decimal reservedQuantity)
    {
        if (lifecycleStatus >= 7)
        {
            return "Отменен";
        }

        if (reservedQuantity > 0m && orderedQuantity > 0m && reservedQuantity >= orderedQuantity)
        {
            return "В резерве";
        }

        if (postingState >= 2)
        {
            return "Подтвержден";
        }

        return "План";
    }

    private static string MapSalesShipmentStatus(int postingState)
    {
        return postingState >= 2 ? "Отгружена" : "К сборке";
    }

    private static string MapOperationalDocumentStatus(int postingState, int lifecycleStatus)
    {
        if (lifecycleStatus >= 7)
        {
            return "Отменен";
        }

        return postingState >= 2 ? "Проведен" : "Черновик";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static IReadOnlyList<OneCFieldValue> BuildFields(params (string Name, string? Value)[] items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => new OneCFieldValue(item.Name, item.Value ?? string.Empty, item.Value ?? string.Empty))
            .ToArray();
    }

    private sealed class PartnerRow
    {
        public string? Id { get; set; }

        public string? Code { get; set; }

        public string? Name { get; set; }

        public string? ContractNumber { get; set; }

        public string? CurrencyCode { get; set; }

        public string? Manager { get; set; }

        public string? Status { get; set; }

        public string? Phone { get; set; }

        public string? Email { get; set; }

        public string? Notes { get; set; }
    }

    private sealed class CatalogRow
    {
        public string? Code { get; set; }

        public string? Name { get; set; }

        public string? Unit { get; set; }

        public decimal DefaultPrice { get; set; }
    }

    private sealed class OperationalCatalogPriceTypeRow
    {
        public string? Code { get; set; }

        public string? Name { get; set; }

        public string? CurrencyCode { get; set; }

        public string? BasePriceTypeName { get; set; }

        public int IsManualEntryOnly { get; set; }

        public int UsesPsychologicalRounding { get; set; }
    }

    private sealed class SalesOrderHeaderRow
    {
        public string? Id { get; set; }

        public string? Number { get; set; }

        public string? OrderDate { get; set; }

        public string? CustomerId { get; set; }

        public string? CustomerCode { get; set; }

        public string? CustomerName { get; set; }

        public string? ContractNumber { get; set; }

        public string? CurrencyCode { get; set; }

        public string? WarehouseName { get; set; }

        public int PostingState { get; set; }

        public int LifecycleStatus { get; set; }

        public string? Manager { get; set; }

        public string? Comment { get; set; }

        public decimal OrderedQuantity { get; set; }

        public decimal ReservedQuantity { get; set; }
    }

    private sealed class SalesInvoiceHeaderRow
    {
        public string? Id { get; set; }

        public string? Number { get; set; }

        public string? InvoiceDate { get; set; }

        public string? DueDate { get; set; }

        public string? SalesOrderId { get; set; }

        public string? SalesOrderNumber { get; set; }

        public string? CustomerId { get; set; }

        public string? CustomerCode { get; set; }

        public string? CustomerName { get; set; }

        public string? ContractNumber { get; set; }

        public string? CurrencyCode { get; set; }

        public int PostingState { get; set; }

        public int LifecycleStatus { get; set; }

        public string? Manager { get; set; }

        public string? Comment { get; set; }
    }

    private sealed class SalesShipmentHeaderRow
    {
        public string? Id { get; set; }

        public string? Number { get; set; }

        public string? ShipmentDate { get; set; }

        public string? SalesOrderId { get; set; }

        public string? SalesOrderNumber { get; set; }

        public string? CustomerId { get; set; }

        public string? CustomerCode { get; set; }

        public string? CustomerName { get; set; }

        public string? ContractNumber { get; set; }

        public string? CurrencyCode { get; set; }

        public string? WarehouseName { get; set; }

        public string? CarrierName { get; set; }

        public int PostingState { get; set; }

        public string? Manager { get; set; }

        public string? Comment { get; set; }
    }

    private sealed class PurchasingHeaderRow
    {
        public string? Id { get; set; }

        public string? Number { get; set; }

        public string? DocumentDate { get; set; }

        public int PostingState { get; set; }

        public int LifecycleStatus { get; set; }

        public string? SupplierName { get; set; }

        public string? SupplierCode { get; set; }

        public string? ContractNumber { get; set; }

        public string? WarehouseName { get; set; }

        public string? RelatedDocument { get; set; }

        public string? Manager { get; set; }

        public string? Comment { get; set; }

        public decimal TotalAmount { get; set; }
    }

    private sealed class DocumentLineRow
    {
        public string? DocumentId { get; set; }

        public int LineNo { get; set; }

        public string? ItemCode { get; set; }

        public string? ItemName { get; set; }

        public string? Unit { get; set; }

        public decimal Quantity { get; set; }

        public decimal Price { get; set; }
    }

    private sealed class PurchasingLineRow
    {
        public string? DocumentId { get; set; }

        public string? SectionName { get; set; }

        public int LineNo { get; set; }

        public string? ItemCode { get; set; }

        public string? ItemName { get; set; }

        public string? Unit { get; set; }

        public decimal Quantity { get; set; }

        public decimal Price { get; set; }

        public decimal Amount { get; set; }
    }

    private sealed class WarehouseBalanceRow
    {
        public string? ItemCode { get; set; }

        public string? ItemName { get; set; }

        public string? WarehouseName { get; set; }

        public string? UnitName { get; set; }

        public decimal Quantity { get; set; }

        public decimal ReservedQuantity { get; set; }

        public decimal ShippedQuantity { get; set; }
    }

    private sealed class WarehouseDocumentHeaderRow
    {
        public string? Id { get; set; }

        public string? Number { get; set; }

        public string? DocumentDate { get; set; }

        public int PostingState { get; set; }

        public int LifecycleStatus { get; set; }

        public string? SourceWarehouseName { get; set; }

        public string? TargetWarehouseName { get; set; }

        public string? RelatedDocument { get; set; }

        public string? Manager { get; set; }

        public string? Comment { get; set; }
    }

    private sealed class WarehouseDocumentLineRow
    {
        public string? DocumentId { get; set; }

        public int LineNo { get; set; }

        public string? ItemCode { get; set; }

        public string? ItemName { get; set; }

        public string? UnitName { get; set; }

        public decimal Quantity { get; set; }

        public decimal ReservedQuantity { get; set; }

        public decimal CollectedQuantity { get; set; }

        public decimal BookQuantity { get; set; }

        public decimal ActualQuantity { get; set; }

        public decimal DifferenceQuantity { get; set; }

        public decimal Price { get; set; }

        public decimal Amount { get; set; }

        public string? SourceLocation { get; set; }

        public string? TargetLocation { get; set; }
    }
}

public sealed class OperationalMySqlDesktopOptions
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 3306;

    public string DatabaseName { get; init; } = "warehouse_automation_raw_dev";

    public string User { get; init; } = "root";

    public string Password { get; init; } = string.Empty;

    public string MysqlExecutablePath { get; init; } = string.Empty;
}

public sealed record OperationalCatalogPriceTypeSeed(
    string Code,
    string Name,
    string CurrencyCode,
    string BasePriceTypeName,
    bool IsManualEntryOnly,
    bool UsesPsychologicalRounding);
