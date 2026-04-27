namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class DesktopOperationalSnapshot
{
    public IReadOnlyList<SalesCustomerRecord> Customers { get; init; } = Array.Empty<SalesCustomerRecord>();

    public IReadOnlyList<SalesCatalogItemOption> CatalogItems { get; init; } = Array.Empty<SalesCatalogItemOption>();

    public IReadOnlyList<string> Managers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Currencies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warehouses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SalesOrderRecord> Orders { get; init; } = Array.Empty<SalesOrderRecord>();

    public IReadOnlyList<SalesInvoiceRecord> Invoices { get; init; } = Array.Empty<SalesInvoiceRecord>();

    public IReadOnlyList<SalesShipmentRecord> Shipments { get; init; } = Array.Empty<SalesShipmentRecord>();

    public IReadOnlyList<PurchasingSupplierRecord> Suppliers { get; init; } = Array.Empty<PurchasingSupplierRecord>();

    public IReadOnlyList<PurchasingDocumentRecord> PurchaseOrders { get; init; } = Array.Empty<PurchasingDocumentRecord>();

    public IReadOnlyList<PurchasingDocumentRecord> SupplierInvoices { get; init; } = Array.Empty<PurchasingDocumentRecord>();

    public IReadOnlyList<PurchasingDocumentRecord> PurchaseReceipts { get; init; } = Array.Empty<PurchasingDocumentRecord>();

    public IReadOnlyList<WarehouseStockBalanceRecord> StockBalances { get; init; } = Array.Empty<WarehouseStockBalanceRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> TransferOrders { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> Reservations { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> InventoryCounts { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public IReadOnlyList<WarehouseDocumentRecord> WriteOffs { get; init; } = Array.Empty<WarehouseDocumentRecord>();

    public bool HasSalesData =>
        Customers.Count > 0
        || CatalogItems.Count > 0
        || Orders.Count > 0
        || Invoices.Count > 0
        || Shipments.Count > 0;

    public bool HasPurchasingData =>
        Suppliers.Count > 0
        || PurchaseOrders.Count > 0
        || SupplierInvoices.Count > 0
        || PurchaseReceipts.Count > 0;

    public bool HasWarehouseData =>
        StockBalances.Count > 0
        || TransferOrders.Count > 0
        || Reservations.Count > 0
        || InventoryCounts.Count > 0
        || WriteOffs.Count > 0;
}
