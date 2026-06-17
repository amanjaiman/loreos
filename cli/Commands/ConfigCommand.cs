using System.CommandLine;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands;

/// <summary><c>lore config get [key]</c> / <c>lore config set &lt;key&gt; &lt;value&gt;</c> — read and
/// change config over <c>GET /config</c> and <c>PATCH /config</c>. Keys are dot-paths
/// (<c>provider.model</c>). The API scrubs secrets out of every <c>GET</c> and routes an inline
/// secret on <c>PATCH</c> into the OS keystore (constitution §4.2); the CLI never echoes a secret
/// value it stored, and re-scrubs anything it prints. Setting a secret prints a confirmation
/// without the value.</summary>
internal static class ConfigCommand
{
    private static readonly Argument<string?> GetKeyArgument = new("key")
    {
        Description = "Dot-path of the setting to read (e.g. provider.model). Omit to print the whole config.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private static readonly Argument<string> SetKeyArgument = new("key")
    {
        Description = "Dot-path of the setting to change (e.g. provider.model).",
    };

    private static readonly Argument<string> SetValueArgument = new("value")
    {
        Description = "The new value. Parsed as JSON when it looks like one (true, 42, 0.3); otherwise a string.",
    };

    public static Command Build()
    {
        var get = new Command("get", "Read a config value (or the whole config).")
        {
            GetKeyArgument,
        };
        get.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => GetAsync(client, output, parseResult.GetValue(GetKeyArgument), token),
            ct));

        var set = new Command("set", "Change a config value (secrets go to the OS keystore).")
        {
            SetKeyArgument,
            SetValueArgument,
        };
        set.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => SetAsync(
                client, output, parseResult.GetValue(SetKeyArgument)!, parseResult.GetValue(SetValueArgument)!, token),
            ct));

        var config = new Command("config", "Read or change Lore's configuration.")
        {
            get,
            set,
        };
        return config;
    }

    public static async Task<int> GetAsync(
        ApiClient client, Output output, string? key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        ApiResponse response = await client
            .SendAsync(HttpMethod.Get, "/config", cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return CommandSupport.Fail(output, response);
        }

        JsonNode config = response.ReadJson() ?? new JsonObject();

        // Whole config.
        if (string.IsNullOrWhiteSpace(key))
        {
            if (output.IsJson)
            {
                output.WriteData(config);
            }
            else
            {
                output.WriteLine(Scrubbed(config));
            }

            return ExitCodes.Success;
        }

        // A single dot-path.
        JsonNode? value = Navigate(config, key);
        if (value is null)
        {
            output.WriteError("not_found", $"no config value at '{key}'.");
            return ExitCodes.NotFound;
        }

        if (output.IsJson)
        {
            output.WriteData(new JsonObject { ["key"] = key, ["value"] = value.DeepClone() });
        }
        else
        {
            output.WriteLine(value is JsonValue scalar ? scalar.ToString() : Scrubbed(value));
        }

        return ExitCodes.Success;
    }

    public static async Task<int> SetAsync(
        ApiClient client, Output output, string key, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        string[] segments = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            output.WriteError("bad_usage", "a config key is required (e.g. provider.model).");
            return ExitCodes.BadUsage;
        }

        JsonObject patch = BuildPatch(segments, ParseValue(value));
        ApiResponse response = await client
            .SendAsync(HttpMethod.Patch, "/config", patch, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return CommandSupport.Fail(output, response);
        }

        bool secret = SecretScrubber.IsSecret(segments[^1]);
        if (output.IsJson)
        {
            // The PATCH response is the updated, already-scrubbed config; forward it.
            output.WriteData(response.ReadJson() ?? new JsonObject());
        }
        else if (secret)
        {
            output.WriteLine($"Set {key} (stored securely in the OS keystore; not shown).");
        }
        else
        {
            output.WriteLine($"Set {key} = {value}.");
        }

        return ExitCodes.Success;
    }

    // Build a nested object { a: { b: { leaf: value } } } from segments [a, b, leaf].
    private static JsonObject BuildPatch(string[] segments, JsonNode? value)
    {
        var root = new JsonObject();
        JsonObject current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var child = new JsonObject();
            current[segments[i]] = child;
            current = child;
        }

        current[segments[^1]] = value;
        return root;
    }

    private static JsonNode? Navigate(JsonNode config, string key)
    {
        JsonNode? current = config;
        foreach (string segment in key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out JsonNode? next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // Treat a value that parses as a JSON scalar (true/false/number/null/quoted) as that scalar;
    // otherwise it's a bare string. So `set capture.enabled true` stores a boolean, while
    // `set provider.api_key sk-...` stores a string.
    private static JsonNode? ParseValue(string raw)
    {
        try
        {
            JsonNode? parsed = JsonNode.Parse(raw);
            if (parsed is JsonValue)
            {
                return parsed;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON — fall through to a plain string.
        }

        return JsonValue.Create(raw);
    }

    private static string Scrubbed(JsonNode node)
    {
        JsonNode clone = node.DeepClone();
        SecretScrubber.Scrub(clone);
        return clone.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
