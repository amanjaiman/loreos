using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lore.Agent.Config;

/// <summary>The read/write face of <c>config.json</c> for the local API (spec 005 T004). It
/// treats the file as a JSON document — provider, capture, blocklist, memory blocks — rather
/// than a strongly-typed model, so the config surface can grow without this class drifting from
/// every option type.
///
/// <para>Two guarantees the constitution requires (§4.2):</para>
/// <list type="bullet">
/// <item><b>No secret echo.</b> Every value handed back is scrubbed of inline key material, so
/// a key can never leave through a config response.</item>
/// <item><b>Keys via the store.</b> A <c>PATCH</c> carrying an inline <c>provider.api_key</c> is
/// moved into the <see cref="ICredentialStore"/> and replaced by an <c>api_key_ref</c> handle
/// before anything is persisted — the file never holds a key.</item>
/// </list>
/// Writes are serialized so concurrent PATCHes can't interleave a half-written file.</summary>
public sealed class LoreConfig : IDisposable
{
    /// <summary>Properties holding raw secret material. Stripped from every response and never
    /// persisted inline (they are migrated to the credential store on write).</summary>
    private static readonly string[] SecretProperties = ["api_key"];

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ICredentialStore _credentials;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="path">Absolute path to <c>config.json</c>.</param>
    /// <param name="credentials">Where inline keys are relocated on write.</param>
    public LoreConfig(string path, ICredentialStore credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(credentials);
        _path = path;
        _credentials = credentials;
    }

    /// <summary>The default config location: <c>%AppData%\Lore\config.json</c>, alongside the
    /// memory data dir. See <see cref="LorePaths"/> for why it is not under LocalAppData.</summary>
    public static string DefaultPath => LorePaths.ConfigFile;

    /// <summary>Read the current config, scrubbed of any inline secret. Returns an empty object
    /// when the file does not exist yet.</summary>
    public async Task<JsonObject> ReadScrubbedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Scrub(await LoadAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deep-merge <paramref name="patch"/> into the current config, relocate any inline
    /// key to the credential store, persist the result, and return it scrubbed. Object values
    /// merge recursively (so a client can change <c>provider.model</c> alone); scalars and arrays
    /// replace.</summary>
    public async Task<JsonObject> PatchAsync(JsonObject patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject merged = await LoadAsync(cancellationToken).ConfigureAwait(false);
            Merge(merged, patch);

            // Relocate an inline provider.api_key into the credential store and rewrite it as a
            // handle, reusing the v1 migration so there is one code path that moves a key out of
            // the file. After this, the persisted text holds no key material.
            string json = merged.ToJsonString(WriteOptions);
            if (InlineKeyMigration.TryMigrate(json, _credentials, out string migrated))
            {
                json = migrated;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, json, cancellationToken).ConfigureAwait(false);

            return Scrub((JsonObject)(JsonNode.Parse(json) ?? new JsonObject()));
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<JsonObject> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new JsonObject();
        }

        string json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }

    // Recursively copy patch into target: objects merge, everything else replaces. Values are
    // cloned because a JsonNode may have only one parent.
    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (KeyValuePair<string, JsonNode?> pair in patch)
        {
            if (pair.Value is JsonObject patchObject && target[pair.Key] is JsonObject targetObject)
            {
                Merge(targetObject, patchObject);
            }
            else
            {
                target[pair.Key] = pair.Value?.DeepClone();
            }
        }
    }

    // Return a clone with every secret-bearing property removed at any depth, so no inline key
    // can ride out in a response even if one somehow reached the file.
    private static JsonObject Scrub(JsonObject root)
    {
        var clone = (JsonObject)root.DeepClone();
        StripSecrets(clone);
        return clone;
    }

    private static void StripSecrets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string secret in SecretProperties)
                {
                    obj.Remove(secret);
                }

                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    StripSecrets(pair.Value);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    StripSecrets(item);
                }

                break;
        }
    }
}
