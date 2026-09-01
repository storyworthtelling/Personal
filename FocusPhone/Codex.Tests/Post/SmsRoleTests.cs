using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Post cannot send or really delete a text unless it holds the SMS role, and the role
/// can go away without anyone touching Post — a policy pass, an OS update, a reset. What
/// the owner is told about that is decided here, so the head only draws it.
/// </summary>
public class SmsRoleTests
{
    // Holding the role is the quiet case. Nothing is announced, because a working app
    // announcing that it works is the definition of noise.
    [Fact]
    public void HoldingTheRoleSaysNothing()
    {
        var notice = SmsRole.Decide(SmsRoleStatus.Held, heldAtLastLaunch: true);

        Assert.False(notice.Speaks);
    }

    // Regaining it is equally quiet: the fold is for what is still wrong.
    [Fact]
    public void RegainingTheRoleSaysNothing()
    {
        var notice = SmsRole.Decide(SmsRoleStatus.Held, heldAtLastLaunch: false);

        Assert.False(notice.Speaks);
    }

    // Never having held it is a setup step, and it is offerable — the consent dialog is
    // one tap away.
    [Fact]
    public void NeverHavingHeldItOffersToTakeIt()
    {
        var notice = SmsRole.Decide(SmsRoleStatus.Available, heldAtLastLaunch: false);

        Assert.True(notice.Speaks);
        Assert.True(notice.CanRequest);
    }

    // Losing it is the case worth catching, and it must not read like first-run setup:
    // something took the role away, and texts have stopped working since.
    [Fact]
    public void LosingTheRoleIsReportedAsALoss()
    {
        var notice = SmsRole.Decide(SmsRoleStatus.Available, heldAtLastLaunch: true);

        Assert.True(notice.Speaks);
        Assert.True(notice.CanRequest);
        Assert.True(notice.WasLost);
        Assert.NotEqual(
            SmsRole.Decide(SmsRoleStatus.Available, heldAtLastLaunch: false).Headline,
            notice.Headline);
    }

    // A device that does not offer the role at all cannot be fixed by asking, so no
    // button is offered — offering one that opens nothing is worse than saying plainly
    // that this needs the lockdown relaxed first.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnavailableRoleSpeaksButOffersNoButton(bool heldAtLastLaunch)
    {
        var notice = SmsRole.Decide(SmsRoleStatus.Unavailable, heldAtLastLaunch);

        Assert.True(notice.Speaks);
        Assert.False(notice.CanRequest);
    }

    // Every notice that speaks has to actually say something, in words rather than a
    // status code — this is what lands in the fold.
    [Theory]
    [InlineData(SmsRoleStatus.Available, false)]
    [InlineData(SmsRoleStatus.Available, true)]
    [InlineData(SmsRoleStatus.Unavailable, false)]
    [InlineData(SmsRoleStatus.Unavailable, true)]
    public void EverySpeakingNoticeCarriesWords(SmsRoleStatus status, bool heldAtLastLaunch)
    {
        var notice = SmsRole.Decide(status, heldAtLastLaunch);

        Assert.False(string.IsNullOrWhiteSpace(notice.Headline));
        Assert.False(string.IsNullOrWhiteSpace(notice.Detail));
    }

    // Nothing here is voiced. Post's only voiced surface is the empty Mailbox; a wry line
    // about a lost role would arrive as a reward for turning the corner, which is exactly
    // the failure the fold is meant to avoid.
    [Fact]
    public void NoticesAreStaticNeutral()
    {
        var lost = SmsRole.Decide(SmsRoleStatus.Available, heldAtLastLaunch: true);

        Assert.DoesNotContain(lost.Headline, EmptyMailbox.Seeds.SelectMany(seed => seed.Rungs));
    }
}
