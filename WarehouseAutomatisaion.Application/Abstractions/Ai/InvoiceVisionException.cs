namespace WarehouseAutomatisaion.Application.Abstractions.Ai;

// Sprint 5: типизированные ошибки AI-сервиса распознавания.
// Используются вместо raw Exception чтобы UI / orchestrator знали как реагировать
// (показать spinner ещё раз / попросить файл получше / упасть с алертом).
public sealed class InvoiceVisionException : Exception
{
    public InvoiceVisionFailureKind Kind { get; }

    public InvoiceVisionException(InvoiceVisionFailureKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

public enum InvoiceVisionFailureKind
{
    /// <summary>API провайдера временно недоступен (5xx, timeout).</summary>
    ProviderUnavailable,

    /// <summary>Превышен rate limit (429). Можно ретраить через backoff.</summary>
    RateLimited,

    /// <summary>Сетевая ошибка до провайдера.</summary>
    NetworkError,

    /// <summary>Файл не подходит как изображение (битый / неподдерживаемый формат).</summary>
    InvalidImage,

    /// <summary>Provider вернул не-JSON или JSON не валиден по схеме.</summary>
    MalformedResponse,

    /// <summary>Закончилась квота / биллинг (402).</summary>
    QuotaExceeded,

    /// <summary>Auth ошибка — неверный или истёкший API key.</summary>
    AuthenticationFailed,

    /// <summary>Всё остальное.</summary>
    Other
}
