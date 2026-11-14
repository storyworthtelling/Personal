using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Report 03's wire-up list (audit W7): domain code that was written and tested and then reached by
/// nothing on the device. Each case names the call the head now makes, so the code cannot quietly go
/// back to being reachable only from its own tests.
/// </summary>
public class OrphansWiredTests
{
    // §26: a suggested name could be accepted, never corrected.
    [Fact]
    public void PlatesEditorCorrectsAWrongName()
    {
        var editor = LogLoopSource.File("FocusPhone/Plate.Android/EditorActivity.cs");

        Assert.Contains(".Correct(", editor, StringComparison.Ordinal);
        Assert.Contains("FaceNameChooser.Show(", editor, StringComparison.Ordinal);
        Assert.Contains("FacePicker.Choose(", LogLoopSource.File("FocusPhone/Plate.Android/FaceNameChooser.cs"), StringComparison.Ordinal);
        Assert.Contains("rolodex.People()", editor, StringComparison.Ordinal);
    }

    // §21: a filing Gmail keeps refusing earned a log line and never its fold.
    [Fact]
    public void PostsMailboxRaisesTheFilingFold()
    {
        var mailbox = LogLoopSource.File("FocusPhone/Post.Android/MailboxArea.cs");

        Assert.Contains(".LoadTombstones().Fold()", mailbox, StringComparison.Ordinal);
        Assert.Contains("NoticeKeys.Filing", mailbox, StringComparison.Ordinal);
    }

    // §19: ScoreUpdateStatusStore was written after every run and read by nothing.
    [Fact]
    public void ScoresSettingsSaysWhatTheLastUpdateCheckDid()
    {
        var settings = LogLoopSource.File("FocusPhone/Score.Android/SettingsArea.cs");

        Assert.Contains("new ScoreUpdateStatusStore(", settings, StringComparison.Ordinal);
        Assert.Contains(".LastSummary", settings, StringComparison.Ordinal);
    }

    // §25: a wrong contact link could not be removed.
    [Fact]
    public void PostsMailboxSeparatesAWronglyJoinedAddressWithUndo()
    {
        var mailbox = LogLoopSource.File("FocusPhone/Post.Android/MailboxArea.cs");

        Assert.Contains("IdentityRepair.CanSeparate(", mailbox, StringComparison.Ordinal);
        Assert.Contains("IdentityLinks.Unlink(", mailbox, StringComparison.Ordinal);
        Assert.Contains("IdentityLinks.Rejoin(", mailbox, StringComparison.Ordinal);
    }
}
