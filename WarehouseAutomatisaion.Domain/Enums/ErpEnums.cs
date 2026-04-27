namespace WarehouseAutomatisaion.Domain.Enums;

[Flags]
public enum BusinessPartnerRole
{
    None = 0,
    Customer = 1,
    Supplier = 2,
    Carrier = 4,
    Consignee = 8
}

public enum WarehouseNodeType
{
    Warehouse = 1,
    Store = 2,
    Transit = 3,
    DeliveryHub = 4
}

public enum DocumentPostingState
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3
}

public enum LifecycleStatus
{
    Draft = 1,
    Planned = 2,
    Confirmed = 3,
    Reserved = 4,
    InProgress = 5,
    Completed = 6,
    Cancelled = 7
}

public enum DiscountKind
{
    Percentage = 1,
    FixedAmount = 2,
    Gift = 3
}

public enum DiscountScope
{
    Document = 1,
    Line = 2,
    Delivery = 3
}

public enum StockReservationPlace
{
    None = 0,
    OnHand = 1,
    Incoming = 2,
    Transfer = 3
}
