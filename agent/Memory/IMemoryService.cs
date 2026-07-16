namespace Lore.Agent.Memory;

/// <summary>The single seam to Lore's memory store (constitution §3.2). Every read
/// and write of memory in the agent goes through this interface; nothing else in the
/// codebase knows that mem0 or memoryd exist. The method names are intentionally
/// mem0-agnostic. Since v2-001 the store is RAW: it stores what it is given
/// verbatim (the distiller owns extraction, the lifecycle engine owns
/// reconciliation), filters apply at query time, and metadata patches merge.</summary>
public interface IMemoryService
{
    /// <summary>Store a fact verbatim with its metadata (see
    /// <see cref="MemoryMetadata.ToDictionary"/>). Raw store: exactly one
    /// <see cref="AddedMemory"/> comes back per call.</summary>
    Task<IReadOnlyList<AddedMemory>> RememberAsync(
        string observation,
        string userId = "default",
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>Semantic search for memories relevant to <paramref name="query"/>,
    /// most relevant first. <paramref name="filters"/> is the flat
    /// <c>field: value</c> / <c>field: {op: value}</c> shape built by
    /// <see cref="MemoryFilters"/>; unsupported shapes fail with an actionable error.</summary>
    Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string userId = "default",
        int limit = 10,
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>One page of memories, optionally filtered (same filter shape as
    /// <see cref="SearchAsync"/>). Ordering follows the engine (typically insertion
    /// order); strict recency ordering is not guaranteed.</summary>
    Task<IReadOnlyList<MemoryRecord>> ListAsync(
        string userId = "default",
        int limit = 100,
        int offset = 0,
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Recent memories for a user, in the order the memory engine returns
    /// them. The engine does not guarantee recency sorting; results typically reflect
    /// insertion order but may vary by backend.</summary>
    Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
        string userId = "default",
        int count = 20,
        CancellationToken cancellationToken = default);

    /// <summary>All memories for a user.</summary>
    Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
        string userId = "default",
        CancellationToken cancellationToken = default);

    /// <summary>One memory by id, or <c>null</c> if it does not exist.</summary>
    Task<MemoryRecord?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Patch a memory's text and/or metadata, returning the updated record,
    /// or <c>null</c> if it does not exist. At least one of <paramref name="text"/> /
    /// <paramref name="metadataPatch"/> must be non-null. Metadata is merged
    /// key-by-key into the existing metadata — a text-only patch never wipes it.</summary>
    Task<MemoryRecord?> UpdateAsync(
        string id,
        string? text = null,
        IReadOnlyDictionary<string, object?>? metadataPatch = null,
        CancellationToken cancellationToken = default);

    /// <summary>Delete a memory. Returns <c>false</c> if it did not exist.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Typed conveniences over the seam so lifecycle/recall code reads in the
/// v2-001 vocabulary instead of raw dictionaries.</summary>
public static class MemoryServiceExtensions
{
    /// <summary>Store one typed fact; returns the stored memory (raw store → exactly one).</summary>
    public static async Task<AddedMemory> StoreAsync(
        this IMemoryService service,
        string statement,
        MemoryMetadata metadata,
        string userId = "default",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(metadata);
        IReadOnlyList<AddedMemory> added = await service
            .RememberAsync(statement, userId, metadata.ToDictionary(), cancellationToken)
            .ConfigureAwait(false);
        return added.Count == 1
            ? added[0]
            : throw new InvalidOperationException(
                $"raw store returned {added.Count} results for one add; expected exactly 1");
    }

    /// <summary>Merge a metadata patch into a memory without touching its text.</summary>
    public static Task<MemoryRecord?> PatchMetadataAsync(
        this IMemoryService service,
        string id,
        IReadOnlyDictionary<string, object?> metadataPatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(metadataPatch);
        return service.UpdateAsync(id, text: null, metadataPatch, cancellationToken);
    }
}
