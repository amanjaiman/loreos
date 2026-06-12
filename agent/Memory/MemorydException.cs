namespace Lore.Agent.Memory;

/// <summary>Thrown when memoryd returns an unexpected response. Carries the HTTP
/// status and (redacted) body so the failure is actionable at the seam
/// (constitution §5).</summary>
public sealed class MemorydException : Exception
{
    public MemorydException()
    {
    }

    public MemorydException(string message)
        : base(message)
    {
    }

    public MemorydException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MemorydException(string operation, int statusCode, string body)
        : base($"memoryd '{operation}' failed with HTTP {statusCode}: {body}")
    {
        Operation = operation;
        StatusCode = statusCode;
        Body = body;
    }

    /// <summary>The client operation that failed (e.g. <c>remember</c>).</summary>
    public string? Operation { get; }

    /// <summary>The HTTP status code memoryd returned.</summary>
    public int StatusCode { get; }

    /// <summary>The response body memoryd returned.</summary>
    public string? Body { get; }
}
