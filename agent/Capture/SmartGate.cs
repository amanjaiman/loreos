namespace Lore.Agent.Capture;

/// <summary>Decides whether a dwelled, extracted, filtered window is worth an inference
/// call — the core of "low idle cost" (constitution NFR). It compares the candidate
/// against recent history with per-content-type heuristics and returns a typed
/// <see cref="GateDecision"/>. The gate runs <em>after</em> extraction and filtering and
/// <em>before</em> analysis, so a window whose content hasn't meaningfully changed never
/// triggers a new model call (acceptance criterion 3).
///
/// <para>Skip rules, in order:</para>
/// <list type="number">
/// <item><b>Coding heartbeat</b> — a coding window re-seen within the heartbeat interval.</item>
/// <item><b>Unchanged</b> — too similar to this window's last capture (per-type bar).</item>
/// <item><b>Recent duplicate</b> — a near-identical capture from any window is still recent.</item>
/// </list></summary>
public sealed class SmartGate
{
    private readonly SmartGateOptions _options;
    private readonly RecentCaptureGate _recent;
    private readonly TimeProvider _time;

    public SmartGate(SmartGateOptions options, RecentCaptureGate recent, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(time);
        _options = options;
        _recent = recent;
        _time = time;
    }

    /// <summary>Evaluate a capture candidate against recent history.</summary>
    public GateDecision Evaluate(WindowSnapshot window, ContentType type, string text)
    {
        ArgumentNullException.ThrowIfNull(window);
        string body = text ?? string.Empty;
        DateTimeOffset now = _time.GetUtcNow();

        CaptureHistoryEntry? last = _recent.LastForWindow(window.Handle, window.Title);
        if (last is not null)
        {
            if (type == ContentType.Coding && now - last.At < _options.CodingHeartbeat)
            {
                return GateDecision.Skip(SkipReason.CodingHeartbeat);
            }

            if (IsUnchanged(type, last.Text, body))
            {
                return GateDecision.Skip(SkipReason.Unchanged);
            }
        }

        if (_recent.HasRecentDuplicate(body, now, _options.RecentDuplicateWindow, _options.DiffThresholdHigh))
        {
            return GateDecision.Skip(SkipReason.RecentDuplicate);
        }

        return GateDecision.Capture;
    }

    /// <summary>Record a capture that was let through, so later candidates can be compared
    /// against it.</summary>
    public void Record(WindowSnapshot window, ContentType type, string text)
    {
        ArgumentNullException.ThrowIfNull(window);
        _recent.Record(new CaptureHistoryEntry(
            window.Handle, window.Title, text ?? string.Empty, type, _time.GetUtcNow()));
    }

    private bool IsUnchanged(ContentType type, string previous, string current)
    {
        double similarity = type == ContentType.Messaging
            ? TextSimilarity.Similarity(Tail(previous), Tail(current)) // new messages append
            : TextSimilarity.Similarity(previous, current);

        if (similarity >= _options.DiffThresholdHigh)
        {
            return true; // clearly the same content
        }

        if (similarity <= _options.DiffThresholdLow)
        {
            return false; // clearly changed enough to be worth capturing
        }

        // Ambiguous band: the per-content-type bar decides.
        double bar = type switch
        {
            ContentType.Reading => _options.ReadingThreshold,
            ContentType.Shopping => _options.ShoppingThreshold,
            _ => _options.DiffThresholdHigh,
        };

        return similarity >= bar;
    }

    private string Tail(string text) =>
        text.Length <= _options.MessagingTailChars
            ? text
            : text[^_options.MessagingTailChars..];
}
