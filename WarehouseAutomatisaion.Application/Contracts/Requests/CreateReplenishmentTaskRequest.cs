using System.ComponentModel.DataAnnotations;

namespace WarehouseAutomatisaion.Application.Contracts.Requests;

public sealed record CreateReplenishmentTaskRequest
{
    public Guid ProductId { get; init; }

    public Guid? SourceCellId { get; init; }

    public Guid? TargetCellId { get; init; }

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal Quantity { get; init; }

    [Range(1, 10)]
    public int Priority { get; init; } = 5;
}
