namespace Lore.Agent.Capture.Episodes;

/// <summary>Groups post-filter observations into episodes with cheap local signals —
/// no inference calls (v2-001). Pure logic over injected time values: the capture loop
/// owns the clock; this class only compares timestamps it is handed, so tests drive it
/// with recorded traces. Not thread-safe by design — the capture loop is single-threaded.</summary>
public sealed class EpisodeBuilder
{
    // Floor on a sample's time weight (v2-008 R6.3). Time-spent leads selection, but it
    // must never silence diversity outright: a brief look at something genuinely new still
    // scores above a long stretch that is 95% the same as a sample already chosen.
    private const double MinTimeWeight = 0.05;

    private readonly EpisodeOptions _options;
    private readonly List<CapturedObservation> _samples = [];
    private readonly List<string> _executables = [];
    private readonly List<string> _titles = [];
    private readonly Dictionary<ContentType, int> _contentTypes = [];

    private string _id = string.Empty;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _lastAt;
    private string _lastText = string.Empty;
    private int _count;

    public EpisodeBuilder(EpisodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxObservations < 2)
        {
            throw new ArgumentException("capture.episodes maxObservations must be at least 2", nameof(options));
        }

        if (options.MaxAge <= TimeSpan.Zero)
        {
            throw new ArgumentException("capture.episodes maxAge must be positive", nameof(options));
        }

        if (options.MaxSamples < 2)
        {
            throw new ArgumentException("capture.episodes maxSamples must be at least 2", nameof(options));
        }

        if (options.SampleMaxChars < 1)
        {
            throw new ArgumentException("capture.episodes sampleMaxChars must be at least 1", nameof(options));
        }

        _options = options;
    }

    /// <summary>Whether an episode is currently open.</summary>
    public bool HasOpenEpisode => _count > 0;

    /// <summary>Feed one observation. Returns the episode this observation CLOSED
    /// (by breaking continuity or hitting a bound), or <c>null</c> when it simply
    /// joined or opened one. The observation itself always lands in an open episode.</summary>
    public Episode? Add(CapturedObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        Episode? closed = null;
        if (HasOpenEpisode && !Joins(observation))
        {
            closed = CloseOpen(_lastAt);
        }

        if (!HasOpenEpisode)
        {
            Open(observation);
        }
        else
        {
            Append(observation);
        }

        // Bounds are checked after the append so the triggering observation stays in
        // the episode it grew — the next observation starts fresh. A just-opened episode
        // (count 1, age zero) can never trip a bound, so a continuity-break close and a
        // bound close are mutually exclusive within one Add.
        if (_count >= _options.MaxObservations || _lastAt - _startedAt >= _options.MaxAge)
        {
            return CloseOpen(_lastAt);
        }

        return closed;
    }

    /// <summary>Close the open episode when nothing has arrived for
    /// <see cref="EpisodeOptions.IdleTimeout"/>. Called every capture tick.</summary>
    public Episode? CloseIfIdle(DateTimeOffset now) =>
        HasOpenEpisode && now - _lastAt >= _options.IdleTimeout ? CloseOpen(_lastAt) : null;

    /// <summary>Close and return the open episode regardless of timing (agent shutdown
    /// flushes so a day's last episode is never lost).</summary>
    public Episode? Flush() => HasOpenEpisode ? CloseOpen(_lastAt) : null;

    // An observation joins when it is RELATED (same app, similar title, or similar text)
    // AND recent enough. Cheap signals only.
    private bool Joins(CapturedObservation observation)
    {
        if (observation.At - _lastAt >= _options.ContinuityGap)
        {
            return false;
        }

        bool sameApp = _executables.Contains(observation.Executable, StringComparer.OrdinalIgnoreCase);
        return sameApp
            || TextSimilarity.Similarity(_titles[^1], observation.Title) >= _options.TitleSimilarityThreshold
            || TextSimilarity.Similarity(_lastText, observation.Text) >= _options.TextSimilarityThreshold;
    }

    private void Open(CapturedObservation observation)
    {
        _id = "ep-" + Guid.NewGuid().ToString("N")[..12];
        _startedAt = observation.At;
        Append(observation, opening: true);
    }

    private void Append(CapturedObservation observation, bool opening = false)
    {
        bool duplicate = !opening
            && TextSimilarity.Similarity(_lastText, observation.Text) >= _options.DuplicateThreshold;

        _lastAt = observation.At;
        _lastText = observation.Text;
        _count++;

        if (!_executables.Contains(observation.Executable, StringComparer.OrdinalIgnoreCase))
        {
            _executables.Add(observation.Executable);
        }

        if (_titles.Count == 0 || !string.Equals(_titles[^1], observation.Title, StringComparison.Ordinal))
        {
            _titles.Add(observation.Title);
        }

        // Tally EVERY observation's content type, including near-duplicates that are not
        // kept as samples (v2-008 R6.1). The mix is about where the time went, so the ten
        // repeat readings of one product page are exactly what should make it read as
        // shopping — dropping them would count a glance the same as a long stretch.
        _contentTypes[observation.ContentType] =
            _contentTypes.GetValueOrDefault(observation.ContentType) + 1;

        if (!duplicate)
        {
            _samples.Add(observation);
        }
    }

    private Episode? CloseOpen(DateTimeOffset endedAt)
    {
        if (!HasOpenEpisode)
        {
            return null;
        }

        var episode = new Episode(
            _id,
            _startedAt,
            endedAt,
            [.. _executables],
            [.. _titles],
            SelectSamples(endedAt),
            _count,
            ContentTypeMix());

        _samples.Clear();
        _executables.Clear();
        _titles.Clear();
        _contentTypes.Clear();
        _lastText = string.Empty;
        _count = 0;
        return episode;
    }

    // Busiest kind first, ties broken by enum order so an episode always renders the same
    // mix — the distill prompt runs at temperature 0 and the golden corpus depends on the
    // whole request being byte-stable.
    private List<ContentTypeTally> ContentTypeMix() =>
    [
        .. _contentTypes
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Select(entry => new ContentTypeTally(entry.Key, entry.Value)),
    ];

    // Representative samples: always the first and last, then greedily the observation with
    // the best combination of NOVELTY (least similar to anything already chosen) and
    // TIME SPENT, bounded by MaxSamples and SampleMaxChars so distiller input can never
    // blow up. Coverage alone — what this did before v2-008 R6.3 — gave a thirty-second
    // glance the same weight as fifteen minutes on one document, so the evidence the
    // distiller read had no relationship to where the time actually went.
    //
    // Time spent is recovered, not measured: each retained sample OWNS the stretch from its
    // own timestamp to the next retained sample's (to the episode end, for the last one).
    // The near-duplicates that Append counted but deliberately did not retain all fall
    // inside that stretch, so the very information the duplicate-suppression path was
    // throwing away comes back as a long span on the sample that represents it. Nothing new
    // is stored per observation to get this.
    //
    // The two signals MULTIPLY: score = novelty × timeWeight. Multiplying (rather than
    // adding) means an exact repeat of something already chosen scores zero however long it
    // was held — time should promote evidence, never buy a slot for a redundant snippet —
    // while MinTimeWeight keeps a short-but-novel observation in contention.
    private List<string> SelectSamples(DateTimeOffset endedAt)
    {
        // Indices, not the observations themselves: CapturedObservation is a record, so two
        // identical readings compare equal and a "have I chosen this?" test by value would
        // silently treat them as one.
        var chosen = new List<int>();
        if (_samples.Count > 0)
        {
            chosen.Add(0);
        }

        if (_samples.Count > 1)
        {
            chosen.Add(_samples.Count - 1);
        }

        TimeSpan[] spans = SampleSpans(endedAt);
        TimeSpan longest = spans.Length == 0 ? TimeSpan.Zero : spans.Max();

        while (chosen.Count < Math.Min(_options.MaxSamples, _samples.Count))
        {
            int best = -1;
            double bestScore = double.MinValue;
            for (int candidate = 0; candidate < _samples.Count; candidate++)
            {
                if (chosen.Contains(candidate))
                {
                    continue;
                }

                double novelty = 1.0 - chosen.Max(
                    c => TextSimilarity.Similarity(_samples[c].Text, _samples[candidate].Text));
                double score = novelty * TimeWeight(spans[candidate], longest);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best < 0)
            {
                break;
            }

            chosen.Add(best);
        }

        chosen.Sort();
        return [.. chosen.Select(index => Truncate(_samples[index].Text))];
    }

    // How long each retained sample stood as the newest thing seen. Clamped at zero because
    // a bound-triggered close can hand back an endedAt equal to the last observation's own
    // timestamp, and a zero-length tail is meaningful where a negative one is not.
    private TimeSpan[] SampleSpans(DateTimeOffset endedAt)
    {
        var spans = new TimeSpan[_samples.Count];
        for (int i = 0; i < spans.Length; i++)
        {
            DateTimeOffset until = i + 1 < _samples.Count ? _samples[i + 1].At : endedAt;
            TimeSpan span = until - _samples[i].At;
            spans[i] = span > TimeSpan.Zero ? span : TimeSpan.Zero;
        }

        return spans;
    }

    // A sample's share of the episode's busiest stretch, in [MinTimeWeight, 1]. Relative to
    // the longest span rather than the total so the scale does not shift with how many
    // samples there happen to be. An episode with no measurable duration — every
    // observation at the same instant, which only tests and clock skew produce — falls back
    // to pure novelty, the pre-R6.3 behaviour.
    private static double TimeWeight(TimeSpan span, TimeSpan longest) =>
        longest <= TimeSpan.Zero ? 1.0 : Math.Max(MinTimeWeight, span / longest);

    private string Truncate(string text) =>
        text.Length <= _options.SampleMaxChars ? text : text[.._options.SampleMaxChars];
}
