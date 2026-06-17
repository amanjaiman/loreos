using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lore.Agent.Config;

/// <summary>A one-time migration that lifts a v1-style inline <c>provider.api_key</c> out
/// of <c>config.json</c> into the credential store and rewrites the config to reference it
/// by handle (<c>api_key_ref</c>). After it runs, no key material remains in the config
/// file (spec 004 acceptance criterion 3). Idempotent: a config that already uses a handle
/// is left untouched.</summary>
public static class InlineKeyMigration
{
    /// <summary>The handle an inline key is filed under when the config names none.</summary>
    public const string DefaultHandle = "lore/provider";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Migrate the file at <paramref name="configPath"/> in place. Returns
    /// <c>true</c> if a key was moved, <c>false</c> if there was nothing to do (no file,
    /// no provider block, or no inline key).</summary>
    public static bool Run(string configPath, ICredentialStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(store);

        if (!File.Exists(configPath))
        {
            return false;
        }

        string original = File.ReadAllText(configPath);
        if (!TryMigrate(original, store, out string migrated))
        {
            return false;
        }

        File.WriteAllText(configPath, migrated);
        return true;
    }

    /// <summary>Pure form for testing: migrate <paramref name="json"/> and produce
    /// <paramref name="migrated"/>. Returns <c>true</c> if a key was moved.</summary>
    public static bool TryMigrate(string json, ICredentialStore store, out string migrated)
    {
        ArgumentNullException.ThrowIfNull(store);
        migrated = json;

        JsonNode? root = JsonNode.Parse(json);
        if (root is not JsonObject rootObject
            || rootObject["provider"] is not JsonObject provider
            || provider["api_key"] is not JsonValue apiKeyNode
            || apiKeyNode.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        string? apiKey = apiKeyNode.GetValue<string>();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        string handle = provider["api_key_ref"]?.GetValue<string>() is { Length: > 0 } existing
            ? existing
            : DefaultHandle;

        store.Write(handle, apiKey);
        provider["api_key_ref"] = handle;
        provider.Remove("api_key");

        migrated = rootObject.ToJsonString(WriteOptions);
        return true;
    }
}
