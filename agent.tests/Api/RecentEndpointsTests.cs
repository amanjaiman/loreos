using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 T003: <c>/recent</c> and <c>/activity</c> read 003's
/// <see cref="ActivityStore"/> only. The harness registers <b>no</b> <see cref="IMemoryService"/>,
/// so the endpoints working at all proves activity/telemetry data is served from its own store
/// and never conflated with mem0 memories.</summary>
public sealed class RecentEndpointsTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Activity_returns_log_entries_newest_first()
    {
        using var store = new ActivityStore(":memory:");
        await store.LogActivityAsync(new ActivityLogEntry(
            At, "code.exe", "older", ActivityDecision.Skipped, "unchanged", "", "ide"));
        await store.LogActivityAsync(new ActivityLogEntry(
            At.AddMinutes(1), "chrome.exe", "newer", ActivityDecision.Captured, "novel", "read the docs", "browser"));
        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument doc = await GetJsonAsync(harness, "/activity");
        JsonElement items = doc.RootElement.GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("newer", items[0].GetProperty("window_title").GetString());
        Assert.Equal("Captured", items[0].GetProperty("decision").GetString());
        Assert.Equal("read the docs", items[0].GetProperty("observation").GetString());
        Assert.Equal("older", items[1].GetProperty("window_title").GetString());
    }

    [Fact]
    public async Task Recent_returns_raw_captures_newest_first()
    {
        using var store = new ActivityStore(":memory:");
        await store.LogRawCaptureAsync(new RawCaptureEntry(
            At, "code.exe", "first", "uia", "code", "first text"));
        await store.LogRawCaptureAsync(new RawCaptureEntry(
            At.AddMinutes(1), "notepad.exe", "second", "ocr", "prose", "second text"));
        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument doc = await GetJsonAsync(harness, "/recent");
        JsonElement items = doc.RootElement.GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("second", items[0].GetProperty("window_title").GetString());
        Assert.Equal("ocr", items[0].GetProperty("extraction_source").GetString());
        Assert.Equal("second text", items[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Limit_caps_the_number_of_rows_returned()
    {
        using var store = new ActivityStore(":memory:");
        for (int i = 0; i < 5; i++)
        {
            await store.LogActivityAsync(new ActivityLogEntry(
                At.AddSeconds(i), "app.exe", $"w{i}", ActivityDecision.Captured, "r", "o", "c"));
        }

        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument doc = await GetJsonAsync(harness, "/activity?limit=2");
        Assert.Equal(2, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    private static Task<LoreApiHarness> StartAsync(ActivityStore store) =>
        LoreApiHarness.StartAsync(
            services => services.AddSingleton(store),
            app => app.MapRecentEndpoints());

    private static async Task<JsonDocument> GetJsonAsync(LoreApiHarness harness, string path)
    {
        HttpResponseMessage response = await harness.Client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
