using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The phone's call-note path, link by link (the owner's report, P14 B7): the platform tells Post a call ended
/// (<c>CallStateReceiver</c>, which needs READ_PHONE_STATE), Post reads the newest call-log row and judges it by the
/// shared rule (<c>JustEndedCall</c>), the offer waits behind the dog-ear, and each step says what became of it in the
/// weekly log, with no number and no words in the line. Flyleaf's grant of READ_PHONE_STATE is
/// <c>PolicyPlanBuilderTests.Plan_GrantsReadPhoneStateSoPostHearsACallEnd</c>. Read from source because the head is an Android project the
/// test project cannot reference, as <c>NativeTextAttachTests</c> does.
/// </summary>
public class CallNotePathTests
{
    [Fact]
    public void TheEndOfACallIsJudgedByTheSharedRule()
    {
        var prompt = Code("CallEndedNotePrompt.cs");

        Assert.Contains("JustEndedCall.Judge(", prompt, StringComparison.Ordinal);
        Assert.Contains("JustEndedCall.ReadAgain(", prompt, StringComparison.Ordinal);
        Assert.Contains("CallLog.Calls.Duration", prompt, StringComparison.Ordinal);
        // The rule that measured the call's start against fifteen seconds is gone.
        Assert.DoesNotContain("FreshnessWindow", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCallEndSaysWhatBecameOfIt()
    {
        var prompt = Code("CallEndedNotePrompt.cs");
        var receiver = Code("CallStateReceiver.cs");

        Assert.Contains("PostLogLines.CallNote(", prompt, StringComparison.Ordinal);
        Assert.Contains("PostLogLines.CallEnded", receiver, StringComparison.Ordinal);
        Assert.Contains("HeadLog.Denied(\"READ_CALL_LOG\")", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedPhoneStateIsSaid()
    {
        // Without READ_PHONE_STATE the platform never tells Post a call ended, and nothing else would show it.
        Assert.Contains("HeadLog.Denied(\"READ_PHONE_STATE\")", Code("PostActivity.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureIsLoggedByItsShape()
    {
        // An exception's words can hold whatever the call held; the receiver logs its type and frames only.
        var receiver = Code("CallStateReceiver.cs");

        Assert.DoesNotContain("{e}", receiver, StringComparison.Ordinal);
        Assert.Contains("HeadLog.Quiet(", receiver, StringComparison.Ordinal);
    }

    private static string Code(string file) => Regex.Replace(File.ReadAllText(Find(file)), @"///.*|//.*", string.Empty);

    private static string Find(string file)
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "FocusPhone", "Post.Android", file)))
        {
            directory = Path.GetDirectoryName(directory);
        }
        Assert.NotNull(directory);
        return Path.Combine(directory!, "FocusPhone", "Post.Android", file);
    }
}
