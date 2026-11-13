using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Audit 11 §22, 12 §3: every app put a raw <c>ex.Message</c> on screen — written for a diagnostics file, naming the
/// call and the exception type. A status line, alert, toast or recorded outcome now says it through
/// <c>Storyworthtelling.Diagnostics.PlainError</c>. Logs, trails and classifiers still read the message.
/// </summary>
public partial class ScreensSpeakPlainlyTests
{
    [Fact]
    public void NoRawExceptionMessageReachesAScreen()
    {
        var found = Sources()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => RawMessage().IsMatch(hit.line) && Screen().IsMatch(hit.line))
            .Select(hit => $"{Relative(hit.file)}:{hit.index + 1}")
            .ToList();

        Assert.True(found.Count == 0, "Say it through PlainError.Describe(exception):\n" + string.Join("\n", found));
    }

    // An exception's own message: ex.Message, e.Message, exception.Message (not a log line's or a reply's).
    [GeneratedRegex(@"\b(?:ex|e|exception)\.Message\b")]
    private static partial Regex RawMessage();

    // Where words reach the owner: a label's text, a status or problem line, an alert, a toast, a stored outcome line.
    [GeneratedRegex(@"\.Text\s*=|ShowStatus\(|ShowProblem\(|DisplayAlert\(|Toast\.MakeText\(|Outcome\s*=|\.Caption\s*=|status\.Text")]
    private static partial Regex Screen();

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "Tools" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
