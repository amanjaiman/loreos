using System.Globalization;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Memory;

/// <summary>An in-memory <see cref="IMemoryService"/> for seeding API tests (spec 005). It is
/// deliberately simple — insertion-ordered storage, naive substring search — because the tests
/// it serves verify the API layer's routing/shapes/status codes, not the memory engine's
/// extraction. Storage preserves insertion order (a plain ordered list under a lock) so list,
/// paging, and export assertions are deterministic. <see cref="Seed"/> preloads records.</summary>
internal sealed class FakeMemoryService : IMemoryService
{
    private readonly object _gate = new();
    private readonly List<MemoryRecord> _records = []; // insertion-ordered
    private int _sequence;

    /// <summary>Preload a record (returns its id) so a test can exercise get/list/search.</summary>
    public string Seed(string text, double? score = null, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? metadata = null)
    {
        lock (_gate)
        {
            string id = NextId();
            _records.Add(new MemoryRecord(id, text, score, metadata, Now(), Now()));
            return id;
        }
    }

    public Task<IReadOnlyList<AddedMemory>> RememberAsync(
        string observation,
        string userId = "default",
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            string id = NextId();
            _records.Add(new MemoryRecord(id, observation, null, null, Now(), Now()));
            IReadOnlyList<AddedMemory> result = [new AddedMemory(id, observation, "ADD")];
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string userId = "default",
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<MemoryRecord> hits = _records
                .Where(record => record.Memory.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .Select(record => record with { Score = 0.9 })
                .ToArray();
            return Task.FromResult(hits);
        }
    }

    public Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
        string userId = "default",
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<MemoryRecord> recent = _records.TakeLast(count).ToArray();
            return Task.FromResult(recent);
        }
    }

    public Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
        string userId = "default",
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<MemoryRecord> all = _records.ToArray();
            return Task.FromResult(all);
        }
    }

    public Task<MemoryRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_records.FirstOrDefault(record => record.Id == id));
        }
    }

    public Task<MemoryRecord?> UpdateAsync(string id, string text, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            int index = _records.FindIndex(record => record.Id == id);
            if (index < 0)
            {
                return Task.FromResult<MemoryRecord?>(null);
            }

            MemoryRecord updated = _records[index] with { Memory = text, UpdatedAt = Now() };
            _records[index] = updated;
            return Task.FromResult<MemoryRecord?>(updated);
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_records.RemoveAll(record => record.Id == id) > 0);
        }
    }

    private string NextId() =>
        "mem-" + (++_sequence).ToString(CultureInfo.InvariantCulture);

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
