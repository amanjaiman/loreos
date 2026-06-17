using System.CommandLine;

namespace Lore.Cli;

/// <summary>Entry point for the <c>lore</c> CLI (spec 007): a thin client of the local API. The
/// command tree and global options are assembled in <see cref="CliRoot"/> (so tests can drive the
/// same surface in-process); <see cref="Main"/> only parses and invokes.</summary>
internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        RootCommand root = CliRoot.Build();
        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }
}
