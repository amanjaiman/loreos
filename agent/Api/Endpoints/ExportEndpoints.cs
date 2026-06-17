using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Lore.Agent.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The export HTTP surface (spec 005 T005, acceptance criterion 4): the full memory set
/// as JSON (<c>GET /export/json</c>) or Markdown (<c>GET /export/markdown</c>). Data ownership,
/// not lock-in — a user can take everything Lore knows about them at any time.</summary>
public static class ExportEndpoints
{
    /// <summary>Map the export endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/export/json", async ([FromServices] IMemoryService memory, CancellationToken ct) =>
        {
            IReadOnlyList<MemoryRecord> all = await memory.GetAllAsync(cancellationToken: ct).ConfigureAwait(false);
            var export = new MemoryExport(
                Now(), all.Count, all.Select(MemoryDto.From).ToArray());
            return Results.Ok(export);
        });

        app.MapGet("/export/markdown", async ([FromServices] IMemoryService memory, CancellationToken ct) =>
        {
            IReadOnlyList<MemoryRecord> all = await memory.GetAllAsync(cancellationToken: ct).ConfigureAwait(false);
            return Results.Text(RenderMarkdown(all), "text/markdown", Encoding.UTF8);
        });

        return app;
    }

    private static string RenderMarkdown(IReadOnlyList<MemoryRecord> memories)
    {
        var builder = new StringBuilder();
        builder.Append("# Lore memory export\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"_Exported {Now()} · {memories.Count} memories_\n");

        if (memories.Count == 0)
        {
            builder.Append("\n_No memories yet._\n");
            return builder.ToString();
        }

        builder.Append('\n');
        foreach (MemoryRecord record in memories)
        {
            // One bullet per memory; the source text is escaped so newlines don't break the list.
            string text = record.Memory.Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\n', ' ').Replace('\r', ' ');
            builder.Append("- ").Append(text);
            if (!string.IsNullOrWhiteSpace(record.CreatedAt))
            {
                builder.Append(CultureInfo.InvariantCulture, $" _({record.CreatedAt})_");
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>The JSON export envelope: when it was taken, how many, and the full set.</summary>
public sealed record MemoryExport(
    [property: JsonPropertyName("exported_at")] string ExportedAt,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("memories")] IReadOnlyList<MemoryDto> Memories);
