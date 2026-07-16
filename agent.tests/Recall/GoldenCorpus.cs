using System.Text.Json;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Recall;

/// <summary>The v2-001 golden corpus: the spec's motivating journeys (wisdom teeth,
/// France) plus distractors, with hand-authored semantic similarities that stand in for
/// a real embedder. These calibrate the blend/floor (spec AC 1/2/6) deterministically;
/// true semantic matching is verified end-to-end by the T010 harness against a live
/// embedder.</summary>
internal static class GoldenCorpus
{
    /// <summary>"Now" for every golden test: 2026-07-15T12:00:00Z.</summary>
    public static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    public sealed record GoldenMemory(
        string Id,
        string Statement,
        string Kind,
        string Status,
        double Confidence,
        int EstablishedDaysAgo,
        int? ExpiresInDays); // null → far-future sentinel

    public static readonly IReadOnlyList<GoldenMemory> Memories =
    [
        new("wisdom", "I'm recovering from a wisdom tooth extraction.",
            MemoryKinds.State, MemoryStatuses.Active, 0.8, 3, 30),
        new("france", "I visited France in spring 2026.",
            MemoryKinds.Experience, MemoryStatuses.Active, 0.9, 60, null),
        new("boston", "I live in Boston.",
            MemoryKinds.Identity, MemoryStatuses.Active, 0.9, 200, null),
        new("tea", "I prefer tea over coffee.",
            MemoryKinds.Preference, MemoryStatuses.Active, 0.8, 90, null),
        new("loreos", "I'm building an open-source memory layer called Lore.",
            MemoryKinds.Project, MemoryStatuses.Active, 0.85, 30, null),
        new("marathon-old", "I ran a marathon in 2019.",
            MemoryKinds.Experience, MemoryStatuses.Active, 0.9, 2500, null),
        new("weak-state", "I might be coming down with a cold.",
            MemoryKinds.State, MemoryStatuses.Active, 0.3, 1, 14),
        new("staged-jobs", "I'm looking for a new job.",
            MemoryKinds.State, MemoryStatuses.Staged, 0.6, 2, 14),
        new("archived-city", "I live in Seattle.",
            MemoryKinds.Identity, MemoryStatuses.Archived, 0.9, 400, null),
    ];

    /// <summary>query → (memoryId → semantic similarity). Absent pairs are 0.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Similarities =
        new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            // AC 1: cross-domain — a food question surfaces the dental state.
            ["should I order takeout tonight"] = new Dictionary<string, double>
            {
                ["wisdom"] = 0.62,
                ["tea"] = 0.48,
                ["weak-state"] = 0.55,
                ["france"] = 0.20,
            },
            // AC 2: travel surfaces the France experience (and would surface the
            // archived/staged rows if filters ever regressed).
            ["what travel destination should I pick next"] = new Dictionary<string, double>
            {
                ["france"] = 0.82,
                ["boston"] = 0.42,
                ["staged-jobs"] = 0.90,
                ["archived-city"] = 0.90,
                ["marathon-old"] = 0.60,
            },
            // AC 6: unrelated queries return empty, not padded.
            ["how do I cook pasta carbonara"] = new Dictionary<string, double>
            {
                ["tea"] = 0.30,
                ["wisdom"] = 0.22,
            },
            // Kind restriction test: strong hits across kinds.
            ["tell me about myself"] = new Dictionary<string, double>
            {
                ["boston"] = 0.80,
                ["tea"] = 0.78,
                ["france"] = 0.85,
                ["loreos"] = 0.82,
                ["wisdom"] = 0.79,
            },
        };

    public static MemoryRecord ToRecord(GoldenMemory memory, double score)
    {
        long now = Now.ToUnixTimeSeconds();
        var metadata = new MemoryMetadata(
            memory.Kind,
            memory.Status,
            memory.Confidence,
            memory.ExpiresInDays is int days
                ? now + days * 86_400L
                : MemoryMetadata.FarFutureUnixSeconds,
            now - memory.EstablishedDaysAgo * 86_400L,
            MemoryUpdateReasons.Promoted);
        return new MemoryRecord(
            memory.Id,
            memory.Statement,
            score,
            metadata.ToDictionary().ToDictionary(
                pair => pair.Key,
                pair => JsonSerializer.SerializeToElement(pair.Value)));
    }
}
