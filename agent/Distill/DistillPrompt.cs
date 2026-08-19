using System.Globalization;
using System.Text;
using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Inference;

namespace Lore.Agent.Distill;

/// <summary>Builds the skeptical distillation prompt (v2-001): given one closed episode,
/// ask the user's model what durable fact about the user — if any — it supports. The
/// prompt's default answer is an empty list; that is the load-bearing difference from
/// v1's "describe what the user is doing" analysis, which produced an activity log.
///
/// <para><b>How fully that fact is written down is the user's choice</b> (v2-008 R1.3). Exactly
/// two things vary with the resolved <see cref="DetailPreset"/>: the character cap the statement
/// must stay under, and one directive sentence about how much of the episode's specifics to
/// carry. Everything else — the skepticism, the kinds, the confidence rule, the worked examples —
/// is byte-identical at all three stops, because those decide <em>what</em> Lore remembers and
/// this control decides only <em>how much of it</em> survives into the record. That split is why
/// <c>balanced</c> renders exactly the prompt shipped before v2-008 (pinned by a test): the
/// refactor must not have changed what Lore remembers for every existing user.</para>
///
/// <para><b>Detail changes depth, never fact count.</b> A higher stop must not split one event
/// into several facts, and must never mint a <c>preference</c> from a single observed choice.
/// That is not a style note. One aisle seat stored as "prefers aisle seats" collides with the
/// next booking's version of the same claim: the two land in the same-topic band, go to LLM
/// arbitration, and one supersedes the other — so eight bookings collapse into a single
/// flip-flopping preference instead of eight records. Lore records what happened; the consuming
/// agent infers what it means, at recall time, with the whole set in view (v2-008 R5). The
/// header's "never infer identity traits from a single page view" carries this at every stop,
/// and the <c>rich</c> directive restates it in the concrete terms detail invites, because more
/// specifics are precisely what tempts a model to generalise from one of them.</para>
///
/// <para>The <em>input</em> cap moves with the output cap and is applied elsewhere
/// (<c>EpisodeOptions.SampleMaxChars</c>, 400/600/900): the model cannot write "seat 14C" if the
/// sample it read was truncated before that text, so raising this cap alone would buy longer
/// statements with no more information in them.</para></summary>
public static class DistillPrompt
{
    private const int MaxTitles = 10;

    // Everything ahead of the statement rule. Identical at every detail stop — and note the last
    // sentence, which is the over-generalization guard the `rich` directive must not undercut.
    private const string Header =
        "You are Lore's memory distiller. You receive a summary of one EPISODE — a "
        + "contiguous stretch of the user's computer activity. Decide whether it reveals any "
        + "DURABLE fact about the user: something a helpful assistant should still know weeks "
        + "from now.\n\n"
        + "Be skeptical. THE USUAL ANSWER IS NO FACTS. Reading news, watching videos, routine "
        + "coding or email, shopping around without buying, and one-off lookups reveal nothing "
        + "durable. Only extract facts about the USER — their situation, preferences, projects, "
        + "and experiences — never facts about content they merely viewed. Never infer identity "
        + "traits from a single page view.\n\n";

    // The statement rule, split around the templated cap (v2-008 R1.3). This sentence used to
    // hardcode "under 200 characters"; the number now comes from `capture.statementMaxChars` —
    // 120 / 200 / 500 across the three stops, and whatever a raw config override says.
    private const string StatementRuleBeforeCap =
        "Each fact must be: first-person, self-contained, under ";

    private const string StatementRuleAfterCap =
        " characters. When a fact has a practical consequence, put it IN the statement ('…and "
        + "can only eat soft foods for now') — the consequence is what makes the memory useful "
        + "later. ";

    // `minimal` is a privacy choice, not just a cheaper one: the core fact survives and the
    // particulars deliberately do not. Stated as what to leave OUT rather than "be brief",
    // because a 120-character cap already enforces brevity and would otherwise just produce a
    // truncated version of the same specifics.
    private const string MinimalDirective =
        "Keep the statement GENERAL — the core fact only, without the particulars: 'booking a "
        + "flight to San Diego' is all that belongs in it, and the airline, dates, amounts, "
        + "seats and reference numbers do not.";

    // Byte-identical to the pre-v2-008 sentence. `balanced` is the upgrade path and what the
    // E2E acceptance harness and the golden corpus pin, so this string is frozen: changing it
    // changes what Lore remembers for every user who never touches the control.
    private const string BalancedDirective =
        "Capture the CONCRETE specifics the episode actually shows — destinations, dates, "
        + "amounts, names, the particular item — not a generic summary: 'booking a United "
        + "flight to San Diego for Sep 9-13' is far more useful later than 'booking a flight'.";

    // Specifics-first, plus the binding rule in the terms this stop actually invites. The second
    // half is not padding: it is the whole reason `rich` is safe to ship. More detail per fact is
    // the goal; more facts per event, or a taste inferred from one instance, is the failure mode.
    private const string RichDirective =
        "Lead with the CONCRETE specifics the episode actually shows — flight and order numbers, "
        + "destinations, dates, amounts, seats, the particular item — and keep them all in the "
        + "ONE statement: 'booking United UA 2411 to San Diego Sep 9-13, seat 14C aisle, one "
        + "checked bag, $312' is far more useful later than 'booking a flight'. Detail means a "
        + "LONGER statement, never MORE facts: one event stays one fact, and a single observed "
        + "choice is never a preference — seat 14C is a detail of this booking, not a preference "
        + "for aisle seats.";

    private const string KindsAndExamples =
        "Kinds:\n"
        + "- identity: stable traits (job, family, home city)\n"
        + "- preference: tastes and working styles\n"
        + "- state: a temporary condition OR a time-bound goal in progress — recovering from "
        + "surgery, job hunting, booking travel, apartment hunting, shopping for a specific "
        + "purchase; include horizon_days (how many days it likely stays relevant)\n"
        + "- experience: past events that happened to the user (took a trip, shipped a launch, "
        + "completed a booking)\n"
        + "- project: a sustained endeavor the user returns to over weeks — building software, "
        + "writing a book, running a business — NOT one-off errands or plans, which are state\n\n"
        + "confidence is 0.0-1.0: use 0.85+ only when the episode shows a committed action "
        + "(a booking confirmation, a signed form), not just browsing.\n\n"
        + "Respond with ONLY a JSON object of the form "
        + "{\"facts\": [{\"statement\": \"...\", \"kind\": \"...\", \"confidence\": 0.0, "
        + "\"horizon_days\": null}]}. When nothing durable is revealed respond "
        + "{\"facts\": []}.\n\n"
        + "Examples:\n"
        + "Episode: 25 min in a browser across 'Wisdom tooth extraction aftercare', 'What to "
        + "eat after oral surgery', 'How long does swelling last' →\n"
        + "{\"facts\": [{\"statement\": \"I'm recovering from a wisdom tooth extraction and can "
        + "only eat soft foods for now.\", \"kind\": \"state\", \"confidence\": 0.7, "
        + "\"horizon_days\": 30}]}\n"
        + "Episode: booking flow ending on 'Booking confirmed — Paris, 14-21 May' →\n"
        + "{\"facts\": [{\"statement\": \"I booked a trip to Paris for May 14-21.\", "
        + "\"kind\": \"experience\", \"confidence\": 0.9, \"horizon_days\": null}]}\n"
        + "Episode: flight-search site across 'San Diego Sep 9-13', 'United — $312 round trip' "
        + "(browsing, not booked) →\n"
        + "{\"facts\": [{\"statement\": \"I'm considering a United flight to San Diego for Sep "
        + "9-13.\", \"kind\": \"state\", \"confidence\": 0.6, \"horizon_days\": 21}]}\n"
        + "Episode: 20 min reading world news headlines →\n"
        + "{\"facts\": []}";

    /// <summary>Render one episode as the user message and pair it with the system prompt for the
    /// detail stop currently in force.</summary>
    /// <param name="episode">The closed episode to distill.</param>
    /// <param name="detail">The resolved "how much detail" stop (v2-008 R1.3), which selects the
    /// directive sentence. Read from <c>LiveCaptureSettings.Current</c> per episode, so a change
    /// in Settings applies to the next distillation with no restart.</param>
    /// <param name="statementMaxChars">The cap the model is told to keep a statement under.
    /// Passed separately from <paramref name="detail"/> rather than derived from it because a raw
    /// <c>capture.statementMaxChars</c> in config.json wins over the preset for that field alone
    /// (v2-008 R3) — the two can legitimately disagree.</param>
    public static InferenceRequest Build(Episode episode, DetailPreset detail, int statementMaxChars)
    {
        ArgumentNullException.ThrowIfNull(episode);

        var user = new StringBuilder();
        TimeSpan duration = episode.EndedAt - episode.StartedAt;
        user.AppendLine(CultureInfo.InvariantCulture, $"Duration: {Math.Max(1, (int)duration.TotalMinutes)} min");
        user.AppendLine(CultureInfo.InvariantCulture, $"Apps: {string.Join(", ", episode.Executables)}");

        // What kind of activity this was, with counts so proportion is visible (v2-008 R6.1).
        // The classifier already runs on every observation; before this the answer was thrown
        // away at episode close and the model had to re-infer "shopping" from the samples.
        // Omitted entirely when nothing was classified — a line reading "Unknown (9)" is
        // prompt weight that carries no signal.
        string mix = FormatContentMix(episode.ContentTypeMix);
        if (mix.Length > 0)
        {
            user.AppendLine(CultureInfo.InvariantCulture, $"Content: {mix}");
        }

        user.AppendLine(CultureInfo.InvariantCulture, $"Observations: {episode.ObservationCount}");
        user.AppendLine("Window titles:");
        foreach (string title in episode.Titles.Take(MaxTitles))
        {
            user.AppendLine(CultureInfo.InvariantCulture, $"- {title}");
        }

        user.AppendLine("Text samples:");
        int index = 1;
        foreach (string sample in episode.Samples)
        {
            user.AppendLine(CultureInfo.InvariantCulture, $"[{index++}] {sample}");
        }

        // Temperature 0: skepticism should not be sampled — identical episodes must
        // distill identically (and the E2E acceptance harness relies on it). The detail
        // control does not touch this: it varies the prompt, never the sampling, so the same
        // episode at the same stop is still the same request byte for byte.
        return new InferenceRequest(
            SystemPrompt(detail, statementMaxChars), user.ToString(), Temperature: 0.0);
    }

    // Assembled per call rather than cached: it is one small concatenation against one inference
    // call per episode, and a cache keyed on (stop, cap) would be state the live-settings path
    // could serve stale.
    private static string SystemPrompt(DetailPreset detail, int statementMaxChars) =>
        Header
        + StatementRuleBeforeCap
        + statementMaxChars.ToString(CultureInfo.InvariantCulture)
        + StatementRuleAfterCap
        + Directive(detail)
        + " "
        + KindsAndExamples;

    // An unrecognised stop cannot reach here — CaptureSnapshot only ever carries a parsed enum —
    // but balanced is the right answer if one ever did, for the same reason it is the fallback
    // everywhere else in v2-008: it is what every config written before the control resolves to.
    private static string Directive(DetailPreset detail) => detail switch
    {
        DetailPreset.Minimal => MinimalDirective,
        DetailPreset.Rich => RichDirective,
        _ => BalancedDirective,
    };

    // "Shopping (7), Reading (2)" — busiest first, as the builder ordered it. Unknown is
    // kept when it sits alongside a real kind, because it is what makes the proportion
    // honest, but a mix that is ONLY Unknown renders as nothing at all.
    private static string FormatContentMix(IReadOnlyList<ContentTypeTally> mix)
    {
        if (mix.Count == 0 || mix.All(tally => tally.Type == ContentType.Unknown))
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            mix.Select(tally => string.Create(
                CultureInfo.InvariantCulture, $"{tally.Type} ({tally.Count})")));
    }
}
