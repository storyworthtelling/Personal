using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Dash's contracts carry the owner's texts, names, numbers and mail subjects between the phone and the wrist (P14
/// W-4.1.1), by design; the log never does (plan §2.5, §5: the contract and the outcome only). So the codec and Post's
/// relay log nothing at all, and the one place a Dash line is made, <c>DashLogLines</c>, takes no message and fills its
/// lines only from a path, a version, a word, a count and an exception's type name. Read by content, like the repo's
/// other log-site guards; <c>DashLogLinesTests</c> runs the lines over sentinel payloads.
///
/// <para>Post's listener (B2) is the one place a Dash message is logged, and it logs only those lines: every call in
/// <c>Post.Android/Dash</c> is <c>HeadLog.Info</c> or <c>HeadLog.Warn</c> with a <c>DashLogLines</c> line or a plain
/// literal, or <c>HeadLog.Quiet</c> with a plain literal and the exception, whose shape is written and never its
/// message. Nothing is interpolated, and no other logger is called.</para>
///
/// <para>The handed-over token's contract (B3, <c>Watch.Domain/Keys</c>) is held to the same rule, with the token as its
/// payload: its codec and hold log nothing, and <c>KeyLogLines</c> takes no message and writes a failure by its type alone.</para>
///
/// <para>Dash on the watch (B4) is the wrist's end: it logs through its one <c>DashLog</c>, every call a
/// <c>DashLogLines</c> line or a plain literal, and a failure by its type and frames only.</para>
/// </summary>
public partial class DashLogsCarryNoPayloadTests
{
    private static readonly string[] Folders = ["Watch/Watch.Domain/Dash", "FocusPhone/Codex.Infrastructure/Dash", "Watch/Watch.Domain/Keys"];

    private const string Lines = "Watch/Watch.Domain/Dash/DashLogLines.cs";

    // The handed-over token's lines (P14 W-4.1.2): the same rule, and the token is the payload.
    private const string KeyLines = "Watch/Watch.Domain/Keys/KeyLogLines.cs";

    private static readonly string[] KeyHoles = ["contract", "read.Version", "KeyCodec.Version", "reader", "outcome", "typeName"];

    private static readonly string[] KeyParameters =
        ["string path", "Exception failure", "ContractRead<KeyMessage> read", "KeyEnd reader", "KeyOutcome outcome"];

    private const string Listener = "FocusPhone/Post.Android/Dash";

    // Dash on the watch (P14 B4): the wrist's half of the same contracts, logged through its one DashLog.
    private const string Wrist = "Watch/Dash.Wear";

    private const string WristLog = "Watch/Dash.Wear/DashLog.cs";

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
        Assert.Contains(sources, source => source.Path.EndsWith("KeyCodec.cs", StringComparison.Ordinal));
        Assert.Contains(sources, source => source.Path.EndsWith("TokenHold.cs", StringComparison.Ordinal));

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

    [Fact]
    public void TheKeyLinesAreFilledFromNoPayload()
    {
        var code = Code(Read(KeyLines));
        var holes = Interpolated().Matches(code)
            .SelectMany(literal => Hole().Matches(literal.Value).Select(hole => hole.Groups["value"].Value.Trim()))
            .ToList();

        Assert.NotEmpty(holes);
        var other = holes.Where(hole => !KeyHoles.Contains(hole, StringComparer.Ordinal)).Distinct().ToList();
        Assert.True(other.Count == 0, "A keys log line filled from something other than a path, version, word or type name:\n" + string.Join("\n", other));
    }

    [Fact]
    public void TheKeyLinesTakeNoToken()
    {
        var code = Code(Read(KeyLines));
        var signatures = PublicMethod().Matches(code).ToList();

        Assert.NotEmpty(signatures);
        var parameters = signatures
            .SelectMany(signature => signature.Groups["parameters"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(parameter => !KeyParameters.Contains(parameter, StringComparer.Ordinal))
            .Distinct()
            .ToList();

        Assert.True(parameters.Count == 0, "A keys log line that takes more than a path, a read, a word or a type name:\n" + string.Join("\n", parameters));
    }

    [Fact]
    public void TheListenerLogsOnlyDashLines()
    {
        var sources = Sources(Listener).ToList();
        Assert.Contains(sources, source => source.Path.EndsWith("DashListener.cs", StringComparison.Ordinal));

        var calls = sources.SelectMany(source => LogCalls(Code(source.Text)).Select(call => (source.Path, Call: call))).ToList();
        Assert.NotEmpty(calls);
        var other = calls.Where(entry => !IsListenerLine(entry.Call)).Select(entry => $"{entry.Path}: {entry.Call}").ToList();

        Assert.True(other.Count == 0,
            "Post's listener logs a DashLogLines line or a plain literal through HeadLog, and an exception only by its shape (HeadLog.Quiet):\n"
            + string.Join("\n", other));
    }

    [Fact]
    public void TheWristLogsOnlyDashLines()
    {
        var sources = Sources(Wrist).ToList();
        Assert.Contains(sources, source => source.Path == WristLog);
        Assert.Contains(sources, source => source.Path.EndsWith("PostLink.cs", StringComparison.Ordinal));

        // No logger but DashLog anywhere else on the wrist: not the platform's, the console or a trace.
        var other = sources.Where(source => source.Path != WristLog)
            .SelectMany(source => LogCall().Matches(Code(source.Text)).Select(match => $"{source.Path}: {match.Value}"))
            .ToList();
        Assert.True(other.Count == 0, "Dash on the wrist logs through DashLog only:\n" + string.Join("\n", other));

        // Every DashLog call is a DashLogLines line or a plain literal, and an exception only beside a plain literal.
        var calls = sources.SelectMany(source => WristCalls(Code(source.Text)).Select(call => (source.Path, Call: call))).ToList();
        Assert.NotEmpty(calls);
        var payload = calls.Where(entry => !IsWristLine(entry.Call)).Select(entry => $"{entry.Path}: {entry.Call}").ToList();
        Assert.True(payload.Count == 0, "A wrist log line that could carry a payload:\n" + string.Join("\n", payload));
    }

    [Fact]
    public void TheWristsLogWritesTheLineAndTheShapeOnly()
    {
        // DashLog hands the platform the line it was given, and for a failure the exception's type and frames, never its
        // message, which can carry whatever the call was given.
        var code = Code(Read(WristLog));
        var calls = LogCalls(code).ToList();

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Matches(@"^Log\.(?:Info|Warn)\(Tag, (?:line|Shape\(line, failure\))\)$", Regex.Replace(call, @"\s+", " ")));
        Assert.Contains("GetType().FullName", code, StringComparison.Ordinal);
        Assert.Contains(".StackTrace", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Message", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DashLog.Info($\"Sent {send.Text}\")")]
    [InlineData("DashLog.Info(send.Text)")]
    [InlineData("DashLog.Warn(\"Sent \" + words)")]
    [InlineData("DashLog.Quiet($\"Stopped on {send.Id}\", failure)")]
    [InlineData("DashLog.Quiet(DashLogLines.Sent(path), failure)")]
    [InlineData("DashLog.Info(DashLogLines.Sent(path), failure)")]
    [InlineData("DashLog.Write(\"A send stopped\")")]
    public void TheWristRuleSeesAPayload(string call)
    {
        Assert.False(IsWristLine(call));
    }

    [Theory]
    [InlineData("DashLog.Info(DashLogLines.Receipt(path, read, DashEnd.Dash))")]
    [InlineData("DashLog.Warn(DashLogLines.Outcome(path, DashOutcome.NoAnswer))")]
    [InlineData("DashLog.Quiet(\"DASH an ask stopped\", failure)")]
    [InlineData("DashLog.Warn(\"DASH no dictation on this watch\")")]
    public void TheWristRuleLeavesItsLinesAlone(string call)
    {
        Assert.True(IsWristLine(call));
    }

    [Theory]
    [InlineData("HeadLog.Info(Tag, $\"Sent {send.Text}\")")]
    [InlineData("HeadLog.Info(Tag, send.Text)")]
    [InlineData("HeadLog.Warn(Tag, DashLogLines.Sent(path), e)")]
    [InlineData("HeadLog.Quiet(Tag, $\"Stopped on {send.Id}\", e)")]
    [InlineData("HeadLog.Quiet(Tag, DashLogLines.Sent(path), e)")]
    [InlineData("PostDiagnostics.LogWarning(\"Dash\", \"A send stopped\", e)")]
    [InlineData("Android.Util.Log.Warn(Tag, \"A send stopped\")")]
    [InlineData("HeadLog.Info(Tag, \"Sent \" + send.Text)")]
    public void TheListenerRuleSeesAPayload(string call)
    {
        Assert.False(IsListenerLine(call));
    }

    [Theory]
    [InlineData("HeadLog.Info(Tag, DashLogLines.Receipt(path, read, DashEnd.Post))")]
    [InlineData("HeadLog.Warn(Tag, DashLogLines.Outcome(path, DashOutcome.Unreached, missed))")]
    [InlineData("HeadLog.Quiet(Tag, \"A message from the wrist stopped\", e)")]
    [InlineData("HeadLog.Warn(Tag, \"DASH the acts’ record didn’t read; a new one starts\")")]
    public void TheListenerRuleLeavesItsLinesAlone(string call)
    {
        Assert.True(IsListenerLine(call));
    }

    [Fact]
    public void TheCallsAreReadWhole()
    {
        var calls = LogCalls("x();\n HeadLog.Info(Tag,\n    DashLogLines.Outcome(path, outcome, Count(\"(\")));\n y();").ToList();

        Assert.Equal(["HeadLog.Info(Tag,\n    DashLogLines.Outcome(path, outcome, Count(\"(\")))"], calls);
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

    // A delete from the wrist reaches the shared delete commit, and a notification's source id can hold a number.
    [Fact]
    public void ADeleteFailureNamesNoSourceId()
    {
        var code = Code(Read("FocusPhone/Post.Android/SourceDeletes.cs"));

        Assert.DoesNotContain("item.SourceId}", code);
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

    // A listener line: HeadLog.Info or Warn with a DashLogLines line or a plain literal, or HeadLog.Quiet with a plain literal
    // and the exception; never an interpolation, a concatenation or a value.
    private static bool IsListenerLine(string call)
    {
        var open = call.IndexOf('(');
        var name = Regex.Replace(call[..open], @"\s", string.Empty);
        var arguments = Arguments(call[(open + 1)..^1]);
        if (call.Contains("$\"", StringComparison.Ordinal) || arguments.Count < 2)
        {
            return false;
        }

        var message = arguments[1];
        var line = message.StartsWith("DashLogLines.", StringComparison.Ordinal) || PlainLiteral().IsMatch(message);
        return name switch
        {
            "HeadLog.Info" or "HeadLog.Warn" => arguments.Count == 2 && line,
            "HeadLog.Quiet" => arguments.Count == 3 && PlainLiteral().IsMatch(message),
            _ => false,
        };
    }

    // A wrist line: DashLog.Info or Warn with a DashLogLines line or a plain literal, or DashLog.Quiet with a plain literal and
    // the exception; never an interpolation, a concatenation or a value.
    private static bool IsWristLine(string call)
    {
        var open = call.IndexOf('(');
        var name = Regex.Replace(call[..open], @"\s", string.Empty);
        var arguments = Arguments(call[(open + 1)..^1]);
        if (call.Contains("$\"", StringComparison.Ordinal) || arguments.Count == 0)
        {
            return false;
        }

        var message = arguments[0];
        var line = message.StartsWith("DashLogLines.", StringComparison.Ordinal) || PlainLiteral().IsMatch(message);
        return name switch
        {
            "DashLog.Info" or "DashLog.Warn" => arguments.Count == 1 && line,
            "DashLog.Quiet" => arguments.Count == 2 && PlainLiteral().IsMatch(message),
            _ => false,
        };
    }

    // Every DashLog call in a source, from its name to its closing bracket.
    private static IEnumerable<string> WristCalls(string code) => CallsOf(code, WristCall());

    // Every logger call in a source, from its name to its closing bracket, strings read as strings.
    private static IEnumerable<string> LogCalls(string code) => CallsOf(code, LogCall());

    private static IEnumerable<string> CallsOf(string code, Regex calls)
    {
        foreach (Match match in calls.Matches(code))
        {
            var start = match.Index;
            var open = code.IndexOf('(', start);
            if (open < 0)
            {
                continue;
            }
            var end = Close(code, open);
            yield return end < 0 ? code[start..] : code[start..(end + 1)];
        }
    }

    // The bracket that closes the one at <paramref name="open"/>, or -1.
    private static int Close(string code, int open)
    {
        var depth = 0;
        var inString = false;
        for (var index = open; index < code.Length; index++)
        {
            var character = code[index];
            if (inString)
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == '"')
                {
                    inString = false;
                }
                continue;
            }
            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                    break;
            }
        }
        return -1;
    }

    // A call's arguments, split at its own commas only.
    private static List<string> Arguments(string inside)
    {
        var arguments = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var index = 0; index < inside.Length; index++)
        {
            var character = inside[index];
            if (inString)
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == '"')
                {
                    inString = false;
                }
                continue;
            }
            if (character == '"')
            {
                inString = true;
            }
            else if (character is '(' or '[' or '{')
            {
                depth++;
            }
            else if (character is ')' or ']' or '}')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                arguments.Add(inside[start..index].Trim());
                start = index + 1;
            }
        }
        arguments.Add(inside[start..].Trim());
        return arguments;
    }

    // One string literal and nothing else: no interpolation and no concatenation.
    [GeneratedRegex(@"^""(?:[^""\\]|\\.)*""$")]
    private static partial Regex PlainLiteral();

    // A call to a logger: the platform's, the suite's heads', the console, a trace, or an ILogger method.
    [GeneratedRegex(@"\b(?:Log|HeadLog|Console|Trace|Debug|PostDiagnostics|DiagnosticLog)\s*\.\s*\w+\s*\(|\.Log(?:Warning|Error|Information|Debug|Trace|Critical)\s*\(|\bILogger\b")]
    private static partial Regex LogCall();

    // A call to the wrist's one log.
    [GeneratedRegex(@"\bDashLog\s*\.\s*\w+\s*\(")]
    private static partial Regex WristCall();

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
