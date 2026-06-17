namespace Lore.Cli.Commands.Install;

/// <summary>The copy logic behind <c>lore skills install</c> (spec 007 T007, acceptance criterion
/// 5): install the bundled Lore Agent Skill into a client's skills directory by copying the package
/// tree. The skill <em>content</em> is authored by spec 008 (<c>skills/lore/</c>); this command only
/// places it. Copying is <b>idempotent</b> (re-running overwrites with identical bytes), and the
/// whole tree is preserved (nested files and folders). Pure file work with explicit paths, so it is
/// unit-tested against a synthetic package.</summary>
internal static class SkillsInstaller
{
    /// <summary>Copy the skill package at <paramref name="sourceDir"/> into <paramref name="destDir"/>,
    /// creating or overwriting it. Reports the destination, how many files were copied, and whether
    /// the destination already existed.</summary>
    public static SkillsInstallResult Install(string sourceDir, string destDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(destDir);

        if (!Directory.Exists(sourceDir))
        {
            throw new SkillsInstallException(
                $"the Lore skill package was not found at {sourceDir}. It ships with the installed Lore product.");
        }

        bool existed = Directory.Exists(destDir);
        int filesCopied = CopyTree(sourceDir, destDir);
        return new SkillsInstallResult(destDir, filesCopied, existed);
    }

    private static int CopyTree(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        int count = 0;
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(destDir, relative);

            string? targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(file, target, overwrite: true);
            count++;
        }

        return count;
    }
}

/// <summary>What <see cref="SkillsInstaller.Install"/> did, for the command to report.</summary>
internal sealed record SkillsInstallResult(string DestinationDir, int FilesCopied, bool AlreadyPresent);

/// <summary>Thrown when the skill package can't be installed — most commonly because the bundled
/// <c>skills/lore/</c> package isn't present (it ships with the installed product).</summary>
public sealed class SkillsInstallException : Exception
{
    public SkillsInstallException()
    {
    }

    public SkillsInstallException(string message)
        : base(message)
    {
    }

    public SkillsInstallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
