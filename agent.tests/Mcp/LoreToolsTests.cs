using System.Text.Json;
using Lore.Agent.Inference;
using Lore.Agent.Mcp;
using Lore.Agent.Memory;
using Lore.Agent.Tests.Memory;

namespace Lore.Agent.Tests.Mcp;

/// <summary>Unit coverage for the MCP tool mappings (spec 006 T001, acceptance criterion 1
/// logic). Each test drives a tool directly against the in-memory <see cref="FakeMemoryService"/>
/// and asserts it translates to the documented <see cref="IMemoryService"/> call and returns the
/// right shape — the tools are thin shims, so testing the mapping is testing the tool.</summary>
public sealed class LoreToolsTests
{
    [Fact]
    public async Task GetContext_searches_and_returns_scored_hits()
    {
        var memory = new FakeMemoryService();
        memory.Seed("The user is using PostgreSQL");
        memory.Seed("The user likes hiking");

        ContextResult result = await LoreTools.GetContext(memory, "PostgreSQL");

        MemoryHit hit = Assert.Single(result.Memories);
        Assert.Equal("The user is using PostgreSQL", hit.Text);
        Assert.Equal(0.9, hit.Score);
    }

    [Fact]
    public async Task GetContext_clamps_and_falls_back_on_non_positive_limit()
    {
        var memory = new FakeMemoryService();
        for (int i = 0; i < 15; i++)
        {
            memory.Seed($"shared topic {i}");
        }

        // limit 0 falls back to the default of 10.
        ContextResult result = await LoreTools.GetContext(memory, "shared", limit: 0);

        Assert.Equal(10, result.Memories.Count);
    }

    [Fact]
    public async Task GetRecent_returns_the_tail_of_the_store()
    {
        var memory = new FakeMemoryService();
        memory.Seed("oldest");
        memory.Seed("middle");
        memory.Seed("newest");

        ContextResult result = await LoreTools.GetRecent(memory, limit: 2);

        Assert.Equal(new[] { "middle", "newest" }, result.Memories.Select(m => m.Text));
    }

    [Fact]
    public async Task GetProfile_groups_by_category_metadata()
    {
        var memory = new FakeMemoryService();
        memory.Seed("Prefers dark mode", metadata: Category("preferences"));
        memory.Seed("Uses C#", metadata: Category("coding"));
        memory.Seed("Uses Rust", metadata: Category("coding"));
        memory.Seed("An untagged memory"); // no category → "general"

        ProfileResult result = await LoreTools.GetProfile(memory);

        Assert.Equal(4, result.Total);
        Assert.Equal(new[] { "coding", "general", "preferences" }, result.Categories.Select(c => c.Category));
        ProfileCategory coding = result.Categories.Single(c => c.Category == "coding");
        Assert.Equal(2, coding.Memories.Count);
    }

    [Fact]
    public async Task SummarizeProfile_sends_all_memories_to_the_model()
    {
        var memory = new FakeMemoryService();
        memory.Seed("Uses C#");
        memory.Seed("Lives in Seattle");
        var inference = new CapturingInferenceBackend("A C# developer in Seattle.");

        string summary = await LoreTools.SummarizeProfile(memory, inference);

        Assert.Equal("A C# developer in Seattle.", summary);
        Assert.Contains("Uses C#", inference.LastUserPrompt, StringComparison.Ordinal);
        Assert.Contains("Lives in Seattle", inference.LastUserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeProfile_reports_when_there_is_nothing_to_summarize()
    {
        var memory = new FakeMemoryService();
        var inference = new CapturingInferenceBackend("unused");

        string summary = await LoreTools.SummarizeProfile(memory, inference);

        Assert.Contains("no memories", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(inference.LastUserPrompt); // the model is not called when there is nothing to say
    }

    [Fact]
    public async Task SummarizeProfile_falls_back_to_raw_facts_when_no_model_is_configured()
    {
        var memory = new FakeMemoryService();
        memory.Seed("Uses C#");
        var inference = new CapturingInferenceBackend(null); // a NullInferenceBackend returns null

        string summary = await LoreTools.SummarizeProfile(memory, inference);

        Assert.Contains("Uses C#", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddContext_remembers_with_the_category_in_metadata()
    {
        var memory = new FakeMemoryService();

        AddResult result = await LoreTools.AddContext(memory, "Prefers TypeScript", "preferences");

        AddedMemoryDto added = Assert.Single(result.Added);
        Assert.Equal("Prefers TypeScript", added.Text);
        Assert.Equal("ADD", added.Event);

        // The stored memory carries the category so get_profile can group it.
        ProfileResult profile = await LoreTools.GetProfile(memory);
        Assert.Equal("preferences", Assert.Single(profile.Categories).Category);
    }

    [Fact]
    public async Task AddContext_defaults_the_category_when_omitted()
    {
        var memory = new FakeMemoryService();

        await LoreTools.AddContext(memory, "Some fact", category: "   ");

        ProfileResult profile = await LoreTools.GetProfile(memory);
        Assert.Equal("general", Assert.Single(profile.Categories).Category);
    }

    [Fact]
    public async Task UpdateContext_replaces_the_best_matching_memory()
    {
        var memory = new FakeMemoryService();
        string id = memory.Seed("The user uses Vim");

        UpdateResult result = await LoreTools.UpdateContext(memory, "Vim", "The user now uses VS Code");

        Assert.Equal("updated", result.Outcome);
        Assert.Equal(id, result.Id);
        Assert.Equal("The user now uses VS Code", result.Text);
        Assert.Equal("The user uses Vim", result.PreviousText);

        MemoryRecord? stored = await memory.GetAsync(id);
        Assert.Equal("The user now uses VS Code", stored!.Memory);
    }

    [Fact]
    public async Task UpdateContext_adds_a_fresh_memory_when_nothing_matches()
    {
        var memory = new FakeMemoryService();

        UpdateResult result = await LoreTools.UpdateContext(memory, "editors", "The user uses Helix");

        Assert.Equal("added", result.Outcome);
        Assert.Equal("The user uses Helix", result.Text);

        IReadOnlyList<MemoryRecord> all = await memory.GetAllAsync();
        Assert.Equal("The user uses Helix", Assert.Single(all).Memory);
    }

    [Fact]
    public async Task Forget_deletes_the_best_match_and_counts_the_rest()
    {
        var memory = new FakeMemoryService();
        memory.Seed("coffee preference one");
        memory.Seed("coffee preference two");

        ForgetResult result = await LoreTools.Forget(memory, "coffee");

        Assert.True(result.Forgotten);
        Assert.Equal("coffee preference one", result.Text);
        Assert.Equal(1, result.RemainingMatches);

        IReadOnlyList<MemoryRecord> all = await memory.GetAllAsync();
        Assert.Equal("coffee preference two", Assert.Single(all).Memory);
    }

    [Fact]
    public async Task Forget_reports_nothing_when_no_memory_matches()
    {
        var memory = new FakeMemoryService();
        memory.Seed("unrelated");

        ForgetResult result = await LoreTools.Forget(memory, "nonexistent topic");

        Assert.False(result.Forgotten);
        Assert.Null(result.Id);
        Assert.Equal(0, result.RemainingMatches);
    }

    private static Dictionary<string, JsonElement> Category(string value) =>
        new()
        {
            [LoreTools.CategoryKey] = JsonSerializer.SerializeToElement(value),
        };

    /// <summary>An <see cref="IInferenceBackend"/> that records the prompt it was given and
    /// returns a canned completion (or <c>null</c> to model a missing provider).</summary>
    private sealed class CapturingInferenceBackend(string? completion) : IInferenceBackend
    {
        public string? LastUserPrompt { get; private set; }

        public Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            LastUserPrompt = request.UserPrompt;
            return Task.FromResult(completion);
        }
    }
}
