using System.Globalization;
using Lore.Agent.Inference;

namespace Lore.Agent.Lifecycle;

/// <summary>How a new candidate fact relates to an existing same-topic memory.</summary>
public enum ArbitrationVerdict
{
    /// <summary>Same fact, different words — reinforce the existing memory.</summary>
    Duplicate,

    /// <summary>The user's situation changed; the new fact is the current truth —
    /// archive the old memory and store the new one.</summary>
    Supersedes,

    /// <summary>Related but independently true — both may stand.</summary>
    Coexist,
}

/// <summary>The one-word arbitration call (v2-001 T006): duplicate / supersedes /
/// coexist. Runs at capture time only — never on the recall path.</summary>
public static class ArbitrationPrompt
{
    private const string System =
        "You decide how a NEW fact about a user relates to an EXISTING memory of them. "
        + "Answer with exactly one word:\n"
        + "- DUPLICATE: they assert the same fact, even if worded differently.\n"
        + "- SUPERSEDES: the NEW fact updates, replaces, or contradicts the EXISTING one — "
        + "the user's situation changed and NEW is the current truth.\n"
        + "- COEXIST: they are related but independently true; neither replaces the other.";

    /// <summary>Build the arbitration request for one candidate/memory pair.</summary>
    public static InferenceRequest Build(string newFact, string existingMemory) =>
        new(
            System,
            string.Format(
                CultureInfo.InvariantCulture,
                "EXISTING: {0}\nNEW: {1}\nAnswer:",
                existingMemory,
                newFact),
            Temperature: 0.0);

    /// <summary>Parse the model's answer; anything unrecognizable is COEXIST — the safe
    /// verdict (nothing is archived, the candidate stages for another look).</summary>
    public static ArbitrationVerdict ParseVerdict(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ArbitrationVerdict.Coexist;
        }

        if (raw.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return ArbitrationVerdict.Duplicate;
        }

        return raw.Contains("supersede", StringComparison.OrdinalIgnoreCase)
            ? ArbitrationVerdict.Supersedes
            : ArbitrationVerdict.Coexist;
    }
}
