using WarehouseAutomatisaion.Domain.Enums;

namespace WarehouseAutomatisaion.Domain.Entities;

public sealed record Organization(
    Guid Id,
    string Code,
    string Name,
    string? TaxId);

public sealed record Employee(
    Guid Id,
    string Code,
    string FullName,
    string? Email);

public sealed record BankAccount(
    Guid Id,
    Guid? OrganizationId,
    Guid? BusinessPartnerId,
    string AccountNumber,
    string? BankName,
    string CurrencyCode,
    bool IsDefault);

public sealed record BusinessPartner(
    Guid Id,
    string Code,
    string Name,
    BusinessPartnerRole Roles,
    Guid? ParentId,
    Guid? HeadPartnerId,
    Guid? DefaultBankAccountId,
    string? SettlementCurrencyCode,
    Guid? CountryId,
    Guid? ResponsibleEmployeeId,
    Guid? PrimaryContactId,
    bool IsArchived)
{
    public bool IsCustomer => Roles.HasFlag(BusinessPartnerRole.Customer);

    public bool IsSupplier => Roles.HasFlag(BusinessPartnerRole.Supplier);
}

public sealed record PartnerContact(
    Guid Id,
    Guid BusinessPartnerId,
    string FullName,
    string? Phone,
    string? Email,
    bool IsPrimary);

public sealed record PartnerContract(
    Guid Id,
    string Number,
    Guid BusinessPartnerId,
    Guid OrganizationId,
    string? SettlementCurrencyCode,
    bool RequiresPrepayment);

public sealed record UnitOfMeasure(
    Guid Id,
    string Code,
    string Name,
    string? Symbol);

public sealed record ItemCategory(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name);

public sealed record PriceGroup(
    Guid Id,
    string Code,
    string Name);

public sealed record WarehouseNode(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    WarehouseNodeType Type,
    bool IsReserveArea);

public sealed record StorageBin(
    Guid Id,
    Guid WarehouseNodeId,
    Guid? ParentBinId,
    string Code,
    string Name);

public sealed record NomenclatureItem(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Sku,
    string Name,
    Guid? UnitOfMeasureId,
    Guid? CategoryId,
    Guid? DefaultSupplierId,
    Guid? DefaultWarehouseNodeId,
    Guid? DefaultStorageBinId,
    Guid? PriceGroupId,
    string? ItemKind,
    string? VatRateCode,
    bool TracksBatches,
    bool TracksSerials);

public sealed record PriceType(
    Guid Id,
    string Code,
    string Name,
    string CurrencyCode,
    Guid? BasePriceTypeId,
    bool IsManualEntryOnly,
    bool UsesPsychologicalRounding);

public sealed record PriceTypeRoundingRule(
    Guid Id,
    Guid PriceTypeId,
    decimal ThresholdAmount,
    int Precision,
    decimal Step);

public sealed record DiscountPolicy(
    Guid Id,
    string Code,
    string Name,
    Guid? PriceTypeId,
    string CurrencyCode,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    bool IsActive,
    DiscountKind Kind,
    DiscountScope Scope,
    decimal Value,
    IReadOnlyCollection<Guid> PartnerIds,
    IReadOnlyCollection<Guid> WarehouseNodeIds,
    IReadOnlyCollection<Guid> ItemCategoryIds,
    IReadOnlyCollection<Guid> PriceGroupIds);

public sealed record StockBalance(
    Guid Id,
    Guid ItemId,
    Guid WarehouseNodeId,
    Guid? StorageBinId,
    Guid? BatchId,
    decimal Quantity,
    decimal ReservedQuantity,
    DateTimeOffset LastMovementAtUtc)
{
    public decimal AvailableQuantity => Quantity - ReservedQuantity;
}
