namespace WarehouseAutomatisaion.Application.Contracts.Vision;

// Sprint 5: результат сопоставления распознанной строки накладной с номенклатурой.
// Используется UI чтобы показать оператору варианты и дать выбрать вручную если AI не уверен.
public sealed record MatchedInvoiceLine(
    InvoiceLineItem Source,
    NomenclatureRef? BestMatch,
    IReadOnlyList<MatchCandidate> Alternatives,
    MatchKind Kind,
    double Confidence);

public sealed record NomenclatureRef(
    string Id,
    string Code,
    string Name);

public sealed record MatchCandidate(
    NomenclatureRef Item,
    double Score,
    MatchKind Kind);

public enum MatchKind
{
    /// <summary>Точное совпадение по коду (артикулу) — самый надёжный сигнал.</summary>
    ExactCode,

    /// <summary>Точное совпадение по названию (case-insensitive).</summary>
    ExactName,

    /// <summary>Название содержит распознанное (или наоборот).</summary>
    PartialName,

    /// <summary>Похожее название по нечёткому сравнению (Levenshtein / Jaccard).</summary>
    FuzzyName,

    /// <summary>Не удалось сопоставить.</summary>
    NoMatch
}
