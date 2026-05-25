namespace WarehouseAutomatisaion.Application.Contracts.Vision;

// Sprint 5: вход для распознавания накладной AI-сервисом.
// Provider-нейтральный — байты + content-type, без зависимостей на конкретный SDK.
public sealed record InvoiceImagePayload(
    byte[] ImageBytes,
    string ContentType,
    string? SourceFileName = null)
{
    public static IReadOnlyList<string> SupportedContentTypes { get; } = new[]
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    public bool IsSupported()
    {
        return SupportedContentTypes.Contains(ContentType, StringComparer.OrdinalIgnoreCase);
    }
}
