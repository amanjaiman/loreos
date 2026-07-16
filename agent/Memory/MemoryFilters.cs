namespace Lore.Agent.Memory;

/// <summary>Builders for the store's query-time filters (v2-001). The wire shape is a
/// flat dict of <c>field: value</c> (equality) or <c>field: {op: value}</c> with the
/// operator set the pinned engine supports; these helpers keep call sites from
/// hand-rolling that shape. Compose by adding entries to the returned dictionary.</summary>
public static class MemoryFilters
{
    /// <summary>The recall path's base filter: only active, unexpired memories.
    /// Expired <c>state</c> never surfaces; sentinel-dated kinds always pass.</summary>
    public static Dictionary<string, object?> ActiveUnexpired(long nowUnixSeconds) => new()
    {
        ["status"] = MemoryStatuses.Active,
        ["expires_at"] = Gt(nowUnixSeconds),
    };

    /// <summary>Equality on <c>status</c>.</summary>
    public static Dictionary<string, object?> WithStatus(string status) => new()
    {
        ["status"] = status,
    };

    /// <summary><c>{op: value}</c> operator terms.</summary>
    public static IReadOnlyDictionary<string, object?> Gt(object value) => Op("gt", value);

    public static IReadOnlyDictionary<string, object?> Ne(object value) => Op("ne", value);

    public static IReadOnlyDictionary<string, object?> In(IReadOnlyCollection<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Op("in", values);
    }

    private static Dictionary<string, object?> Op(string op, object value) =>
        new() { [op] = value };
}
