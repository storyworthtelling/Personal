using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The music scanner's log lines carry counts and statuses, never a file's name or its Drive id: a name is a
/// track's title, and one warning per unreadable file was what filled the log in the first place.
/// </summary>
public class MusicScannerLogsTests
{
    private static readonly string SourcePath = Path.Combine(
        RepoRoot(), "FocusPhone", "Score.Android", "MusicScanner.cs");

    [Fact]
    public void NoLogCallInTheScannerNamesAFile()
    {
        var source = File.ReadAllText(SourcePath);
        var calls = Regex.Matches(source, @"(?:AudioDiagnostics\.Log\w*|HeadLog\.\w+|Log\.\w+)\s*\((?<args>[^;]*)\);");

        Assert.NotEmpty(calls);
        foreach (Match call in calls)
        {
            Assert.DoesNotMatch(new Regex(@"\bfile\.(?:Name|Id)\b"), call.Groups["args"].Value);
        }
    }

    [Fact]
    public void TheScannerCountsItsFailuresThroughTheTally()
    {
        var source = File.ReadAllText(SourcePath);

        Assert.Contains("new ScanFailureTally(", source);
        Assert.Matches(new Regex(@"\.Tripped\b"), source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repo root (.github) above " + AppContext.BaseDirectory);
    }
}
