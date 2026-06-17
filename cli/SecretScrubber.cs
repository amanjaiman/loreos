using System.Text.Json.Nodes;

namespace Lore.Cli;

/// <summary>Defense-in-depth secret redaction for CLI output (spec 007 non-functional: "never
/// prints secret material, even in <c>--json</c>"). The local API already strips inline key
/// material from <c>GET /config</c>, but the CLI re-scrubs anything it is about to print so a
/// future field can never leak through a surface the user reads or pipes. Operates on a
/// <see cref="JsonNode"/> tree in place, replacing the <em>value</em> of any secret-looking key
/// with a fixed placeholder — keys that are <b>handles</b> (ending in <c>_ref</c>) are left
/// intact, since they are deliberately returned and carry no secret.</summary>
internal static class SecretScrubber
{
    /// <summary>What a redacted value is replaced with.</summary>
    public const string Placeholder = "***redacted***";

    /// <summary>Walk <paramref name="node"/> and redact the value of every secret-looking key.</summary>
    public static void Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot keys: we reassign values while iterating.
                foreach (string key in obj.Select(pair => pair.Key).ToArray())
                {
                    if (IsSecretKey(key))
                    {
                        obj[key] = Placeholder;
                    }
                    else
                    {
                        Scrub(obj[key]);
                    }
                }

                break;

            case JsonArray array:
                foreach (JsonNode? element in array)
                {
                    Scrub(element);
                }

                break;
        }
    }

    // A key names a secret if it looks like raw key material — but never a "*_ref" handle, which
    // is the safe pointer the keystore design hands back (constitution §4.2). Compared
    // case-insensitively without allocating a lowercased copy.
    private static bool IsSecretKey(string key)
    {
        if (key.EndsWith("_ref", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Is("api_key") || Is("apikey") || Is("secret") || Is("password") || Is("token") || Is("authorization")
            || key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_token", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_key", StringComparison.OrdinalIgnoreCase);

        bool Is(string name) => string.Equals(key, name, StringComparison.OrdinalIgnoreCase);
    }
}
