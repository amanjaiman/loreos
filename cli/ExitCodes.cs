namespace Lore.Cli;

/// <summary>The CLI's exit-code scheme (spec 007, plan "--json contract &amp; exit codes"):
/// a <b>stable machine contract</b> a calling agent or script can branch on. Each value names a
/// distinct failure class so a caller need not parse output to know what happened. The full
/// scheme is asserted in tests and documented in <c>docs/integrations/cli.md</c> (T005/T007).</summary>
internal static class ExitCodes
{
    /// <summary>The command succeeded.</summary>
    public const int Success = 0;

    /// <summary>A runtime failure that isn't one of the more specific classes below (e.g. the
    /// agent answered with a 5xx, or returned a body the CLI couldn't parse).</summary>
    public const int RuntimeFailure = 1;

    /// <summary>The invocation was malformed — an unknown command, a missing required argument,
    /// or a bad option value. System.CommandLine returns this for parse errors; commands return
    /// it when the agent rejects the request as a 400 Bad Request.</summary>
    public const int BadUsage = 2;

    /// <summary>The local API could not be reached — Lore isn't running. Paired with the
    /// friendly "Lore isn't running" message, never a stack trace (acceptance criterion 6).</summary>
    public const int AgentUnreachable = 3;

    /// <summary>The requested resource does not exist (a 404 from the agent, e.g. <c>get</c> on
    /// an unknown id).</summary>
    public const int NotFound = 4;
}
