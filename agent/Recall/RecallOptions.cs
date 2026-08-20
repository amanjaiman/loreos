namespace Lore.Agent.Recall;

/// <summary>The recall hot path's tunables (v2-001), bound from the <c>recall</c> config
/// section. The floor and weights are calibrated against the golden corpus; they are
/// config, not code.</summary>
public sealed class RecallOptions
{
    /// <summary>Default number of memories returned.</summary>
    public int DefaultK { get; init; } = 5;

    /// <summary>How many candidates are fetched per requested result before blending
    /// (over-fetch so kind/temporal weighting can reorder).</summary>
    public int OverFetchMultiplier { get; init; } = 3;

    /// <summary>Semantic score below which a hit is dropped. An empty result is the
    /// common, correct case — the floor errs toward empty (spec AC 6). The default is
    /// calibrated against live nomic-embed-text distributions (v2-001 T010): relevant
    /// cross-domain hits score ≥ ~0.50, unrelated pairs ≤ ~0.44.
    ///
    /// <para>This is compared against the raw semantic score, not the blend (v2-008 R5.3)
    /// — the calibration above was always a statement about embedding distances, and
    /// applying it to <c>semantic × kind × temporal × confidence</c> meant an old or
    /// uncertain memory could fail a <i>relevance</i> test it was never being asked. The
    /// blend still decides rank. Re-tune this against semantic distances only, and re-run
    /// the golden corpus: raising it drops genuine cross-domain hits, lowering it lets
    /// unrelated pairs in, and the two bands are only ~0.06 apart.</para></summary>
    public double Floor { get; init; } = 0.47;

    /// <summary>Confidence below which a hit is dropped, however relevant it is. The second
    /// of recall's two inclusion gates: a hit is kept when
    /// <c>semantic >= Floor AND confidence >= MinConfidence</c>, and the blend then decides
    /// rank (v2-008 R5.4).
    ///
    /// <para><b>Why this exists separately from <see cref="Floor"/>:</b> the two answer
    /// different questions. <see cref="Floor"/> asks "is this memory <i>relevant</i> to the
    /// query?", which is a statement about embedding distance. This asks "is this memory
    /// <i>trustworthy</i> enough to spend a client's context on?", which has nothing to do
    /// with the query at all. Before v2-008 R5.3 confidence gated inclusion only as a side
    /// effect of being a factor in the blend, and when R5.3 moved the floor onto the
    /// semantic axis that side effect went away — a 0.3-confidence hunch ("I might be
    /// coming down with a cold") started returning on any on-topic query, weakening the
    /// contract <see cref="RecallService"/> states for itself. Hence a gate of its own, at
    /// 0.5: real captured facts sit at ~0.9, the wisdom-teeth relevance case at 0.7 still
    /// surfaces, and near-speculation does not.</para>
    ///
    /// <para><b>Do not fold either gate back into the blend.</b> Multiplying relevance,
    /// recency and trust into one number and testing it against one threshold is what
    /// caused the v2-008 R5.3 age bug: an aged <c>experience</c> failed a <i>relevance</i>
    /// test purely because it was old. Two independent questions need two independent
    /// gates; the blend is for ordering.</para></summary>
    public double MinConfidence { get; init; } = 0.5;

    /// <summary>Recall weight of a current <c>state</c> — the "wisdom teeth" boost.</summary>
    public double StateWeight { get; init; } = 1.15;

    /// <summary>Recall weight of an <c>experience</c> before recency decay.</summary>
    public double ExperienceWeight { get; init; } = 0.9;

    /// <summary>e-folding time of experience recency decay, in days.</summary>
    public double ExperienceDecayDays { get; init; } = 365;

    /// <summary>Experience decay never drops the temporal factor below this. Saturation is
    /// <c>ExperienceDecayDays × -ln(floor)</c> — at 0.2 over 365 days, ~587 days, past
    /// which age stops separating one experience from another.
    ///
    /// <para><b>This was 0.6 (saturating at ~187 days) and was lowered to 0.2 by v2-008
    /// R5.4. Do not "restore" it.</b> The high floor was there to stop old memories
    /// vanishing, and while inclusion was decided by the blend it genuinely was load-bearing
    /// for that — but it never worked, because the recall <see cref="Floor"/> sat above
    /// where the guarantee landed and cancelled it (see <see cref="RecallService"/> for the
    /// arithmetic). R5.3 moved that guarantee onto the semantic score, where age is not a
    /// term at all, so this value <b>can no longer cause any memory to be excluded</b> — it
    /// only scales rank. The vanishing risk it was defending against does not exist on this
    /// side of R5.3.</para>
    ///
    /// <para>Freed of that job it can do its real one — ordering — for three times as long.
    /// At 0.6 every experience older than ~187 days blended to the same value and the aged
    /// tail sorted by embedder noise instead of recency (measured on the golden corpus:
    /// 1mo, 3mo, 5mo, 16mo, 20mo, 12mo, 8mo, 24mo, with 8mo and 24mo tying exactly). At 0.2
    /// the same eight sort strictly recent-first.</para></summary>
    public double ExperienceDecayFloor { get; init; } = 0.2;
}
