namespace Lore.Cli;

/// <summary>Entry point for the <c>lore</c> CLI (spec 007): a thin client of the local API. The
/// command tree, global options, and the exit-code finalization all live in <see cref="CliRoot"/>
/// (so tests can drive the same surface in-process); <see cref="Main"/> only delegates.</summary>
internal static class Program
{
    internal static Task<int> Main(string[] args) => CliRoot.InvokeAsync(args);
}
