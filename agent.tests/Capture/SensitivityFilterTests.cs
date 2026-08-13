using System.Text.Json.Nodes;
using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class SensitivityFilterTests
{
    private sealed class FakeProbe : IWindowSecurityProbe
    {
        private readonly bool _protected;

        public FakeProbe(bool isProtected) => _protected = isProtected;

        public bool HasProtectedContent(WindowSnapshot window) => _protected;
    }

    private static WindowSnapshot Win(string executable = "editor", string title = "untitled") =>
        new(1, executable, title);

    private static SensitivityFilter Filter(
        IEnumerable<string>? apps = null,
        IEnumerable<string>? keywords = null,
        bool isProtected = false) =>
        new(
            new LiveCaptureSettings(new CaptureOptions
            {
                BlocklistApps = (apps ?? []).ToArray(),
                BlocklistKeywords = (keywords ?? []).ToArray(),
            }),
            new FakeProbe(isProtected));

    // ── The acceptance-criterion-2 case table ────────────────────────────────

    [Fact]
    public void Blocklisted_app_is_dropped_as_BlockedApp()
    {
        SensitivityFilter filter = Filter(apps: new[] { "1password" });

        FilterResult result = filter.Apply(Win(executable: "1Password"), "some text");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.BlockedApp, result.Reason);
    }

    [Fact]
    public void Blocklisted_keyword_in_the_title_is_dropped_as_BlockedKeyword()
    {
        SensitivityFilter filter = Filter(keywords: new[] { "banking" });

        FilterResult result = filter.Apply(Win(title: "Chase Online Banking"), "balance overview");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.BlockedKeyword, result.Reason);
    }

    [Fact]
    public void Blocklisted_keyword_in_the_text_is_dropped_as_BlockedKeyword()
    {
        SensitivityFilter filter = Filter(keywords: new[] { "routing number" });

        FilterResult result = filter.Apply(Win(title: "Notes"), "my routing number is on the check");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.BlockedKeyword, result.Reason);
    }

    [Fact]
    public void A_password_field_is_dropped_as_ProtectedContent()
    {
        SensitivityFilter filter = Filter(isProtected: true);

        FilterResult result = filter.Apply(Win(), "login form");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.ProtectedContent, result.Reason);
    }

    [Fact]
    public void An_ssn_in_the_text_is_dropped_as_SensitivePattern()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.Apply(Win(), "applicant SSN 123-45-6789 on file");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.SensitivePattern, result.Reason);
    }

    [Fact]
    public void A_card_number_in_the_text_is_dropped_as_SensitivePattern()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.Apply(Win(), "pay with 4111 1111 1111 1111 today");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.SensitivePattern, result.Reason);
    }

    [Fact]
    public void A_sensitive_pattern_in_the_title_is_also_dropped()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.Apply(Win(title: "SSN 123-45-6789"), "benign body");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.SensitivePattern, result.Reason);
    }

    [Fact]
    public void A_safe_control_with_a_card_like_but_invalid_number_is_allowed()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.Apply(Win(), "order id 4111111111111112 confirmed");

        Assert.False(result.Blocked);
        Assert.Equal(FilterReason.None, result.Reason);
    }

    [Fact]
    public void Benign_content_is_allowed_and_carries_its_text_through()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.Apply(Win(title: "Trip planning"), "comparing flights to Lisbon");

        Assert.False(result.Blocked);
        Assert.Equal(FilterDecision.Allow, result.Decision);
        Assert.Equal("comparing flights to Lisbon", result.Text);
    }

    [Fact]
    public void Empty_text_on_a_benign_window_is_allowed()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.Apply(Win(), null);

        Assert.False(result.Blocked);
        Assert.Equal(string.Empty, result.Text);
    }

    // ── Ordering / fail-closed precedence ─────────────────────────────────────

    [Fact]
    public void App_block_takes_precedence_over_every_later_layer()
    {
        // Everything is sensitive at once; the app layer must win because it is first.
        SensitivityFilter filter = Filter(
            apps: new[] { "1password" }, keywords: new[] { "banking" }, isProtected: true);

        FilterResult result = filter.Apply(
            Win(executable: "1password", title: "banking"), "ssn 123-45-6789");

        Assert.Equal(FilterReason.BlockedApp, result.Reason);
    }

    [Fact]
    public void Keyword_block_takes_precedence_over_structural_and_regex()
    {
        SensitivityFilter filter = Filter(keywords: new[] { "banking" }, isProtected: true);

        FilterResult result = filter.Apply(Win(title: "banking"), "ssn 123-45-6789");

        Assert.Equal(FilterReason.BlockedKeyword, result.Reason);
    }

    [Fact]
    public void Structural_block_takes_precedence_over_regex()
    {
        SensitivityFilter filter = Filter(isProtected: true);

        FilterResult result = filter.Apply(Win(), "ssn 123-45-6789");

        Assert.Equal(FilterReason.ProtectedContent, result.Reason);
    }

    // ── The trust-critical guarantee ──────────────────────────────────────────

    [Fact]
    public void A_blocked_result_never_carries_text_downstream()
    {
        SensitivityFilter filter = Filter(keywords: new[] { "secret" });

        FilterResult result = filter.Apply(Win(title: "secret"), "highly sensitive body text");

        Assert.True(result.Blocked);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new SensitivityFilter(null!, new FakeProbe(false)));
        Assert.Throws<ArgumentNullException>(() => new SensitivityFilter(
            new LiveCaptureSettings(new CaptureOptions()), null!));
    }

    [Fact]
    public void Apply_rejects_a_null_window()
    {
        SensitivityFilter filter = Filter();
        Assert.Throws<ArgumentNullException>(() => filter.Apply(null!, "text"));
    }

    [Fact]
    public void A_running_filter_uses_an_updated_blocklist_immediately()
    {
        var settings = new LiveCaptureSettings(new CaptureOptions { BlocklistApps = ["editor"] });
        var filter = new SensitivityFilter(settings, new FakeProbe(false));
        Assert.True(filter.Apply(Win(), "ordinary text").Blocked);

        settings.Update(new JsonObject
        {
            ["blocklistApps"] = new JsonArray(),
            ["blocklistKeywords"] = new JsonArray("salary"),
        });

        Assert.False(filter.Apply(Win(), "ordinary text").Blocked);
        Assert.Equal(FilterReason.BlockedKeyword, filter.Apply(Win(), "salary note").Reason);
    }

    // ── ApplyToText: the document variant (spec 009) ──────────────────────────
    // Same Blocklist keyword + SensitivePatterns layers, in the same order; the two
    // window-only layers (app, UIA) do not apply to a file and are skipped.

    [Fact]
    public void ApplyToText_drops_a_blocklisted_keyword()
    {
        SensitivityFilter filter = Filter(keywords: new[] { "salary" });

        FilterResult result = filter.ApplyToText("my salary history is attached");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.BlockedKeyword, result.Reason);
    }

    [Fact]
    public void ApplyToText_drops_an_ssn()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.ApplyToText("applicant SSN 123-45-6789 on file");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.SensitivePattern, result.Reason);
    }

    [Fact]
    public void ApplyToText_drops_a_luhn_valid_card_number()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.ApplyToText("pay with 4111 1111 1111 1111 today");

        Assert.True(result.Blocked);
        Assert.Equal(FilterReason.SensitivePattern, result.Reason);
    }

    [Fact]
    public void ApplyToText_keyword_takes_precedence_over_regex()
    {
        SensitivityFilter filter = Filter(keywords: new[] { "salary" });

        FilterResult result = filter.ApplyToText("salary and ssn 123-45-6789");

        Assert.Equal(FilterReason.BlockedKeyword, result.Reason);
    }

    [Fact]
    public void ApplyToText_allows_benign_text_and_carries_it_through()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.ApplyToText("I am planning a trip to Lisbon in May");

        Assert.False(result.Blocked);
        Assert.Equal(FilterDecision.Allow, result.Decision);
        Assert.Equal("I am planning a trip to Lisbon in May", result.Text);
    }

    [Fact]
    public void ApplyToText_allows_a_card_like_but_invalid_number()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.ApplyToText("order id 4111111111111112 confirmed");

        Assert.False(result.Blocked);
        Assert.Equal(FilterReason.None, result.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ApplyToText_allows_empty_text(string? text)
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.ApplyToText(text);

        Assert.False(result.Blocked);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void ApplyToText_blocked_result_never_carries_text()
    {
        SensitivityFilter filter = Filter();

        FilterResult result = filter.ApplyToText("ssn 123-45-6789 is private");

        Assert.True(result.Blocked);
        Assert.Equal(string.Empty, result.Text);
    }
}
