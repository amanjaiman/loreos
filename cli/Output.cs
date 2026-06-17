using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lore.Cli;

/// <summary>The CLI's output layer: every command renders through here in exactly one of two
/// modes. Human mode (default) writes friendly text to stdout; <c>--json</c> mode writes a single
/// <b>versioned envelope</b> — <c>{ schema_version, ok, data, error }</c> — that agents and scripts
/// can pin and branch on (spec 007, acceptance criterion 2). The envelope shape and
/// <see cref="SchemaVersion"/> are a stable machine contract; both modes scrub secrets before
/// printing (<see cref="SecretScrubber"/>). Writers are injected so tests can capture output.</summary>
internal sealed class Output
{
    /// <summary>The <c>--json</c> envelope schema version. Bumped only on a breaking change to the
    /// envelope, so a script can assert the shape it was written against.</summary>
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Output is for a terminal/pipe, never HTML; relaxed escaping keeps apostrophes and other
        // punctuation readable (e.g. don't, café) while remaining valid JSON.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    public Output(TextWriter stdout, TextWriter stderr, bool json)
    {
        _stdout = stdout;
        _stderr = stderr;
        IsJson = json;
    }

    /// <summary>Whether the CLI is in <c>--json</c> mode. Commands branch on this to choose between
    /// <see cref="WriteData"/> and human rendering.</summary>
    public bool IsJson { get; }

    /// <summary>Emit a successful result in <c>--json</c> mode: wrap <paramref name="data"/> in the
    /// envelope (<c>ok: true</c>), scrub secrets, and write it to stdout. <paramref name="data"/>
    /// may be a typed DTO or a <see cref="JsonNode"/> forwarded straight from the API (the thinnest
    /// shim). Call only when <see cref="IsJson"/> is true.</summary>
    public void WriteData(object data)
    {
        ArgumentNullException.ThrowIfNull(data);

        JsonNode? node = data as JsonNode ?? JsonSerializer.SerializeToNode(data);
        SecretScrubber.Scrub(node);
        WriteEnvelope(new Envelope(SchemaVersion, Ok: true, Data: node, Error: null));
    }

    /// <summary>Emit a failure. In <c>--json</c> mode, write the envelope with <c>ok: false</c> and
    /// a stable <paramref name="code"/> to stdout (so a script still gets one parseable object); in
    /// human mode, write <paramref name="message"/> to stderr.</summary>
    public void WriteError(string code, string message)
    {
        if (IsJson)
        {
            WriteEnvelope(new Envelope(SchemaVersion, Ok: false, Data: null, Error: new EnvelopeError(code, message)));
        }
        else
        {
            _stderr.WriteLine(message);
        }
    }

    /// <summary>Write a line of human-readable output to stdout. Used only in human mode.</summary>
    public void WriteLine(string text = "") => _stdout.WriteLine(text);

    /// <summary>Write human-readable output to stdout without a trailing newline.</summary>
    public void Write(string text) => _stdout.Write(text);

    private void WriteEnvelope(Envelope envelope) =>
        _stdout.WriteLine(JsonSerializer.Serialize(envelope, EnvelopeOptions));

    /// <summary>The <c>--json</c> envelope. <c>data</c> is present on success, <c>error</c> on
    /// failure; the unused one is omitted (null-ignoring serialization).</summary>
    private sealed record Envelope(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("data")] JsonNode? Data,
        [property: JsonPropertyName("error")] EnvelopeError? Error);

    /// <summary>The error half of the envelope: a stable <c>code</c> plus a human message.</summary>
    private sealed record EnvelopeError(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message);
}
