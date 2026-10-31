using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogLoopReader;

public sealed record GateCommit(string Sha, string Message, IReadOnlyList<string> Files, int Parents);

public sealed record ReportIssue(string Title, string Author, string Body);

/// <summary>One check run on the PR's head commit, as the checks API reports it.</summary>
public sealed record GateCheck(string Name, string Status, string? Conclusion);

/// <summary>What the workflow gathers about a PR for the gate, from the GitHub API.</summary>
public sealed record GateInput(
    string Branch,
    bool FromFork,
    string PrAuthor,
    IReadOnlyList<string> TrustedAuthors,
    string Body,
    IReadOnlyList<GateCommit> Commits,
    IReadOnlyList<ReportIssue> Reports,
    IReadOnlyList<GateCheck>? Checks = null)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static GateInput FromJson(string json) =>
        JsonSerializer.Deserialize<GateInput>(json, Options) ?? throw new ArgumentException("The gate input is empty.");
}

public sealed record GateDecision(bool Enable, IReadOnlyList<string> Reasons);

/// <summary>
/// Whether a PR is a log-derived bug fix that may auto-merge (LOG-LOOP-PLAN.md §6.3, O3 widened by
/// O14–O17). It checks conditions 1–4 — the branch and author, the log-loop blocks against the week's
/// report, a fingerprint trailer on every commit, and each fix's test commit first. Conditions 5 and 6
/// (every touched app's tests green, log-loop-guard green) are also branch protection's, which auto-merge waits
/// for — and, because auto-merge waits only for the checks branch protection requires while the app test
/// jobs are path-filtered pushes that cannot all be required, the gate itself also waits for every check
/// on the head commit to finish green, <c>log-loop-guard</c> among them. Anything that does not hold keeps
/// the PR under the normal rule: it waits for the owner.
/// </summary>
public static partial class AutoMergeGate
{
    public const string ReportTitlePrefix = "Log loop — week of ";

    /// <summary>The leak guard's job name, which branch protection also requires.</summary>
    public const string GuardCheck = "log-loop-guard";

    public static GateDecision Decide(GateInput input)
    {
        var reasons = new List<string>();

        var branch = Branch().Match(input.Branch);
        if (!branch.Success)
        {
            reasons.Add($"branch {input.Branch} is not claude/logfix-<yyyy-MM-dd>-<app>");
        }
        if (input.FromFork)
        {
            reasons.Add("the PR comes from a fork");
        }
        var trusted = input.TrustedAuthors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!trusted.Contains(input.PrAuthor))
        {
            reasons.Add($"the PR was not opened by the log-loop session's account ({input.PrAuthor})");
        }

        var declared = Blocks(input.Body, "log-loop")
            .SelectMany(b => Field(b, "fingerprint"))
            .ToHashSet(StringComparer.Ordinal);
        if (declared.Count == 0)
        {
            reasons.Add("the PR body has no log-loop block");
        }

        var weekOfBranch = branch.Success
            ? DateOnly.ParseExact(branch.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.MaxValue;
        var fixes = input.Reports
            .Where(r => trusted.Contains(r.Author) && r.Title.StartsWith(ReportTitlePrefix, StringComparison.Ordinal))
            .SelectMany(r => Blocks(r.Body, "log-loop-report"))
            .Where(b => Field(b, "week").Any(w => DateOnly.TryParseExact(w, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var week) && week >= weekOfBranch))
            .SelectMany(b => Field(b, "fix"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var fingerprint in declared.Where(f => !fixes.Contains(f)).Order(StringComparer.Ordinal))
        {
            reasons.Add($"fingerprint {Short(fingerprint)} is not marked Fix in a log loop report for this week or later");
        }

        var tagged = new List<(GateCommit Commit, IReadOnlyList<string> Fingerprints)>();
        foreach (var commit in input.Commits)
        {
            if (commit.Parents != 1)
            {
                reasons.Add($"commit {commit.Sha} is a merge commit");
            }
            var trailers = Trailer().Matches(commit.Message).Select(m => m.Groups["fp"].Value).ToList();
            if (trailers.Count == 0)
            {
                reasons.Add($"commit {commit.Sha} has no Log-Loop-Fingerprint trailer");
            }
            foreach (var fingerprint in trailers.Where(f => !declared.Contains(f)))
            {
                reasons.Add($"commit {commit.Sha} names fingerprint {Short(fingerprint)}, which the PR body does not declare");
            }
            tagged.Add((commit, trailers));
        }

        foreach (var fingerprint in declared.Order(StringComparer.Ordinal))
        {
            var mine = tagged.Where(t => t.Fingerprints.Contains(fingerprint)).Select(t => t.Commit).ToList();
            var firstTest = mine.FindIndex(c => c.Files.Any(CiLeakScan.IsTestPath));
            var firstFix = mine.FindIndex(c => c.Files.Any(f => !CiLeakScan.IsTestPath(f)));
            if (mine.Count == 0)
            {
                reasons.Add($"fingerprint {Short(fingerprint)} has no commit");
            }
            else if (firstTest < 0)
            {
                reasons.Add($"fingerprint {Short(fingerprint)} has no test commit");
            }
            else if (firstFix >= 0 && firstFix < firstTest)
            {
                reasons.Add($"fingerprint {Short(fingerprint)}'s fix comes before its test");
            }
            else if (firstFix < 0)
            {
                reasons.Add($"fingerprint {Short(fingerprint)} has a test and no fix");
            }
        }

        var checks = input.Checks ?? [];
        foreach (var check in checks.Where(c => c.Status != "completed"))
        {
            reasons.Add($"check {check.Name} has not finished");
        }
        foreach (var check in checks.Where(c => c.Status == "completed" && c.Conclusion is not ("success" or "skipped" or "neutral")))
        {
            reasons.Add($"check {check.Name} ended {check.Conclusion ?? "without a conclusion"}");
        }
        if (!checks.Any(c => c.Name == GuardCheck && c.Status == "completed" && c.Conclusion == "success"))
        {
            reasons.Add($"{GuardCheck} has not passed on the head commit");
        }

        return new GateDecision(reasons.Count == 0, reasons);
    }

    private static IEnumerable<string> Blocks(string text, string name) =>
        Block().Matches(text).Where(m => m.Groups["name"].Value == name).Select(m => m.Groups["body"].Value);

    private static IEnumerable<string> Field(string block, string field) =>
        block.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(field + ":", StringComparison.Ordinal))
            .Select(l => l[(field.Length + 1)..].Trim())
            .Where(v => v.Length > 0);

    private static string Short(string fingerprint) => fingerprint.Length > 12 ? fingerprint[..12] : fingerprint;

    [GeneratedRegex(@"^claude/logfix-(?<date>\d{4}-\d{2}-\d{2})-(?<app>[a-z]+)$")]
    private static partial Regex Branch();

    [GeneratedRegex(@"<!--\s*(?<name>log-loop(?:-report)?)\s*\n(?<body>.*?)-->", RegexOptions.Singleline)]
    private static partial Regex Block();

    [GeneratedRegex(@"^Log-Loop-Fingerprint:\s*(?<fp>[0-9a-f]{64})\s*$", RegexOptions.Multiline)]
    private static partial Regex Trailer();
}
