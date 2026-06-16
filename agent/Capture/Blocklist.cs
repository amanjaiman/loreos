namespace Lore.Agent.Capture;

/// <summary>The user's explicit list of apps and keywords never to capture — the first
/// and cheapest layer of the sensitivity chain. Apps are matched by executable name
/// (case-insensitive, with or without a <c>.exe</c> suffix); keywords are matched as
/// case-insensitive substrings of a window's title or text. Both lists come from user
/// config, so this is the layer the user directly controls.</summary>
public sealed class Blocklist
{
    private readonly HashSet<string> _apps;
    private readonly IReadOnlyList<string> _keywords;

    public Blocklist(IEnumerable<string> apps, IEnumerable<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(keywords);

        _apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string app in apps)
        {
            string normalized = NormalizeApp(app);
            if (normalized.Length > 0)
            {
                _apps.Add(normalized);
            }
        }

        _keywords = keywords
            .Where(static k => !string.IsNullOrWhiteSpace(k))
            .Select(static k => k.Trim())
            .ToArray();
    }

    /// <summary>An empty blocklist — blocks nothing. Useful as a default before config loads.</summary>
    public static Blocklist Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    /// <summary><c>true</c> when <paramref name="executable"/> is a blocklisted app.</summary>
    public bool MatchesApp(string? executable) =>
        !string.IsNullOrWhiteSpace(executable) && _apps.Contains(NormalizeApp(executable));

    /// <summary><c>true</c> when any blocklisted keyword is a substring of
    /// <paramref name="text"/> (case-insensitive).</summary>
    public bool MatchesKeyword(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (string keyword in _keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // "1Password.exe", "1password", " 1Password " all normalize to "1password" so the
    // user's config matches the executable name the window source reports.
    private static string NormalizeApp(string app)
    {
        string trimmed = app.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed;
    }
}
