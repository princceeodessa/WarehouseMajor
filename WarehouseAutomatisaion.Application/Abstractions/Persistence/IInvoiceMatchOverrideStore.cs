using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Sprint 5 enhancement: learning loop. Хранит подтверждённые/исправленные
// оператором матчи распознанных строк накладной с nomenclature_items.
// При следующем распознавании любой накладной с похожим текстом override
// возвращается с Confidence=1.0 — система учится без переобучения модели.
public interface IInvoiceMatchOverrideStore
{
    /// <summary>
    /// Найти ранее подтверждённый матч для распознанной строки.
    /// Поиск по нормализованной форме текста.
    /// Возвращает null если override отсутствует.
    /// </summary>
    Task<NomenclatureRef?> FindOverrideAsync(
        string recognizedText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохранить или обновить override. UPSERT по нормализованной форме —
    /// если связка уже была, увеличиваем usage_count и обновляем last_used_at_utc.
    /// </summary>
    Task SaveOverrideAsync(
        string recognizedText,
        NomenclatureRef matchedItem,
        string actor,
        string? supplierName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Нормализация текста для exact-match lookup: lower-case, удаление пунктуации
    /// и whitespace runs. «Дюбель 6х40 (Серый)» ≈ «дюбель 6х40 серый».
    /// Должна совпадать с NormalizeName в InvoiceLineMatcher.
    /// </summary>
    static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = new char[value.Length];
        var length = 0;
        var lastWasSpace = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                chars[length++] = char.ToLowerInvariant(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && length > 0)
            {
                chars[length++] = ' ';
                lastWasSpace = true;
            }
        }

        while (length > 0 && chars[length - 1] == ' ')
        {
            length--;
        }

        return new string(chars, 0, length);
    }
}
