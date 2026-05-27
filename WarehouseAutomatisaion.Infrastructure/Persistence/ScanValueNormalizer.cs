using System.Globalization;
using MySqlConnector;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

// Общая нормализация для поиска по сканировке: UPPER + удаление пробелов/-/_/./
// Используется и Infrastructure DAO (через SQL-выражение NormalizeSql),
// и Tsd-фасадом для InMemory fallback. Договор один: одинаковая нормализация
// и в SQL, и в C# сравнениях.
public static class ScanValueNormalizer
{
    public static string NormalizeSql(string expression)
    {
        return $"""
            UPPER(
                REPLACE(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(COALESCE({expression}, ''), ' ', ''),
                                '-', ''),
                            '_', ''),
                        '/', ''),
                    '.', '')
            )
            """;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Trim()
            .Where(character =>
                !char.IsWhiteSpace(character)
                && character is not '-' and not '_' and not '/' and not '.')
            .ToArray();

        return new string(normalized).ToUpperInvariant();
    }

    public static string ReadString(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            Guid guid => guid.ToString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    public static Guid ReadGuid(MySqlDataReader reader, string name)
    {
        var raw = ReadString(reader, name);
        return Guid.TryParse(raw, out var guid) ? guid : Guid.Empty;
    }
}
