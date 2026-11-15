using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// A failed Messages-bridge delete is said once, by whoever asked for it (log loop, C10). The bridge client's
/// delete used to write a full warning with its stack on every failure, so the filing retry — which already
/// throttles its own line to the fold's threshold and every tenth try after — and the Mailbox's direct delete,
/// which already writes its own line, were both drowned out by a second copy of every failure. The client now
/// only returns what happened; the callers own the line.
/// </summary>
public class BridgeDeleteLogsOnceTests
{
    private const string Client = "FocusPhone/Post.Android/GmBridgeClient.cs";

    private static readonly Regex LogCall =
        new(@"\b(PostDiagnostics|HeadLog|Log)\s*\.\s*(Log)?(Warn|Warning|Info|Error|Quiet|E|W|I|D)\w*\s*\(");

    [Fact]
    public void TheBridgeClientsDeleteWritesNoLogLineOfItsOwn()
    {
        var bodies = Bodies(LogLoopSource.File(Client), "TryDelete(string messageId)");

        Assert.NotEmpty(bodies);
        foreach (var body in bodies)
        {
            Assert.DoesNotMatch(LogCall, body);
        }
    }

    [Fact]
    public void TheBridgeClientsDeleteSaysWhatHappenedRatherThanTrueOrFalse()
    {
        var source = LogLoopSource.File(Client);

        Assert.DoesNotContain("public bool TryDelete(", source, StringComparison.Ordinal);
        Assert.Contains("public BridgeDeleteResult TryDelete(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFilingRetryNamesWhyTheDeleteFailed()
    {
        var sync = LogLoopSource.File("FocusPhone/Post.Android/PostSync.cs");

        Assert.Matches(new Regex(@"PostLogLines\.OwedDeleteFailing\([^;]*,\s*\w+"), sync);
    }

    // Every method body that follows the signature (the real client and the stub beside it).
    private static List<string> Bodies(string source, string signature)
    {
        var bodies = new List<string>();
        for (var start = source.IndexOf(signature, StringComparison.Ordinal);
             start >= 0;
             start = source.IndexOf(signature, start + signature.Length, StringComparison.Ordinal))
        {
            var arrow = source.IndexOf("=>", start, StringComparison.Ordinal);
            var open = source.IndexOf('{', start);
            if (arrow >= 0 && (open < 0 || arrow < open))
            {
                bodies.Add(source[start..source.IndexOf(';', arrow)]);
                continue;
            }
            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;
                if (depth == 0)
                {
                    bodies.Add(source[start..(i + 1)]);
                    break;
                }
            }
        }
        return bodies;
    }
}
