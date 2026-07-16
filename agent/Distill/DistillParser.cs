using System.Text.Json;
using Lore.Agent.Memory;

namespace Lore.Agent.Distill;

/// <summary>Parses the distiller model's completion into candidate facts, tolerantly
/// (fences, preamble, trailing commas) and defensively (v2-001). A pure function.
/// Distinguishes the two non-happy outcomes the pipeline treats differently:
/// <c>null</c> = unparseable (a <c>distill_failed</c> decision) versus an empty list =
/// the model's correct "nothing durable here" answer.</summary>
public static class DistillParser
{
    /// <summary>Most facts accepted from one episode; the strongest win.</summary>
    public const int MaxFactsPerEpisode = 3;

    /// <summary>Longest accepted statement, per the prompt's contract.</summary>
    public const int MaxStatementChars = 200;

    /// <summary>Longest accepted state horizon; larger values are capped, non-positive
    /// values are ignored.</summary>
    public const int MaxHorizonDays = 365;

    private static readonly JsonDocumentOptions Tolerant = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parse <paramref name="raw"/> into accepted facts, or <c>null</c> when the
    /// completion carries no parseable <c>facts</c> array at all.</summary>
    public static IReadOnlyList<CandidateFact>? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        int start = raw.IndexOf('{', StringComparison.Ordinal);
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw[start..(end + 1)], Tolerant);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("facts", out JsonElement facts)
                || facts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var accepted = new List<CandidateFact>();
            foreach (JsonElement element in facts.EnumerateArray())
            {
                CandidateFact? fact = ParseFact(element);
                if (fact is not null)
                {
                    accepted.Add(fact);
                }
            }

            // The cap forces ranking: only the strongest candidates from one episode.
            return accepted
                .OrderByDescending(fact => fact.Confidence)
                .Take(MaxFactsPerEpisode)
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // One fact object → a CandidateFact, or null when it is not usable (wrong types,
    // unknown kind, blank/oversized statement). Bad facts are dropped, never coerced
    // into the store's closed vocabulary.
    private static CandidateFact? ParseFact(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? statement = GetString(element, "statement")?.Trim();
        string? kind = CanonicalKind(GetString(element, "kind"));
        if (string.IsNullOrEmpty(statement) || statement.Length > MaxStatementChars || kind is null)
        {
            return null;
        }

        double confidence = 0.5;
        if (element.TryGetProperty("confidence", out JsonElement confidenceElement)
            && confidenceElement.ValueKind == JsonValueKind.Number)
        {
            confidence = Math.Clamp(confidenceElement.GetDouble(), 0.0, 1.0);
        }

        int? horizonDays = null;
        if (kind == MemoryKinds.State
            && element.TryGetProperty("horizon_days", out JsonElement horizonElement)
            && horizonElement.ValueKind == JsonValueKind.Number
            && horizonElement.TryGetInt32(out int days)
            && days > 0)
        {
            horizonDays = Math.Min(days, MaxHorizonDays);
        }

        return new CandidateFact(statement, kind!, confidence, horizonDays);
    }

    // Map the model's kind (any casing) onto the closed vocabulary's canonical value.
    private static string? CanonicalKind(string? kind) =>
        kind is null
            ? null
            : MemoryKinds.All.FirstOrDefault(
                known => string.Equals(known, kind.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
