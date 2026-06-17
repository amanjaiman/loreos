namespace Lore.Agent.Providers;

/// <summary>Why a model call failed, in terms the user can act on. The test-connection
/// endpoint (spec 004 T005) maps these to the specific messages required by acceptance
/// criterion 4 (bad key / unreachable URL / unknown model).</summary>
public enum ProviderErrorKind
{
    /// <summary>The endpoint rejected the credentials (HTTP 401/403): bad or missing key.</summary>
    Unauthorized,

    /// <summary>The endpoint could not be reached: DNS failure, refused connection,
    /// wrong host/port, or a timeout.</summary>
    Unreachable,

    /// <summary>The endpoint is reachable and authorized but does not know the model.</summary>
    ModelNotFound,

    /// <summary>Anything else: an unexpected status or an unparseable body.</summary>
    BadResponse,
}
