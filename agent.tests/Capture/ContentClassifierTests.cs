using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class ContentClassifierTests
{
    private static WindowSnapshot Win(string executable, string title = "") =>
        new(1, executable, title);

    [Theory]
    [InlineData("code")]
    [InlineData("Code")]      // case-insensitive
    [InlineData("devenv")]
    [InlineData("powershell")]
    public void Developer_apps_are_classified_as_coding(string executable)
    {
        Assert.Equal(ContentType.Coding, ContentClassifier.Classify(Win(executable), "anything"));
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("discord")]
    [InlineData("Outlook")]
    public void Chat_and_mail_apps_are_classified_as_messaging(string executable)
    {
        Assert.Equal(ContentType.Messaging, ContentClassifier.Classify(Win(executable), "anything"));
    }

    [Fact]
    public void The_executable_signal_wins_over_keyword_cues()
    {
        // An IDE window whose text mentions shopping is still Coding — exe is the stronger signal.
        WindowSnapshot ide = Win("code", "checkout branch");
        Assert.Equal(ContentType.Coding, ContentClassifier.Classify(ide, "proceed to checkout"));
    }

    [Fact]
    public void Shopping_cues_in_a_browser_window_classify_as_shopping()
    {
        WindowSnapshot browser = Win("chrome", "Wireless Headphones - Shop");
        Assert.Equal(ContentType.Shopping, ContentClassifier.Classify(browser, "Add to cart — free shipping"));
    }

    [Fact]
    public void Messaging_cues_classify_as_messaging()
    {
        WindowSnapshot browser = Win("chrome", "Webmail");
        Assert.Equal(ContentType.Messaging, ContentClassifier.Classify(browser, "You have 3 unread messages"));
    }

    [Fact]
    public void Coding_cues_classify_a_browser_as_coding()
    {
        WindowSnapshot browser = Win("firefox", "How to parse JSON - Stack Overflow");
        Assert.Equal(ContentType.Coding, ContentClassifier.Classify(browser, "accepted answer"));
    }

    [Fact]
    public void Reading_cues_classify_as_reading()
    {
        WindowSnapshot browser = Win("chrome", "A Long Essay");
        Assert.Equal(ContentType.Reading, ContentClassifier.Classify(browser, "12 min read · published yesterday"));
    }

    [Fact]
    public void Shopping_takes_precedence_over_other_keyword_cues()
    {
        WindowSnapshot browser = Win("chrome", "Deals");
        // Contains both a shopping cue and a reading cue; shopping is checked first.
        Assert.Equal(
            ContentType.Shopping,
            ContentClassifier.Classify(browser, "add to cart — 5 min read"));
    }

    [Fact]
    public void A_window_with_no_signal_is_unknown()
    {
        WindowSnapshot browser = Win("chrome", "Untitled");
        Assert.Equal(ContentType.Unknown, ContentClassifier.Classify(browser, "just some plain words"));
    }

    [Fact]
    public void Null_text_is_tolerated()
    {
        Assert.Equal(ContentType.Unknown, ContentClassifier.Classify(Win("chrome", "Untitled"), null));
    }

    [Fact]
    public void Classify_rejects_a_null_window()
    {
        Assert.Throws<ArgumentNullException>(() => ContentClassifier.Classify(null!, "text"));
    }
}
