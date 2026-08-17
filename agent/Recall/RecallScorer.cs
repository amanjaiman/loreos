using Lore.Agent.Memory;

namespace Lore.Agent.Recall;

/// <summary>The recall blend (v2-001): <c>semantic × kind × temporal × confidence</c>.
/// Pure math — no model calls ever run on the recall path; expired and non-active rows
/// are already excluded by the query-time filter before scoring.
///
/// <para>Since v2-008 R5.3 this decides <i>rank</i> only. Inclusion is decided separately,
/// by the semantic score against <see cref="RecallOptions.Floor"/>, so that the decay
/// below can do what it says it does — see <see cref="RecallService"/> for why.</para></summary>
public static class RecallScorer
{
    /// <summary>Blend one hit's semantic score with its metadata. Rows without v2
    /// metadata (not yet migrated) score 0, which sorts them last; since v2-008 R5.3 it is
    /// <see cref="RecallService"/>'s explicit null-metadata check, not this 0, that keeps
    /// them out of results.</summary>
    public static double Blend(
        double semanticScore, MemoryMetadata? metadata, long nowUnixSeconds, RecallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (metadata is null)
        {
            return 0.0;
        }

        return semanticScore
            * KindWeight(metadata.Kind, options)
            * TemporalFactor(metadata, nowUnixSeconds, options)
            * metadata.Confidence;
    }

    private static double KindWeight(string kind, RecallOptions options) => kind switch
    {
        MemoryKinds.State => options.StateWeight,
        MemoryKinds.Experience => options.ExperienceWeight,
        _ => 1.0,
    };

    // Current facts don't fade; experiences fade gently with age but never vanish —
    // "visited France" still matters on a travel query years later (spec AC 2).
    private static double TemporalFactor(
        MemoryMetadata metadata, long nowUnixSeconds, RecallOptions options)
    {
        if (metadata.Kind != MemoryKinds.Experience)
        {
            return 1.0;
        }

        double ageDays = Math.Max(0, nowUnixSeconds - metadata.EstablishedAt) / 86_400.0;
        return Math.Max(
            options.ExperienceDecayFloor,
            Math.Exp(-ageDays / options.ExperienceDecayDays));
    }
}
