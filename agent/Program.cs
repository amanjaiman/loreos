using System.Reflection;
using System.Text;

namespace Lore.Agent;

/// <summary>Entry point for the Lore capture agent. Behavior arrives with spec 003+;
/// for now it prints a version banner and exits.</summary>
internal static class Program
{
    internal static int Main()
    {
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        Console.WriteLine($"LoreAgent {version.ToString(3)}");
        return 0;
    }
}
