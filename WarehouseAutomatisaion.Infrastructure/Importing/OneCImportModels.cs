namespace WarehouseAutomatisaion.Infrastructure.Importing;

public sealed class OneCImportSnapshot
{
    public IReadOnlyList<string> SourceFolders { get; init; } = Array.Empty<string>();

    public OneCEntityDataset Customers { get; init; } = OneCEntityDataset.Empty("Контрагенты");

    public OneCEntityDataset Items { get; init; } = OneCEntityDataset.Empty("Номенклатура");

    public OneCEntityDataset SalesOrders { get; init; } = OneCEntityDataset.Empty("ЗаказПокупателя");

    public OneCEntityDataset SalesInvoices { get; init; } = OneCEntityDataset.Empty("СчетНаОплату");

    public OneCEntityDataset SalesShipments { get; init; } = OneCEntityDataset.Empty("РасходнаяНакладная");

    public OneCEntityDataset PurchaseOrders { get; init; } = OneCEntityDataset.Empty("ЗаказПоставщику");

    public OneCEntityDataset SupplierInvoices { get; init; } = OneCEntityDataset.Empty("СчетНаОплатуПоставщика");

    public OneCEntityDataset PurchaseReceipts { get; init; } = OneCEntityDataset.Empty("ПриходнаяНакладная");

    public OneCEntityDataset TransferOrders { get; init; } = OneCEntityDataset.Empty("ЗаказНаПеремещение");

    public OneCEntityDataset StockReservations { get; init; } = OneCEntityDataset.Empty("РезервированиеЗапасов");

    public OneCEntityDataset InventoryCounts { get; init; } = OneCEntityDataset.Empty("ИнвентаризацияЗапасов");

    public OneCEntityDataset StockWriteOffs { get; init; } = OneCEntityDataset.Empty("СписаниеЗапасов");

    public IReadOnlyList<OneCSchemaDefinition> Schemas { get; init; } = Array.Empty<OneCSchemaDefinition>();

    public bool HasAnyData =>
        Customers.Records.Count > 0
        || Items.Records.Count > 0
        || SalesOrders.Records.Count > 0
        || SalesInvoices.Records.Count > 0
        || SalesShipments.Records.Count > 0
        || PurchaseOrders.Records.Count > 0
        || SupplierInvoices.Records.Count > 0
        || PurchaseReceipts.Records.Count > 0
        || TransferOrders.Records.Count > 0
        || StockReservations.Records.Count > 0
        || InventoryCounts.Records.Count > 0
        || StockWriteOffs.Records.Count > 0
        || Schemas.Count > 0;

    public OneCSchemaDefinition? FindSchema(string objectName)
    {
        return Schemas.FirstOrDefault(
            schema => string.Equals(schema.ObjectName, objectName, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class OneCEntityDataset
{
    public string ObjectName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public OneCSchemaDefinition? Schema { get; init; }

    public IReadOnlyList<OneCRecordSnapshot> Records { get; init; } = Array.Empty<OneCRecordSnapshot>();

    public static OneCEntityDataset Empty(string objectName)
    {
        return new OneCEntityDataset
        {
            ObjectName = objectName,
            DisplayName = objectName
        };
    }
}

public sealed class OneCRecordSnapshot
{
    public string ObjectName { get; init; } = string.Empty;

    public string Reference { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string Number { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime? Date { get; init; }

    public IReadOnlyList<OneCFieldValue> Fields { get; init; } = Array.Empty<OneCFieldValue>();

    public IReadOnlyList<OneCTabularSectionSnapshot> TabularSections { get; init; } = Array.Empty<OneCTabularSectionSnapshot>();

    public OneCFieldValue? FindField(string fieldName)
    {
        return Fields.FirstOrDefault(field => OneCTextNormalizer.TextEquals(field.Name, fieldName));
    }
}

public sealed record OneCFieldValue(string Name, string RawValue, string DisplayValue);

public sealed class OneCTabularSectionSnapshot
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OneCTabularSectionRowSnapshot> Rows { get; init; } = Array.Empty<OneCTabularSectionRowSnapshot>();
}

public sealed class OneCTabularSectionRowSnapshot
{
    public int RowNumber { get; init; }

    public IReadOnlyList<OneCFieldValue> Fields { get; init; } = Array.Empty<OneCFieldValue>();

    public OneCFieldValue? FindField(string fieldName)
    {
        return Fields.FirstOrDefault(field => OneCTextNormalizer.TextEquals(field.Name, fieldName));
    }
}

public sealed class OneCSchemaDefinition
{
    public string Kind { get; init; } = string.Empty;

    public string ObjectName { get; init; } = string.Empty;

    public string SourceFileName { get; init; } = string.Empty;

    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OneCSchemaTabularSectionDefinition> TabularSections { get; init; } = Array.Empty<OneCSchemaTabularSectionDefinition>();
}

public sealed class OneCSchemaTabularSectionDefinition
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
}
