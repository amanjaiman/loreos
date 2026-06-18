using System.IO;

namespace Lore.Agent.Hosting;

/// <summary>Configuration for the memoryd sidecar and how the agent supervises it.
/// Bound from the <c>memory</c> config section.</summary>
public sealed class MemorydOptions
{
    /// <summary><c>embedded</c> (spawn and supervise a local sidecar) or
    /// <c>remote</c> (point at a user-hosted mem0 server; spec 002 T006).</summary>
    public string Engine { get; set; } = "embedded";

    /// <summary>Loopback host the embedded sidecar binds to.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Port the embedded sidecar listens on.</summary>
    public int Port { get; set; } = 7843;

    /// <summary>Used when <see cref="Engine"/> is <c>remote</c>.</summary>
    public Uri? RemoteUrl { get; set; }

    /// <summary>Lore data dir; the sidecar keeps Qdrant + history here.</summary>
    public string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore");

    /// <summary>Explicit path to the packaged PyInstaller sidecar. When set and
    /// present it is launched instead of the bundled default or the Python module;
    /// normally left unset so <see cref="BundledExecutablePath"/> is auto-discovered.</summary>
    public string? PackagedExecutable { get; set; }

    /// <summary>Directory the agent runs from; the bundled sidecar is discovered
    /// relative to it. Defaults to the executable's directory; overridable for tests.</summary>
    public string BaseDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>Conventional location of the frozen sidecar the installer (spec 011)
    /// lays down next to the agent: <c>&lt;BaseDirectory&gt;/memoryd/lore-memoryd.exe</c>.
    /// Used when <see cref="PackagedExecutable"/> is unset, so a Release/installed
    /// build runs the packaged exe while a from-source dev build falls back to Python.</summary>
    public string BundledExecutablePath => Path.Combine(BaseDirectory, "memoryd", "lore-memoryd.exe");

    /// <summary>Python interpreter used in dev when no packaged sidecar is present
    /// (<c>python -m lore_memoryd</c>).</summary>
    public string PythonExecutable { get; set; } = "python";

    /// <summary>How long to wait for <c>/health</c> to go green on launch. Generous
    /// to cover a PyInstaller <c>--onefile</c> cold start, which unpacks to a temp
    /// dir on first run (spike T001 Finding 4).</summary>
    public TimeSpan HealthGateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Interval between <c>/health</c> polls while gating.</summary>
    public TimeSpan HealthPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Delay before restarting the sidecar after an unexpected exit.</summary>
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Whether the agent spawns and supervises a child process.</summary>
    public bool IsEmbedded => !string.Equals(Engine, "remote", StringComparison.OrdinalIgnoreCase);

    /// <summary>The base address of memoryd, embedded or remote. Always ends with a
    /// trailing slash so the client's relative request URIs resolve correctly.</summary>
    public Uri BaseAddress
    {
        get
        {
            if (IsEmbedded)
            {
                return new Uri($"http://{Host}:{Port}/");
            }

            if (RemoteUrl is null)
            {
                throw new InvalidOperationException("memory.engine is 'remote' but memory.remote_url is not set");
            }

            if (!string.IsNullOrEmpty(RemoteUrl.Query) || !string.IsNullOrEmpty(RemoteUrl.Fragment))
            {
                throw new InvalidOperationException(
                    "memory.remote_url must be a base URL without a query string or fragment");
            }

            string url = RemoteUrl.AbsoluteUri;
            return url.EndsWith('/') ? RemoteUrl : new Uri(url + "/");
        }
    }
}
