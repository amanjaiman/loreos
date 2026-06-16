using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class BlocklistTests
{
    [Theory]
    [InlineData("1password")]          // config bare, query bare
    [InlineData("1Password")]          // case-insensitive
    [InlineData("1password.exe")]      // query carries .exe
    [InlineData("  1password  ")]      // surrounding whitespace
    public void Matches_a_blocklisted_app_however_the_name_is_spelled(string query)
    {
        var blocklist = new Blocklist(new[] { "1Password.exe" }, Array.Empty<string>());
        Assert.True(blocklist.MatchesApp(query));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Does_not_match_an_app_that_is_not_listed(string? query)
    {
        var blocklist = new Blocklist(new[] { "1password" }, Array.Empty<string>());
        Assert.False(blocklist.MatchesApp(query));
    }

    [Fact]
    public void Config_entries_are_normalized_so_exe_suffix_and_case_do_not_matter()
    {
        var blocklist = new Blocklist(new[] { "  BANKApp.EXE  " }, Array.Empty<string>());
        Assert.True(blocklist.MatchesApp("bankapp"));
        Assert.True(blocklist.MatchesApp("BankApp.exe"));
    }

    [Theory]
    [InlineData("Chase Online Banking", true)]   // substring, different case
    [InlineData("my BANKING portal", true)]
    [InlineData("grocery list", false)]
    public void Matches_keywords_as_case_insensitive_substrings(string text, bool expected)
    {
        var blocklist = new Blocklist(Array.Empty<string>(), new[] { "banking" });
        Assert.Equal(expected, blocklist.MatchesKeyword(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_text_matches_no_keyword(string? text)
    {
        var blocklist = new Blocklist(Array.Empty<string>(), new[] { "banking" });
        Assert.False(blocklist.MatchesKeyword(text));
    }

    [Fact]
    public void Blank_keywords_and_apps_in_config_are_ignored()
    {
        var blocklist = new Blocklist(new[] { "", "   " }, new[] { "  ", "" });
        Assert.False(blocklist.MatchesApp(""));
        Assert.False(blocklist.MatchesKeyword("anything at all"));
    }

    [Fact]
    public void The_empty_blocklist_blocks_nothing()
    {
        Assert.False(Blocklist.Empty.MatchesApp("1password"));
        Assert.False(Blocklist.Empty.MatchesKeyword("banking"));
    }

    [Fact]
    public void Constructor_rejects_null_collections()
    {
        Assert.Throws<ArgumentNullException>(() => new Blocklist(null!, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => new Blocklist(Array.Empty<string>(), null!));
    }
}
