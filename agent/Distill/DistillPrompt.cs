using System.Globalization;
using System.Text;
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
        + "Each fact must be: first-person, self-contained, under 200 characters. Kinds:\n"
        + "- identity: stable traits (job, family, home city)\n"
        + "- preference: tastes and working styles\n"
        + "- state: temporary conditions (recovering from surgery, job hunting); include "
        + "horizon_days — how many days it likely stays true\n"
        + "- experience: past events that happened to the user (took a trip, shipped a launch)\n"
        + "- project: ongoing endeavors\n\n"
        + "confidence is 0.0-1.0: use 0.85+ only when the episode shows a committed action "
        + "(a booking confirmation, a signed form), not just browsing.\n\n"
        + "Respond with ONLY a JSON object of the form "
        + "{\"facts\": [{\"statement\": \"...\", \"kind\": \"...\", \"confidence\": 0.0, "
        + "\"horizon_days\": null}]}. When nothing durable is revealed respond "
        + "{\"facts\": []}.\n\n"
        + "Examples:\n"
        + "Episode: 25 min in a browser across 'Wisdom tooth extraction aftercare', 'What to "
        + "eat after oral surgery', 'How long does swelling last' →\n"
        + "{\"facts\": [{\"statement\": \"I'm recovering from a wisdom tooth extraction.\", "
        + "\"kind\": \"state\", \"confidence\": 0.7, \"horizon_days\": 30}]}\n"
        + "Episode: booking flow ending on 'Booking confirmed — Paris, 14-21 May' →\n"
        + "{\"facts\": [{\"statement\": \"I booked a trip to Paris for May 14-21.\", "
        + "\"kind\": \"experience\", \"confidence\": 0.9, \"horizon_days\": null}]}\n"
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

        return new InferenceRequest(System, user.ToString());
    }
}
