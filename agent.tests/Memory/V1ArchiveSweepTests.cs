using System.Text.Json;
using Lore.Agent.Capture;
using Lore.Agent.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lore.Agent.Tests.Memory;

public sealed class V1ArchiveSweepTests
{
    private sealed class ReadySignal : IReadinessSignal
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A store whose reads fail the first N times — memoryd answering 503
    /// while the provider configuration is still landing (the startup race).</summary>
    private sealed class FlakyStore : IMemoryService
    {
        private readonly List<MemoryRecord> _records;

        public FlakyStore(int failuresBeforeSuccess, params MemoryRecord[] records)
        {
            FailuresRemaining = failuresBeforeSuccess;
            _records = [.. records];
        }

        public int FailuresRemaining { get; private set; }

        public int GetAllCalls { get; private set; }

        public List<(string Id, IReadOnlyDictionary<string, object?> Patch)> Patches { get; } = [];

        public Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
            string userId = "default", CancellationToken cancellationToken = default)
        {
            GetAllCalls++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new MemorydException("memoryd is not configured; POST /config first");
            }

            return Task.FromResult<IReadOnlyList<MemoryRecord>>([.. _records]);
        }

        public Task<MemoryRecord?> UpdateAsync(
            string id, string? text = null,
            IReadOnlyDictionary<string, object?>? metadataPatch = null,
            CancellationToken cancellationToken = default)
        {
            Patches.Add((id, metadataPatch!));
            return Task.FromResult<MemoryRecord?>(_records.First(r => r.Id == id));
        }

        public Task<IReadOnlyList<AddedMemory>> RememberAsync(
            string observation, string userId = "default",
            IReadOnlyDictionary<string, object?>? metadata = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MemoryRecord>> SearchAsync(
            string query, string userId = "default", int limit = 10,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MemoryRecord>> ListAsync(
            string userId = "default", int limit = 100, int offset = 0,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
            string userId = "default", int count = 20,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MemoryRecord?> GetAsync(
            string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
    }

    private static MemoryRecord V1Row(string id) => new(
        id, "I looked at a webpage.",
        Metadata: new Dictionary<string, JsonElement>
        {
            ["category"] = JsonSerializer.SerializeToElement("reading"),
        });

    private static MemoryRecord V2Row(string id) => new(
        id, "I live in Boston.",
        Metadata: new MemoryMetadata(
                MemoryKinds.Identity, MemoryStatuses.Active, 0.9,
                MemoryMetadata.FarFutureUnixSeconds, 0, MemoryUpdateReasons.Promoted)
            .ToDictionary()
            .ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)));

    private static V1ArchiveSweep Build(FlakyStore store) => new(
        store, new ReadySignal(), new FixedTime(), NullLogger<V1ArchiveSweep>.Instance)
    {
        MaxAttempts = 5,
        RetryDelay = TimeSpan.FromMilliseconds(10),
    };

    private static async Task RunToCompletionAsync(FlakyStore store)
    {
        using V1ArchiveSweep sweep = Build(store);
        await sweep.StartAsync(CancellationToken.None);
        await sweep.ExecuteTask!;
        await sweep.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stamps_v1_rows_as_archived_and_leaves_v2_rows_alone()
    {
        var store = new FlakyStore(0, V1Row("old-1"), V2Row("new-1"), V1Row("old-2"));

        await RunToCompletionAsync(store);

        Assert.Equal(2, store.Patches.Count);
        Assert.All(store.Patches, p => Assert.StartsWith("old-", p.Id, StringComparison.Ordinal));
        (_, IReadOnlyDictionary<string, object?> patch) = store.Patches[0];
        Assert.Equal(MemoryStatuses.Archived, patch["status"]);
        Assert.Equal(MemoryKinds.Experience, patch["kind"]);
        Assert.Equal(MemoryUpdateReasons.V1Archive, patch["updated_reason"]);
        Assert.Equal(MemoryMetadata.CurrentVersion, patch["v"]);
    }

    [Fact]
    public async Task Retries_through_the_startup_race_until_memoryd_is_configured()
    {
        // Readiness fires on HEALTHY, but POST /config lands moments later — the
        // first reads see 503. The sweep must ride that out, not defer a whole start.
        var store = new FlakyStore(3, V1Row("old-1"));

        await RunToCompletionAsync(store);

        Assert.Equal(4, store.GetAllCalls); // 3 failures + the success
        Assert.Single(store.Patches);
    }

    [Fact]
    public async Task Gives_up_quietly_after_the_attempt_budget()
    {
        var store = new FlakyStore(int.MaxValue, V1Row("old-1"));

        await RunToCompletionAsync(store); // must complete, not throw or hang

        Assert.Equal(5, store.GetAllCalls); // MaxAttempts
        Assert.Empty(store.Patches);
    }

    [Fact]
    public async Task Second_run_is_idempotent()
    {
        var store = new FlakyStore(0, V2Row("new-1"));

        await RunToCompletionAsync(store);

        Assert.Empty(store.Patches); // nothing un-versioned left to stamp
    }
}
