using System.ComponentModel;
using System.Text.Json;
using MySqlConnector;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed partial class DesktopMySqlBackplaneService
{
    private const string SalesModuleCode = "sales";
    private const int MysqlSalesCommandTimeoutSeconds = 90;

    public DesktopModuleSnapshotRecord<SalesWorkspaceSnapshot>? TryLoadSalesWorkspaceSnapshotRecord()
    {
        try
        {
            EnsureDatabaseAndSchema();
            var metadata = LoadSalesWorkspaceStateMetadata();
            if (metadata is null)
            {
                return null;
            }

            return new DesktopModuleSnapshotRecord<SalesWorkspaceSnapshot>(
                LoadSalesWorkspaceSnapshotRows(),
                metadata);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public DesktopModuleSnapshotMetadata? TryLoadSalesWorkspaceSnapshotMetadata()
    {
        try
        {
            EnsureDatabaseAndSchema();
            return LoadSalesWorkspaceStateMetadata();
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public DesktopModuleSnapshotSaveResult TrySaveSalesWorkspaceSnapshot(
        SalesWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        try
        {
            var metadata = SaveSalesWorkspaceSnapshot(snapshot, actorName, expectedMetadata, auditEvents);
            return DesktopModuleSnapshotSaveResult.Saved(metadata);
        }
        catch (DesktopModuleSnapshotConflictException exception)
        {
            TryWriteErrorLog(exception);
            return DesktopModuleSnapshotSaveResult.Conflict(exception.ServerMetadata);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return DesktopModuleSnapshotSaveResult.Failed(exception.Message);
        }
    }

    private DesktopModuleSnapshotMetadata SaveSalesWorkspaceSnapshot(
        SalesWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(actorName);

        var moduleCode = NormalizeModuleCode(SalesModuleCode);
        var actor = NormalizeUserName(actorName);
        var payloadHash = ComputeSha256(JsonSerializer.Serialize(snapshot, JsonOptions));

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlSalesCommandTimeoutSeconds);
        using var transaction = connection.BeginTransaction();

        var currentMetadata = LoadSalesWorkspaceStateMetadata(connection, transaction);
        if (currentMetadata is not null
            && string.Equals(currentMetadata.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
        {
            transaction.Rollback();
            return currentMetadata;
        }

        if (expectedMetadata is null)
        {
            if (currentMetadata is not null)
            {
                throw new DesktopModuleSnapshotConflictException(
                    "Sales workspace rows were changed by another client.",
                    currentMetadata);
            }
        }
        else if (currentMetadata is null
                 || currentMetadata.VersionNo != expectedMetadata.VersionNo
                 || !string.Equals(currentMetadata.PayloadHash, expectedMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopModuleSnapshotConflictException(
                "Sales workspace rows were changed by another client.",
                currentMetadata);
        }

        ReplaceSalesWorkspaceRows(connection, transaction, snapshot);

        var nextVersionNo = currentMetadata is null
            ? 1
            : string.Equals(currentMetadata.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase)
                ? currentMetadata.VersionNo
                : currentMetadata.VersionNo + 1;

        using (var stateCommand = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_module_states (
                module_code,
                payload_hash,
                version_no,
                updated_by,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                @module_code,
                @payload_hash,
                @version_no,
                @updated_by,
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            )
            ON DUPLICATE KEY UPDATE
                payload_hash = VALUES(payload_hash),
                version_no = VALUES(version_no),
                updated_by = VALUES(updated_by),
                updated_at_utc = UTC_TIMESTAMP(6);
            """))
        {
            AddParameter(stateCommand, "@module_code", moduleCode);
            AddParameter(stateCommand, "@payload_hash", payloadHash);
            AddParameter(stateCommand, "@version_no", nextVersionNo);
            AddParameter(stateCommand, "@updated_by", actor);
            stateCommand.ExecuteNonQuery();
        }

        transaction.Commit();

        if (auditEvents is not null)
        {
            ReplaceAuditEvents(moduleCode, auditEvents);
        }

        return LoadSalesWorkspaceStateMetadata()
               ?? new DesktopModuleSnapshotMetadata(moduleCode, nextVersionNo, payloadHash, actor, DateTime.UtcNow);
    }

    private SalesWorkspaceSnapshot LoadSalesWorkspaceSnapshotRows()
    {
        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlSalesCommandTimeoutSeconds);

        var snapshot = new SalesWorkspaceSnapshot
        {
            Customers = LoadSalesCustomers(connection),
            CashReceipts = LoadSalesCashReceipts(connection),
            OperationLog = LoadSalesOperationLog(connection)
        };

        var documents = LoadSalesDocuments(connection);
        snapshot.Orders = documents.Orders;
        snapshot.Invoices = documents.Invoices;
        snapshot.Shipments = documents.Shipments;
        snapshot.Returns = documents.Returns;
        return snapshot;
    }

    private DesktopModuleSnapshotMetadata? LoadSalesWorkspaceStateMetadata()
    {
        var sql = $"""
            SELECT COALESCE(
                CAST(
                    JSON_OBJECT(
                        'VersionNo', version_no,
                        'PayloadHash', payload_hash,
                        'UpdatedBy', updated_by,
                        'UpdatedAtUtc', DATE_FORMAT(updated_at_utc, '%Y-%m-%dT%H:%i:%s.%fZ')
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                'null'
            )
            FROM app_module_states
            WHERE module_code = {SqlUtf8TextExpression(NormalizeModuleCode(SalesModuleCode))}
            LIMIT 1;
            """;

        var output = ExecuteSqlScalar(sql, useDatabase: true, commandTimeoutSeconds: MysqlSalesCommandTimeoutSeconds).Trim();
        if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = JsonSerializer.Deserialize<DesktopModuleSnapshotRow>(output, JsonOptions);
        return row is null ? null : CreateSnapshotMetadata(SalesModuleCode, row);
    }

    private DesktopModuleSnapshotMetadata? LoadSalesWorkspaceStateMetadata(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            SELECT
                version_no,
                payload_hash,
                updated_by,
                updated_at_utc
            FROM app_module_states
            WHERE module_code = @module_code
            LIMIT 1;
            """);
        AddParameter(command, "@module_code", NormalizeModuleCode(SalesModuleCode));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DesktopModuleSnapshotMetadata(
            NormalizeModuleCode(SalesModuleCode),
            reader.GetInt32(reader.GetOrdinal("version_no")),
            ReadString(reader, "payload_hash"),
            ReadString(reader, "updated_by"),
            DateTime.SpecifyKind(ReadDateTime(reader, "updated_at_utc"), DateTimeKind.Utc));
    }

    private void ReplaceSalesWorkspaceRows(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SalesWorkspaceSnapshot snapshot)
    {
        var customers = snapshot.Customers ?? [];
        var orders = snapshot.Orders ?? [];
        var invoices = snapshot.Invoices ?? [];
        var shipments = snapshot.Shipments ?? [];
        var returns = snapshot.Returns ?? [];
        var cashReceipts = snapshot.CashReceipts ?? [];
        var operationLog = snapshot.OperationLog ?? [];

        CreateSalesKeepTables(connection, transaction);
        PopulateSalesKeepTables(connection, transaction, customers, orders, invoices, shipments, returns, cashReceipts, operationLog);
        DeleteMissingSalesRows(connection, transaction);

        InsertSalesCustomers(connection, transaction, customers);
        InsertSalesDocuments(
            connection,
            transaction,
            orders,
            invoices,
            shipments,
            returns);
        InsertSalesCashReceipts(connection, transaction, cashReceipts);
        InsertSalesOperationLog(connection, transaction, operationLog);
    }

    private static void CreateSalesKeepTables(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        foreach (var tableName in new[]
                 {
                     "tmp_app_sales_keep_customers",
                     "tmp_app_sales_keep_customer_contacts",
                     "tmp_app_sales_keep_documents",
                     "tmp_app_sales_keep_document_lines",
                     "tmp_app_sales_keep_cash_receipts",
                     "tmp_app_sales_keep_operation_log"
                 })
        {
            ExecuteMySqlNonQuery(connection, transaction, $"CREATE TEMPORARY TABLE {tableName} (id CHAR(36) NOT NULL PRIMARY KEY) ENGINE=MEMORY;");
        }
    }

    private static void PopulateSalesKeepTables(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<SalesCustomerRecord> customers,
        IEnumerable<SalesOrderRecord> orders,
        IEnumerable<SalesInvoiceRecord> invoices,
        IEnumerable<SalesShipmentRecord> shipments,
        IEnumerable<SalesReturnRecord> returns,
        IEnumerable<SalesCashReceiptRecord> cashReceipts,
        IEnumerable<SalesOperationLogEntry> operationLog)
    {
        InsertKeepIds(connection, transaction, "tmp_app_sales_keep_customers", customers
            .Select(customer => EnsureId(customer.Id, $"sales-customer|{customer.Code}|{customer.Name}")));
        InsertKeepIds(connection, transaction, "tmp_app_sales_keep_customer_contacts", customers
            .SelectMany(customer => BuildContactIds(customer)));

        var documentIds = EnumerateDocumentIds(orders, invoices, shipments, returns).ToArray();
        InsertKeepIds(connection, transaction, "tmp_app_sales_keep_documents", documentIds);
        InsertKeepIds(connection, transaction, "tmp_app_sales_keep_document_lines", EnumerateDocumentLineRowIds(orders, invoices, shipments, returns));

        InsertKeepIds(connection, transaction, "tmp_app_sales_keep_cash_receipts", cashReceipts
            .Select(receipt => EnsureId(receipt.Id, $"cash|{receipt.Number}")));
        InsertKeepIds(connection, transaction, "tmp_app_sales_keep_operation_log", operationLog
            .Take(500)
            .Select(entry => EnsureId(entry.Id, $"sales-log|{entry.EntityNumber}|{entry.Action}|{entry.LoggedAt:O}")));
    }

    private static void DeleteMissingSalesRows(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_sales_operation_log target
            LEFT JOIN tmp_app_sales_keep_operation_log keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_sales_cash_receipts target
            LEFT JOIN tmp_app_sales_keep_cash_receipts keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_sales_document_lines target
            LEFT JOIN tmp_app_sales_keep_document_lines keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_sales_documents target
            LEFT JOIN tmp_app_sales_keep_documents keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_sales_customer_contacts target
            LEFT JOIN tmp_app_sales_keep_customer_contacts keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_sales_customers target
            LEFT JOIN tmp_app_sales_keep_customers keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
    }

    private static List<SalesCustomerRecord> LoadSalesCustomers(MySqlConnection connection)
    {
        var customers = new List<SalesCustomerRecord>();
        using (var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                code,
                name,
                counterparty_type,
                is_buyer,
                is_supplier,
                is_other,
                contract_number,
                currency_code,
                manager_name,
                status_text,
                phone,
                email,
                inn,
                kpp,
                ogrn,
                legal_address,
                actual_address,
                region,
                city,
                source_text,
                responsible_name,
                tags,
                bank_account,
                notes
            FROM app_sales_customers
            ORDER BY name, code;
            """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                customers.Add(new SalesCustomerRecord
                {
                    Id = ReadGuid(reader, "id"),
                    Code = ReadString(reader, "code"),
                    Name = ReadString(reader, "name"),
                    CounterpartyType = ReadString(reader, "counterparty_type"),
                    IsBuyer = ReadBoolean(reader, "is_buyer"),
                    IsSupplier = ReadBoolean(reader, "is_supplier"),
                    IsOther = ReadBoolean(reader, "is_other"),
                    ContractNumber = ReadString(reader, "contract_number"),
                    CurrencyCode = ReadString(reader, "currency_code"),
                    Manager = ReadString(reader, "manager_name"),
                    Status = ReadString(reader, "status_text"),
                    Phone = ReadString(reader, "phone"),
                    Email = ReadString(reader, "email"),
                    Inn = ReadString(reader, "inn"),
                    Kpp = ReadString(reader, "kpp"),
                    Ogrn = ReadString(reader, "ogrn"),
                    LegalAddress = ReadString(reader, "legal_address"),
                    ActualAddress = ReadString(reader, "actual_address"),
                    Region = ReadString(reader, "region"),
                    City = ReadString(reader, "city"),
                    Source = ReadString(reader, "source_text"),
                    Responsible = ReadString(reader, "responsible_name"),
                    Tags = ReadString(reader, "tags"),
                    BankAccount = ReadString(reader, "bank_account"),
                    Notes = ReadString(reader, "notes"),
                    Contacts = new BindingList<SalesCustomerContactRecord>()
                });
            }
        }

        var byId = customers.ToDictionary(item => item.Id);
        using (var command = CreateMySqlCommand(connection, null, """
            SELECT
                customer_id,
                contact_name,
                contact_role,
                phone,
                email,
                comment_text
            FROM app_sales_customer_contacts
            ORDER BY customer_id, line_no;
            """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var customerId = ReadGuid(reader, "customer_id");
                if (!byId.TryGetValue(customerId, out var customer))
                {
                    continue;
                }

                customer.Contacts.Add(new SalesCustomerContactRecord
                {
                    Name = ReadString(reader, "contact_name"),
                    Role = ReadString(reader, "contact_role"),
                    Phone = ReadString(reader, "phone"),
                    Email = ReadString(reader, "email"),
                    Comment = ReadString(reader, "comment_text")
                });
            }
        }

        return customers;
    }

    private static SalesDocumentsSnapshot LoadSalesDocuments(MySqlConnection connection)
    {
        var lineLookup = LoadSalesDocumentLines(connection);
        var orders = new List<SalesOrderRecord>();
        var invoices = new List<SalesInvoiceRecord>();
        var shipments = new List<SalesShipmentRecord>();
        var returns = new List<SalesReturnRecord>();

        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                document_kind,
                number,
                document_date,
                due_date,
                sales_order_id,
                sales_order_number,
                customer_id,
                customer_code,
                customer_name,
                contract_number,
                currency_code,
                warehouse_name,
                status_text,
                carrier_name,
                manager_name,
                reason_text,
                comment_text,
                manual_discount_percent,
                manual_discount_amount
            FROM app_sales_documents
            ORDER BY document_date DESC, number DESC;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = ReadGuid(reader, "id");
            var kind = ReadString(reader, "document_kind");
            var documentDate = ReadDateTime(reader, "document_date");
            var lines = ToBindingList(lineLookup.GetValueOrDefault(id, []));

            switch (kind)
            {
                case "order":
                    orders.Add(new SalesOrderRecord
                    {
                        Id = id,
                        Number = ReadString(reader, "number"),
                        OrderDate = documentDate,
                        CustomerId = ReadGuid(reader, "customer_id"),
                        CustomerCode = ReadString(reader, "customer_code"),
                        CustomerName = ReadString(reader, "customer_name"),
                        ContractNumber = ReadString(reader, "contract_number"),
                        CurrencyCode = ReadString(reader, "currency_code"),
                        Warehouse = ReadString(reader, "warehouse_name"),
                        Status = ReadString(reader, "status_text"),
                        Manager = ReadString(reader, "manager_name"),
                        Comment = ReadString(reader, "comment_text"),
                        ManualDiscountPercent = ReadDecimal(reader, "manual_discount_percent"),
                        ManualDiscountAmount = ReadDecimal(reader, "manual_discount_amount"),
                        Lines = lines
                    });
                    break;
                case "invoice":
                    invoices.Add(new SalesInvoiceRecord
                    {
                        Id = id,
                        Number = ReadString(reader, "number"),
                        InvoiceDate = documentDate,
                        DueDate = ReadNullableDateTime(reader, "due_date") ?? documentDate,
                        SalesOrderId = ReadGuid(reader, "sales_order_id"),
                        SalesOrderNumber = ReadString(reader, "sales_order_number"),
                        CustomerId = ReadGuid(reader, "customer_id"),
                        CustomerCode = ReadString(reader, "customer_code"),
                        CustomerName = ReadString(reader, "customer_name"),
                        ContractNumber = ReadString(reader, "contract_number"),
                        CurrencyCode = ReadString(reader, "currency_code"),
                        Status = ReadString(reader, "status_text"),
                        Manager = ReadString(reader, "manager_name"),
                        Comment = ReadString(reader, "comment_text"),
                        ManualDiscountPercent = ReadDecimal(reader, "manual_discount_percent"),
                        ManualDiscountAmount = ReadDecimal(reader, "manual_discount_amount"),
                        Lines = lines
                    });
                    break;
                case "shipment":
                    shipments.Add(new SalesShipmentRecord
                    {
                        Id = id,
                        Number = ReadString(reader, "number"),
                        ShipmentDate = documentDate,
                        SalesOrderId = ReadGuid(reader, "sales_order_id"),
                        SalesOrderNumber = ReadString(reader, "sales_order_number"),
                        CustomerId = ReadGuid(reader, "customer_id"),
                        CustomerCode = ReadString(reader, "customer_code"),
                        CustomerName = ReadString(reader, "customer_name"),
                        ContractNumber = ReadString(reader, "contract_number"),
                        CurrencyCode = ReadString(reader, "currency_code"),
                        Warehouse = ReadString(reader, "warehouse_name"),
                        Status = ReadString(reader, "status_text"),
                        Carrier = ReadString(reader, "carrier_name"),
                        Manager = ReadString(reader, "manager_name"),
                        Comment = ReadString(reader, "comment_text"),
                        ManualDiscountPercent = ReadDecimal(reader, "manual_discount_percent"),
                        ManualDiscountAmount = ReadDecimal(reader, "manual_discount_amount"),
                        Lines = lines
                    });
                    break;
                case "return":
                    returns.Add(new SalesReturnRecord
                    {
                        Id = id,
                        Number = ReadString(reader, "number"),
                        ReturnDate = documentDate,
                        SalesOrderId = ReadGuid(reader, "sales_order_id"),
                        SalesOrderNumber = ReadString(reader, "sales_order_number"),
                        CustomerId = ReadGuid(reader, "customer_id"),
                        CustomerCode = ReadString(reader, "customer_code"),
                        CustomerName = ReadString(reader, "customer_name"),
                        ContractNumber = ReadString(reader, "contract_number"),
                        CurrencyCode = ReadString(reader, "currency_code"),
                        Warehouse = ReadString(reader, "warehouse_name"),
                        Status = ReadString(reader, "status_text"),
                        Manager = ReadString(reader, "manager_name"),
                        Reason = ReadString(reader, "reason_text"),
                        Comment = ReadString(reader, "comment_text"),
                        ManualDiscountPercent = ReadDecimal(reader, "manual_discount_percent"),
                        ManualDiscountAmount = ReadDecimal(reader, "manual_discount_amount"),
                        Lines = lines
                    });
                    break;
            }
        }

        return new SalesDocumentsSnapshot(orders, invoices, shipments, returns);
    }

    private static Dictionary<Guid, List<SalesOrderLineRecord>> LoadSalesDocumentLines(MySqlConnection connection)
    {
        var lineLookup = new Dictionary<Guid, List<SalesOrderLineRecord>>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                document_id,
                line_id,
                item_code,
                item_name,
                unit_name,
                quantity,
                price
            FROM app_sales_document_lines
            ORDER BY document_id, line_no;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var documentId = ReadGuid(reader, "document_id");
            if (!lineLookup.TryGetValue(documentId, out var lines))
            {
                lines = [];
                lineLookup[documentId] = lines;
            }

            lines.Add(new SalesOrderLineRecord
            {
                Id = ReadGuid(reader, "line_id"),
                ItemCode = ReadString(reader, "item_code"),
                ItemName = ReadString(reader, "item_name"),
                Unit = ReadString(reader, "unit_name"),
                Quantity = ReadDecimal(reader, "quantity"),
                Price = ReadDecimal(reader, "price")
            });
        }

        return lineLookup;
    }

    private static List<SalesCashReceiptRecord> LoadSalesCashReceipts(MySqlConnection connection)
    {
        var receipts = new List<SalesCashReceiptRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                number,
                receipt_date,
                sales_order_id,
                sales_order_number,
                customer_id,
                customer_code,
                customer_name,
                contract_number,
                currency_code,
                amount,
                status_text,
                cash_box,
                manager_name,
                comment_text
            FROM app_sales_cash_receipts
            ORDER BY receipt_date DESC, number DESC;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            receipts.Add(new SalesCashReceiptRecord
            {
                Id = ReadGuid(reader, "id"),
                Number = ReadString(reader, "number"),
                ReceiptDate = ReadDateTime(reader, "receipt_date"),
                SalesOrderId = ReadGuid(reader, "sales_order_id"),
                SalesOrderNumber = ReadString(reader, "sales_order_number"),
                CustomerId = ReadGuid(reader, "customer_id"),
                CustomerCode = ReadString(reader, "customer_code"),
                CustomerName = ReadString(reader, "customer_name"),
                ContractNumber = ReadString(reader, "contract_number"),
                CurrencyCode = ReadString(reader, "currency_code"),
                Amount = ReadDecimal(reader, "amount"),
                Status = ReadString(reader, "status_text"),
                CashBox = ReadString(reader, "cash_box"),
                Manager = ReadString(reader, "manager_name"),
                Comment = ReadString(reader, "comment_text")
            });
        }

        return receipts;
    }

    private static List<SalesOperationLogEntry> LoadSalesOperationLog(MySqlConnection connection)
    {
        var log = new List<SalesOperationLogEntry>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                logged_at,
                actor_user_name,
                entity_type,
                entity_id,
                entity_number,
                action_text,
                result_text,
                message_text
            FROM app_sales_operation_log
            ORDER BY logged_at DESC, id
            LIMIT 500;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            log.Add(new SalesOperationLogEntry
            {
                Id = ReadGuid(reader, "id"),
                LoggedAt = ReadDateTime(reader, "logged_at"),
                Actor = ReadString(reader, "actor_user_name"),
                EntityType = ReadString(reader, "entity_type"),
                EntityId = ReadGuid(reader, "entity_id"),
                EntityNumber = ReadString(reader, "entity_number"),
                Action = ReadString(reader, "action_text"),
                Result = ReadString(reader, "result_text"),
                Message = ReadString(reader, "message_text")
            });
        }

        return log;
    }

    private static void InsertSalesCustomers(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<SalesCustomerRecord> customers)
    {
        using var customerCommand = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_sales_customers (
                id,
                code,
                name,
                counterparty_type,
                is_buyer,
                is_supplier,
                is_other,
                contract_number,
                currency_code,
                manager_name,
                status_text,
                phone,
                email,
                inn,
                kpp,
                ogrn,
                legal_address,
                actual_address,
                region,
                city,
                source_text,
                responsible_name,
                tags,
                bank_account,
                notes
            )
            VALUES (
                @id,
                @code,
                @name,
                @counterparty_type,
                @is_buyer,
                @is_supplier,
                @is_other,
                @contract_number,
                @currency_code,
                @manager_name,
                @status_text,
                @phone,
                @email,
                @inn,
                @kpp,
                @ogrn,
                @legal_address,
                @actual_address,
                @region,
                @city,
                @source_text,
                @responsible_name,
                @tags,
                @bank_account,
                @notes
            )
            ON DUPLICATE KEY UPDATE
                code = VALUES(code),
                name = VALUES(name),
                counterparty_type = VALUES(counterparty_type),
                is_buyer = VALUES(is_buyer),
                is_supplier = VALUES(is_supplier),
                is_other = VALUES(is_other),
                contract_number = VALUES(contract_number),
                currency_code = VALUES(currency_code),
                manager_name = VALUES(manager_name),
                status_text = VALUES(status_text),
                phone = VALUES(phone),
                email = VALUES(email),
                inn = VALUES(inn),
                kpp = VALUES(kpp),
                ogrn = VALUES(ogrn),
                legal_address = VALUES(legal_address),
                actual_address = VALUES(actual_address),
                region = VALUES(region),
                city = VALUES(city),
                source_text = VALUES(source_text),
                responsible_name = VALUES(responsible_name),
                tags = VALUES(tags),
                bank_account = VALUES(bank_account),
                notes = VALUES(notes);
            """);
        AddSalesCustomerParameters(customerCommand);

        using var contactCommand = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_sales_customer_contacts (
                id,
                customer_id,
                line_no,
                contact_name,
                contact_role,
                phone,
                email,
                comment_text
            )
            VALUES (
                @id,
                @customer_id,
                @line_no,
                @contact_name,
                @contact_role,
                @phone,
                @email,
                @comment_text
            )
            ON DUPLICATE KEY UPDATE
                customer_id = VALUES(customer_id),
                line_no = VALUES(line_no),
                contact_name = VALUES(contact_name),
                contact_role = VALUES(contact_role),
                phone = VALUES(phone),
                email = VALUES(email),
                comment_text = VALUES(comment_text);
            """);
        AddContactParameters(contactCommand);

        foreach (var customer in customers)
        {
            var customerId = EnsureId(customer.Id, $"sales-customer|{customer.Code}|{customer.Name}");
            SetParameter(customerCommand, "@id", customerId.ToString());
            SetParameter(customerCommand, "@code", customer.Code);
            SetParameter(customerCommand, "@name", customer.Name);
            SetParameter(customerCommand, "@counterparty_type", customer.CounterpartyType);
            SetParameter(customerCommand, "@is_buyer", customer.IsBuyer ? 1 : 0);
            SetParameter(customerCommand, "@is_supplier", customer.IsSupplier ? 1 : 0);
            SetParameter(customerCommand, "@is_other", customer.IsOther ? 1 : 0);
            SetParameter(customerCommand, "@contract_number", customer.ContractNumber);
            SetParameter(customerCommand, "@currency_code", customer.CurrencyCode);
            SetParameter(customerCommand, "@manager_name", customer.Manager);
            SetParameter(customerCommand, "@status_text", customer.Status);
            SetParameter(customerCommand, "@phone", customer.Phone);
            SetParameter(customerCommand, "@email", customer.Email);
            SetParameter(customerCommand, "@inn", customer.Inn);
            SetParameter(customerCommand, "@kpp", customer.Kpp);
            SetParameter(customerCommand, "@ogrn", customer.Ogrn);
            SetParameter(customerCommand, "@legal_address", customer.LegalAddress);
            SetParameter(customerCommand, "@actual_address", customer.ActualAddress);
            SetParameter(customerCommand, "@region", customer.Region);
            SetParameter(customerCommand, "@city", customer.City);
            SetParameter(customerCommand, "@source_text", customer.Source);
            SetParameter(customerCommand, "@responsible_name", customer.Responsible);
            SetParameter(customerCommand, "@tags", customer.Tags);
            SetParameter(customerCommand, "@bank_account", customer.BankAccount);
            SetParameter(customerCommand, "@notes", customer.Notes);
            customerCommand.ExecuteNonQuery();

            var lineNo = 1;
            foreach (var contact in customer.Contacts ?? new BindingList<SalesCustomerContactRecord>())
            {
                SetParameter(contactCommand, "@id", CreateDeterministicGuid($"{customerId:N}|contact|{lineNo}").ToString());
                SetParameter(contactCommand, "@customer_id", customerId.ToString());
                SetParameter(contactCommand, "@line_no", lineNo++);
                SetParameter(contactCommand, "@contact_name", contact.Name);
                SetParameter(contactCommand, "@contact_role", contact.Role);
                SetParameter(contactCommand, "@phone", contact.Phone);
                SetParameter(contactCommand, "@email", contact.Email);
                SetParameter(contactCommand, "@comment_text", contact.Comment);
                contactCommand.ExecuteNonQuery();
            }
        }
    }

    private static void InsertSalesDocuments(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<SalesOrderRecord> orders,
        IEnumerable<SalesInvoiceRecord> invoices,
        IEnumerable<SalesShipmentRecord> shipments,
        IEnumerable<SalesReturnRecord> returns)
    {
        using var documentCommand = CreateSalesDocumentCommand(connection, transaction);
        using var lineCommand = CreateSalesDocumentLineCommand(connection, transaction);

        foreach (var order in orders)
        {
            ExecuteDocumentInsert(
                documentCommand,
                lineCommand,
                "order",
                order.Id,
                order.Number,
                order.OrderDate,
                null,
                Guid.Empty,
                string.Empty,
                order.CustomerId,
                order.CustomerCode,
                order.CustomerName,
                order.ContractNumber,
                order.CurrencyCode,
                order.Warehouse,
                order.Status,
                string.Empty,
                order.Manager,
                string.Empty,
                order.Comment,
                order.ManualDiscountPercent,
                order.ManualDiscountAmount,
                order.Lines ?? []);
        }

        foreach (var invoice in invoices)
        {
            ExecuteDocumentInsert(
                documentCommand,
                lineCommand,
                "invoice",
                invoice.Id,
                invoice.Number,
                invoice.InvoiceDate,
                invoice.DueDate,
                invoice.SalesOrderId,
                invoice.SalesOrderNumber,
                invoice.CustomerId,
                invoice.CustomerCode,
                invoice.CustomerName,
                invoice.ContractNumber,
                invoice.CurrencyCode,
                string.Empty,
                invoice.Status,
                string.Empty,
                invoice.Manager,
                string.Empty,
                invoice.Comment,
                invoice.ManualDiscountPercent,
                invoice.ManualDiscountAmount,
                invoice.Lines ?? []);
        }

        foreach (var shipment in shipments)
        {
            ExecuteDocumentInsert(
                documentCommand,
                lineCommand,
                "shipment",
                shipment.Id,
                shipment.Number,
                shipment.ShipmentDate,
                null,
                shipment.SalesOrderId,
                shipment.SalesOrderNumber,
                shipment.CustomerId,
                shipment.CustomerCode,
                shipment.CustomerName,
                shipment.ContractNumber,
                shipment.CurrencyCode,
                shipment.Warehouse,
                shipment.Status,
                shipment.Carrier,
                shipment.Manager,
                string.Empty,
                shipment.Comment,
                shipment.ManualDiscountPercent,
                shipment.ManualDiscountAmount,
                shipment.Lines ?? []);
        }

        foreach (var returnDocument in returns)
        {
            ExecuteDocumentInsert(
                documentCommand,
                lineCommand,
                "return",
                returnDocument.Id,
                returnDocument.Number,
                returnDocument.ReturnDate,
                null,
                returnDocument.SalesOrderId,
                returnDocument.SalesOrderNumber,
                returnDocument.CustomerId,
                returnDocument.CustomerCode,
                returnDocument.CustomerName,
                returnDocument.ContractNumber,
                returnDocument.CurrencyCode,
                returnDocument.Warehouse,
                returnDocument.Status,
                string.Empty,
                returnDocument.Manager,
                returnDocument.Reason,
                returnDocument.Comment,
                returnDocument.ManualDiscountPercent,
                returnDocument.ManualDiscountAmount,
                returnDocument.Lines ?? []);
        }
    }

    private static void InsertSalesCashReceipts(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<SalesCashReceiptRecord> receipts)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_sales_cash_receipts (
                id,
                number,
                receipt_date,
                sales_order_id,
                sales_order_number,
                customer_id,
                customer_code,
                customer_name,
                contract_number,
                currency_code,
                amount,
                status_text,
                cash_box,
                manager_name,
                comment_text
            )
            VALUES (
                @id,
                @number,
                @receipt_date,
                @sales_order_id,
                @sales_order_number,
                @customer_id,
                @customer_code,
                @customer_name,
                @contract_number,
                @currency_code,
                @amount,
                @status_text,
                @cash_box,
                @manager_name,
                @comment_text
            )
            ON DUPLICATE KEY UPDATE
                number = VALUES(number),
                receipt_date = VALUES(receipt_date),
                sales_order_id = VALUES(sales_order_id),
                sales_order_number = VALUES(sales_order_number),
                customer_id = VALUES(customer_id),
                customer_code = VALUES(customer_code),
                customer_name = VALUES(customer_name),
                contract_number = VALUES(contract_number),
                currency_code = VALUES(currency_code),
                amount = VALUES(amount),
                status_text = VALUES(status_text),
                cash_box = VALUES(cash_box),
                manager_name = VALUES(manager_name),
                comment_text = VALUES(comment_text);
            """);
        AddReceiptParameters(command);

        foreach (var receipt in receipts)
        {
            SetParameter(command, "@id", EnsureId(receipt.Id, $"cash|{receipt.Number}").ToString());
            SetParameter(command, "@number", receipt.Number);
            SetParameter(command, "@receipt_date", receipt.ReceiptDate);
            SetParameter(command, "@sales_order_id", ToNullableId(receipt.SalesOrderId));
            SetParameter(command, "@sales_order_number", receipt.SalesOrderNumber);
            SetParameter(command, "@customer_id", ToNullableId(receipt.CustomerId));
            SetParameter(command, "@customer_code", receipt.CustomerCode);
            SetParameter(command, "@customer_name", receipt.CustomerName);
            SetParameter(command, "@contract_number", receipt.ContractNumber);
            SetParameter(command, "@currency_code", receipt.CurrencyCode);
            SetParameter(command, "@amount", receipt.Amount);
            SetParameter(command, "@status_text", receipt.Status);
            SetParameter(command, "@cash_box", receipt.CashBox);
            SetParameter(command, "@manager_name", receipt.Manager);
            SetParameter(command, "@comment_text", receipt.Comment);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertSalesOperationLog(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<SalesOperationLogEntry> operationLog)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_sales_operation_log (
                id,
                logged_at,
                actor_user_name,
                entity_type,
                entity_id,
                entity_number,
                action_text,
                result_text,
                message_text
            )
            VALUES (
                @id,
                @logged_at,
                @actor_user_name,
                @entity_type,
                @entity_id,
                @entity_number,
                @action_text,
                @result_text,
                @message_text
            )
            ON DUPLICATE KEY UPDATE
                logged_at = VALUES(logged_at),
                actor_user_name = VALUES(actor_user_name),
                entity_type = VALUES(entity_type),
                entity_id = VALUES(entity_id),
                entity_number = VALUES(entity_number),
                action_text = VALUES(action_text),
                result_text = VALUES(result_text),
                message_text = VALUES(message_text);
            """);
        AddOperationLogParameters(command);

        foreach (var entry in operationLog.Take(500))
        {
            SetParameter(command, "@id", EnsureId(entry.Id, $"sales-log|{entry.EntityNumber}|{entry.Action}|{entry.LoggedAt:O}").ToString());
            SetParameter(command, "@logged_at", entry.LoggedAt);
            SetParameter(command, "@actor_user_name", entry.Actor);
            SetParameter(command, "@entity_type", entry.EntityType);
            SetParameter(command, "@entity_id", ToNullableId(entry.EntityId));
            SetParameter(command, "@entity_number", entry.EntityNumber);
            SetParameter(command, "@action_text", entry.Action);
            SetParameter(command, "@result_text", entry.Result);
            SetParameter(command, "@message_text", entry.Message);
            command.ExecuteNonQuery();
        }
    }

    private static MySqlCommand CreateSalesDocumentCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_sales_documents (
                id,
                document_kind,
                number,
                document_date,
                due_date,
                sales_order_id,
                sales_order_number,
                customer_id,
                customer_code,
                customer_name,
                contract_number,
                currency_code,
                warehouse_name,
                status_text,
                carrier_name,
                manager_name,
                reason_text,
                comment_text,
                manual_discount_percent,
                manual_discount_amount
            )
            VALUES (
                @id,
                @document_kind,
                @number,
                @document_date,
                @due_date,
                @sales_order_id,
                @sales_order_number,
                @customer_id,
                @customer_code,
                @customer_name,
                @contract_number,
                @currency_code,
                @warehouse_name,
                @status_text,
                @carrier_name,
                @manager_name,
                @reason_text,
                @comment_text,
                @manual_discount_percent,
                @manual_discount_amount
            )
            ON DUPLICATE KEY UPDATE
                document_kind = VALUES(document_kind),
                number = VALUES(number),
                document_date = VALUES(document_date),
                due_date = VALUES(due_date),
                sales_order_id = VALUES(sales_order_id),
                sales_order_number = VALUES(sales_order_number),
                customer_id = VALUES(customer_id),
                customer_code = VALUES(customer_code),
                customer_name = VALUES(customer_name),
                contract_number = VALUES(contract_number),
                currency_code = VALUES(currency_code),
                warehouse_name = VALUES(warehouse_name),
                status_text = VALUES(status_text),
                carrier_name = VALUES(carrier_name),
                manager_name = VALUES(manager_name),
                reason_text = VALUES(reason_text),
                comment_text = VALUES(comment_text),
                manual_discount_percent = VALUES(manual_discount_percent),
                manual_discount_amount = VALUES(manual_discount_amount);
            """);
        foreach (var name in new[]
                 {
                     "@id",
                     "@document_kind",
                     "@number",
                     "@document_date",
                     "@due_date",
                     "@sales_order_id",
                     "@sales_order_number",
                     "@customer_id",
                     "@customer_code",
                     "@customer_name",
                     "@contract_number",
                     "@currency_code",
                     "@warehouse_name",
                     "@status_text",
                     "@carrier_name",
                     "@manager_name",
                     "@reason_text",
                     "@comment_text",
                     "@manual_discount_percent",
                     "@manual_discount_amount"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static MySqlCommand CreateSalesDocumentLineCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_sales_document_lines (
                id,
                document_id,
                line_id,
                line_no,
                item_code,
                item_name,
                unit_name,
                quantity,
                price,
                amount
            )
            VALUES (
                @id,
                @document_id,
                @line_id,
                @line_no,
                @item_code,
                @item_name,
                @unit_name,
                @quantity,
                @price,
                @amount
            )
            ON DUPLICATE KEY UPDATE
                document_id = VALUES(document_id),
                line_id = VALUES(line_id),
                line_no = VALUES(line_no),
                item_code = VALUES(item_code),
                item_name = VALUES(item_name),
                unit_name = VALUES(unit_name),
                quantity = VALUES(quantity),
                price = VALUES(price),
                amount = VALUES(amount);
            """);
        foreach (var name in new[]
                 {
                     "@id",
                     "@document_id",
                     "@line_id",
                     "@line_no",
                     "@item_code",
                     "@item_name",
                     "@unit_name",
                     "@quantity",
                     "@price",
                     "@amount"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static void ExecuteDocumentInsert(
        MySqlCommand documentCommand,
        MySqlCommand lineCommand,
        string documentKind,
        Guid documentId,
        string number,
        DateTime documentDate,
        DateTime? dueDate,
        Guid salesOrderId,
        string salesOrderNumber,
        Guid customerId,
        string customerCode,
        string customerName,
        string contractNumber,
        string currencyCode,
        string warehouseName,
        string status,
        string carrier,
        string manager,
        string reason,
        string comment,
        decimal manualDiscountPercent,
        decimal manualDiscountAmount,
        IEnumerable<SalesOrderLineRecord> lines)
    {
        var ensuredDocumentId = EnsureId(documentId, $"{documentKind}|{number}");
        SetParameter(documentCommand, "@id", ensuredDocumentId.ToString());
        SetParameter(documentCommand, "@document_kind", documentKind);
        SetParameter(documentCommand, "@number", number);
        SetParameter(documentCommand, "@document_date", documentDate);
        SetParameter(documentCommand, "@due_date", dueDate);
        SetParameter(documentCommand, "@sales_order_id", ToNullableId(salesOrderId));
        SetParameter(documentCommand, "@sales_order_number", salesOrderNumber);
        SetParameter(documentCommand, "@customer_id", ToNullableId(customerId));
        SetParameter(documentCommand, "@customer_code", customerCode);
        SetParameter(documentCommand, "@customer_name", customerName);
        SetParameter(documentCommand, "@contract_number", contractNumber);
        SetParameter(documentCommand, "@currency_code", currencyCode);
        SetParameter(documentCommand, "@warehouse_name", warehouseName);
        SetParameter(documentCommand, "@status_text", status);
        SetParameter(documentCommand, "@carrier_name", carrier);
        SetParameter(documentCommand, "@manager_name", manager);
        SetParameter(documentCommand, "@reason_text", reason);
        SetParameter(documentCommand, "@comment_text", comment);
        SetParameter(documentCommand, "@manual_discount_percent", manualDiscountPercent);
        SetParameter(documentCommand, "@manual_discount_amount", manualDiscountAmount);
        documentCommand.ExecuteNonQuery();

        var lineNo = 1;
        foreach (var line in lines)
        {
            var lineId = EnsureId(line.Id, $"{ensuredDocumentId:N}|line|{lineNo}");
            SetParameter(lineCommand, "@id", CreateDeterministicGuid($"{ensuredDocumentId:N}|{lineId:N}|{lineNo}").ToString());
            SetParameter(lineCommand, "@document_id", ensuredDocumentId.ToString());
            SetParameter(lineCommand, "@line_id", lineId.ToString());
            SetParameter(lineCommand, "@line_no", lineNo++);
            SetParameter(lineCommand, "@item_code", line.ItemCode);
            SetParameter(lineCommand, "@item_name", line.ItemName);
            SetParameter(lineCommand, "@unit_name", line.Unit);
            SetParameter(lineCommand, "@quantity", line.Quantity);
            SetParameter(lineCommand, "@price", line.Price);
            SetParameter(lineCommand, "@amount", line.Amount);
            lineCommand.ExecuteNonQuery();
        }
    }

    private static void AddSalesCustomerParameters(MySqlCommand command)
    {
        foreach (var name in new[]
                 {
                     "@id",
                     "@code",
                     "@name",
                     "@counterparty_type",
                     "@is_buyer",
                     "@is_supplier",
                     "@is_other",
                     "@contract_number",
                     "@currency_code",
                     "@manager_name",
                     "@status_text",
                     "@phone",
                     "@email",
                     "@inn",
                     "@kpp",
                     "@ogrn",
                     "@legal_address",
                     "@actual_address",
                     "@region",
                     "@city",
                     "@source_text",
                     "@responsible_name",
                     "@tags",
                     "@bank_account",
                     "@notes"
                 })
        {
            AddParameter(command, name);
        }
    }

    private static void AddContactParameters(MySqlCommand command)
    {
        foreach (var name in new[] { "@id", "@customer_id", "@line_no", "@contact_name", "@contact_role", "@phone", "@email", "@comment_text" })
        {
            AddParameter(command, name);
        }
    }

    private static void AddReceiptParameters(MySqlCommand command)
    {
        foreach (var name in new[]
                 {
                     "@id",
                     "@number",
                     "@receipt_date",
                     "@sales_order_id",
                     "@sales_order_number",
                     "@customer_id",
                     "@customer_code",
                     "@customer_name",
                     "@contract_number",
                     "@currency_code",
                     "@amount",
                     "@status_text",
                     "@cash_box",
                     "@manager_name",
                     "@comment_text"
                 })
        {
            AddParameter(command, name);
        }
    }

    private static void AddOperationLogParameters(MySqlCommand command)
    {
        foreach (var name in new[]
                 {
                     "@id",
                     "@logged_at",
                     "@actor_user_name",
                     "@entity_type",
                     "@entity_id",
                     "@entity_number",
                     "@action_text",
                     "@result_text",
                     "@message_text"
                 })
        {
            AddParameter(command, name);
        }
    }

    private static IEnumerable<Guid> BuildContactIds(SalesCustomerRecord customer)
    {
        var customerId = EnsureId(customer.Id, $"sales-customer|{customer.Code}|{customer.Name}");
        var lineNo = 1;
        foreach (var _ in customer.Contacts ?? new BindingList<SalesCustomerContactRecord>())
        {
            yield return CreateDeterministicGuid($"{customerId:N}|contact|{lineNo++}");
        }
    }

    private static IEnumerable<Guid> EnumerateDocumentIds(
        IEnumerable<SalesOrderRecord> orders,
        IEnumerable<SalesInvoiceRecord> invoices,
        IEnumerable<SalesShipmentRecord> shipments,
        IEnumerable<SalesReturnRecord> returns)
    {
        foreach (var order in orders)
        {
            yield return EnsureId(order.Id, $"order|{order.Number}");
        }

        foreach (var invoice in invoices)
        {
            yield return EnsureId(invoice.Id, $"invoice|{invoice.Number}");
        }

        foreach (var shipment in shipments)
        {
            yield return EnsureId(shipment.Id, $"shipment|{shipment.Number}");
        }

        foreach (var returnDocument in returns)
        {
            yield return EnsureId(returnDocument.Id, $"return|{returnDocument.Number}");
        }
    }

    private static IEnumerable<Guid> EnumerateDocumentLineRowIds(
        IEnumerable<SalesOrderRecord> orders,
        IEnumerable<SalesInvoiceRecord> invoices,
        IEnumerable<SalesShipmentRecord> shipments,
        IEnumerable<SalesReturnRecord> returns)
    {
        foreach (var order in orders)
        {
            foreach (var id in EnumerateLineRowIds("order", order.Id, order.Number, order.Lines ?? []))
            {
                yield return id;
            }
        }

        foreach (var invoice in invoices)
        {
            foreach (var id in EnumerateLineRowIds("invoice", invoice.Id, invoice.Number, invoice.Lines ?? []))
            {
                yield return id;
            }
        }

        foreach (var shipment in shipments)
        {
            foreach (var id in EnumerateLineRowIds("shipment", shipment.Id, shipment.Number, shipment.Lines ?? []))
            {
                yield return id;
            }
        }

        foreach (var returnDocument in returns)
        {
            foreach (var id in EnumerateLineRowIds("return", returnDocument.Id, returnDocument.Number, returnDocument.Lines ?? []))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<Guid> EnumerateLineRowIds(
        string documentKind,
        Guid documentId,
        string number,
        IEnumerable<SalesOrderLineRecord> lines)
    {
        var ensuredDocumentId = EnsureId(documentId, $"{documentKind}|{number}");
        var lineNo = 1;
        foreach (var line in lines)
        {
            var lineId = EnsureId(line.Id, $"{ensuredDocumentId:N}|line|{lineNo}");
            yield return CreateDeterministicGuid($"{ensuredDocumentId:N}|{lineId:N}|{lineNo}");
            lineNo++;
        }
    }

    private static void InsertKeepIds(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string tableName,
        IEnumerable<Guid> ids)
    {
        using var command = CreateMySqlCommand(connection, transaction, $"INSERT IGNORE INTO {tableName} (id) VALUES (@id);");
        AddParameter(command, "@id");
        foreach (var id in ids.Where(id => id != Guid.Empty).Distinct())
        {
            SetParameter(command, "@id", id.ToString());
            command.ExecuteNonQuery();
        }
    }

    private static void ExecuteMySqlNonQuery(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql)
    {
        using var command = CreateMySqlCommand(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    private static MySqlCommand CreateMySqlCommand(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = MysqlSalesCommandTimeoutSeconds;
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    private static void AddParameter(MySqlCommand command, string name, object? value = null)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static void SetParameter(MySqlCommand command, string name, object? value)
    {
        command.Parameters[name].Value = value ?? DBNull.Value;
    }

    private static BindingList<SalesOrderLineRecord> ToBindingList(IEnumerable<SalesOrderLineRecord> source)
    {
        return new BindingList<SalesOrderLineRecord>(source.Select(item => item.Clone()).ToList());
    }

    private static Guid EnsureId(Guid id, string fallbackSeed)
    {
        return id == Guid.Empty ? CreateDeterministicGuid(fallbackSeed) : id;
    }

    private static string? ToNullableId(Guid id)
    {
        return id == Guid.Empty ? null : id.ToString();
    }

    private static string ReadString(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static bool ReadBoolean(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) != 0;
    }

    private static decimal ReadDecimal(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0m : reader.GetDecimal(ordinal);
    }

    private static DateTime ReadDateTime(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? DateTime.Today : reader.GetDateTime(ordinal);
    }

    private static DateTime? ReadNullableDateTime(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static Guid ReadGuid(MySqlDataReader reader, string name)
    {
        var raw = ReadString(reader, name);
        return Guid.TryParse(raw, out var parsed) ? parsed : Guid.Empty;
    }

    private sealed record SalesDocumentsSnapshot(
        List<SalesOrderRecord> Orders,
        List<SalesInvoiceRecord> Invoices,
        List<SalesShipmentRecord> Shipments,
        List<SalesReturnRecord> Returns);

    private const string AppSalesSchemaSql = """
        CREATE TABLE IF NOT EXISTS app_module_states (
            module_code VARCHAR(64) NOT NULL,
            payload_hash CHAR(64) NOT NULL,
            version_no INT UNSIGNED NOT NULL DEFAULT 1,
            updated_by VARCHAR(128) NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_module_states PRIMARY KEY (module_code)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_sales_customers (
            id CHAR(36) NOT NULL,
            code VARCHAR(64) NOT NULL,
            name VARCHAR(512) NOT NULL,
            counterparty_type VARCHAR(128) NOT NULL,
            is_buyer TINYINT(1) NOT NULL DEFAULT 1,
            is_supplier TINYINT(1) NOT NULL DEFAULT 0,
            is_other TINYINT(1) NOT NULL DEFAULT 0,
            contract_number VARCHAR(128) NULL,
            currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',
            manager_name VARCHAR(256) NULL,
            status_text VARCHAR(128) NULL,
            phone VARCHAR(128) NULL,
            email VARCHAR(256) NULL,
            inn VARCHAR(64) NULL,
            kpp VARCHAR(64) NULL,
            ogrn VARCHAR(64) NULL,
            legal_address TEXT NULL,
            actual_address TEXT NULL,
            region VARCHAR(256) NULL,
            city VARCHAR(256) NULL,
            source_text VARCHAR(256) NULL,
            responsible_name VARCHAR(256) NULL,
            tags VARCHAR(512) NULL,
            bank_account VARCHAR(128) NULL,
            notes TEXT NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_sales_customers PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_sales_customer_contacts (
            id CHAR(36) NOT NULL,
            customer_id CHAR(36) NOT NULL,
            line_no INT UNSIGNED NOT NULL,
            contact_name VARCHAR(256) NULL,
            contact_role VARCHAR(128) NULL,
            phone VARCHAR(128) NULL,
            email VARCHAR(256) NULL,
            comment_text TEXT NULL,
            CONSTRAINT pk_app_sales_customer_contacts PRIMARY KEY (id),
            CONSTRAINT uq_app_sales_customer_contacts_line UNIQUE (customer_id, line_no),
            CONSTRAINT fk_app_sales_customer_contacts_customer FOREIGN KEY (customer_id) REFERENCES app_sales_customers (id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_sales_documents (
            id CHAR(36) NOT NULL,
            document_kind VARCHAR(32) NOT NULL,
            number VARCHAR(128) NOT NULL,
            document_date DATETIME(6) NOT NULL,
            due_date DATETIME(6) NULL,
            sales_order_id CHAR(36) NULL,
            sales_order_number VARCHAR(128) NULL,
            customer_id CHAR(36) NULL,
            customer_code VARCHAR(64) NULL,
            customer_name VARCHAR(512) NULL,
            contract_number VARCHAR(128) NULL,
            currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',
            warehouse_name VARCHAR(256) NULL,
            status_text VARCHAR(128) NULL,
            carrier_name VARCHAR(256) NULL,
            manager_name VARCHAR(256) NULL,
            reason_text VARCHAR(256) NULL,
            comment_text TEXT NULL,
            manual_discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
            manual_discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_sales_documents PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_sales_document_lines (
            id CHAR(36) NOT NULL,
            document_id CHAR(36) NOT NULL,
            line_id CHAR(36) NOT NULL,
            line_no INT UNSIGNED NOT NULL,
            item_code VARCHAR(128) NULL,
            item_name VARCHAR(512) NULL,
            unit_name VARCHAR(64) NULL,
            quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
            price DECIMAL(18, 4) NOT NULL DEFAULT 0,
            amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
            CONSTRAINT pk_app_sales_document_lines PRIMARY KEY (id),
            CONSTRAINT uq_app_sales_document_lines_line UNIQUE (document_id, line_no),
            CONSTRAINT fk_app_sales_document_lines_document FOREIGN KEY (document_id) REFERENCES app_sales_documents (id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_sales_cash_receipts (
            id CHAR(36) NOT NULL,
            number VARCHAR(128) NOT NULL,
            receipt_date DATETIME(6) NOT NULL,
            sales_order_id CHAR(36) NULL,
            sales_order_number VARCHAR(128) NULL,
            customer_id CHAR(36) NULL,
            customer_code VARCHAR(64) NULL,
            customer_name VARCHAR(512) NULL,
            contract_number VARCHAR(128) NULL,
            currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',
            amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
            status_text VARCHAR(128) NULL,
            cash_box VARCHAR(256) NULL,
            manager_name VARCHAR(256) NULL,
            comment_text TEXT NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_sales_cash_receipts PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_sales_operation_log (
            id CHAR(36) NOT NULL,
            logged_at DATETIME(6) NOT NULL,
            actor_user_name VARCHAR(128) NOT NULL,
            entity_type VARCHAR(128) NOT NULL,
            entity_id CHAR(36) NULL,
            entity_number VARCHAR(128) NULL,
            action_text VARCHAR(256) NOT NULL,
            result_text VARCHAR(128) NOT NULL,
            message_text TEXT NULL,
            CONSTRAINT pk_app_sales_operation_log PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        """;
}
