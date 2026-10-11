using Pulp.Domain.Actions;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;

namespace Pulp.Tests.Actions;

/// <summary>
/// What waits behind Pulp's dog-ear (mockup 12, D260, D122, D286): each notice is what happened and
/// what to do next, with a role colour and at most one button. Invented services, titles and leagues.
///
/// <para>The bugs these reintroduce: a hand-off that saw no playback shouting a Problem it cannot
/// know (D286 asks for the hedged NotSeen, quietly), a landed hand-off leaving a notice behind (a
/// notice leaves when its cause does), two causes sharing a key so one erases the other, and the
/// detail sentence drifting from <see cref="HandOffNotice"/>'s plain-language copy.</para>
/// </summary>
public class WaitingNoticesTests
{
    private static readonly DateTimeOffset Tonight = new(2026, 10, 9, 21, 0, 0, TimeSpan.Zero);
    private static readonly TitleId Saltmarsh = new("title-saltmarsh");

    [Fact]
    public void LandedElsewhereIsAProblemWithTheHandOffSentenceAndOpensTheTitle()
    {
        var notice = WaitingNotices.AfterLanding(Landing.LandedElsewhere, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight);

        Assert.NotNull(notice);
        Assert.Equal("Lanternbox opened, but not on Saltmarsh", notice.Headline);
        Assert.Equal(HandOffNotice.LandedElsewhere("Lanternbox"), notice.Detail);
        Assert.Equal(NoticeRole.Problem, notice.Role);
        Assert.Equal("Open Saltmarsh", notice.ActionLabel);
        Assert.Equal(NoticeAction.OpenTitle, notice.Action);
        Assert.Equal(Tonight, notice.At);
        Assert.Equal(WaitingNotices.HandOffKey(Saltmarsh), notice.Key);
    }

    [Fact]
    public void NotSeenIsHedgedAndQuiet()
    {
        var notice = WaitingNotices.AfterLanding(Landing.NotSeen, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight);

        Assert.NotNull(notice);
        Assert.Equal("Lanternbox may not have opened on Saltmarsh", notice.Headline);
        Assert.Equal(HandOffNotice.NotSeen("Lanternbox"), notice.Detail);
        Assert.Equal(NoticeRole.Reference, notice.Role);
        Assert.Equal(WaitingNotices.HandOffKey(Saltmarsh), notice.Key);
    }

    [Theory]
    [InlineData(Landing.Landed)]
    [InlineData(Landing.Unknown)]
    public void ALandingThatWorkedOrCannotBeToldLeavesNothing(Landing landing) =>
        Assert.Null(WaitingNotices.AfterLanding(landing, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight));

    [Theory]
    [InlineData(StartRefusal.Hidden)]
    [InlineData(StartRefusal.NotAHome)]
    [InlineData(StartRefusal.ServiceBlocked)]
    [InlineData(StartRefusal.ServiceNotAttested)]
    [InlineData(StartRefusal.UnknownService)]
    public void ARefusalSaysTheTitleWasNotOpenedAndWhy(StartRefusal refusal)
    {
        var notice = WaitingNotices.Refused(refusal, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight);

        Assert.Equal("Saltmarsh was not opened", notice.Headline);
        Assert.Equal(HandOffNotice.ForRefusal(refusal, "Lanternbox"), notice.Detail);
        Assert.Equal(NoticeRole.Problem, notice.Role);
        Assert.Equal(WaitingNotices.HandOffKey(Saltmarsh), notice.Key);
    }

    [Fact]
    public void ARefusalTheExecutorAlreadyWroteKeepsItsSentenceUnderTheSameHeadline()
    {
        var sentence = HandOffNotice.ForRefusal(StartRefusal.ServiceBlocked, "Lanternbox");
        var notice = WaitingNotices.Refused(sentence, "Saltmarsh", Saltmarsh, Tonight);

        Assert.Equal(WaitingNotices.Refused(StartRefusal.ServiceBlocked, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight), notice);
        Assert.Throws<ArgumentException>(() => WaitingNotices.Refused(" ", "Saltmarsh", Saltmarsh, Tonight));
    }

    [Fact]
    public void NotInstalledNamesTheService()
    {
        var notice = WaitingNotices.NotInstalled("Lanternbox", Saltmarsh, Tonight);

        Assert.Equal("Lanternbox is not installed", notice.Headline);
        Assert.Equal(HandOffNotice.NotInstalled("Lanternbox"), notice.Detail);
        Assert.Equal(NoticeRole.Problem, notice.Role);
    }

    [Fact]
    public void ARejectedKeyPointsAtTheVault()
    {
        var notice = WaitingNotices.KeyRejected("availability", "availability", Tonight);

        Assert.Equal("The availability key stopped working", notice.Headline);
        Assert.Contains("Settings, under Vault", notice.Detail, StringComparison.Ordinal);
        Assert.Equal("Open Vault", notice.ActionLabel);
        Assert.Equal(NoticeAction.OpenVault, notice.Action);
        Assert.Equal(NoticeRole.Problem, notice.Role);
    }

    [Fact]
    public void AnUnreachableFeedIsNamedByLeagueAndKeyedByLeague()
    {
        var east = WaitingNotices.SportsFeedUnreachable("league-east", "Eastern Rowing League", Tonight);
        var west = WaitingNotices.SportsFeedUnreachable("league-west", "Western Rowing League", Tonight);

        Assert.Equal("The Eastern Rowing League schedule could not be reached", east.Headline);
        Assert.NotEqual(east.Key, west.Key);
        Assert.Null(east.ActionLabel);
    }

    [Fact]
    public void BothHouseholdFileProblemsShareOneSlot()
    {
        var unreadable = WaitingNotices.HouseholdUnreadable(Tonight);
        var newer = WaitingNotices.HouseholdFromNewerVersion(Tonight);

        Assert.Equal(unreadable.Key, newer.Key);
        Assert.Equal(NoticeAction.OpenVault, unreadable.Action);
        Assert.Equal(NoticeAction.None, newer.Action);
    }

    [Fact]
    public void EveryNoticeSaysWhatHappenedWhatToDoAndAtMostOneButton()
    {
        var all = Every().ToList();
        Assert.True(all.Count >= 10, "No notices were built; this guard would pass on nothing.");

        Assert.All(all, notice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(notice.Headline));
            Assert.False(string.IsNullOrWhiteSpace(notice.Detail));
            Assert.False(string.IsNullOrWhiteSpace(notice.Key));
            Assert.Equal(notice.ActionLabel is null, notice.Action == NoticeAction.None);
        });
    }

    [Fact]
    public void DifferentCausesNeverShareAKey()
    {
        var keys = new[]
        {
            WaitingNotices.HandOffKey(Saltmarsh),
            WaitingNotices.HandOffKey(new TitleId("title-foxglove")),
            WaitingNotices.KeyRejected("availability", "availability", Tonight).Key,
            WaitingNotices.KeyRejected("trailers", "trailer", Tonight).Key,
            WaitingNotices.SportsFeedUnreachable("league-east", "Eastern Rowing League", Tonight).Key,
            WaitingNotices.HouseholdUnreadable(Tonight).Key,
        };

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    private static IEnumerable<WaitingNotice> Every()
    {
        foreach (var refusal in new[] { StartRefusal.Hidden, StartRefusal.NotAHome, StartRefusal.ServiceBlocked,
                     StartRefusal.ServiceNotAttested, StartRefusal.UnknownService })
        {
            yield return WaitingNotices.Refused(refusal, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight);
        }

        yield return WaitingNotices.NotInstalled("Lanternbox", Saltmarsh, Tonight);
        yield return WaitingNotices.AfterLanding(Landing.LandedElsewhere, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight)!;
        yield return WaitingNotices.AfterLanding(Landing.NotSeen, "Lanternbox", "Saltmarsh", Saltmarsh, Tonight)!;
        yield return WaitingNotices.KeyRejected("availability", "availability", Tonight);
        yield return WaitingNotices.SportsFeedUnreachable("league-east", "Eastern Rowing League", Tonight);
        yield return WaitingNotices.HouseholdUnreadable(Tonight);
        yield return WaitingNotices.HouseholdFromNewerVersion(Tonight);
    }
}
