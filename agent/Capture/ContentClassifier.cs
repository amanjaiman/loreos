namespace Lore.Agent.Capture;

/// <summary>Classifies a window into a <see cref="ContentType"/> from cheap signals: the
/// executable name first (a strong, stable signal — an IDE is an IDE), then keyword cues
/// in the title and text. Pure and deterministic so the gates' per-type behavior is
/// unit-testable. The signal lists are deliberately small and obvious (constitution §4
/// "readable over clever"); they are heuristics meant to be tuned, not a taxonomy.</summary>
public static class ContentClassifier
{
    // Executable names (without .exe) that are unambiguous on their own.
    private static readonly HashSet<string> CodingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "devenv", "rider64", "idea64", "pycharm64", "webstorm64", "goland64",
        "clion64", "sublime_text", "notepad++", "windowsterminal", "powershell", "pwsh",
        "cmd", "wezterm", "alacritty",
    };

    private static readonly HashSet<string> MessagingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "slack", "discord", "teams", "ms-teams", "telegram", "whatsapp", "signal",
        "outlook", "thunderbird",
    };

    // Keyword cues, checked case-insensitively as substrings of the title + text.
    private static readonly string[] ShoppingCues =
    {
        "add to cart", "add to bag", "shopping cart", "proceed to checkout",
        "free shipping", "order total", "buy now", "add to basket",
    };

    private static readonly string[] CodingCues =
    {
        "stack overflow", "pull request", "merge request", "compiler error", "git commit",
        ".py", ".java", ".rs", ".go", ".cpp",
    };

    private static readonly string[] MessagingCues =
    {
        "new message", "sent you a message", "is typing", "unread message",
    };

    private static readonly string[] ReadingCues =
    {
        "min read", "published", "read more", "table of contents",
    };

    /// <summary>Classify <paramref name="window"/> and its extracted
    /// <paramref name="text"/>. Executable-based signals win over keyword cues; within
    /// keyword cues the order is shopping → messaging → coding → reading; everything else
    /// is <see cref="ContentType.Unknown"/>.</summary>
    public static ContentType Classify(WindowSnapshot window, string? text)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (CodingApps.Contains(window.ProcessExecutable))
        {
            return ContentType.Coding;
        }

        if (MessagingApps.Contains(window.ProcessExecutable))
        {
            return ContentType.Messaging;
        }

        string haystack = window.Title + "\n" + (text ?? string.Empty);

        if (ContainsAny(haystack, ShoppingCues))
        {
            return ContentType.Shopping;
        }

        if (ContainsAny(haystack, MessagingCues))
        {
            return ContentType.Messaging;
        }

        if (ContainsAny(haystack, CodingCues))
        {
            return ContentType.Coding;
        }

        if (ContainsAny(haystack, ReadingCues))
        {
            return ContentType.Reading;
        }

        return ContentType.Unknown;
    }

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
