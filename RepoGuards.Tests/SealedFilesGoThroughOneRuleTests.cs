namespace RepoGuards.Tests;

/// <summary>
/// Duplication audit §5: the refuse-to-overwrite rule for encrypted Drive files was written about six times and
/// the copies disagreed (Index's snapshot overwrote a damaged file). Every sealed file an app rewrites now goes
/// through <c>SealedDriveFile</c>, so a new store that seals its own text and writes it is a decision to review,
/// not a diff. The two allowed callers never write over anything: the log uploader creates a new file each time
/// and the snowball sealer is the owner's offline tool's encoder.
/// </summary>
public class SealedFilesGoThroughOneRuleTests
{
    private static readonly string[] Allowed =
    [
        "Storyworthtelling.Voice/Secrets/SealedDriveFile.cs",
        "Storyworthtelling.Voice/Logs/LogUploader.cs",
        "Ledger/Ledger.Infrastructure/Snowball/SnowballHistorySealer.cs",
    ];

    [Fact]
    public void OnlyTheSealedFileRuleSealsTextForDrive()
    {
        var root = GuardScan.RepoRoot();
        var callers = GuardScan.Files(root)
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !relative.Split('/').Any(part => part.EndsWith(".Tests", StringComparison.Ordinal) || part == "RepoGuards.Tests"))
            .Where(relative => File.ReadAllText(Path.Combine(root, relative)).Contains("VaultEnvelope.Write(", StringComparison.Ordinal))
            .ToList();

        Assert.Contains("Storyworthtelling.Voice/Secrets/SealedDriveFile.cs", callers);
        var outside = callers.Except(Allowed).ToList();
        Assert.True(outside.Count == 0,
            "These files seal text for Drive themselves instead of through SealedDriveFile:\n" + string.Join("\n", outside));
    }
}
