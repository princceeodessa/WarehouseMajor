using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class SalesWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private readonly DesktopMySqlBackplaneService? _backplane;
    private readonly bool _serverModeEnabled;
    private DesktopModuleSnapshotMetadata? _remoteMetadata;

    public SalesWorkspaceStore(
        string storagePath,
        DesktopMySqlBackplaneService? backplane = null,
        bool serverModeEnabled = false)
    {
        StoragePath = storagePath;
        _backplane = backplane;
        _serverModeEnabled = serverModeEnabled;
    }

    public string StoragePath { get; }

    public bool IsServerModeEnabled => _serverModeEnabled && _backplane is not null;

    public static SalesWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        var storagePath = Path.Combine(root, "app_data", "sales-workspace.json");
        return new SalesWorkspaceStore(
            storagePath,
            DesktopMySqlBackplaneService.TryCreateDefault(),
            DesktopRemoteDatabaseSettings.IsRemoteDatabaseEnabled());
    }

    public SalesWorkspace LoadOrCreate(
        string currentOperator,
        bool includeOperationalSnapshot = true,
        IReadOnlyList<string>? importRoots = null)
    {
        var workspace = SalesWorkspace.Create(currentOperator);
        var shouldAttachImportSnapshot = importRoots is { Count: > 0 };
        if (shouldAttachImportSnapshot)
        {
            AttachImportSnapshot(workspace, importRoots);
        }
        else
        {
            workspace.AttachOneCImportSnapshot(null);
        }
        DesktopOperationalSnapshot? operationalSnapshot = null;
        if (includeOperationalSnapshot)
        {
            operationalSnapshot = AttachOperationalSnapshot(workspace);
            if (operationalSnapshot?.HasSalesData == true)
            {
                ApplyOperationalSnapshot(workspace, operationalSnapshot, currentOperator);
            }
        }
        else
        {
            workspace.AttachOperationalSnapshot(null);
        }

        _backplane?.TryEnsureUserProfile(currentOperator);
        SalesWorkspaceImportMerger.Merge(workspace);
        var backplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<SalesWorkspaceSnapshot>("sales");
        if (backplaneRecord is not null)
        {
            _remoteMetadata = backplaneRecord.Metadata;
            ApplySnapshotToWorkspace(workspace, backplaneRecord.Snapshot, operationalSnapshot, importRoots);
            return RepairAndReturn(workspace, currentOperator);
        }

        try
        {
            var json = File.ReadAllText(StoragePath, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<SalesWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return RepairAndReturn(workspace, currentOperator);
            }

            if (operationalSnapshot?.HasSalesData == true)
            {
                MergeSnapshotIntoOperationalWorkspace(workspace, snapshot);
            }
            else
            {
                ApplySnapshot(workspace, snapshot);
                if (importRoots is { Count: > 0 })
                {
                    AttachImportSnapshot(workspace, importRoots);
                    SalesWorkspaceImportMerger.Merge(workspace);
                }
                else
                {
                    workspace.AttachOneCImportSnapshot(null);
                }
            }

            if (_backplane?.TrySaveModuleSnapshot("sales", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true)
            {
                _remoteMetadata = _backplane.TryLoadModuleSnapshotMetadata("sales");
            }

            return RepairAndReturn(workspace, currentOperator);
        }
        catch
        {
            return RepairAndReturn(workspace, currentOperator);
        }
    }

    public void Save(SalesWorkspace workspace)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Storage directory is not configured.");
        }

        Directory.CreateDirectory(directory);
        RepairWorkspace(workspace);
        var snapshot = SalesWorkspaceSnapshot.FromWorkspace(workspace);
        if (TrySaveToBackplane(snapshot, workspace.CurrentOperator))
        {
            return;
        }

        if (_serverModeEnabled)
        {
            throw new InvalidOperationException("Не удалось сохранить изменения в серверную БД. Локальное сохранение отключено для общего режима.");
        }

        var tempPath = $"{StoragePath}.tmp";
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, StoragePath, true);
    }

    public bool HasRemoteChanges()
    {
        if (_backplane is null || _remoteMetadata is null)
        {
            return false;
        }

        var latest = _backplane.TryLoadModuleSnapshotMetadata("sales");
        return latest is not null
               && (!string.Equals(latest.PayloadHash, _remoteMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase)
                   || latest.VersionNo != _remoteMetadata.VersionNo);
    }

    public bool TryRefreshFromBackplane(SalesWorkspace workspace)
    {
        var record = _backplane?.TryLoadModuleSnapshotRecord<SalesWorkspaceSnapshot>("sales");
        if (record is null)
        {
            return false;
        }

        if (_remoteMetadata is not null
            && record.Metadata.VersionNo == _remoteMetadata.VersionNo
            && string.Equals(record.Metadata.PayloadHash, _remoteMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ApplySnapshotToWorkspace(workspace, record.Snapshot, workspace.OperationalSnapshot, importRoots: null);
        _remoteMetadata = record.Metadata;
        workspace.NotifyExternalChange();
        return true;
    }

    private SalesWorkspace RepairAndReturn(SalesWorkspace workspace, string currentOperator)
    {
        if (!RepairWorkspace(workspace))
        {
            return workspace;
        }

        var snapshot = SalesWorkspaceSnapshot.FromWorkspace(workspace);
        try
        {
            if (TrySaveToBackplane(snapshot, currentOperator))
            {
                return workspace;
            }

            if (!_serverModeEnabled)
            {
                WriteSnapshot(snapshot);
            }
        }
        catch
        {
        }

        return workspace;
    }

    private void WriteSnapshot(SalesWorkspaceSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var tempPath = $"{StoragePath}.tmp";
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, StoragePath, true);
    }

    private static bool RepairWorkspace(SalesWorkspace workspace)
    {
        var changed = false;
        changed |= RemoveEmptyOrphanOrders(workspace);
        changed |= RepairCustomerLinks(workspace);
        changed |= EnsureRelatedOrders(workspace);
        changed |= EnrichCatalogItemsFromDocuments(workspace);
        return changed;
    }

    private static bool RemoveEmptyOrphanOrders(SalesWorkspace workspace)
    {
        var emptyOrders = workspace.Orders
            .Where(order => order.CustomerId == Guid.Empty
                            && string.IsNullOrWhiteSpace(order.CustomerCode)
                            && string.IsNullOrWhiteSpace(order.CustomerName)
                            && order.Lines.Count == 0
                            && order.TotalAmount == 0m)
            .ToArray();

        foreach (var order in emptyOrders)
        {
            workspace.Orders.Remove(order);
        }

        return emptyOrders.Length > 0;
    }

    private static bool RepairCustomerLinks(SalesWorkspace workspace)
    {
        var customersById = workspace.Customers
            .Where(item => item.Id != Guid.Empty)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var customersByCode = BuildUniqueLookup(workspace.Customers, item => item.Code);
        var customersByName = BuildUniqueLookup(workspace.Customers, item => item.Name);
        var changed = false;

        foreach (var order in workspace.Orders)
        {
            if (TryResolveCustomer(order.CustomerId, order.CustomerCode, order.CustomerName, customersById, customersByCode, customersByName, out var customer))
            {
                changed |= FillOrderCustomer(order, customer);
            }
        }

        foreach (var invoice in workspace.Invoices)
        {
            if (TryResolveCustomer(invoice.CustomerId, invoice.CustomerCode, invoice.CustomerName, customersById, customersByCode, customersByName, out var customer))
            {
                changed |= FillInvoiceCustomer(invoice, customer);
            }
        }

        foreach (var shipment in workspace.Shipments)
        {
            if (TryResolveCustomer(shipment.CustomerId, shipment.CustomerCode, shipment.CustomerName, customersById, customersByCode, customersByName, out var customer))
            {
                changed |= FillShipmentCustomer(shipment, customer);
            }
        }

        foreach (var returnDocument in workspace.Returns)
        {
            if (TryResolveCustomer(returnDocument.CustomerId, returnDocument.CustomerCode, returnDocument.CustomerName, customersById, customersByCode, customersByName, out var customer))
            {
                changed |= FillReturnCustomer(returnDocument, customer);
            }
        }

        foreach (var receipt in workspace.CashReceipts)
        {
            if (TryResolveCustomer(receipt.CustomerId, receipt.CustomerCode, receipt.CustomerName, customersById, customersByCode, customersByName, out var customer))
            {
                changed |= FillReceiptCustomer(receipt, customer);
            }
        }

        return changed;
    }

    private static bool EnsureRelatedOrders(SalesWorkspace workspace)
    {
        var changed = false;
        var ordersById = workspace.Orders
            .Where(item => item.Id != Guid.Empty)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var ordersByNumber = workspace.Orders
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .GroupBy(item => item.Number.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var invoice in workspace.Invoices.Where(item => item.Lines.Count > 0 || item.TotalAmount > 0m))
        {
            if (TryResolveOrder(invoice.SalesOrderId, invoice.SalesOrderNumber, ordersById, ordersByNumber, out var order))
            {
                changed |= FillInvoiceOrder(invoice, order);
                continue;
            }

            var derivedOrder = CreateOrderFromInvoice(invoice, workspace, ordersByNumber);
            workspace.Orders.Add(derivedOrder);
            ordersById[derivedOrder.Id] = derivedOrder;
            ordersByNumber[derivedOrder.Number] = derivedOrder;
            changed |= FillInvoiceOrder(invoice, derivedOrder);
        }

        foreach (var shipment in workspace.Shipments.Where(item => item.Lines.Count > 0 || item.TotalAmount > 0m))
        {
            if (TryResolveOrder(shipment.SalesOrderId, shipment.SalesOrderNumber, ordersById, ordersByNumber, out var order))
            {
                changed |= FillShipmentOrder(shipment, order);
                continue;
            }

            var derivedOrder = CreateOrderFromShipment(shipment, workspace, ordersByNumber);
            workspace.Orders.Add(derivedOrder);
            ordersById[derivedOrder.Id] = derivedOrder;
            ordersByNumber[derivedOrder.Number] = derivedOrder;
            changed |= FillShipmentOrder(shipment, derivedOrder);
        }

        return changed;
    }

    private static bool EnrichCatalogItemsFromDocuments(SalesWorkspace workspace)
    {
        var items = workspace.CatalogItems.ToList();
        var codes = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .Select(item => item.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var item in GetBaselineCatalogItems())
        {
            if (codes.Contains(item.Code))
            {
                continue;
            }

            items.Add(item);
            codes.Add(item.Code);
            changed = true;
        }

        foreach (var line in EnumerateSalesLines(workspace))
        {
            if (string.IsNullOrWhiteSpace(line.ItemCode) || codes.Contains(line.ItemCode))
            {
                continue;
            }

            items.Add(new SalesCatalogItemOption(
                line.ItemCode,
                string.IsNullOrWhiteSpace(line.ItemName) ? line.ItemCode : line.ItemName,
                string.IsNullOrWhiteSpace(line.Unit) ? "шт" : line.Unit,
                line.Price > 0m ? line.Price : 0.01m));
            codes.Add(line.ItemCode);
            changed = true;
        }

        if (changed)
        {
            workspace.CatalogItems = items
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return changed;
    }

    private static IReadOnlyList<SalesCatalogItemOption> GetBaselineCatalogItems()
    {
        return
        [
            new SalesCatalogItemOption("ALTEZA-P50-BL", "ALTEZA профиль P-50 гардина черный мат", "м", 840m),
            new SalesCatalogItemOption("LUM-CLAMP-50", "Профиль LumFer Clamp Level 50", "м", 1_180m),
            new SalesCatalogItemOption("SCREEN-30", "Экран световой SCREEN 30 белый", "м", 2_450m),
            new SalesCatalogItemOption("GX53-BASE", "Платформа GX-53 белая", "шт", 380m),
            new SalesCatalogItemOption("KLEM-2X", "Клеммы 2-контактные", "шт", 22m)
        ];
    }

    private static IEnumerable<SalesOrderLineRecord> EnumerateSalesLines(SalesWorkspace workspace)
    {
        return workspace.Orders.SelectMany(item => item.Lines)
            .Concat(workspace.Invoices.SelectMany(item => item.Lines))
            .Concat(workspace.Shipments.SelectMany(item => item.Lines))
            .Concat(workspace.Returns.SelectMany(item => item.Lines));
    }

    private static SalesOrderRecord CreateOrderFromInvoice(
        SalesInvoiceRecord invoice,
        SalesWorkspace workspace,
        IReadOnlyDictionary<string, SalesOrderRecord> ordersByNumber)
    {
        var id = invoice.SalesOrderId != Guid.Empty
            ? invoice.SalesOrderId
            : CreateDeterministicGuid($"invoice-order|{invoice.Id:N}|{invoice.Number}");
        var number = BuildUniqueOrderNumber($"AUTO-{invoice.Number}", ordersByNumber);
        return new SalesOrderRecord
        {
            Id = id,
            Number = number,
            OrderDate = invoice.InvoiceDate,
            CustomerId = invoice.CustomerId,
            CustomerCode = invoice.CustomerCode,
            CustomerName = invoice.CustomerName,
            ContractNumber = invoice.ContractNumber,
            CurrencyCode = string.IsNullOrWhiteSpace(invoice.CurrencyCode) ? "RUB" : invoice.CurrencyCode,
            Warehouse = workspace.Warehouses.FirstOrDefault() ?? string.Empty,
            Status = "Подтвержден",
            Manager = invoice.Manager,
            Comment = $"Создано автоматически по счету {invoice.Number}, потому что исходный заказ отсутствовал в данных.",
            Lines = CloneLines(invoice.Lines, id)
        };
    }

    private static SalesOrderRecord CreateOrderFromShipment(
        SalesShipmentRecord shipment,
        SalesWorkspace workspace,
        IReadOnlyDictionary<string, SalesOrderRecord> ordersByNumber)
    {
        var id = shipment.SalesOrderId != Guid.Empty
            ? shipment.SalesOrderId
            : CreateDeterministicGuid($"shipment-order|{shipment.Id:N}|{shipment.Number}");
        var number = BuildUniqueOrderNumber($"AUTO-{shipment.Number}", ordersByNumber);
        return new SalesOrderRecord
        {
            Id = id,
            Number = number,
            OrderDate = shipment.ShipmentDate,
            CustomerId = shipment.CustomerId,
            CustomerCode = shipment.CustomerCode,
            CustomerName = shipment.CustomerName,
            ContractNumber = shipment.ContractNumber,
            CurrencyCode = string.IsNullOrWhiteSpace(shipment.CurrencyCode) ? "RUB" : shipment.CurrencyCode,
            Warehouse = string.IsNullOrWhiteSpace(shipment.Warehouse) ? workspace.Warehouses.FirstOrDefault() ?? string.Empty : shipment.Warehouse,
            Status = "Готов к отгрузке",
            Manager = shipment.Manager,
            Comment = $"Создано автоматически по отгрузке {shipment.Number}, потому что исходный заказ отсутствовал в данных.",
            Lines = CloneLines(shipment.Lines, id)
        };
    }

    private static BindingList<SalesOrderLineRecord> CloneLines(IEnumerable<SalesOrderLineRecord> lines, Guid documentId)
    {
        return new BindingList<SalesOrderLineRecord>(lines
            .Select((line, index) => new SalesOrderLineRecord
            {
                Id = CreateDeterministicGuid($"{documentId:N}|line|{index}|{line.ItemCode}|{line.ItemName}"),
                ItemCode = line.ItemCode,
                ItemName = line.ItemName,
                Unit = line.Unit,
                Quantity = line.Quantity,
                Price = line.Price
            })
            .ToList());
    }

    private static string BuildUniqueOrderNumber(string preferred, IReadOnlyDictionary<string, SalesOrderRecord> ordersByNumber)
    {
        var baseNumber = string.IsNullOrWhiteSpace(preferred) ? "AUTO-ORDER" : preferred.Trim();
        if (!ordersByNumber.ContainsKey(baseNumber))
        {
            return baseNumber;
        }

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{baseNumber}-{index}";
            if (!ordersByNumber.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return $"{baseNumber}-{Guid.NewGuid():N}";
    }

    private static bool TryResolveOrder(
        Guid id,
        string number,
        IReadOnlyDictionary<Guid, SalesOrderRecord> ordersById,
        IReadOnlyDictionary<string, SalesOrderRecord> ordersByNumber,
        out SalesOrderRecord order)
    {
        order = null!;
        if (id != Guid.Empty && ordersById.TryGetValue(id, out order!))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(number)
               && ordersByNumber.TryGetValue(number.Trim(), out order!);
    }

    private static bool TryResolveCustomer(
        Guid id,
        string code,
        string name,
        IReadOnlyDictionary<Guid, SalesCustomerRecord> customersById,
        IReadOnlyDictionary<string, SalesCustomerRecord> customersByCode,
        IReadOnlyDictionary<string, SalesCustomerRecord> customersByName,
        out SalesCustomerRecord customer)
    {
        customer = null!;
        if (id != Guid.Empty && customersById.TryGetValue(id, out customer!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(code) && customersByCode.TryGetValue(code.Trim(), out customer!))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(name)
               && customersByName.TryGetValue(name.Trim(), out customer!);
    }

    private static Dictionary<string, T> BuildUniqueLookup<T>(IEnumerable<T> source, Func<T, string> keySelector)
    {
        return source
            .Select(item => (Key: keySelector(item)?.Trim() ?? string.Empty, Item: item))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Item, StringComparer.OrdinalIgnoreCase);
    }

    private static bool FillOrderCustomer(SalesOrderRecord order, SalesCustomerRecord customer)
    {
        var changed = false;
        if (customer.Id != Guid.Empty && order.CustomerId != customer.Id)
        {
            order.CustomerId = customer.Id;
            changed = true;
        }

        if (SetIfMissing(order.CustomerCode, customer.Code, out var customerCode))
        {
            order.CustomerCode = customerCode;
            changed = true;
        }

        if (SetIfMissing(order.CustomerName, customer.Name, out var customerName))
        {
            order.CustomerName = customerName;
            changed = true;
        }

        if (SetIfMissing(order.ContractNumber, customer.ContractNumber, out var contractNumber))
        {
            order.ContractNumber = contractNumber;
            changed = true;
        }

        return changed;
    }

    private static bool FillInvoiceCustomer(SalesInvoiceRecord invoice, SalesCustomerRecord customer)
    {
        var changed = false;
        if (customer.Id != Guid.Empty && invoice.CustomerId != customer.Id)
        {
            invoice.CustomerId = customer.Id;
            changed = true;
        }

        if (SetIfMissing(invoice.CustomerCode, customer.Code, out var customerCode))
        {
            invoice.CustomerCode = customerCode;
            changed = true;
        }

        if (SetIfMissing(invoice.CustomerName, customer.Name, out var customerName))
        {
            invoice.CustomerName = customerName;
            changed = true;
        }

        if (SetIfMissing(invoice.ContractNumber, customer.ContractNumber, out var contractNumber))
        {
            invoice.ContractNumber = contractNumber;
            changed = true;
        }

        return changed;
    }

    private static bool FillShipmentCustomer(SalesShipmentRecord shipment, SalesCustomerRecord customer)
    {
        var changed = false;
        if (customer.Id != Guid.Empty && shipment.CustomerId != customer.Id)
        {
            shipment.CustomerId = customer.Id;
            changed = true;
        }

        if (SetIfMissing(shipment.CustomerCode, customer.Code, out var customerCode))
        {
            shipment.CustomerCode = customerCode;
            changed = true;
        }

        if (SetIfMissing(shipment.CustomerName, customer.Name, out var customerName))
        {
            shipment.CustomerName = customerName;
            changed = true;
        }

        if (SetIfMissing(shipment.ContractNumber, customer.ContractNumber, out var contractNumber))
        {
            shipment.ContractNumber = contractNumber;
            changed = true;
        }

        return changed;
    }

    private static bool FillReturnCustomer(SalesReturnRecord returnDocument, SalesCustomerRecord customer)
    {
        var changed = false;
        if (customer.Id != Guid.Empty && returnDocument.CustomerId != customer.Id)
        {
            returnDocument.CustomerId = customer.Id;
            changed = true;
        }

        if (SetIfMissing(returnDocument.CustomerCode, customer.Code, out var customerCode))
        {
            returnDocument.CustomerCode = customerCode;
            changed = true;
        }

        if (SetIfMissing(returnDocument.CustomerName, customer.Name, out var customerName))
        {
            returnDocument.CustomerName = customerName;
            changed = true;
        }

        if (SetIfMissing(returnDocument.ContractNumber, customer.ContractNumber, out var contractNumber))
        {
            returnDocument.ContractNumber = contractNumber;
            changed = true;
        }

        return changed;
    }

    private static bool FillReceiptCustomer(SalesCashReceiptRecord receipt, SalesCustomerRecord customer)
    {
        var changed = false;
        if (customer.Id != Guid.Empty && receipt.CustomerId != customer.Id)
        {
            receipt.CustomerId = customer.Id;
            changed = true;
        }

        if (SetIfMissing(receipt.CustomerCode, customer.Code, out var customerCode))
        {
            receipt.CustomerCode = customerCode;
            changed = true;
        }

        if (SetIfMissing(receipt.CustomerName, customer.Name, out var customerName))
        {
            receipt.CustomerName = customerName;
            changed = true;
        }

        if (SetIfMissing(receipt.ContractNumber, customer.ContractNumber, out var contractNumber))
        {
            receipt.ContractNumber = contractNumber;
            changed = true;
        }

        return changed;
    }

    private static bool FillInvoiceOrder(SalesInvoiceRecord invoice, SalesOrderRecord order)
    {
        var changed = false;
        if (order.Id != Guid.Empty && invoice.SalesOrderId != order.Id)
        {
            invoice.SalesOrderId = order.Id;
            changed = true;
        }

        if (!string.Equals(invoice.SalesOrderNumber, order.Number, StringComparison.Ordinal))
        {
            invoice.SalesOrderNumber = order.Number;
            changed = true;
        }

        changed |= FillInvoiceCustomerFromOrder(invoice, order);
        return changed;
    }

    private static bool FillShipmentOrder(SalesShipmentRecord shipment, SalesOrderRecord order)
    {
        var changed = false;
        if (order.Id != Guid.Empty && shipment.SalesOrderId != order.Id)
        {
            shipment.SalesOrderId = order.Id;
            changed = true;
        }

        if (!string.Equals(shipment.SalesOrderNumber, order.Number, StringComparison.Ordinal))
        {
            shipment.SalesOrderNumber = order.Number;
            changed = true;
        }

        changed |= FillShipmentCustomerFromOrder(shipment, order);
        return changed;
    }

    private static bool FillInvoiceCustomerFromOrder(SalesInvoiceRecord invoice, SalesOrderRecord order)
    {
        var changed = false;
        if (order.CustomerId != Guid.Empty && invoice.CustomerId != order.CustomerId)
        {
            invoice.CustomerId = order.CustomerId;
            changed = true;
        }

        if (SetIfMissing(invoice.CustomerCode, order.CustomerCode, out var customerCode))
        {
            invoice.CustomerCode = customerCode;
            changed = true;
        }

        if (SetIfMissing(invoice.CustomerName, order.CustomerName, out var customerName))
        {
            invoice.CustomerName = customerName;
            changed = true;
        }

        if (SetIfMissing(invoice.ContractNumber, order.ContractNumber, out var contractNumber))
        {
            invoice.ContractNumber = contractNumber;
            changed = true;
        }

        return changed;
    }

    private static bool FillShipmentCustomerFromOrder(SalesShipmentRecord shipment, SalesOrderRecord order)
    {
        var changed = false;
        if (order.CustomerId != Guid.Empty && shipment.CustomerId != order.CustomerId)
        {
            shipment.CustomerId = order.CustomerId;
            changed = true;
        }

        if (SetIfMissing(shipment.CustomerCode, order.CustomerCode, out var customerCode))
        {
            shipment.CustomerCode = customerCode;
            changed = true;
        }

        if (SetIfMissing(shipment.CustomerName, order.CustomerName, out var customerName))
        {
            shipment.CustomerName = customerName;
            changed = true;
        }

        if (SetIfMissing(shipment.ContractNumber, order.ContractNumber, out var contractNumber))
        {
            shipment.ContractNumber = contractNumber;
            changed = true;
        }

        return changed;
    }

    private static bool SetIfMissing(string target, string value, out string resolved)
    {
        resolved = target;
        if (!string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        resolved = value;
        return true;
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private bool TrySaveToBackplane(SalesWorkspaceSnapshot snapshot, string currentOperator)
    {
        if (_backplane is null)
        {
            return false;
        }

        var auditEvents = CreateAuditSeeds(snapshot.OperationLog);
        var result = _backplane.TrySaveModuleSnapshot("sales", snapshot, currentOperator, _remoteMetadata, auditEvents);
        if (result.Succeeded)
        {
            _remoteMetadata = result.Metadata;
            return true;
        }

        if (result.State != DesktopModuleSnapshotSaveState.Conflict)
        {
            return false;
        }

        var latest = _backplane.TryLoadModuleSnapshotRecord<SalesWorkspaceSnapshot>("sales");
        if (latest is null)
        {
            return false;
        }

        var merged = MergeSnapshots(latest.Snapshot, snapshot);
        var mergedAuditEvents = CreateAuditSeeds(merged.OperationLog);
        var retry = _backplane.TrySaveModuleSnapshot("sales", merged, currentOperator, latest.Metadata, mergedAuditEvents);
        if (!retry.Succeeded)
        {
            throw new InvalidOperationException("Данные на сервере изменились другим рабочим местом. Обновите данные и повторите действие.");
        }

        _remoteMetadata = retry.Metadata;
        return true;
    }

    private static void ApplySnapshotToWorkspace(
        SalesWorkspace workspace,
        SalesWorkspaceSnapshot snapshot,
        DesktopOperationalSnapshot? operationalSnapshot,
        IReadOnlyList<string>? importRoots)
    {
        if (operationalSnapshot?.HasSalesData == true)
        {
            MergeSnapshotIntoOperationalWorkspace(workspace, snapshot);
            return;
        }

        ApplySnapshot(workspace, snapshot);
        if (importRoots is { Count: > 0 })
        {
            AttachImportSnapshot(workspace, importRoots);
            SalesWorkspaceImportMerger.Merge(workspace);
        }
        else
        {
            workspace.AttachOneCImportSnapshot(null);
        }
    }

    private static void ApplySnapshot(SalesWorkspace workspace, SalesWorkspaceSnapshot snapshot)
    {
        ReplaceList(workspace.Customers, snapshot.Customers, item => item.Clone());
        ReplaceList(workspace.Orders, snapshot.Orders, item => item.Clone());
        ReplaceList(workspace.Invoices, snapshot.Invoices, item => item.Clone());
        ReplaceList(workspace.Shipments, snapshot.Shipments, item => item.Clone());
        ReplaceList(workspace.Returns, snapshot.Returns, item => item.Clone());
        ReplaceList(workspace.CashReceipts, snapshot.CashReceipts, item => item.Clone());
        ReplaceList(workspace.OperationLog, snapshot.OperationLog, item => item.Clone());
    }

    private static SalesWorkspaceSnapshot MergeSnapshots(SalesWorkspaceSnapshot server, SalesWorkspaceSnapshot local)
    {
        return new SalesWorkspaceSnapshot
        {
            Customers = MergeRecords(server.Customers, local.Customers, BuildCustomerKey, item => item.Clone()),
            Orders = MergeRecords(server.Orders, local.Orders, BuildOrderKey, item => item.Clone()),
            Invoices = MergeRecords(server.Invoices, local.Invoices, BuildInvoiceKey, item => item.Clone()),
            Shipments = MergeRecords(server.Shipments, local.Shipments, BuildShipmentKey, item => item.Clone()),
            Returns = MergeRecords(server.Returns, local.Returns, BuildReturnKey, item => item.Clone()),
            CashReceipts = MergeRecords(server.CashReceipts, local.CashReceipts, BuildCashReceiptKey, item => item.Clone()),
            OperationLog = server.OperationLog
                .Concat(local.OperationLog)
                .GroupBy(item => item.Id == Guid.Empty ? CreateFallbackLogKey(item) : item.Id.ToString("N"), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.LoggedAt).First().Clone())
                .OrderByDescending(item => item.LoggedAt)
                .Take(500)
                .ToList()
        };
    }

    private static List<T> MergeRecords<T>(
        IEnumerable<T> server,
        IEnumerable<T> local,
        Func<T, string> keySelector,
        Func<T, T> clone)
    {
        var merged = new List<T>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in server)
        {
            var key = keySelector(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                merged.Add(clone(item));
                continue;
            }

            indexes[key] = merged.Count;
            merged.Add(clone(item));
        }

        foreach (var item in local)
        {
            var key = keySelector(item);
            var cloneItem = clone(item);
            if (!string.IsNullOrWhiteSpace(key) && indexes.TryGetValue(key, out var index))
            {
                merged[index] = cloneItem;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                indexes[key] = merged.Count;
            }

            merged.Add(cloneItem);
        }

        return merged;
    }

    private static string BuildCustomerKey(SalesCustomerRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : !string.IsNullOrWhiteSpace(item.Code)
                ? $"code:{item.Code}"
                : $"name:{item.Name}";
    }

    private static string BuildOrderKey(SalesOrderRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildInvoiceKey(SalesInvoiceRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildShipmentKey(SalesShipmentRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildReturnKey(SalesReturnRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildCashReceiptKey(SalesCashReceiptRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string CreateFallbackLogKey(SalesOperationLogEntry item)
    {
        return $"{item.EntityType}|{item.EntityId:N}|{item.EntityNumber}|{item.Action}|{item.LoggedAt:O}";
    }

    private static void ApplyOperationalSnapshot(
        SalesWorkspace workspace,
        DesktopOperationalSnapshot snapshot,
        string currentOperator)
    {
        ReplaceList(workspace.Customers, snapshot.Customers, item => item.Clone());
        ReplaceList(workspace.Orders, snapshot.Orders, item => item.Clone());
        ReplaceList(workspace.Invoices, snapshot.Invoices, item => item.Clone());
        ReplaceList(workspace.Shipments, snapshot.Shipments, item => item.Clone());
        ReplaceList(workspace.Returns, Array.Empty<SalesReturnRecord>(), item => item.Clone());
        ReplaceList(workspace.CashReceipts, Array.Empty<SalesCashReceiptRecord>(), item => item.Clone());

        if (snapshot.CatalogItems.Count > 0)
        {
            workspace.CatalogItems = snapshot.CatalogItems
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        workspace.Managers = BuildLookupList(snapshot.Managers, workspace.Managers, currentOperator);
        workspace.Currencies = BuildLookupList(snapshot.Currencies, workspace.Currencies, "RUB");
        workspace.Warehouses = BuildLookupList(snapshot.Warehouses, workspace.Warehouses);
    }

    private static void MergeSnapshotIntoOperationalWorkspace(SalesWorkspace workspace, SalesWorkspaceSnapshot snapshot)
    {
        MergeCustomers(workspace.Customers, snapshot.Customers);

        var knownCustomerIds = workspace.Customers
            .Select(item => item.Id)
            .ToHashSet();

        MergeOrders(workspace.Orders, snapshot.Orders, knownCustomerIds);
        MergeInvoices(workspace.Invoices, snapshot.Invoices, knownCustomerIds);
        MergeShipments(workspace.Shipments, snapshot.Shipments, knownCustomerIds);
        MergeReturns(workspace.Returns, snapshot.Returns, knownCustomerIds);
        MergeCashReceipts(workspace.CashReceipts, snapshot.CashReceipts, knownCustomerIds);
        ReplaceList(workspace.OperationLog, snapshot.OperationLog, item => item.Clone());
    }

    private static void MergeCustomers(
        ICollection<SalesCustomerRecord> target,
        IEnumerable<SalesCustomerRecord> source)
    {
        var targetByKey = target
            .Select(item => (Key: BuildCustomerKey(item), Item: item))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => item.Item, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            var key = BuildCustomerKey(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                target.Add(item.Clone());
                continue;
            }

            if (targetByKey.TryGetValue(key, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByKey[key] = clone;
        }
    }

    private static void ReplaceList<T>(ICollection<T> target, IEnumerable<T>? source, Func<T, T> clone)
    {
        target.Clear();
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            target.Add(clone(item));
        }
    }

    private static void AttachImportSnapshot(SalesWorkspace workspace, IReadOnlyList<string>? importRoots = null)
    {
        try
        {
            var workspaceRoot = WorkspacePathResolver.ResolveWorkspaceRoot();
            var importService = importRoots is { Count: > 0 }
                ? new OneCImportService(workspaceRoot, importRoots)
                : new OneCImportService(workspaceRoot);
            workspace.AttachOneCImportSnapshot(importService.LoadSnapshot());
        }
        catch
        {
            workspace.AttachOneCImportSnapshot(null);
        }
    }

    private static DesktopOperationalSnapshot? AttachOperationalSnapshot(SalesWorkspace workspace)
    {
        try
        {
            var snapshot = OperationalMySqlDesktopService.TryCreateConfigured()?.TryLoadSnapshot();
            workspace.AttachOperationalSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception exception)
        {
            try
            {
                var root = WorkspacePathResolver.ResolveWorkspaceRoot();
                var path = Path.Combine(root, "app_data", "operational-desktop-attach-error.log");
                File.WriteAllText(path, exception.ToString(), Encoding.UTF8);
            }
            catch
            {
            }

            workspace.AttachOperationalSnapshot(null);
            return null;
        }
    }

    private static void MergeOrders(
        ICollection<SalesOrderRecord> target,
        IEnumerable<SalesOrderRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeInvoices(
        ICollection<SalesInvoiceRecord> target,
        IEnumerable<SalesInvoiceRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeShipments(
        ICollection<SalesShipmentRecord> target,
        IEnumerable<SalesShipmentRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeReturns(
        ICollection<SalesReturnRecord> target,
        IEnumerable<SalesReturnRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeCashReceipts(
        ICollection<SalesCashReceiptRecord> target,
        IEnumerable<SalesCashReceiptRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static IReadOnlyList<string> BuildLookupList(
        IEnumerable<string> preferredValues,
        IReadOnlyList<string> fallbackValues,
        params string[] enforcedValues)
    {
        return preferredValues
            .Concat(fallbackValues)
            .Concat(enforcedValues)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DesktopAuditEventSeed> CreateAuditSeeds(IEnumerable<SalesOperationLogEntry> entries)
    {
        return entries
            .Select(item => new DesktopAuditEventSeed(
                item.Id,
                item.LoggedAt.Kind == DateTimeKind.Utc ? item.LoggedAt : item.LoggedAt.ToUniversalTime(),
                item.Actor,
                item.EntityType,
                item.EntityId,
                item.EntityNumber,
                item.Action,
                item.Result,
                item.Message))
            .ToArray();
    }
}

public sealed class SalesWorkspaceSnapshot
{
    public List<SalesCustomerRecord> Customers { get; set; } = [];

    public List<SalesOrderRecord> Orders { get; set; } = [];

    public List<SalesInvoiceRecord> Invoices { get; set; } = [];

    public List<SalesShipmentRecord> Shipments { get; set; } = [];

    public List<SalesReturnRecord> Returns { get; set; } = [];

    public List<SalesCashReceiptRecord> CashReceipts { get; set; } = [];

    public List<SalesOperationLogEntry> OperationLog { get; set; } = [];

    public static SalesWorkspaceSnapshot FromWorkspace(SalesWorkspace workspace)
    {
        return new SalesWorkspaceSnapshot
        {
            Customers = workspace.Customers.Select(item => item.Clone()).ToList(),
            Orders = workspace.Orders.Select(item => item.Clone()).ToList(),
            Invoices = workspace.Invoices.Select(item => item.Clone()).ToList(),
            Shipments = workspace.Shipments.Select(item => item.Clone()).ToList(),
            Returns = workspace.Returns.Select(item => item.Clone()).ToList(),
            CashReceipts = workspace.CashReceipts.Select(item => item.Clone()).ToList(),
            OperationLog = workspace.OperationLog.Select(item => item.Clone()).ToList()
        };
    }
}
