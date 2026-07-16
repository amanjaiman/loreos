using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Distill;
using Lore.Agent.Lifecycle;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Lore.Agent.Recall;
using Lore.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lore.Agent.Tests.E2E;

/// <summary>The v2-001 acceptance harness (spec AC 1/2), end-to-end and LIVE: real
/// memoryd (spawned via python), real mem0 + Qdrant, real Ollama for distillation,
/// arbitration, and embeddings. Opt-in — this is the documented pre-release check, not
/// CI (constitution: no live model calls in CI).
///
/// <para>Run:  set LORE_TEST_E2E=1 (and LORE_E2E_PYTHON to a python whose environment
/// has lore-memoryd installed), have Ollama serving qwen2.5:7b-instruct +
/// nomic-embed-text, then: dotnet test --filter MemoryLayerE2E</para></summary>
public sealed class MemoryLayerE2ETests : IAsyncLifetime, IDisposable
{
    private const string OllamaUrl = "http://localhost:11434";
    private const string ChatModel = "qwen2.5:7b-instruct";
    private const int Port = 7981;

    private static bool Enabled => Environment.GetEnvironmentVariable("LORE_TEST_E2E") == "1";

    private Process? _memoryd;
    private HttpClient? _memorydHttp;
    private HttpClient? _ollamaHttp;
    private string _dataDir = string.Empty;

    public async Task InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        _dataDir = Path.Combine(Path.GetTempPath(), "lore-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dataDir);

        string python = Environment.GetEnvironmentVariable("LORE_E2E_PYTHON") ?? "python";
        var start = new ProcessStartInfo(python, "-m lore_memoryd")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.Environment["LORE_MEMORYD_PORT"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["MEM0_TELEMETRY"] = "False";
        _memoryd = Process.Start(start) ?? throw new InvalidOperationException("could not start memoryd");

        _memorydHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/") };
        await WaitForHealthAsync(_memorydHttp);

        _ollamaHttp = new HttpClient { BaseAddress = new Uri(OllamaUrl) };
    }

    public async Task DisposeAsync()
    {
        if (_memoryd is not null && !_memoryd.HasExited)
        {
            _memoryd.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _memoryd.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // best-effort teardown
            }
        }

        Dispose();
        if (_dataDir.Length > 0 && Directory.Exists(_dataDir))
        {
            try
            {
                Directory.Delete(_dataDir, recursive: true);
            }
            catch (IOException)
            {
                // Qdrant may hold the lock a beat longer; temp cleanup is best-effort.
            }
        }
    }

    public void Dispose()
    {
        _memorydHttp?.Dispose();
        _ollamaHttp?.Dispose();
        _memoryd?.Dispose();
    }

    private static async Task WaitForHealthAsync(HttpClient http)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(60))
        {
            try
            {
                HttpResponseMessage response = await http.GetAsync(new Uri("health", UriKind.Relative));
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // still starting
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("memoryd did not become healthy in 60s");
    }

    private static Episode MakeEpisode(string id, DateTimeOffset at, string[] titles, string[] samples) =>
        new(id, at.AddMinutes(-20), at, ["browser"], titles, samples, samples.Length * 2);

    [Fact]
    public async Task Wisdom_teeth_and_France_journeys_end_to_end()
    {
        if (!Enabled)
        {
            return; // opt-in live harness: set LORE_TEST_E2E=1 (with Ollama + python env)
        }

        MemorydClient memory = new(_memorydHttp!);
        await memory.ConfigureAsync(new MemoryConfig(
            new MemoryProviderConfig("ollama", ChatModel, new Uri(OllamaUrl)),
            _dataDir,
            new MemoryEmbedderConfig("ollama", "nomic-embed-text", new Uri(OllamaUrl), 768)));

        using var activity = new ActivityStore(":memory:");
        var backend = new OpenAiCompatibleBackend(
            _ollamaHttp!, new Uri(OllamaUrl + "/v1"), apiKey: null, ChatModel, maxTokens: 800);
        var engine = new LifecycleEngine(
            new Distiller(backend, NullLogger<Distiller>.Instance),
            memory,
            activity,
            backend,
            new LifecycleOptions(),
            TimeProvider.System,
            NullLogger<LifecycleEngine>.Instance);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // AC 1 journey: two ordinary dental episodes across "days" → staged, then promoted.
        await engine.ProcessAsync(MakeEpisode(
            "ep-wisdom-1", now.AddDays(-2),
            ["Wisdom tooth extraction aftercare - Dental Clinic", "What to eat after oral surgery"],
            [
                "Aftercare instructions following wisdom tooth extraction: bite on gauze, avoid "
                + "rinsing for 24 hours, expect swelling to peak around day three.",
                "Soft foods recommended after oral surgery: yogurt, mashed potatoes, smoothies. "
                + "Avoid chewy, crunchy, or spicy foods while the extraction site heals.",
            ]));
        await engine.ProcessAsync(MakeEpisode(
            "ep-wisdom-2", now.AddDays(-1),
            ["Soft food meal ideas for the week after wisdom teeth removal", "When can I eat solid food again"],
            [
                "Easy soft meals for wisdom teeth recovery week: blended soups, scrambled eggs, "
                + "oatmeal, and smoothies. Skip anything crunchy or chewy until the sockets close.",
                "Most people can reintroduce solid foods about a week after wisdom tooth "
                + "extraction, starting with pasta and soft vegetables.",
            ]));

        // AC 2 journey: a booking confirmation → high-signal, promotes directly.
        await engine.ProcessAsync(MakeEpisode(
            "ep-france", now,
            ["Booking confirmed - Paris & Loire Valley, 12-21 May 2026 - TravelCo"],
            [
                "Your booking is confirmed! Trip: Paris and the Loire Valley, France. Dates: "
                + "May 12-21, 2026. Traveler: 1 adult. Confirmation number TC-88412.",
            ]));

        IReadOnlyList<DecisionEntry> decisions = await activity.GetRecentDecisionsAsync(50);
        IReadOnlyList<MemoryRecord> stored = await memory.GetAllAsync();
        string diagnostic =
            "decisions: "
            + string.Join(" | ", decisions.Select(d => $"{d.Action}:{d.Statement}"))
            + "\nstored: "
            + string.Join(" | ", stored.Select(record =>
                $"[{MemoryMetadata.From(record)?.Status}] {record.Memory}"));
        Assert.True(decisions.Any(d => d.Action == "promoted"), diagnostic);

        // The every-turn contract, against real embeddings.
        var recall = new RecallService(memory, new RecallOptions(), TimeProvider.System);

        IReadOnlyList<RecallHit> takeout = await recall.RecallAsync(
            "Should I order takeout for dinner tonight? Thinking about getting something crunchy.");
        Assert.True(
            takeout.Any(hit =>
                hit.Statement.Contains("wisdom", StringComparison.OrdinalIgnoreCase)
                || hit.Statement.Contains("tooth", StringComparison.OrdinalIgnoreCase)
                || hit.Statement.Contains("extraction", StringComparison.OrdinalIgnoreCase)),
            "takeout recall: [" + string.Join(" | ", takeout.Select(h => $"{h.Statement} ({h.Score})")) + "]\n" + diagnostic);

        IReadOnlyList<RecallHit> travel = await recall.RecallAsync(
            "I want to plan my next vacation abroad. Which country should I pick?");
        Assert.True(
            travel.Any(hit =>
                hit.Statement.Contains("France", StringComparison.OrdinalIgnoreCase)
                || hit.Statement.Contains("Paris", StringComparison.OrdinalIgnoreCase)),
            "travel recall: [" + string.Join(" | ", travel.Select(h => $"{h.Statement} ({h.Score})")) + "]\n" + diagnostic);
    }
}
