using System.Text.Json.Serialization;

namespace Lore.Agent.Providers;

/// <summary>The outcome of a "test connection" (spec 004 T005). Serialized as the
/// <c>POST /providers/test</c> body: <c>{ok, model, latency_ms}</c> on success, or
/// <c>{ok:false, error, error_kind}</c> with an actionable message on failure.</summary>
public sealed record ProviderTestResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("latency_ms")]
    public long? LatencyMs { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>The <see cref="ProviderErrorKind"/> name, or <c>"configuration"</c> for a
    /// bad config — so a UI can branch without parsing the message.</summary>
    [JsonPropertyName("error_kind")]
    public string? ErrorKind { get; init; }

    public static ProviderTestResult Success(string model, long latencyMs) =>
        new() { Ok = true, Model = model, LatencyMs = latencyMs };

    public static ProviderTestResult Fail(string errorKind, string error) =>
        new() { Ok = false, ErrorKind = errorKind, Error = error };
}
