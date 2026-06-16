namespace Lore.Agent.Capture;

/// <summary>Whether the sensitivity filter let a capture through or dropped it.</summary>
public enum FilterDecision
{
    /// <summary>The text cleared every layer and may proceed to analysis/storage.</summary>
    Allow,

    /// <summary>The text was dropped; nothing downstream sees it.</summary>
    Block,
}

/// <summary>The typed reason a capture was dropped — recorded by <c>CaptureMetrics</c>
/// (T008) so the user can see exactly why Lore declined to record a window. Ordered to
/// mirror the chain: blocklist first, then UIA structural, then regex.</summary>
public enum FilterReason
{
    /// <summary>Not blocked.</summary>
    None,

    /// <summary>The foreground app is on the user's blocklist.</summary>
    BlockedApp,

    /// <summary>A blocklisted keyword appeared in the title or text.</summary>
    BlockedKeyword,

    /// <summary>UI Automation reported protected content (a password field, a secure
    /// window), or could not rule it out — the structural layer fails closed.</summary>
    ProtectedContent,

    /// <summary>A sensitive pattern (SSN, or a Luhn-valid payment-card number) was found.</summary>
    SensitivePattern,
}

/// <summary>The outcome of running text through the sensitivity filter chain. On a block,
/// <see cref="Text"/> is always empty — the trust-critical guarantee that no unfiltered
/// text leaves the filter (constitution §4.4): a dropped capture carries no payload. A
/// plain class (not a record) so the trust-critical coverage measures only real branches,
/// not compiler-generated equality members the chain never uses.</summary>
public sealed class FilterResult
{
    private FilterResult(FilterDecision decision, FilterReason reason, string text)
    {
        Decision = decision;
        Reason = reason;
        Text = text;
    }

    public FilterDecision Decision { get; }

    public FilterReason Reason { get; }

    /// <summary>The text cleared to proceed. Empty whenever <see cref="Blocked"/>.</summary>
    public string Text { get; }

    public bool Blocked => Decision == FilterDecision.Block;

    /// <summary>Allow this text downstream. The caller passes already non-null text (the
    /// filter coalesces it), so this stores it directly.</summary>
    public static FilterResult Allow(string text) =>
        new(FilterDecision.Allow, FilterReason.None, text);

    /// <summary>Drop this capture for the given reason, carrying no text.</summary>
    public static FilterResult Block(FilterReason reason) =>
        new(FilterDecision.Block, reason, string.Empty);
}
