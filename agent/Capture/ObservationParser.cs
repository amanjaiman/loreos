using System.Text.Json;

namespace Lore.Agent.Capture;

/// <summary>Parses a model's raw completion into a <see cref="CaptureAnalysis"/>,
/// tolerantly: models wrap JSON in markdown fences, add a sentence of preamble, or trail a
/// comma. A pure function so it is exhaustively unit-testable (constitution §5). Anything
/// it can't turn into a usable observation — non-JSON, missing/blank observation, wrong
/// types — returns <c>null</c>, which the loop treats as "skip", never a crash.</summary>
public static class ObservationParser
{
    private const string DefaultCategory = "general";

    private static readonly JsonDocumentOptions Tolerant = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parse <paramref name="raw"/>, or return <c>null</c> if it carries no usable
    /// observation.</summary>
    public static CaptureAnalysis? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string json = ExtractJsonObject(raw);
        if (json.Length == 0)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, Tolerant);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("observation", out JsonElement observationElement)
                || observationElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string observation = observationElement.GetString()!.Trim();
            if (observation.Length == 0)
            {
                return null; // the model's "nothing worth remembering" signal
            }

            string category = DefaultCategory;
            if (root.TryGetProperty("category", out JsonElement categoryElement)
                && categoryElement.ValueKind == JsonValueKind.String)
            {
                string parsed = categoryElement.GetString()!.Trim();
                if (parsed.Length > 0)
                {
                    category = parsed;
                }
            }

            return new CaptureAnalysis(observation, category);
        }
        catch (JsonException)
        {
            return null; // malformed despite the tolerant options
        }
    }

    // Pull the outermost {...} out of whatever the model wrapped it in (fences, prose).
    private static string ExtractJsonObject(string raw)
    {
        int start = raw.IndexOf('{', StringComparison.Ordinal);
        int end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : string.Empty;
    }
}
