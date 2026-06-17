namespace Lore.Agent.Capture;

/// <summary>The kind of activity a window represents, used by the smart gate (T005) to
/// pick per-type capture heuristics — e.g. a reading page is worth re-capturing as you
/// scroll, a coding window is mostly heartbeat noise. Classification is a cheap heuristic
/// over the window's executable, title, and text; it is a hint, not a guarantee.</summary>
public enum ContentType
{
    /// <summary>No strong signal — treated with the default gating thresholds.</summary>
    Unknown,

    /// <summary>Long-form reading: articles, docs, PDFs, web pages of prose.</summary>
    Reading,

    /// <summary>Shopping: product pages, carts, checkout.</summary>
    Shopping,

    /// <summary>Messaging: chat and email clients.</summary>
    Messaging,

    /// <summary>Coding: editors, IDEs, terminals, developer sites.</summary>
    Coding,
}
