namespace Lore.Agent.Memory;

/// <summary>The single seam to Lore's memory store (constitution §3.2). Every read
/// and write of memory in the agent goes through this interface; nothing else in the
/// codebase knows that mem0 or memoryd exist. The method names are intentionally
/// mem0-agnostic.</summary>
public interface IMemoryService
{
    /// <summary>Store a fact. memoryd runs mem0 as a raw store (v2-001): the text is
    /// stored verbatim — no extraction or reconciliation — so the caller supplies the
    /// finished fact. The list return shape survives for wire compatibility.</summary>
    Task<IReadOnlyList<AddedMemory>> RememberAsync(
        string observation,
        string userId = "default",
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>Semantic search for memories relevant to <paramref name="query"/>,
    /// most relevant first.</summary>
    Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string userId = "default",
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Recent memories for a user, in the order the memory engine returns
    /// them. The engine does not guarantee recency sorting; results typically reflect
    /// insertion order but may vary by backend. Strict recency ordering is deferred
    /// until spec 005+ consumers (context injection) require it.</summary>
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

    /// <summary>Replace a memory's text, returning the updated record, or <c>null</c>
    /// if it does not exist.</summary>
    Task<MemoryRecord?> UpdateAsync(string id, string text, CancellationToken cancellationToken = default);

    /// <summary>Delete a memory. Returns <c>false</c> if it did not exist.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
