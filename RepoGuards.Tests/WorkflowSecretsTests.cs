namespace RepoGuards.Tests;

/// <summary>
/// A workflow run's summary and log are public on a public repository. generate-keystore.yml once wrote
/// the base64 of the signing keystore into <c>$GITHUB_STEP_SUMMARY</c> (privacy audit §2); this keeps any
/// workflow from writing a keystore, a base64 dump or a secret into the summary again.
/// </summary>
public class WorkflowSecretsTests
{
    private static readonly string[] Forbidden = ["base64", ".keystore", ".jks", ".p12", "secrets."];

    [Fact]
    public void NoWorkflowWritesAKeystoreOrASecretIntoTheRunSummary()
    {
        var offenders = Workflows()
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, index)))
            .Where(entry => entry.line.Contains("GITHUB_STEP_SUMMARY", StringComparison.Ordinal)
                && Forbidden.Any(word => entry.line.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.index + 1}")
            .ToList();

        Assert.True(offenders.Count == 0, "A workflow writes a keystore or secret into the public run summary:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheKeystoreWorkflowRefusesWhileAKeystoreIsInUseAndOnlyUploadsItEncrypted()
    {
        var text = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), ".github", "workflows", "generate-keystore.yml"));

        Assert.Contains("EXISTING: ${{ secrets.KEYSTORE_BASE64 }}", text);
        Assert.Contains("openssl enc -aes-256-cbc", text);
        Assert.Contains("rm dailyloader.keystore", text);
        Assert.Contains("path: dailyloader.keystore.enc", text);
        Assert.Contains("retention-days: 1", text);
    }

    [Fact]
    public void TheGuardSeesASummaryLeak()
    {
        var line = "base64 -w 0 dailyloader.keystore >> $GITHUB_STEP_SUMMARY";
        Assert.Contains(Forbidden, word => line.Contains(word, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("GITHUB_STEP_SUMMARY", line);
    }

    private static IEnumerable<string> Workflows()
    {
        var files = Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), ".github"), "*.yml", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count >= 10, $"Only {files.Count} workflow files found; the guard would pass on nothing.");
        return files;
    }
}
