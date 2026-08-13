namespace Lore.Agent.Capture;

/// <summary>The trust-critical path of the entire project (constitution §4.4, §6): one
/// ordered, fail-closed chain that every capture must clear before anything analyzes,
/// stores, or transmits it. No downstream code ever sees text this filter blocked.
///
/// <para>The chain, in order — cheapest and most user-explicit first:</para>
/// <list type="number">
/// <item><b>Blocklist</b> — the user's own apps and keywords.</item>
/// <item><b>UIA structural</b> — password fields / secure windows, via
/// <see cref="IWindowSecurityProbe"/>.</item>
/// <item><b>Regex</b> — SSNs and Luhn-valid payment-card numbers.</item>
/// </list>
///
/// <para>The first matching layer wins and returns a typed <see cref="FilterReason"/>;
/// a block carries no text. Both the window's title and its extracted text are screened,
/// so a sensitive title can't slip through on benign body text or vice versa.</para></summary>
public sealed class SensitivityFilter
{
    private readonly LiveCaptureSettings _settings;
    private readonly IWindowSecurityProbe _securityProbe;

    public SensitivityFilter(LiveCaptureSettings settings, IWindowSecurityProbe securityProbe)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(securityProbe);
        _settings = settings;
        _securityProbe = securityProbe;
    }

    /// <summary>Run <paramref name="window"/> and its extracted <paramref name="text"/>
    /// through the full chain. Returns the first block reason, or
    /// <see cref="FilterResult.Allow"/> with the text when every layer passes.</summary>
    public FilterResult Apply(WindowSnapshot window, string? text)
    {
        ArgumentNullException.ThrowIfNull(window);
        string body = text ?? string.Empty;
        string title = window.Title;

        // Layer 1: blocklist — the user's explicit choices.
        Blocklist blocklist = _settings.Blocklist;
        if (blocklist.MatchesApp(window.ProcessExecutable))
        {
            return FilterResult.Block(FilterReason.BlockedApp);
        }

        if (blocklist.MatchesKeyword(title) || blocklist.MatchesKeyword(body))
        {
            return FilterResult.Block(FilterReason.BlockedKeyword);
        }

        // Layer 2: UIA structural — password fields / secure windows (fails closed).
        if (_securityProbe.HasProtectedContent(window))
        {
            return FilterResult.Block(FilterReason.ProtectedContent);
        }

        // Layer 3: regex — SSN / payment-card patterns.
        if (SensitivePatterns.ContainsSensitive(title) || SensitivePatterns.ContainsSensitive(body))
        {
            return FilterResult.Block(FilterReason.SensitivePattern);
        }

        return FilterResult.Allow(body);
    }

    /// <summary>The document variant of the chain (spec 009): screen free-standing text — a chunk
    /// of an imported document — through the layers that <em>apply to text</em>. The trust bar does
    /// not drop because the text came from a file (constitution §4): the same user
    /// <see cref="Blocklist">keywords</see> and the same trust-critical <see cref="SensitivePatterns"/>
    /// regex run, in the same order. The two window-only layers are skipped because they have no
    /// meaning for a file: there is no foreground app to match, and no live UI element to probe for
    /// a password field. A block carries no text, exactly as <see cref="Apply"/>.</summary>
    public FilterResult ApplyToText(string? text)
    {
        string body = text ?? string.Empty;

        // Layer 1 (keywords): the user's explicit choices apply to any text source.
        if (_settings.Blocklist.MatchesKeyword(body))
        {
            return FilterResult.Block(FilterReason.BlockedKeyword);
        }

        // Layer 3 (regex): SSN / payment-card patterns — the same detector capture uses.
        if (SensitivePatterns.ContainsSensitive(body))
        {
            return FilterResult.Block(FilterReason.SensitivePattern);
        }

        return FilterResult.Allow(body);
    }
}
