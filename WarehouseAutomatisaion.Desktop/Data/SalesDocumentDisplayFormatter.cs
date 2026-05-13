using System.Globalization;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Data;

public static class SalesDocumentDisplayFormatter
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] PieceItemHints =
    [
        "лампа",
        "просекатель",
        "перчат",
        "мешок",
        "заглуш",
        "элемент",
        "платформа",
        "решет",
        "клемм",
        "комплект",
        "шт"
    ];

    private static readonly string[] MeterItemHints =
    [
        "м/п",
        "п/м",
        "п.м",
        "пог",
        "м/уп",
        "профиль",
        "гардин",
        "потолок",
        "лента",
        "кабель",
        "шина",
        "карниз"
    ];

    public static string NormalizeUnit(string? unit, string? itemName = null)
    {
        var normalized = Clean(unit);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return InferUnitFromItemName(itemName);
        }

        var lower = normalized.ToLower(RuCulture);
        if (IsPieceUnit(lower))
        {
            return "шт";
        }

        if (IsMeterUnit(lower))
        {
            return "м";
        }

        if (IsTechnicalUnitName(lower))
        {
            return InferUnitFromItemName(itemName);
        }

        return normalized;
    }

    private static string InferUnitFromItemName(string? itemName)
    {
        var normalizedName = Clean(itemName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return "шт";
        }

        var lowerName = normalizedName.ToLower(RuCulture);
        if (PieceItemHints.Any(hint => lowerName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            return "шт";
        }

        if (MeterItemHints.Any(hint => lowerName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            return "м";
        }

        return "шт";
    }

    private static bool IsPieceUnit(string value)
    {
        return value is "шт" or "шт." or "штука" or "штуки" or "штук" or "pcs" or "pc";
    }

    private static bool IsMeterUnit(string value)
    {
        return value is "м" or "м." or "метр" or "метра" or "метров" or "meter" or "metre";
    }

    private static bool IsTechnicalUnitName(string value)
    {
        return value.StartsWith("единица ", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("ед. ", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("unit ", StringComparison.OrdinalIgnoreCase);
    }

    private static string Clean(string? value)
    {
        var normalized = TextMojibakeFixer.NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.Trim();
    }
}
