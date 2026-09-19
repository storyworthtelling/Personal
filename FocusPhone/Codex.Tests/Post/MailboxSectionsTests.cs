using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Marketing mail moves to its own bucket, but the split changes only where it is seen —
/// not when it goes. <see cref="MailAgeing"/> still owns the clock.
/// </summary>
public class MailboxSectionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static TimelineItem Email(string contactKey, string sourceId, MailClass? mailClass) =>
        new(TimelineItemKind.Email, contactKey, T0, "hi", Incoming: true, SourceId: sourceId)
        {
            MailClass = mailClass,
        };

    [Fact]
    public void MarketingMailGoesToItsOwnBucket()
    {
        var marketing = Email("shop@example.com", "m1", MailClass.Marketing);
        var human = Email("friend@example.com", "m2", MailClass.Human);

        var (ordinary, marketingBucket) = MailboxSections.SplitMarketing([marketing, human]);

        Assert.Equal([human], ordinary);
        Assert.Equal([marketing], marketingBucket);
    }

    // Null means "not yet judged" — waiting is the ordinary Mailbox's job, not the
    // marketing bucket's, and treating an unjudged message as marketing would be a guess
    // exactly like guessing it was human.
    [Fact]
    public void AnUnjudgedMessageStaysInTheOrdinaryMailbox()
    {
        var unjudged = Email("someone@example.com", "m1", mailClass: null);

        var (ordinary, marketing) = MailboxSections.SplitMarketing([unjudged]);

        Assert.Equal([unjudged], ordinary);
        Assert.Empty(marketing);
    }

    [Theory]
    [InlineData(MailClass.Human)]
    [InlineData(MailClass.Newsletter)]
    [InlineData(MailClass.Bill)]
    [InlineData(MailClass.Transactional)]
    public void EveryOtherKindStaysOrdinary(MailClass kind)
    {
        var item = Email("someone@example.com", "m1", kind);

        var (ordinary, marketing) = MailboxSections.SplitMarketing([item]);

        Assert.Equal([item], ordinary);
        Assert.Empty(marketing);
    }

    [Fact]
    public void AnEmptyInputSplitsToTwoEmptyLists()
    {
        var (ordinary, marketing) = MailboxSections.SplitMarketing([]);

        Assert.Empty(ordinary);
        Assert.Empty(marketing);
    }
}
