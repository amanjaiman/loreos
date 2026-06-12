using System.Text.Json;

namespace Lore.Agent.Memory;

/// <summary>A stored memory as returned by the memory service. Mem0-agnostic: this
/// is Lore's vocabulary, not mem0's.</summary>
public sealed record MemoryRecord(
    string Id,
    string Memory,
    double? Score = null,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null,
    string? CreatedAt = null,
    string? UpdatedAt = null);

/// <summary>The outcome of remembering one observation. A single observation may be
/// distilled into several memories. <see cref="Event"/> is <c>ADD</c>, <c>UPDATE</c>,
/// or <c>NONE</c>.</summary>
public sealed record AddedMemory(string Id, string Memory, string Event);

/// <summary>The model provider used for extraction, forwarded from the provider
/// layer (spec 004). <see cref="ApiKey"/> is resolved from the OS keystore by the
/// agent and sent only over <c>127.0.0.1</c>.</summary>
public sealed record MemoryProviderConfig(
    string Type,
    string Model,
    Uri? BaseUrl = null,
    string? ApiKey = null,
    double Temperature = 0.0);

/// <summary>How to embed memories. <c>follow_provider</c> lets memoryd pick the
/// provider's own embeddings or a local default.</summary>
public sealed record MemoryEmbedderConfig(
    string Type = "follow_provider",
    string? Model = null,
    Uri? BaseUrl = null,
    int? Dims = null);

/// <summary>Everything memoryd needs to (re)initialize its engine.</summary>
public sealed record MemoryConfig(
    MemoryProviderConfig Provider,
    string DataDir,
    MemoryEmbedderConfig? Embedder = null,
    string CollectionName = "lore",
    string UserId = "default");
