using System.Reflection;

namespace Lore.Cli;

/// <summary>Entry point for the `lore` CLI. Commands arrive with spec 007;
/// for now it prints a version banner and exits.</summary>
internal static class Program
{
    internal static int Main()
    {
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        Console.WriteLine($"lore {version.ToString(3)}");
        return 0;
    }
}
