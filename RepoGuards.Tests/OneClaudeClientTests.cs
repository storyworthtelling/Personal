namespace RepoGuards.Tests;

/// <summary>
/// Every non-streaming Claude call goes through <c>Storyworthtelling.Voice.Claude.ClaudeMessagesClient</c> (audit
/// 01 §4). Eighteen clients each re-declared the endpoint and the version header, and ten of them told credential
/// health nothing, so a revoked key silently turned off mail sorting, Filed search, Score and Plate.
/// </summary>
public class OneClaudeClientTests
{
    /// <summary>
    /// The shared client, and Folio's reading client, which streams (the shared one does not) and logs usage per
    /// call over the whole manuscript; its failures already go through Folio's own classification.
    /// </summary>
    private static readonly string[] Allowed =
    [
        "Storyworthtelling.Voice/Claude/ClaudeMessagesClient.cs",
        "Storyboarder/Folio.Infrastructure/External/ClaudeAnalysisClient.cs",
    ];

    [Fact]
    public void NoOtherCodeTalksToTheMessagesApiItself()
    {
        var offenders = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(path => (Path: path, Relative: Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/')))
            .Where(file => !Allowed.Contains(file.Relative))
            .Where(file => File.ReadAllText(file.Path).Contains("api.anthropic.com/v1/" + "messages", StringComparison.Ordinal))
            .Select(file => file.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Use ClaudeMessagesClient instead of calling the Messages API directly:\n" + string.Join("\n", offenders));
    }
}
