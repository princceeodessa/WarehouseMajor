namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

public sealed record WmsReadinessSnapshot(
    int CellCount,
    int RealCellCount,
    int UnplacedCellCount,
    int WarehousesWithCells,
    int BalanceRows,
    decimal BalanceQuantity,
    int LocationRows,
    decimal LocationQuantity,
    int RealLocationRows,
    decimal RealLocationQuantity,
    int UnplacedRows,
    decimal UnplacedQuantity,
    int NegativeBalanceRows,
    decimal NegativeBalanceQuantity,
    int MismatchedPairs,
    decimal NetDifference,
    decimal AbsoluteDifference,
    DateTime? LatestBalanceProjectionUtc,
    DateTime? LatestLocationUpdateUtc)
{
    public bool HasUnplacedStart => UnplacedRows > 0 && UnplacedQuantity > 0;

    public bool HasRealCells => RealCellCount > 0;

    public bool HasPlacedInRealCells => RealLocationRows > 0 && RealLocationQuantity > 0;

    public bool HasOnlyExpectedNegativeMismatch =>
        MismatchedPairs == NegativeBalanceRows
        && Math.Abs(AbsoluteDifference - Math.Abs(NegativeBalanceQuantity)) < 0.0001m;

    public bool IsReadyForPilot => HasRealCells && HasUnplacedStart;

    public bool IsReadyForFullCutover =>
        IsReadyForPilot
        && HasPlacedInRealCells
        && MismatchedPairs == 0
        && NegativeBalanceRows == 0;
}
