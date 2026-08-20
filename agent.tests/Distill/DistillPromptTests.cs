using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Distill;
using Lore.Agent.Inference;

namespace Lore.Agent.Tests.Distill;

public sealed class DistillPromptTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    /// <summary>The distill system prompt exactly as it shipped before v2-008 T005, copied
    /// verbatim from <c>DistillPrompt.System</c> at commit-time and frozen here.
    ///
    /// <para>This is the single most valuable assertion in T005. The golden corpus and the E2E
    /// acceptance harness both pin <c>balanced</c>, and <c>balanced</c> is what every config
    /// written before this spec resolves to — so proving the refactor renders the old prompt byte
    /// for byte is what proves it did not silently change what Lore remembers for every existing
    /// user. The only permitted difference is the templated character cap, which is 200 here
    /// because 200 is what balanced resolves to.</para>
    ///
    /// <para>If a later change to the prompt makes this fail, that is the point: it means
    /// <c>balanced</c> moved, and the corpus has to be re-run rather than the constant
    /// updated.</para></summary>
    private const string LegacySystemPrompt =
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

    private static Episode Episode(params ContentTypeTally[] mix) => new(
        "ep-1",
        T0,
        T0 + TimeSpan.FromMinutes(25),
        ["browser"],
        ["Espresso machines — buying guide"],
        ["add to cart 64mm burr grinder", "extraction ratios explained"],
        9,
        mix);

    // Balanced is the stop everything pinned before this task ran, so it is what the pre-existing
    // tests keep exercising.
    private static InferenceRequest Build(Episode episode) =>
        DistillPrompt.Build(episode, DetailPreset.Balanced, 200);

    // v2-008 R6.1. ContentClassifier already ran on every one of these observations; before
    // this the answer died at episode close and the model had to re-infer "shopping" from
    // the samples it was given.
    [Fact]
    public void The_content_type_mix_reaches_the_user_message_with_counts()
    {
        InferenceRequest request = Build(Episode(
            new ContentTypeTally(ContentType.Shopping, 7),
            new ContentTypeTally(ContentType.Reading, 2)));

        Assert.Contains("Content: Shopping (7), Reading (2)", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mix_of_only_unknown_renders_no_content_line_at_all()
    {
        // "Content: Unknown (9)" is prompt weight carrying no signal, and prompt bloat has a
        // cost on every episode of every day.
        InferenceRequest request = Build(Episode(new ContentTypeTally(ContentType.Unknown, 9)));

        Assert.DoesNotContain("Content:", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_is_kept_when_it_sits_beside_a_real_kind()
    {
        // Here it is load-bearing: it says two-thirds of the episode was NOT shopping.
        InferenceRequest request = Build(Episode(
            new ContentTypeTally(ContentType.Unknown, 6),
            new ContentTypeTally(ContentType.Shopping, 3)));

        Assert.Contains("Content: Unknown (6), Shopping (3)", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_episode_read_from_storage_still_builds_a_prompt()
    {
        // The mix is not persisted, so an episode reconstituted from the episodes table
        // carries an empty one. That must degrade to the pre-R6.1 prompt, not throw.
        InferenceRequest request = Build(Episode());

        Assert.DoesNotContain("Content:", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Observations: 9", request.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(0.0, request.Temperature);
    }

    // ---- v2-008 R1.3: the detail control -------------------------------------------------

    /// <summary>The equivalence proof. Everything else in this task is a refactor of one prompt
    /// into three; this is what says the middle one did not move.</summary>
    [Fact]
    public void Balanced_renders_the_pre_v2_008_prompt_byte_for_byte()
    {
        InferenceRequest request = DistillPrompt.Build(Episode(), DetailPreset.Balanced, 200);

        Assert.Equal(LegacySystemPrompt, request.SystemPrompt, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(DetailPreset.Minimal, 120)]
    [InlineData(DetailPreset.Balanced, 200)]
    [InlineData(DetailPreset.Rich, 500)]
    public void Each_stop_states_its_own_character_cap(DetailPreset detail, int cap)
    {
        // The literal this used to hardcode. It is the only number in the prompt the user can
        // move, so a stop that failed to template it would silently hold `rich` to 200.
        InferenceRequest request = DistillPrompt.Build(Episode(), detail, cap);

        Assert.Contains(
            $"self-contained, under {cap} characters.", request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cap_is_the_value_handed_in_not_the_stop_s_preset()
    {
        // A raw `capture.statementMaxChars` wins over the preset for that field alone (R3), so
        // the two arguments can legitimately disagree and the prompt must follow the number.
        InferenceRequest request = DistillPrompt.Build(Episode(), DetailPreset.Rich, 300);

        Assert.Contains("under 300 characters.", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("under 500 characters", request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_three_stops_render_three_different_directives()
    {
        string minimal = Prompt(DetailPreset.Minimal);
        string balanced = Prompt(DetailPreset.Balanced);
        string rich = Prompt(DetailPreset.Rich);

        Assert.NotEqual(minimal, balanced, StringComparer.Ordinal);
        Assert.NotEqual(balanced, rich, StringComparer.Ordinal);
        Assert.NotEqual(minimal, rich, StringComparer.Ordinal);

        // And each is the directive it claims to be: general at the bottom, today's
        // specifics-not-a-summary in the middle, specifics-first at the top.
        Assert.Contains("Keep the statement GENERAL", minimal, StringComparison.Ordinal);
        Assert.Contains("Capture the CONCRETE specifics", balanced, StringComparison.Ordinal);
        Assert.Contains("Lead with the CONCRETE specifics", rich, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_directive_and_the_cap_move_with_the_stop()
    {
        // The kinds, the confidence rule, the examples and the skepticism decide WHAT Lore
        // remembers. This control decides only how fully it is written down, so all three stops
        // must still carry them identically — including the header's over-generalization guard.
        foreach (DetailPreset detail in Enum.GetValues<DetailPreset>())
        {
            string prompt = Prompt(detail);

            Assert.Contains("THE USUAL ANSWER IS NO FACTS", prompt, StringComparison.Ordinal);
            Assert.Contains(
                "Never infer identity traits from a single page view.", prompt, StringComparison.Ordinal);
            Assert.Contains("- preference: tastes and working styles", prompt, StringComparison.Ordinal);
            Assert.Contains("confidence is 0.0-1.0: use 0.85+", prompt, StringComparison.Ordinal);
            Assert.EndsWith("""{"facts": []}""", prompt, StringComparison.Ordinal);
        }
    }

    /// <summary>The binding rule, at the stop that threatens it. More specifics per episode is
    /// exactly what tempts a model to generalise from one of them, and a `preference` minted from
    /// a single observed choice does not merely add noise — the next booking's version of the
    /// same claim lands in the same-topic band, goes to arbitration, and supersedes it, so a
    /// history of eight bookings collapses into one flip-flopping preference.</summary>
    [Fact]
    public void Rich_forbids_minting_a_preference_from_one_observed_choice()
    {
        string rich = Prompt(DetailPreset.Rich);

        Assert.Contains("a single observed choice is never a preference", rich, StringComparison.Ordinal);
        Assert.Contains("not a preference for aisle seats", rich, StringComparison.Ordinal);

        // Depth, never fact count: one event stays one fact however much detail it carries.
        Assert.Contains("LONGER statement, never MORE facts", rich, StringComparison.Ordinal);
        Assert.Contains("one event stays one fact", rich, StringComparison.Ordinal);

        // And the header's guard is still there underneath it, not undercut by the directive.
        Assert.Contains(
            "Never infer identity traits from a single page view.", rich, StringComparison.Ordinal);
    }

    [Fact]
    public void The_directive_grows_with_the_stop_and_the_rest_of_the_prompt_does_not()
    {
        // Prompt bloat is paid on every episode of every day, so the cost of the control is worth
        // pinning: `minimal` and `balanced` are within a couple of lines of each other, and only
        // `rich` — which is asking for more and needs the guard stated in its own terms — is
        // materially longer. A change that made every user pay rich's weight fails here.
        int minimal = Prompt(DetailPreset.Minimal).Length;
        int balanced = Prompt(DetailPreset.Balanced).Length;
        int rich = Prompt(DetailPreset.Rich).Length;

        Assert.True(minimal < balanced, $"minimal ({minimal}) should be terser than balanced ({balanced})");
        Assert.True(balanced < rich, $"balanced ({balanced}) should be terser than rich ({rich})");
        Assert.True(rich - balanced < 400, $"rich adds {rich - balanced} characters over balanced");
    }

    [Theory]
    [InlineData(DetailPreset.Minimal, 120)]
    [InlineData(DetailPreset.Balanced, 200)]
    [InlineData(DetailPreset.Rich, 500)]
    public void The_same_episode_at_the_same_stop_is_the_same_request_byte_for_byte(
        DetailPreset detail, int cap)
    {
        // Temperature 0 only buys determinism if the prompt is deterministic too, and the golden
        // corpus depends on the whole request being stable.
        InferenceRequest first = DistillPrompt.Build(
            Episode(new ContentTypeTally(ContentType.Shopping, 9)), detail, cap);
        InferenceRequest second = DistillPrompt.Build(
            Episode(new ContentTypeTally(ContentType.Shopping, 9)), detail, cap);

        Assert.Equal(first.SystemPrompt, second.SystemPrompt, StringComparer.Ordinal);
        Assert.Equal(first.UserPrompt, second.UserPrompt, StringComparer.Ordinal);
        Assert.Equal(0.0, first.Temperature);
        Assert.Equal(0.0, second.Temperature);
    }

    /// <summary>The other half of R1.3, and the reason both caps have to move together: the model
    /// cannot write "seat 14C" if the sample it read was truncated before that text. This drives
    /// the real <see cref="EpisodeBuilder"/> off the same resolved preset the distiller reads, so
    /// it fails if <c>SampleMaxChars</c> ever stops reaching truncation.</summary>
    [Theory]
    [InlineData("minimal", 400, false)]
    [InlineData("balanced", 600, false)]
    [InlineData("rich", 900, true)]
    public void The_sample_cap_decides_whether_a_late_specific_can_ever_be_written(
        string stop, int sampleMaxChars, bool reachesTheModel)
    {
        var settings = new LiveCaptureSettings(
            CapturePresets.Resolve(attentiveness: null, certainty: null, detail: stop));
        Assert.Equal(sampleMaxChars, settings.Current.Episodes.SampleMaxChars);

        // The specific sits at character 700 of the page — inside rich's window, past the cut at
        // both of the others.
        var builder = new EpisodeBuilder(settings);
        builder.Add(new CapturedObservation(
            T0,
            "browser",
            "Booking confirmed — United",
            new string('x', 700) + " seat 14C aisle, $312",
            ContentType.Shopping));
        Episode episode = builder.Flush()!;

        InferenceRequest request = DistillPrompt.Build(
            episode, settings.Current.Detail, settings.Current.StatementMaxChars);

        Assert.Equal(
            reachesTheModel, request.UserPrompt.Contains("seat 14C", StringComparison.Ordinal));
    }

    private static string Prompt(DetailPreset detail) =>
        DistillPrompt.Build(Episode(), detail, Cap(detail)).SystemPrompt;

    // The preset's own cap, so a directive comparison is against the prompt each stop really
    // renders rather than an assembled one no user would see.
    private static int Cap(DetailPreset detail) => detail switch
    {
        DetailPreset.Minimal => 120,
        DetailPreset.Rich => 500,
        _ => 200,
    };
}
