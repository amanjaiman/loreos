using System.IO;
using System.Reflection;
using System.Text.Json;
using Lore.Agent.Api;

namespace Lore.Agent.Tests;

/// <summary>Spec 012 T001 — the product version is single-sourced. The agent assembly version
/// (what <c>GET /system/status</c> reports, driven by the root <c>Directory.Build.props</c>
/// <c>&lt;Version&gt;</c>) and <c>app/package.json</c> (which names the Squirrel installer
/// artifact) must agree, or <c>winget upgrade</c> keys off a version the build never produced.
/// This test fails the build if they drift; the release workflow additionally asserts the git
/// tag matches both (see <c>.github/workflows/release.yml</c>).</summary>
public sealed class VersionSyncTests
{
    [Fact]
    public void Agent_assembly_version_matches_app_package_json()
    {
        string agentVersion = AgentInformationalVersion();
        string packageVersion = PackageJsonVersion();

        Assert.Equal(packageVersion, agentVersion);
    }

    // Mirrors SystemEndpoints.AppVersion: the friendly InformationalVersion, with any build
    // metadata after '+' trimmed. This is exactly the string /system/status returns.
    private static string AgentInformationalVersion()
    {
        Assembly assembly = typeof(ApiHost).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(informational), "agent assembly has no InformationalVersion");

        int plus = informational!.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }

    private static string PackageJsonVersion()
    {
        string path = Path.Combine(RepoRoot(), "app", "package.json");
        Assert.True(File.Exists(path), $"app/package.json not found at {path}");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        string? version = doc.RootElement.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version), "app/package.json has no version");
        return version!;
    }

    // Walk up from the test binary until the directory that holds both Lore.sln and app/.
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lore.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "app")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repo root (Lore.sln + app/) from the test binary.");
    }
}
