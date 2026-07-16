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
        6);

    private static Distiller Build(StubBackend backend) =>
        new(backend, NullLogger<Distiller>.Instance);

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
