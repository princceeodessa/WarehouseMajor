using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Services;

// Sprint 5: сопоставляет распознанные строки накладной с номенклатурой пользователя.
// Чистая функция: на вход — recognized lines + catalog snapshot, на выход — matches с кандидатами.
// Стратегия (по убыванию confidence):
//   1. ExactCode    — точный match по коду/артикулу (если AI распознал SKU)
//   2. ExactName    — точный match по нормализованному названию (без регистра, без пробелов в начале/конце)
//   3. PartialName  — одно содержит другое как substring
//   4. SemanticName — embedding cosine ≥ 0.82 (Sprint 11, опционально)
//   5. FuzzyName    — Levenshtein с порогом (для опечаток / разных формулировок)
//   6. NoMatch      — оставляем для ручного выбора в UI
public sealed class InvoiceLineMatcher
{
    private const double FuzzyMatchThreshold = 0.65;
    private const double SemanticStrongThreshold = 0.82;  // высокая уверенность — почти точный матч
    private const double SemanticCandidateThreshold = 0.65; // достаточно для попадания в alternatives
    private const int MaxAlternatives = 5;

    public IReadOnlyList<MatchedInvoiceLine> Match(
        IReadOnlyList<InvoiceLineItem> recognizedLines,
        IReadOnlyList<NomenclatureRef> catalog,
        EmbeddingMatchContext? embeddingContext = null)
    {
        if (recognizedLines.Count == 0)
        {
            return Array.Empty<MatchedInvoiceLine>();
        }

        // Прединдексируем каталог для быстрого matching.
        var byCode = new Dictionary<string, NomenclatureRef>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedName = new Dictionary<string, NomenclatureRef>(StringComparer.OrdinalIgnoreCase);
        var normalizedNames = new List<(string Normalized, NomenclatureRef Item)>(catalog.Count);

        foreach (var item in catalog)
        {
            if (!string.IsNullOrWhiteSpace(item.Code))
            {
                byCode[item.Code.Trim()] = item;
            }

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                var normalized = NormalizeName(item.Name);
                if (!byNormalizedName.ContainsKey(normalized))
                {
                    byNormalizedName[normalized] = item;
                }
                normalizedNames.Add((normalized, item));
            }
        }

        // Sprint 11: подготовим catalog embeddings для cosine search.
        // Делаем массив параллельный catalog'у, чтобы за O(catalog) пройтись на каждый line.
        List<(NomenclatureRef Item, float[] Vec)>? catalogVectors = null;
        if (embeddingContext is not null && embeddingContext.CatalogEmbeddings.Count > 0)
        {
            catalogVectors = new List<(NomenclatureRef, float[])>(embeddingContext.CatalogEmbeddings.Count);
            foreach (var item in catalog)
            {
                if (Guid.TryParse(item.Id, out var itemId)
                    && embeddingContext.CatalogEmbeddings.TryGetValue(itemId, out var vec))
                {
                    catalogVectors.Add((item, vec));
                }
            }
        }

        var results = new List<MatchedInvoiceLine>(recognizedLines.Count);
        for (var i = 0; i < recognizedLines.Count; i++)
        {
            var line = recognizedLines[i];
            float[]? lineVec = null;
            if (embeddingContext is not null
                && i < embeddingContext.LineEmbeddings.Count
                && embeddingContext.LineEmbeddings[i] is { Length: > 0 } v)
            {
                lineVec = v;
            }
            results.Add(MatchSingle(line, byCode, byNormalizedName, normalizedNames, catalogVectors, lineVec));
        }

        return results;
    }

    private static MatchedInvoiceLine MatchSingle(
        InvoiceLineItem line,
        Dictionary<string, NomenclatureRef> byCode,
        Dictionary<string, NomenclatureRef> byNormalizedName,
        List<(string Normalized, NomenclatureRef Item)> normalizedNames,
        List<(NomenclatureRef Item, float[] Vec)>? catalogVectors,
        float[]? lineVec)
    {
        // 1. Точный код.
        if (!string.IsNullOrWhiteSpace(line.Sku)
            && byCode.TryGetValue(line.Sku.Trim(), out var byCodeMatch))
        {
            return new MatchedInvoiceLine(
                Source: line,
                BestMatch: byCodeMatch,
                Alternatives: Array.Empty<MatchCandidate>(),
                Kind: MatchKind.ExactCode,
                Confidence: 1.0);
        }

        // 2. Точное имя.
        var normalizedQuery = NormalizeName(line.Name);
        if (!string.IsNullOrWhiteSpace(normalizedQuery)
            && byNormalizedName.TryGetValue(normalizedQuery, out var byNameMatch))
        {
            return new MatchedInvoiceLine(
                Source: line,
                BestMatch: byNameMatch,
                Alternatives: Array.Empty<MatchCandidate>(),
                Kind: MatchKind.ExactName,
                Confidence: 0.95);
        }

        // 3-5. Partial / Semantic / Fuzzy — все кандидаты в одну кучу, ranking по score.
        var candidates = ScoreCandidates(normalizedQuery, normalizedNames);

        // Sprint 11: добавляем семантические кандидаты если есть embeddings.
        if (catalogVectors is not null && lineVec is not null)
        {
            var semanticCandidates = ScoreSemanticCandidates(lineVec, catalogVectors);
            candidates.AddRange(semanticCandidates);
        }

        if (candidates.Count == 0)
        {
            return new MatchedInvoiceLine(
                Source: line,
                BestMatch: null,
                Alternatives: Array.Empty<MatchCandidate>(),
                Kind: MatchKind.NoMatch,
                Confidence: 0.0);
        }

        // Дедуплицируем по Item.Id, оставляя лучший score.
        var bestByItemId = new Dictionary<string, MatchCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            if (!bestByItemId.TryGetValue(c.Item.Id, out var existing) || c.Score > existing.Score)
            {
                bestByItemId[c.Item.Id] = c;
            }
        }

        var ranked = bestByItemId.Values.OrderByDescending(c => c.Score).Take(MaxAlternatives).ToList();
        var best = ranked[0];
        var alternatives = ranked.Skip(1).ToArray();

        return new MatchedInvoiceLine(
            Source: line,
            BestMatch: best.Item,
            Alternatives: alternatives,
            Kind: best.Kind,
            Confidence: best.Score);
    }

    // Sprint 11: cosine similarity между line embedding и каталогом.
    private static List<MatchCandidate> ScoreSemanticCandidates(
        float[] lineVec,
        List<(NomenclatureRef Item, float[] Vec)> catalogVectors)
    {
        var lineNorm = VectorNorm(lineVec);
        if (lineNorm <= 0f)
        {
            return new List<MatchCandidate>();
        }

        var scored = new List<MatchCandidate>();
        foreach (var (item, vec) in catalogVectors)
        {
            if (vec.Length != lineVec.Length)
            {
                continue;
            }
            var sim = CosineSimilarity(lineVec, vec, lineNorm);
            if (sim < SemanticCandidateThreshold)
            {
                continue;
            }
            // Sprint 11 scoring: чистый cosine score. Сильные семантические матчи (>0.82)
            // обходят PartialName/FuzzyName в финальном ranking.
            scored.Add(new MatchCandidate(item, sim, MatchKind.SemanticName));
        }

        return scored.OrderByDescending(c => c.Score).Take(MaxAlternatives).ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b, double normA)
    {
        // Pre-computed normA — экономит sqrt на каждой паре.
        double dot = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normB += b[i] * b[i];
        }
        normB = Math.Sqrt(normB);
        if (normA <= 0 || normB <= 0)
        {
            return 0;
        }
        var sim = dot / (normA * normB);
        // Clamp [0..1] — OpenAI embeddings нормализованы, отрицательные значения редкие но возможны.
        return Math.Max(0, Math.Min(1, sim));
    }

    private static double VectorNorm(float[] v)
    {
        double sum = 0;
        for (var i = 0; i < v.Length; i++)
        {
            sum += v[i] * v[i];
        }
        return Math.Sqrt(sum);
    }

    private static List<MatchCandidate> ScoreCandidates(
        string normalizedQuery,
        List<(string Normalized, NomenclatureRef Item)> catalog)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new List<MatchCandidate>();
        }

        var scored = new List<MatchCandidate>();

        foreach (var (normalized, item) in catalog)
        {
            // Partial: одна строка содержит другую.
            if (normalized.Contains(normalizedQuery, StringComparison.Ordinal)
                || normalizedQuery.Contains(normalized, StringComparison.Ordinal))
            {
                // Уверенность зависит от того насколько query покрывает item (или наоборот).
                var shorterLength = Math.Min(normalized.Length, normalizedQuery.Length);
                var longerLength = Math.Max(normalized.Length, normalizedQuery.Length);
                var ratio = longerLength == 0 ? 0.0 : (double)shorterLength / longerLength;
                var score = 0.6 + ratio * 0.25; // 0.6..0.85
                scored.Add(new MatchCandidate(item, score, MatchKind.PartialName));
                continue;
            }

            // Fuzzy: Levenshtein-based similarity.
            var similarity = ComputeSimilarity(normalizedQuery, normalized);
            if (similarity >= FuzzyMatchThreshold)
            {
                scored.Add(new MatchCandidate(item, similarity * 0.85, MatchKind.FuzzyName));
            }
        }

        return scored
            .OrderByDescending(c => c.Score)
            .Take(MaxAlternatives)
            .ToList();
    }

    /// <summary>
    /// Нормализация для сравнения: lower-case, удаление лишних пробелов и пунктуации.
    /// «Дюбель 6х40 (Серый)» → «дюбель 6х40 серый».
    /// </summary>
    private static string NormalizeName(string? value)
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

        // Убираем trailing space.
        while (length > 0 && chars[length - 1] == ' ')
        {
            length--;
        }

        return new string(chars, 0, length);
    }

    /// <summary>
    /// Сходство на основе Levenshtein. 0.0 = ничего общего, 1.0 = идентично.
    /// </summary>
    private static double ComputeSimilarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1.0;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        var distance = LevenshteinDistance(a, b);
        var maxLength = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a == b)
        {
            return 0;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
