using System.Text.Json.Nodes;
using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Distill;
using Lore.Agent.Inference;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lore.Agent.Tests.Distill;

public sealed class DistillerTests
{
    private sealed class StubBackend : IInferenceBackend
    {
        private readonly string? _response;
        private readonly bool _throws;

        public StubBackend(string? response, bool throws = false)
        {
            _response = response;
            _throws = throws;
        }

        public InferenceRequest? LastRequest { get; private set; }

        public Task<string?> CompleteAsync(
            InferenceRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return _throws
                ? throw new InvalidOperationException("provider unreachable")
                : Task.FromResult(_response);
        }
    }

    private static readonly Episode Episode = new(
        "ep-test",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(25),
        ["browser"],
        ["Wisdom tooth extraction aftercare", "What to eat after oral surgery"],
        ["aftercare instructions...", "soft food suggestions..."],
        6,
        [new ContentTypeTally(ContentType.Reading, 6)]);

    private static Distiller Build(StubBackend backend, LiveCaptureSettings? settings = null) =>
        new(backend, settings ?? new LiveCaptureSettings(new CaptureOptions()),
            NullLogger<Distiller>.Instance);

    [Fact]
    public async Task Happy_path_returns_parsed_facts()
    {
        var backend = new StubBackend(
            """{"facts": [{"statement": "I'm recovering from a wisdom tooth extraction.", "kind": "state", "confidence": 0.7, "horizon_days": 30}]}""");

        IReadOnlyList<CandidateFact>? facts = await Build(backend).DistillAsync(Episode);

        Assert.Equal("state", Assert.Single(facts!).Kind);
    }

    [Fact]
    public async Task Prompt_carries_the_episode_summary_not_raw_dumps()
    {
        var backend = new StubBackend("""{"facts": []}""");

        await Build(backend).DistillAsync(Episode);

        Assert.NotNull(backend.LastRequest);
        Assert.Contains("Wisdom tooth extraction aftercare", backend.LastRequest!.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Duration: 25 min", backend.LastRequest.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("NO FACTS", backend.LastRequest.SystemPrompt, StringComparison.Ordinal);
    }

    // v2-008 R1.3 + R2. The detail stop is read from the live snapshot on every episode, so a
    // user who moves the control in Settings gets the new directive on the NEXT episode — no
    // restart, and the episode currently open is not dropped to get it.
    [Fact]
    public async Task The_detail_stop_in_force_reaches_the_prompt_without_a_restart()
    {
        var settings = new LiveCaptureSettings(new CaptureOptions());
        var backend = new StubBackend("""{"facts": []}""");
        Distiller distiller = Build(backend, settings);

        await distiller.DistillAsync(Episode);
        Assert.Contains("under 200 characters", backend.LastRequest!.SystemPrompt, StringComparison.Ordinal);

        settings.Update(new JsonObject { ["detail"] = "rich" });
        await distiller.DistillAsync(Episode);

        Assert.Contains("under 500 characters", backend.LastRequest!.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Lead with the CONCRETE specifics", backend.LastRequest.SystemPrompt, StringComparison.Ordinal);
    }

    // The two halves of R1.3 must come from ONE read. A PATCH landing between them could pair
    // rich's "lead with the specifics" with minimal's 120-character cap — a prompt that asks for
    // something it forbids in the same breath, and one no settings combination should produce.
    [Fact]
    public async Task The_directive_and_the_cap_come_from_the_same_snapshot()
    {
        var settings = new LiveCaptureSettings(new CaptureOptions());
        var backend = new StubBackend("""{"facts": []}""");

        settings.Update(new JsonObject { ["detail"] = "minimal" });
        await Build(backend, settings).DistillAsync(Episode);

        Assert.Contains("under 120 characters", backend.LastRequest!.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Keep the statement GENERAL", backend.LastRequest.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Lead with the CONCRETE", backend.LastRequest.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Model_failure_returns_null_never_throws()
    {
        var backend = new StubBackend(null, throws: true);

        IReadOnlyList<CandidateFact>? facts = await Build(backend).DistillAsync(Episode);

        Assert.Null(facts);
    }

    [Fact]
    public async Task Garbage_output_returns_null()
    {
        var backend = new StubBackend("I could not process this request.");

        Assert.Null(await Build(backend).DistillAsync(Episode));
    }

    [Fact]
    public async Task Empty_facts_flow_through_as_the_normal_case()
    {
        var backend = new StubBackend("""{"facts": []}""");

        IReadOnlyList<CandidateFact>? facts = await Build(backend).DistillAsync(Episode);

        Assert.NotNull(facts);
        Assert.Empty(facts);
    }
}
