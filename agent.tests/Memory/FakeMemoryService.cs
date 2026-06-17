using System.Collections.Concurrent;
using System.Globalization;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Memory;

/// <summary>An in-memory <see cref="IMemoryService"/> for seeding API tests (spec 005). It is
/// deliberately simple — insertion-ordered storage, naive substring search — because the tests
/// it serves verify the API layer's routing/shapes/status codes, not the memory engine's
/// extraction. <see cref="Seed"/> preloads records; the CRUD verbs round-trip them.</summary>
internal sealed class FakeMemoryService : IMemoryService
{
    private readonly ConcurrentDictionary<string, MemoryRecord> _byId = new(StringComparer.Ordinal);
    private int _sequence;

    /// <summary>Preload a record (returns its id) so a test can exercise get/list/search.</summary>
    public string Seed(string text, double? score = null, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? metadata = null)
    {
        string id = NextId();
        _byId[id] = new MemoryRecord(id, text, score, metadata, Now(), Now());
        return id;
    }

    public Task<IReadOnlyList<AddedMemory>> RememberAsync(
        string observation,
        string userId = "default",
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        string id = NextId();
        _byId[id] = new MemoryRecord(id, observation, null, null, Now(), Now());
        IReadOnlyList<AddedMemory> result = [new AddedMemory(id, observation, "ADD")];
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string userId = "default",
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryRecord> hits = _byId.Values
            .Where(record => record.Memory.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(record => record with { Score = 0.9 })
            .ToArray();
        return Task.FromResult(hits);
    }

    public Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
        string userId = "default",
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryRecord> recent = _byId.Values.TakeLast(count).ToArray();
        return Task.FromResult(recent);
    }

    public Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
        string userId = "default",
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryRecord> all = _byId.Values.ToArray();
        return Task.FromResult(all);
    }

    public Task<MemoryRecord?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(id, out MemoryRecord? record) ? record : null);

    public Task<MemoryRecord?> UpdateAsync(string id, string text, CancellationToken cancellationToken = default)
    {
        if (!_byId.TryGetValue(id, out MemoryRecord? existing))
        {
            return Task.FromResult<MemoryRecord?>(null);
        }

        MemoryRecord updated = existing with { Memory = text, UpdatedAt = Now() };
        _byId[id] = updated;
        return Task.FromResult<MemoryRecord?>(updated);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryRemove(id, out _));

    private string NextId() =>
        "mem-" + Interlocked.Increment(ref _sequence).ToString(CultureInfo.InvariantCulture);

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
