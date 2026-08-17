using System.Text.Json;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Recall;

/// <summary>The v2-001 golden corpus: the spec's motivating journeys (wisdom teeth,
/// France) plus distractors, with hand-authored semantic similarities that stand in for
/// a real embedder. These calibrate the blend/floor (spec AC 1/2/6) deterministically;
/// true semantic matching is verified end-to-end by the T010 harness against a live
/// embedder.</summary>
/// <summary>A <see cref="TimeProvider"/> pinned to <see cref="GoldenCorpus.Now"/>.</summary>
internal sealed class GoldenTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => GoldenCorpus.Now;
}

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

        // v2-008 T001 — the recall-floor verification gate (spec R5.2). Eight flight
        // bookings spread across ~2 years, confidence pinned to 1.0 so the test isolates
        // the effect under study (kind weight × temporal decay vs. Floor) from R1.2's
        // confidence dial, a separate control. Ages are the spec's example months
        // (1/3/5/8/12/16/20/24) expressed as days.
        new("flight-1mo", "I booked a United flight to San Diego for Sep 9-13.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 30, null),
        new("flight-3mo", "I booked a Delta flight to Boston for Jun 2-6.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 90, null),
        new("flight-5mo", "I booked a JetBlue flight to Austin for Apr 14-18.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 150, null),
        new("flight-8mo", "I booked an American Airlines flight to Chicago for Jan 20-24.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 240, null),
        new("flight-12mo", "I booked a Southwest flight to Phoenix for Aug 3-7.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 365, null),
        new("flight-16mo", "I booked a United flight to Seattle for Apr 11-15.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 480, null),
        new("flight-20mo", "I booked a Delta flight to Miami for Dec 5-9.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 600, null),
        new("flight-24mo", "I booked an Alaska Airlines flight to Portland for Aug 20-24.",
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0, 730, null),
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
            // v2-008 T001 (R5.2 verification gate): REAL nomic-embed-text similarities,
            // not hand-authored — measured 2026-08-16 against a locally running
            // `nomic-embed-text` via Ollama's /api/embed, cosine similarity between the
            // query embedding and each flight statement's embedding. Unlike the
            // hand-authored pairs above, these numbers are load-bearing for T001's
            // conclusion, so they come from the real embedder the floor was calibrated
            // against (see RecallOptions.Floor) rather than a guess. All eight flights
            // land in a tight 0.81-0.83 band — nomic-embed-text does not distinguish "a
            // flight I booked" from "a flight to Denver" by destination/airline/date, only
            // by topic — so age (via ExperienceDecayFloor) is what separates them, not
            // semantic score.
            ["book a flight to Denver"] = new Dictionary<string, double>
            {
                ["flight-1mo"] = 0.8188,
                ["flight-3mo"] = 0.8226,
                ["flight-5mo"] = 0.8141,
                ["flight-8mo"] = 0.8140,
                ["flight-12mo"] = 0.8170,
                ["flight-16mo"] = 0.8309,
                ["flight-20mo"] = 0.8250,
                ["flight-24mo"] = 0.8140,
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
