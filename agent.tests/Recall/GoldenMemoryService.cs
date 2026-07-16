using System.Text.Json;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Recall;

/// <summary>An <see cref="IMemoryService"/> over the golden corpus that behaves like
/// memoryd's filtered search: scripted semantic scores, honest application of the
/// status/expiry/kind filters (equality, <c>in</c>, <c>gt</c>) BEFORE the limit — so a
/// filter regression fails these tests the way it would fail in production.</summary>
internal sealed class GoldenMemoryService : IMemoryService
{
    public Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query, string userId = "default", int limit = 10,
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, double> sims =
            GoldenCorpus.Similarities.TryGetValue(query, out IReadOnlyDictionary<string, double>? s)
                ? s
                : new Dictionary<string, double>();

        IReadOnlyList<MemoryRecord> hits = GoldenCorpus.Memories
            .Where(memory => sims.ContainsKey(memory.Id))
            .Select(memory => GoldenCorpus.ToRecord(memory, sims[memory.Id]))
            .Where(record => Matches(record, filters))
            .OrderByDescending(record => record.Score)
            .Take(limit)
            .ToArray();
        return Task.FromResult(hits);
    }

    private static bool Matches(MemoryRecord record, IReadOnlyDictionary<string, object?>? filters)
    {
        if (filters is null)
        {
            return true;
        }

        foreach ((string field, object? condition) in filters)
        {
            JsonElement value = record.Metadata![field];
            if (condition is IReadOnlyDictionary<string, object?> op)
            {
                foreach ((string name, object? operand) in op)
                {
                    bool ok = name switch
                    {
                        "gt" => value.GetInt64() > Convert.ToInt64(
                            operand, System.Globalization.CultureInfo.InvariantCulture),
                        "in" => ((IEnumerable<string>)operand!).Contains(value.GetString()),
                        "ne" => value.GetString() != (string?)operand,
                        _ => throw new NotSupportedException($"golden fake: operator {name}"),
                    };
                    if (!ok)
                    {
                        return false;
                    }
                }
            }
            else if (value.GetString() != condition?.ToString())
            {
                return false;
            }
        }

        return true;
    }

    // Recall never calls the members below.
    public Task<IReadOnlyList<AddedMemory>> RememberAsync(
        string observation, string userId = "default",
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<MemoryRecord>> ListAsync(
        string userId = "default", int limit = 100, int offset = 0,
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
        string userId = "default", int count = 20, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
        string userId = "default", CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<MemoryRecord?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<MemoryRecord?> UpdateAsync(
        string id, string? text = null,
        IReadOnlyDictionary<string, object?>? metadataPatch = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
