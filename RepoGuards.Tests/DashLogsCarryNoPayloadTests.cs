using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Dash's contracts carry the owner's texts, names, numbers and mail subjects between the phone and the wrist (P14
/// W-4.1.1), by design; the log never does (plan §2.5, §5: the contract and the outcome only). So the codec and Post's
/// relay log nothing at all, and the one place a Dash line is made, <c>DashLogLines</c>, takes no message and fills its
/// lines only from a path, a version, a word, a count and an exception's type name. Read by content, like the repo's
/// other log-site guards; <c>DashLogLinesTests</c> runs the lines over sentinel payloads.
/// </summary>
public partial class DashLogsCarryNoPayloadTests
{
    private static readonly string[] Folders = ["Watch/Watch.Domain/Dash", "FocusPhone/Codex.Infrastructure/Dash"];

    private const string Lines = "Watch/Watch.Domain/Dash/DashLogLines.cs";

    // The only things a Dash line may be filled from.
    private static readonly string[] AllowedHoles = ["contract", "read.Version", "DashCodec.Version", "reader", "outcome", "count", "typeName"];

    // The only parameters a Dash line may take: no message, no read's payload, no free text but the type name it checks.
    private static readonly string[] AllowedParameters =
    [
        "string path", "string failure", "ContractRead<DashMessage> read", "DashEnd reader", "DashOutcome outcome", "int count = 0",
        "DashSendOutcome outcome",
    ];

    [Fact]
    public void TheCodecAndTheRelayNeverLog()
    {
        var sources = Folders.SelectMany(Sources).ToList();
        Assert.Contains(sources, source => source.Path.EndsWith("DashRelay.cs", StringComparison.Ordinal));
        Assert.Contains(sources, source => source.Path.EndsWith("DashCodec.cs", StringComparison.Ordinal));

        var calls = sources
            .SelectMany(source => LogCall().Matches(Code(source.Text)).Select(match => $"{source.Path}: {match.Value}"))
            .ToList();

        Assert.True(calls.Count == 0, "Dash's codec and relay decide; the head logs, through DashLogLines only:\n" + string.Join("\n", calls));
    }

    [Fact]
    public void TheLinesAreFilledFromNoPayload()
    {
        var code = Code(Read(Lines));
        var holes = Interpolated().Matches(code)
            .SelectMany(literal => Hole().Matches(literal.Value).Select(hole => hole.Groups["value"].Value.Trim()))
            .ToList();

        Assert.NotEmpty(holes);
        var other = holes.Where(hole => !AllowedHoles.Contains(hole, StringComparer.Ordinal)).Distinct().ToList();
        Assert.True(other.Count == 0, "A Dash log line filled from something other than a path, version, word or count:\n" + string.Join("\n", other));
    }

    [Fact]
    public void TheLinesTakeNoMessage()
    {
        var code = Code(Read(Lines));
        var signatures = PublicMethod().Matches(code).ToList();

        Assert.NotEmpty(signatures);
        var parameters = signatures
            .SelectMany(signature => signature.Groups["parameters"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(parameter => !AllowedParameters.Contains(parameter, StringComparer.Ordinal))
            .Distinct()
            .ToList();

        Assert.True(parameters.Count == 0, "A Dash log line that takes more than a path, a read, a word or a count:\n" + string.Join("\n", parameters));
    }

    [Theory]
    [InlineData("Log.Warn(\"Post\", $\"Sent {send.Text}\");")]
    [InlineData("HeadLog.Info(\"Dash\", line);")]
    [InlineData("Console.WriteLine(person.Name);")]
    [InlineData("Android.Util.Log.Warn(\"Dash\", text);")]
    [InlineData("PostDiagnostics.LogWarning(\"Dash\", \"x\", e);")]
    [InlineData("logger.LogInformation(\"{Text}\", text);")]
    [InlineData("Trace.WriteLine(text);")]
    public void TheGuardSeesALogCall(string sample)
    {
        Assert.Matches(LogCall(), sample);
    }

    [Theory]
    [InlineData("DashLogLines.Outcome(path, outcome, count)")]
    [InlineData("var line = DashLogLines.Receipt(path, read, DashEnd.Post);")]
    [InlineData("Logger = null;")]
    public void TheGuardLeavesTheLinesAlone(string sample)
    {
        Assert.DoesNotMatch(LogCall(), sample);
    }

    private static string Code(string source) => Regex.Replace(source, @"///.*|//.*", string.Empty);

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IEnumerable<(string Path, string Text)> Sources(string relative)
    {
        var root = GuardScan.RepoRoot();
        var folder = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}") && !path.Contains($"{separator}obj{separator}"))
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));
    }

    // A call to a logger: the platform's, the suite's heads', the console, a trace, or an ILogger method.
    [GeneratedRegex(@"\b(?:Log|HeadLog|Console|Trace|Debug|PostDiagnostics|DiagnosticLog)\s*\.\s*\w+\s*\(|\.Log(?:Warning|Error|Information|Debug|Trace|Critical)\s*\(|\bILogger\b")]
    private static partial Regex LogCall();

    // An interpolated string literal on one line.
    [GeneratedRegex(@"\$""(?:[^""\\\n]|\\.)*""")]
    private static partial Regex Interpolated();

    // An interpolation hole, without its format.
    [GeneratedRegex(@"(?<!\{)\{(?<value>[^{}:]+)(?::[^{}]*)?\}")]
    private static partial Regex Hole();

    // A public method's parameter list.
    [GeneratedRegex(@"public static \w+ \w+\((?<parameters>[^)]*)\)")]
    private static partial Regex PublicMethod();
}
