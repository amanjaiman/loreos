using System.Text.Json;

namespace Lore.Agent.Memory;

/// <summary>The closed set of memory kinds (v2-001). The kind determines default
/// lifespan and recall weight; anything outside this set is rejected at the seam.</summary>
public static class MemoryKinds
{
    public const string Identity = "identity";
    public const string Preference = "preference";
    public const string State = "state";
    public const string Experience = "experience";
    public const string Project = "project";

    /// <summary>All kinds, for validation and UI grouping.</summary>
    public static readonly IReadOnlyList<string> All =
        [Identity, Preference, State, Experience, Project];

    public static bool IsKnown(string? kind) =>
        kind is Identity or Preference or State or Experience or Project;
}

/// <summary>Memory lifecycle status (v2-001). Recall filters <see cref="Active"/>;
/// <see cref="Staged"/> candidates are invisible to clients until promoted;
/// <see cref="Archived"/> covers superseded and revision-retired facts.</summary>
public static class MemoryStatuses
{
    public const string Staged = "staged";
    public const string Active = "active";
    public const string Archived = "archived";

    public static bool IsKnown(string? status) => status is Staged or Active or Archived;
}

/// <summary>Why a memory last changed — provenance for the UI and decision trail.</summary>
public static class MemoryUpdateReasons
{
    public const string Staged = "staged";
    public const string Promoted = "promoted";
    public const string Reinforced = "reinforced";
    public const string Revised = "revised";
    public const string UserEdit = "user_edit";
    public const string Confirmed = "confirmed";
    public const string Deduped = "deduped";
    public const string V1Archive = "v1_archive";
}

/// <summary>Lore's typed metadata for one memory (the v2-001 schema). Serialized
/// into the store's metadata dict with every key non-null (the raw store cannot
/// distinguish "absent" from "never set", and the recall filter's
/// <c>expires_at gt now</c> would silently drop keyless rows).</summary>
public sealed record MemoryMetadata(
    string Kind,
    string Status,
    double Confidence,
    long ExpiresAt,
    long EstablishedAt,
    string UpdatedReason,
    int Reinforced = 0,
    IReadOnlyList<string>? Episodes = null,
    bool Pinned = false,
    bool UserEdited = false,
    string Supersedes = "",
    int Version = MemoryMetadata.CurrentVersion)
{
    /// <summary>Schema version stamped as <c>v</c>; rows without it are v1 relics.</summary>
    public const int CurrentVersion = 1;

    /// <summary>2100-01-01T00:00:00Z — the sentinel for non-expiring memories, so the
    /// recall path's numeric <c>expires_at gt now</c> filter never drops them.</summary>
    public const long FarFutureUnixSeconds = 4_102_444_800;

    /// <summary>The wire shape for <see cref="IMemoryService"/> adds and patches.</summary>
    public IReadOnlyDictionary<string, object?> ToDictionary() => new Dictionary<string, object?>
    {
        ["kind"] = Kind,
        ["status"] = Status,
        ["confidence"] = Confidence,
        ["expires_at"] = ExpiresAt,
        ["established_at"] = EstablishedAt,
        ["updated_reason"] = UpdatedReason,
        ["reinforced"] = Reinforced,
        ["episodes"] = Episodes ?? [],
        ["pinned"] = Pinned,
        ["user_edited"] = UserEdited,
        ["supersedes"] = Supersedes,
        ["v"] = Version,
    };

    /// <summary>Parse a stored record's metadata, or <c>null</c> when the record is not
    /// v2-shaped (no <c>v</c> key — i.e. a v1 relic awaiting the archive sweep).</summary>
    public static MemoryMetadata? From(IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        if (metadata is null || !metadata.ContainsKey("v"))
        {
            return null;
        }

        return new MemoryMetadata(
            Kind: GetString(metadata, "kind") ?? string.Empty,
            Status: GetString(metadata, "status") ?? string.Empty,
            Confidence: GetDouble(metadata, "confidence") ?? 0.0,
            ExpiresAt: GetLong(metadata, "expires_at") ?? FarFutureUnixSeconds,
            EstablishedAt: GetLong(metadata, "established_at") ?? 0,
            UpdatedReason: GetString(metadata, "updated_reason") ?? string.Empty,
            Reinforced: (int)(GetLong(metadata, "reinforced") ?? 0),
            Episodes: GetStringList(metadata, "episodes"),
            Pinned: GetBool(metadata, "pinned") ?? false,
            UserEdited: GetBool(metadata, "user_edited") ?? false,
            Supersedes: GetString(metadata, "supersedes") ?? string.Empty,
            Version: (int)(GetLong(metadata, "v") ?? CurrentVersion));
    }

    /// <summary>Convenience over <see cref="From(IReadOnlyDictionary{string, JsonElement}?)"/>.</summary>
    public static MemoryMetadata? From(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return From(record.Metadata);
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out JsonElement e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()
            : null;

    private static double? GetDouble(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out JsonElement e) && e.ValueKind == JsonValueKind.Number
            ? e.GetDouble()
            : null;

    private static long? GetLong(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long v)
            ? v
            : null;

    private static bool? GetBool(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out JsonElement e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? e.GetBoolean()
            : null;

    private static List<string>? GetStringList(
        IReadOnlyDictionary<string, JsonElement> d, string key)
    {
        if (!d.TryGetValue(key, out JsonElement e) || e.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<string>();
        foreach (JsonElement item in e.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                list.Add(item.GetString()!);
            }
        }

        return list;
    }
}
