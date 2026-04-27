using System.Text;

namespace WarehouseAutomatisaion.Desktop.Text;

public static class TextMojibakeFixer
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, false);
    private static readonly Encoding Cp1251;

    static TextMojibakeFixer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }

        return NormalizeCandidate(ReplaceKnownArtifacts(value));
    }

    private static string ReplaceKnownArtifacts(string value)
    {
        return value
            .Replace("\u0421\u20AC\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0421\u20AC\u0421", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0421\u20AC\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0420\u0458", "\u043C", StringComparison.Ordinal)
            .Replace("\u0421\u20AC\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0421\u0454\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0420\u0406,S", "\u20BD", StringComparison.Ordinal)
            .Replace("\u0432\u201A\u0405", "\u20BD", StringComparison.Ordinal)
            .Replace("\u00E2\u201A\u00BD", "\u20BD", StringComparison.Ordinal)
            .Replace("\u0413\u045E\u0432\u201A\u0412\u0405", "\u20BD", StringComparison.Ordinal)
            .Replace("\u0420\u0406\u0420\u201A\u0432\u20AC\u045A", "\u2014", StringComparison.Ordinal)
            .Replace("\u0420\u0406\u0420\u201A\u0432\u20AC\u045A", "\u2014", StringComparison.Ordinal)
            .Replace("\u0432\u20AC\u201D", "\u2014", StringComparison.Ordinal);
    }

    private static string NormalizeCandidate(string original)
    {
        var originalScore = GetQualityScore(original);
        if (originalScore <= 0)
        {
            return original;
        }

        var best = original;
        var bestScore = originalScore;
        var current = original;

        for (var i = 0; i < 4; i++)
        {
            current = TransformOnce(current);
            if (IntroducesReplacementGlyphs(original, current))
            {
                break;
            }

            var score = GetQualityScore(current);
            if (score < bestScore)
            {
                best = current;
                bestScore = score;
            }
        }

        return best;
    }

    private static string TransformOnce(string text)
    {
        return Utf8.GetString(Cp1251.GetBytes(text));
    }

    private static bool IntroducesReplacementGlyphs(string original, string candidate)
    {
        return candidate.Contains('\uFFFD', StringComparison.Ordinal) ||
               CountChar(candidate, '?') > CountChar(original, '?');
    }

    private static int CountChar(string value, char target)
    {
        var count = 0;
        foreach (var ch in value)
        {
            if (ch == target)
            {
                count++;
            }
        }

        return count;
    }

    private static int GetQualityScore(string text)
    {
        var score = 0;
        var chars = text.AsSpan();
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            var code = (int)ch;
            if (code == 0xFFFD)
            {
                score += 1000;
            }

            if ((code >= 0x0080 && code <= 0x009F) ||
                (code >= 0x00A0 && code <= 0x00BF) ||
                (code >= 0x201A && code <= 0x203A) ||
                code is 0x20AC or 0x2122)
            {
                score += 20;
            }

            if (i < chars.Length - 1 && IsMojibakeLead(ch) && IsLikelyMojibakeTrail(chars[i + 1]))
            {
                score += 35;
            }
        }

        return score;
    }

    private static bool IsMojibakeLead(char ch)
    {
        return ch is '\u0420' or '\u0421';
    }

    private static bool IsLikelyMojibakeTrail(char ch)
    {
        var code = (int)ch;
        return IsMojibakeCyrillicTrail(ch) ||
               (code >= 0x00A0 && code <= 0x00BF) ||
               (code >= 0x201A && code <= 0x203A) ||
               code is 0x20AC or 0x2122;
    }

    private static bool IsMojibakeCyrillicTrail(char ch)
    {
        return ch is '\u0402' or '\u0403' or '\u0405' or '\u0406' or '\u0408' or '\u0409' or '\u040A' or '\u040B' or '\u040C' or '\u040E' or '\u040F'
            or '\u0452' or '\u0453' or '\u0455' or '\u0456' or '\u0458' or '\u0459' or '\u045A' or '\u045B' or '\u045C' or '\u045E' or '\u045F';
    }
}
