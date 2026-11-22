using System.Text.RegularExpressions;
using Watch.Domain;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The phone and the wrist answer by one set of rules (P14 W-4.1.1): the Mailbox and Post's relay to Dash both build
/// what is waiting, name a row, choose a reply's numbers and a text's route through the same pure types, so the watch
/// can never be shown a different Mailbox, or reply by a different road, than the phone. And Post names Dash's paths
/// only through <see cref="WatchIdentity"/>, so the two ends cannot drift.
/// </summary>
public class DashRelayGuardTests
{
    private static string Code(string relative) => Regex.Replace(LogLoopSource.File(relative), @"///.*|//.*", string.Empty);

    [Theory]
    [InlineData("MailboxView.Build(")]
    [InlineData("ThreadTitles.ForRow(")]
    [InlineData("ReplyTarget.PhoneOf(")]
    [InlineData("ReplyTarget.GroupRecipients(")]
    [InlineData("ReplyTarget.MembershipKnown(")]
    [InlineData("ReplyTarget.ConversationOf(")]
    [InlineData("TextSend.Choose(")]
    [InlineData("TextSend.WithoutBridge(")]
    public void TheMailboxUsesTheSharedRules(string call)
    {
        Assert.Contains(call, Code("FocusPhone/Post.Android/MailboxArea.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MailboxView.Build(")]
    [InlineData("ThreadTitles.ForRow(")]
    [InlineData("ReplyTarget.For(")]
    [InlineData("TextSend.Choose(")]
    [InlineData("TextSend.WithoutBridge(")]
    [InlineData("SyncMoment.Delay(PostAction.Filed")]
    [InlineData("SyncMoment.Delay(PostAction.Deleted")]
    [InlineData("CallNote.For(")]
    [InlineData("FoldTiming.WindowFor(")]
    public void TheRelayUsesTheSameRules(string call)
    {
        Assert.Contains(call, Code("FocusPhone/Codex.Infrastructure/Dash/DashRelay.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMailboxNoLongerBuildsItsOwn()
    {
        // The reload's merge and the row title live in Codex.Domain now; a second copy here could drift from the wrist's.
        var mailbox = Code("FocusPhone/Post.Android/MailboxArea.cs");

        Assert.DoesNotContain("thread.Participants.Take(3)", mailbox.Split("private string OpenThreadTitle")[0], StringComparison.Ordinal);
        Assert.DoesNotContain("recipients.Count > 1", mailbox, StringComparison.Ordinal);
        Assert.DoesNotContain("LastOrDefault(item => item.ExternalConversationId", mailbox, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDashPathsAreNotCopied()
    {
        Assert.All(WatchIdentity.DashPaths, path => Assert.StartsWith("/dash/", path, StringComparison.Ordinal));
        var copies = new[] { "FocusPhone/Post.Android", "FocusPhone/Codex.Infrastructure", "FocusPhone/Codex.Domain" }
            .SelectMany(LogLoopSource.Sources)
            .Where(source => WatchIdentity.DashPaths.Any(path => source.Text.Contains($"\"{path}", StringComparison.Ordinal)))
            .Select(source => source.Path)
            .ToList();

        Assert.True(copies.Count == 0, "Use WatchIdentity rather than a copy of a Dash path:\n" + string.Join("\n", copies));
    }
}
