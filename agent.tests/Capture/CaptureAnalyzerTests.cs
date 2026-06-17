using Lore.Agent.Capture;
using Lore.Agent.Inference;

namespace Lore.Agent.Tests.Capture;

public sealed class CaptureAnalyzerTests
{
    private sealed class FakeBackend : IInferenceBackend
    {
        private readonly Func<InferenceRequest, string?> _respond;

        public FakeBackend(string? response) => _respond = _ => response;

        public FakeBackend(Func<InferenceRequest, string?> respond) => _respond = respond;

        public int Calls { get; private set; }

        public InferenceRequest? LastRequest { get; private set; }

        public Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
        }
    }

    [Fact]
    public async Task Good_input_yields_a_clean_distilled_observation()
    {
        var backend = new FakeBackend(
            """{"observation": "I'm reading about distributed systems", "category": "reading"}""");
        var analyzer = new CaptureAnalyzer(backend);

        CaptureAnalysis? result = await analyzer.AnalyzeAsync("Designing Data-Intensive Apps", "chapter 5 ...");

        Assert.NotNull(result);
        Assert.Equal("I'm reading about distributed systems", result!.Observation);
        Assert.Equal("reading", result.Category);
    }

    [Fact]
    public async Task The_prompt_carries_the_title_and_text()
    {
        var backend = new FakeBackend("""{"observation": "x", "category": "y"}""");
        var analyzer = new CaptureAnalyzer(backend);

        await analyzer.AnalyzeAsync("My Window Title", "the visible body text");

        Assert.NotNull(backend.LastRequest);
        Assert.Contains("My Window Title", backend.LastRequest!.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("the visible body text", backend.LastRequest.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_model_output_is_handled_without_crashing()
    {
        var backend = new FakeBackend("I could not analyze this, sorry.");
        var analyzer = new CaptureAnalyzer(backend);

        CaptureAnalysis? result = await analyzer.AnalyzeAsync("Title", "text");

        Assert.Null(result);
    }

    [Fact]
    public async Task An_empty_observation_from_the_model_is_a_skip()
    {
        var backend = new FakeBackend("""{"observation": "", "category": ""}""");
        var analyzer = new CaptureAnalyzer(backend);

        Assert.Null(await analyzer.AnalyzeAsync("Title", "text"));
    }

    [Fact]
    public async Task A_null_completion_is_a_skip()
    {
        var backend = new FakeBackend((string?)null);
        var analyzer = new CaptureAnalyzer(backend);

        Assert.Null(await analyzer.AnalyzeAsync("Title", "text"));
    }

    [Fact]
    public async Task Empty_title_and_text_skips_without_calling_the_model()
    {
        var backend = new FakeBackend("""{"observation": "x", "category": "y"}""");
        var analyzer = new CaptureAnalyzer(backend);

        CaptureAnalysis? result = await analyzer.AnalyzeAsync("   ", "");

        Assert.Null(result);
        Assert.Equal(0, backend.Calls); // no model call for nothing
    }

    [Fact]
    public async Task Analysis_proceeds_with_a_title_even_when_text_is_empty()
    {
        var backend = new FakeBackend("""{"observation": "I'm on the settings screen", "category": "general"}""");
        var analyzer = new CaptureAnalyzer(backend);

        CaptureAnalysis? result = await analyzer.AnalyzeAsync("App Settings", "");

        Assert.NotNull(result);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task Backend_failures_propagate_for_the_loop_to_handle()
    {
        var backend = new FakeBackend(_ => throw new InvalidOperationException("model down"));
        var analyzer = new CaptureAnalyzer(backend);

        await Assert.ThrowsAsync<InvalidOperationException>(() => analyzer.AnalyzeAsync("Title", "text"));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var backend = new FakeBackend("""{"observation": "x", "category": "y"}""");
        var analyzer = new CaptureAnalyzer(backend);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync("Title", "text", cts.Token));
    }

    [Fact]
    public void Constructor_rejects_a_null_backend()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptureAnalyzer(null!));
    }
}
