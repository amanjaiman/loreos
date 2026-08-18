using System.Globalization;
using System.Text;
using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Inference;

namespace Lore.Agent.Distill;

/// <summary>Builds the skeptical distillation prompt (v2-001): given one closed episode,
/// ask the user's model what durable fact about the user — if any — it supports. The
/// prompt's default answer is an empty list; that is the load-bearing difference from
/// v1's "describe what the user is doing" analysis, which produced an activity log.</summary>
public static class DistillPrompt
{
    private const int MaxTitles = 10;

    private const string System =
        "You are Lore's memory distiller. You receive a summary of one EPISODE — a "
        + "contiguous stretch of the user's computer activity. Decide whether it reveals any "
        + "DURABLE fact about the user: something a helpful assistant should still know weeks "
        + "from now.\n\n"
        + "Be skeptical. THE USUAL ANSWER IS NO FACTS. Reading news, watching videos, routine "
        + "coding or email, shopping around without buying, and one-off lookups reveal nothing "
        + "durable. Only extract facts about the USER — their situation, preferences, projects, "
        + "and experiences — never facts about content they merely viewed. Never infer identity "
        + "traits from a single page view.\n\n"
        + "Each fact must be: first-person, self-contained, under 200 characters. When a fact "
        + "has a practical consequence, put it IN the statement ('…and can only eat soft foods "
        + "for now') — the consequence is what makes the memory useful later. Capture the "
        + "CONCRETE specifics the episode actually shows — destinations, dates, amounts, names, "
        + "the particular item — not a generic summary: 'booking a United flight to San Diego "
        + "for Sep 9-13' is far more useful later than 'booking a flight'. Kinds:\n"
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

    /// <summary>Render one episode as the user message and pair it with the system prompt.</summary>
    public static InferenceRequest Build(Episode episode)
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
        // distill identically (and the E2E acceptance harness relies on it).
        return new InferenceRequest(System, user.ToString(), Temperature: 0.0);
    }

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
