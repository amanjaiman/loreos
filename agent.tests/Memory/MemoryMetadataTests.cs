using System.Text.Json;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Memory;

public sealed class MemoryMetadataTests
{
    private static MemoryMetadata Sample() => new(
        Kind: MemoryKinds.State,
        Status: MemoryStatuses.Active,
        Confidence: 0.8,
        ExpiresAt: 1_800_000_000,
        EstablishedAt: 1_799_000_000,
        UpdatedReason: MemoryUpdateReasons.Promoted,
        Reinforced: 2,
        Episodes: ["ep-1", "ep-2"],
        Pinned: false,
        UserEdited: false,
        Supersedes: "old-id");

    private static Dictionary<string, JsonElement> AsWire(MemoryMetadata metadata) =>
        metadata.ToDictionary().ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value));

    [Fact]
    public void ToDictionary_has_every_key_non_null()
    {
        IReadOnlyDictionary<string, object?> wire = Sample().ToDictionary();

        // The store cannot distinguish absent from never-set, and the recall
        // filter's `expires_at gt now` silently drops keyless rows — every key
        // must always be present and non-null.
        string[] expected =
        [
            "kind",
            "status",
            "confidence",
            "expires_at",
            "established_at",
            "updated_reason",
            "reinforced",
            "episodes",
            "pinned",
            "user_edited",
            "supersedes",
            "v",
        ];
        Assert.Equal(expected.OrderBy(k => k), wire.Keys.OrderBy(k => k));
        Assert.All(wire.Values, value => Assert.NotNull(value));
    }

    [Fact]
    public void Roundtrips_through_the_wire_shape()
    {
        MemoryMetadata original = Sample();

        MemoryMetadata? parsed = MemoryMetadata.From(AsWire(original));

        Assert.NotNull(parsed);
        Assert.Equal(original.Kind, parsed.Kind);
        Assert.Equal(original.Status, parsed.Status);
        Assert.Equal(original.Confidence, parsed.Confidence);
        Assert.Equal(original.ExpiresAt, parsed.ExpiresAt);
        Assert.Equal(original.EstablishedAt, parsed.EstablishedAt);
        Assert.Equal(original.UpdatedReason, parsed.UpdatedReason);
        Assert.Equal(original.Reinforced, parsed.Reinforced);
        Assert.Equal(original.Episodes, parsed.Episodes);
        Assert.Equal(original.Supersedes, parsed.Supersedes);
        Assert.Equal(MemoryMetadata.CurrentVersion, parsed.Version);
    }

    [Fact]
    public void From_returns_null_for_v1_relics_and_null_metadata()
    {
        // v1 rows carry no "v" key; they are invisible to the typed schema until
        // the archive sweep stamps them.
        var v1Shaped = new Dictionary<string, JsonElement>
        {
            ["category"] = JsonSerializer.SerializeToElement("research"),
        };

        Assert.Null(MemoryMetadata.From(v1Shaped));
        Assert.Null(MemoryMetadata.From((IReadOnlyDictionary<string, JsonElement>?)null));
    }

    [Fact]
    public void From_is_defensive_about_malformed_values()
    {
        var malformed = new Dictionary<string, JsonElement>
        {
            ["v"] = JsonSerializer.SerializeToElement(1),
            ["kind"] = JsonSerializer.SerializeToElement(42), // wrong type
            ["confidence"] = JsonSerializer.SerializeToElement("high"), // wrong type
            ["episodes"] = JsonSerializer.SerializeToElement("not-a-list"),
        };

        MemoryMetadata? parsed = MemoryMetadata.From(malformed);

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, parsed.Kind);
        Assert.Equal(0.0, parsed.Confidence);
        Assert.Null(parsed.Episodes);
        Assert.Equal(MemoryMetadata.FarFutureUnixSeconds, parsed.ExpiresAt);
    }

    [Fact]
    public void Kind_and_status_vocabularies_are_closed()
    {
        Assert.True(MemoryKinds.IsKnown(MemoryKinds.Experience));
        Assert.False(MemoryKinds.IsKnown("vibe"));
        Assert.False(MemoryKinds.IsKnown(null));
        Assert.True(MemoryStatuses.IsKnown(MemoryStatuses.Staged));
        Assert.False(MemoryStatuses.IsKnown("pending"));
        Assert.Equal(5, MemoryKinds.All.Count);
    }

    [Fact]
    public void ActiveUnexpired_filter_has_recall_shape()
    {
        Dictionary<string, object?> filter = MemoryFilters.ActiveUnexpired(1_800_000_000);

        Assert.Equal(MemoryStatuses.Active, filter["status"]);
        var op = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(filter["expires_at"]);
        Assert.Equal(1_800_000_000L, op["gt"]);
    }
}
