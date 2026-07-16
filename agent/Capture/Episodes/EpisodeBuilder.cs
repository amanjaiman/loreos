namespace Lore.Agent.Capture.Episodes;

/// <summary>Groups post-filter observations into episodes with cheap local signals —
/// no inference calls (v2-001). Pure logic over injected time values: the capture loop
/// owns the clock; this class only compares timestamps it is handed, so tests drive it
/// with recorded traces. Not thread-safe by design — the capture loop is single-threaded.</summary>
public sealed class EpisodeBuilder
{
    private readonly EpisodeOptions _options;
    private readonly List<CapturedObservation> _samples = [];
    private readonly List<string> _executables = [];
    private readonly List<string> _titles = [];

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
            SelectSamples(),
            _count);

        _samples.Clear();
        _executables.Clear();
        _titles.Clear();
        _lastText = string.Empty;
        _count = 0;
        return episode;
    }

    // Representative samples: always the first and last, then greedily the observation
    // least similar to anything already chosen — coverage over redundancy, bounded by
    // MaxSamples and SampleMaxChars so distiller input can never blow up.
    private List<string> SelectSamples()
    {
        var chosen = new List<CapturedObservation>();
        if (_samples.Count > 0)
        {
            chosen.Add(_samples[0]);
        }

        if (_samples.Count > 1)
        {
            chosen.Add(_samples[^1]);
        }

        while (chosen.Count < Math.Min(_options.MaxSamples, _samples.Count))
        {
            CapturedObservation? best = null;
            double bestScore = double.MaxValue;
            foreach (CapturedObservation candidate in _samples)
            {
                if (chosen.Contains(candidate))
                {
                    continue;
                }

                double closest = chosen.Max(c => TextSimilarity.Similarity(c.Text, candidate.Text));
                if (closest < bestScore)
                {
                    bestScore = closest;
                    best = candidate;
                }
            }

            if (best is null)
            {
                break;
            }

            chosen.Add(best);
        }

        return [.. chosen
            .OrderBy(observation => observation.At)
            .Select(observation => Truncate(observation.Text))];
    }

    private string Truncate(string text) =>
        text.Length <= _options.SampleMaxChars ? text : text[.._options.SampleMaxChars];
}
