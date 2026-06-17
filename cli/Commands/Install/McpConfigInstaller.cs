using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands.Install;

/// <summary>The merge logic behind <c>lore mcp install</c> (spec 007 T006, acceptance criterion 4):
/// add a <c>lore</c> entry to an MCP client's <c>mcpServers</c> map without disturbing anything else.
/// Every supported client (Claude Desktop, Cursor, Claude Code) uses the same
/// <c>{ "mcpServers": { "lore": { "command", "args" } } }</c> shape, so one merger serves them all.
/// It is <b>idempotent</b> (re-running with the same entry rewrites nothing), it <b>merges</b> rather
/// than overwrites (other servers and unrelated keys are preserved), and it <b>backs up</b> the file
/// before any change. Pure file/JSON work with explicit paths, so it is fully unit-tested.</summary>
internal static class McpConfigInstaller
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Ensure the config at <paramref name="configPath"/> contains a <c>lore</c> MCP server
    /// pointing at <paramref name="agentExePath"/> (<c>--mcp</c>). Creates the file if absent, merges
    /// into it otherwise, and reports what it did.</summary>
    public static InstallResult Install(string configPath, string agentExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentExePath);

        bool existed = File.Exists(configPath);
        JsonObject root = existed ? ParseObject(File.ReadAllText(configPath), configPath) : new JsonObject();
        JsonObject servers = GetOrCreateObject(root, "mcpServers");

        // Already correct? Then do nothing — re-running install is a no-op (idempotent).
        if (servers["lore"] is JsonObject existing && EntryMatches(existing, agentExePath))
        {
            return new InstallResult(InstallAction.Unchanged, configPath, BackupPath: null);
        }

        // Back up before the first byte changes, but only when there is a file to lose.
        string? backupPath = null;
        if (existed)
        {
            backupPath = configPath + ".bak";
            File.Copy(configPath, backupPath, overwrite: true);
        }

        servers["lore"] = BuildEntry(agentExePath);

        string? directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configPath, root.ToJsonString(WriteOptions));
        return new InstallResult(existed ? InstallAction.Updated : InstallAction.Created, configPath, backupPath);
    }

    private static JsonObject BuildEntry(string agentExePath) => new()
    {
        ["command"] = agentExePath,
        ["args"] = new JsonArray("--mcp"),
    };

    private static bool EntryMatches(JsonObject entry, string agentExePath)
    {
        string? command = (entry["command"] as JsonValue)?.GetValue<string>();
        if (!string.Equals(command, agentExePath, StringComparison.Ordinal))
        {
            return false;
        }

        return entry["args"] is JsonArray args
            && args.Count == 1
            && (args[0] as JsonValue)?.GetValue<string>() == "--mcp";
    }

    private static JsonObject ParseObject(string text, string configPath)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new McpInstallException($"{configPath} is not valid JSON; leaving it untouched.", ex);
        }

        return node as JsonObject
            ?? throw new McpInstallException($"{configPath} is not a JSON object; leaving it untouched.");
    }

    private static JsonObject GetOrCreateObject(JsonObject root, string key)
    {
        switch (root[key])
        {
            case JsonObject existing:
                return existing;
            case null:
                var created = new JsonObject();
                root[key] = created;
                return created;
            default:
                throw new McpInstallException($"'{key}' in the config is not a JSON object; leaving it untouched.");
        }
    }
}

/// <summary>What <see cref="McpConfigInstaller.Install"/> did, for the command to report.</summary>
internal sealed record InstallResult(InstallAction Action, string ConfigPath, string? BackupPath);

/// <summary>Whether the install created a new file, merged into an existing one, or found the entry
/// already correct.</summary>
internal enum InstallAction
{
    Created,
    Updated,
    Unchanged,
}

/// <summary>Thrown when an existing client config can't be safely merged (it isn't valid JSON, or a
/// key the installer needs holds the wrong type). The file is left untouched.</summary>
public sealed class McpInstallException : Exception
{
    public McpInstallException()
    {
    }

    public McpInstallException(string message)
        : base(message)
    {
    }

    public McpInstallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
