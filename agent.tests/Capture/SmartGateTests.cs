using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class SmartGateTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private static WindowSnapshot Win(string title = "doc", long handle = 1) =>
        new(handle, "app", title);

    private static (SmartGate Gate, FakeTimeProvider Time) Build(SmartGateOptions? options = null)
    {
        var time = new FakeTimeProvider();
        var recent = new RecentCaptureGate(maxHistory: (options ?? new SmartGateOptions()).MaxCaptureHistory);
        return (new SmartGate(options ?? new SmartGateOptions(), recent, time), time);
    }

    [Fact]
    public void A_window_never_captured_before_is_captured()
    {
        (SmartGate gate, _) = Build();

        GateDecision decision = gate.Evaluate(Win(), ContentType.Reading, "fresh content");

        Assert.True(decision.ShouldCapture);
        Assert.Equal(SkipReason.None, decision.Reason);
    }

    [Fact]
    public void An_unchanged_window_does_not_trigger_analysis()
    {
        // Acceptance criterion 3: identical content since the last capture → skip.
        (SmartGate gate, _) = Build();
        gate.Record(Win(), ContentType.Reading, "the same article text");

        GateDecision decision = gate.Evaluate(Win(), ContentType.Reading, "the same article text");

        Assert.False(decision.ShouldCapture);
        Assert.Equal(SkipReason.Unchanged, decision.Reason);
    }

    [Fact]
    public void Substantially_changed_content_is_captured_again()
    {
        (SmartGate gate, _) = Build();
        gate.Record(Win(), ContentType.Reading, "alpha beta gamma delta");

        GateDecision decision = gate.Evaluate(Win(), ContentType.Reading, "nine ten eleven twelve");

        Assert.True(decision.ShouldCapture);
    }

    [Fact]
    public void Per_content_type_thresholds_decide_the_ambiguous_band()
    {
        // Tuned so the two texts have Jaccard = 0.4, which sits between Shopping's bar
        // (0.3 → skip) and Reading's bar (0.5 → capture), inside the (low, high) band.
        var options = new SmartGateOptions
        {
            DiffThresholdLow = 0.1,
            DiffThresholdHigh = 0.95,
            ReadingThreshold = 0.5,
            ShoppingThreshold = 0.3,
        };
        const string previous = "a b c d e f g"; // 7 tokens
        const string current = "a b c d h i j";  // shares 4, union 10 → 0.4

        (SmartGate readingGate, _) = Build(options);
        readingGate.Record(Win(), ContentType.Reading, previous);
        Assert.True(readingGate.Evaluate(Win(), ContentType.Reading, current).ShouldCapture);

        (SmartGate shoppingGate, _) = Build(options);
        shoppingGate.Record(Win(), ContentType.Shopping, previous);
        GateDecision shopping = shoppingGate.Evaluate(Win(), ContentType.Shopping, current);
        Assert.False(shopping.ShouldCapture);
        Assert.Equal(SkipReason.Unchanged, shopping.Reason);
    }

    [Fact]
    public void Unknown_content_in_the_ambiguous_band_uses_the_high_bar_and_is_captured()
    {
        var options = new SmartGateOptions { DiffThresholdLow = 0.1, DiffThresholdHigh = 0.95 };
        (SmartGate gate, _) = Build(options);
        gate.Record(Win(), ContentType.Unknown, "a b c d e f g");

        // Jaccard 0.4 < the high bar (0.95) → captured for Unknown.
        Assert.True(gate.Evaluate(Win(), ContentType.Unknown, "a b c d h i j").ShouldCapture);
    }

    [Fact]
    public void A_coding_window_reseen_within_the_heartbeat_is_skipped_even_if_changed()
    {
        var options = new SmartGateOptions { CodingHeartbeat = TimeSpan.FromMinutes(2) };
        (SmartGate gate, FakeTimeProvider time) = Build(options);
        gate.Record(Win(), ContentType.Coding, "int x = 1;");

        time.Advance(TimeSpan.FromMinutes(1)); // inside the heartbeat
        GateDecision decision = gate.Evaluate(Win(), ContentType.Coding, "totally different code here");

        Assert.False(decision.ShouldCapture);
        Assert.Equal(SkipReason.CodingHeartbeat, decision.Reason);
    }

    [Fact]
    public void A_coding_window_past_the_heartbeat_with_changed_content_is_captured()
    {
        var options = new SmartGateOptions { CodingHeartbeat = TimeSpan.FromMinutes(2) };
        (SmartGate gate, FakeTimeProvider time) = Build(options);
        gate.Record(Win(), ContentType.Coding, "int x = 1;");

        time.Advance(TimeSpan.FromMinutes(3)); // past the heartbeat
        Assert.True(gate.Evaluate(Win(), ContentType.Coding, "different code entirely now").ShouldCapture);
    }

    [Fact]
    public void Messaging_compares_only_the_tail_so_a_new_message_is_captured()
    {
        var options = new SmartGateOptions { MessagingTailChars = 10 };
        (SmartGate gate, _) = Build(options);
        gate.Record(Win(), ContentType.Messaging, "hello there friend");

        // A new message appends to the end, changing the tail → captured.
        GateDecision decision = gate.Evaluate(Win(), ContentType.Messaging, "hello there friend how are you");

        Assert.True(decision.ShouldCapture);
    }

    [Fact]
    public void Messaging_with_no_new_message_is_unchanged()
    {
        var options = new SmartGateOptions { MessagingTailChars = 10 };
        (SmartGate gate, _) = Build(options);
        gate.Record(Win(), ContentType.Messaging, "a long conversation that has not changed at all");

        GateDecision decision = gate.Evaluate(
            Win(), ContentType.Messaging, "a long conversation that has not changed at all");

        Assert.False(decision.ShouldCapture);
        Assert.Equal(SkipReason.Unchanged, decision.Reason);
    }

    [Fact]
    public void Messaging_shorter_than_the_tail_window_compares_the_whole_text()
    {
        // Text shorter than MessagingTailChars exercises the "return whole text" path.
        var options = new SmartGateOptions { MessagingTailChars = 1000 };
        (SmartGate gate, _) = Build(options);
        gate.Record(Win(), ContentType.Messaging, "hi");

        GateDecision decision = gate.Evaluate(Win(), ContentType.Messaging, "hi");

        Assert.False(decision.ShouldCapture); // identical short text → unchanged
        Assert.Equal(SkipReason.Unchanged, decision.Reason);
    }

    [Fact]
    public void A_near_identical_capture_from_another_window_is_a_recent_duplicate()
    {
        (SmartGate gate, _) = Build();
        gate.Record(Win(title: "window one", handle: 1), ContentType.Reading, "shared report contents here");

        GateDecision decision = gate.Evaluate(
            Win(title: "window two", handle: 2), ContentType.Reading, "shared report contents here");

        Assert.False(decision.ShouldCapture);
        Assert.Equal(SkipReason.RecentDuplicate, decision.Reason);
    }

    [Fact]
    public void A_recent_duplicate_is_allowed_again_once_the_window_passes()
    {
        var options = new SmartGateOptions { RecentDuplicateWindow = TimeSpan.FromSeconds(30) };
        (SmartGate gate, FakeTimeProvider time) = Build(options);
        gate.Record(Win(title: "window one", handle: 1), ContentType.Reading, "shared report contents here");

        time.Advance(TimeSpan.FromSeconds(31)); // the duplicate window has passed
        GateDecision decision = gate.Evaluate(
            Win(title: "window two", handle: 2), ContentType.Reading, "shared report contents here");

        Assert.True(decision.ShouldCapture);
    }

    [Fact]
    public void Null_text_is_treated_as_empty_and_does_not_throw()
    {
        (SmartGate gate, _) = Build();
        gate.Record(Win(), ContentType.Unknown, null!);

        GateDecision decision = gate.Evaluate(Win(), ContentType.Unknown, null!);

        // Two empty texts are identical → unchanged.
        Assert.False(decision.ShouldCapture);
        Assert.Equal(SkipReason.Unchanged, decision.Reason);
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var options = new SmartGateOptions();
        var recent = new RecentCaptureGate(1);
        Assert.Throws<ArgumentNullException>(() => new SmartGate(null!, recent, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SmartGate(options, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SmartGate(options, recent, null!));
    }

    [Fact]
    public void Evaluate_rejects_a_null_window()
    {
        (SmartGate gate, _) = Build();
        Assert.Throws<ArgumentNullException>(() => gate.Evaluate(null!, ContentType.Unknown, "x"));
    }
}
