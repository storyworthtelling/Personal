using System.Globalization;
using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Guard G25 (D161): notices are recomputed from live state every call, never cached as "seen" — so
/// a resolved cause simply stops appearing the next time <see cref="AccountNotices.Build"/> runs,
/// rather than requiring a dismissal.
///
/// <para>R-G26 (remediation 3.5): the four notices read EXACTLY what mockup 17 draws — headline,
/// detail, action label and role — compared against strings parsed out of
/// <c>Ledger/design/mockup-17.html</c> at test time. These tests used to assert loose substrings
/// ("Meridian Card" and "reconnect" somewhere in a headline), which passed for wording the owner never
/// approved.</para>
/// </summary>
public class AccountNoticesTests
{
    // Frame 1 of mockup 17 is drawn some days after the lost connection (Sep 22) and a week after the
    // balance went stale (Sep 25); the failed-refresh quotation is "2h ago".
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    // Midday UTC, so the calendar day is the same in every zone from UTC-11 to UTC+11.
    private static DateTimeOffset Noon(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.Zero);

    private static Institution Failed(string name) => new()
    {
        Name = name,
        AccessToken = "access-1",
        RefreshStatus = WorkStatus.Failed,
        LastRefreshedUtc = Noon(9, 22),
    };

    private static Institution HandEntered(string name, DateTimeOffset asOf) => new()
    {
        Name = name,
        AccessToken = string.Empty, // hand-entered: no Plaid Item behind it
        Accounts = [new Account { Name = $"{name} loan", BalanceAsOfUtc = asOf }],
    };

    /// <summary>Nine connected banks (the ninth linked on Sep 12) plus the two the mockup names.</summary>
    private static List<Institution> TheMockupsHousehold()
    {
        var household = new List<Institution>
        {
            Failed("Meridian Card"),
            HandEntered("Evergreen Home Lending", Noon(8, 25)),
        };
        household[0].CreatedAtUtc = Noon(1, 5);
        for (var i = 1; i <= 8; i++)
        {
            household.Add(new Institution
            {
                Name = $"Bank {i}",
                AccessToken = $"access-{i}",
                RefreshStatus = WorkStatus.Completed,
                LastRefreshedUtc = Now.AddHours(-2),
                CreatedAtUtc = i == 8 ? Noon(9, 12) : Noon(1, 5 + i),
            });
        }

        return household;
    }

    private static string RoleOf(AccountNotice notice) => notice.Severity.ToString();

    // ---- R-G26: exact strings, read from the mockup ----

    [Fact]
    public void TheMockupDrawsThreeNoticesAndAStatusLine()
    {
        // Parsed once here so a drawing that stops parsing fails loudly instead of every guard below
        // quietly comparing against nothing.
        Assert.Equal(3, Mockup17.Notices().Count);
        Assert.Equal("For Accounts · newest first", Mockup17.StatusLine());
    }

    [Fact]
    public void TheLostConnectionNoticeIsExactlyTheMockups()
    {
        var drawn = Mockup17.Notices()[1];
        var notice = AccountNotices.Build(TheMockupsHousehold(), Now, lastRefreshFailed: false)
            .Single(n => n.Headline == drawn.Headline);

        Assert.Equal(drawn.Headline, notice.Headline);
        Assert.Equal(drawn.Detail, notice.Detail);
        Assert.Equal(drawn.Action, notice.ActionLabel);
        Assert.Equal(Mockup17.RoleOf(drawn.BarHex), RoleOf(notice));
    }

    [Fact]
    public void TheStaleHandEnteredBalanceNoticeIsExactlyTheMockups()
    {
        var drawn = Mockup17.Notices()[0];
        var notice = AccountNotices.Build(TheMockupsHousehold(), Now, lastRefreshFailed: false)
            .Single(n => n.Headline == drawn.Headline);

        Assert.Equal(drawn.Headline, notice.Headline);
        Assert.Equal(drawn.Detail, notice.Detail);
        Assert.Equal(drawn.Action, notice.ActionLabel);
        Assert.Equal(Mockup17.RoleOf(drawn.BarHex), RoleOf(notice));
    }

    [Fact]
    public void TheTrialLimitNoticeIsExactlyTheMockups()
    {
        var drawn = Mockup17.Notices()[2];
        var notice = AccountNotices.Build(TheMockupsHousehold(), Now, lastRefreshFailed: false)
            .Single(n => n.Headline == drawn.Headline);

        Assert.Equal(drawn.Headline, notice.Headline);
        Assert.Equal(drawn.Detail, notice.Detail);
        Assert.Equal(drawn.Action, notice.ActionLabel);
        Assert.Null(notice.ActionLabel);
        Assert.Equal(Mockup17.RoleOf(drawn.BarHex), RoleOf(notice));
    }

    [Fact]
    public void TheFailedRefreshNoticeReadsTheSentenceTheNotesQuote()
    {
        // The notes quote this one rather than draw it. Headline and detail are the two halves of the
        // quoted sentence, so together they must reproduce it, with the relative age filled in.
        var household = new List<Institution>
        {
            new()
            {
                Name = "Redwood Bank", AccessToken = "access-1", RefreshStatus = WorkStatus.Completed,
                LastRefreshedUtc = Now.AddHours(-2),
            },
        };

        var notice = Assert.Single(AccountNotices.Build(household, Now, lastRefreshFailed: true));

        Assert.Equal(Mockup17.FailedRefreshSentence(), $"{notice.Headline}. {notice.Detail}");
        Assert.Null(notice.ActionLabel);
    }

    // ---- G25: a cause that goes takes its notice with it ----

    [Fact]
    public void NoticeLifetime_ReconnectingMakesTheNoticeDisappear_WithoutBeingDismissed()
    {
        var before = AccountNotices.Build([Failed("Meridian Card")], Now, lastRefreshFailed: false);
        Assert.Single(before);

        // The cause resolves — the institution refreshes successfully — and nothing marks the old
        // notice "read"; Build is simply asked again with the new state.
        var reconnected = new Institution
        {
            Name = "Meridian Card",
            AccessToken = "access-1",
            RefreshStatus = WorkStatus.Completed,
            LastRefreshedUtc = Now,
        };

        Assert.Empty(AccountNotices.Build([reconnected], Now, lastRefreshFailed: false));
    }

    [Fact]
    public void HandEnteredBalance_LessThanAMonthOld_ProducesNoNotice()
    {
        Assert.Empty(AccountNotices.Build([HandEntered("My Mortgage", Now.AddDays(-10))], Now, lastRefreshFailed: false));
    }

    [Fact]
    public void ConnectedAccountsBalance_NeverProducesAStaleManualNotice_EvenIfVeryOld()
    {
        var connected = new Institution
        {
            Name = "Redwood Bank",
            AccessToken = "access-1",
            Accounts = [new Account { Name = "Checking", BalanceAsOfUtc = Now.AddYears(-1) }],
        };

        Assert.Empty(AccountNotices.Build([connected], Now, lastRefreshFailed: false));
    }

    [Fact]
    public void FewerThanNineConnections_ProducesNoTrialWarning()
    {
        var connected = Enumerable.Range(0, 3)
            .Select(i => new Institution { Name = $"Bank {i}", AccessToken = $"access-{i}" })
            .ToList();

        Assert.Empty(AccountNotices.Build(connected, Now, lastRefreshFailed: false));
    }

    // ---- R-G27: each notice carries the date its cause began, so the page can run newest-first ----

    [Fact]
    public void EachNoticeIsDatedByWhenItsCauseBegan_AndRunsInTheMockupsOrder()
    {
        var drawn = Mockup17.Notices();
        var notices = AccountNotices.Build(TheMockupsHousehold(), Now, lastRefreshFailed: false);

        // Newest first by timestamp reproduces the order the mockup draws, whatever order Build lists them.
        var byDate = notices.OrderByDescending(n => n.At).Select(n => n.Headline).ToList();
        Assert.Equal(drawn.Select(d => d.Headline).ToList(), byDate);

        foreach (var row in drawn)
        {
            var notice = notices.Single(n => n.Headline == row.Headline);
            Assert.NotNull(notice.At);
            Assert.Equal(row.Date, notice.At!.Value.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void TheStaleNoticeIsDatedWhenTheBalanceTurnedAMonthOld_NotWhenItWasSet()
    {
        var notice = AccountNotices.Build([HandEntered("Evergreen Home Lending", Noon(8, 25))], Now, lastRefreshFailed: false).Single();

        Assert.Equal(Noon(8, 25).AddMonths(1), notice.At);
    }

    [Fact]
    public void ABalanceJustUnderAMonthOldIsNotYetStale()
    {
        var asOf = Now.AddMonths(-1).AddHours(1);

        Assert.Empty(AccountNotices.Build([HandEntered("Evergreen Home Lending", asOf)], Now, lastRefreshFailed: false));
    }

    [Fact]
    public void TheFailedRefreshNoticeIsDatedByTheLastGoodRefresh()
    {
        var household = new List<Institution>
        {
            new() { Name = "A", AccessToken = "a", LastRefreshedUtc = Now.AddHours(-5) },
            new() { Name = "B", AccessToken = "b", LastRefreshedUtc = Now.AddHours(-2) },
        };

        var notice = Assert.Single(AccountNotices.Build(household, Now, lastRefreshFailed: true));

        Assert.Equal(Now.AddHours(-2), notice.At);
        Assert.Contains("2h ago", notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTrialNoticeIsDatedByWhenTheNinthItemWasLinked_AndUndatedWhenThatIsUnknown()
    {
        var household = TheMockupsHousehold();
        Assert.Equal(Noon(9, 12), AccountNotices.Build(household, Now, false).Single(n => n.Severity.ToString() == "Reference").At);

        foreach (var institution in household)
        {
            institution.CreatedAtUtc = default;
        }

        Assert.Null(AccountNotices.Build(household, Now, false).Single(n => n.Severity.ToString() == "Reference").At);
    }

    // ---- finding 4: the action carries the id it acts on, not the headline text ----

    [Fact]
    public void TheReconnectActionNamesTheInstitutionItReconnects()
    {
        var household = TheMockupsHousehold();
        var meridian = household.Single(i => i.Name == "Meridian Card");

        var notice = AccountNotices.Build(household, Now, false).Single(n => n.Headline == "Meridian Card needs signing in");

        Assert.Equal(NoticeAction.Reconnect, notice.Target!.Action);
        Assert.Equal(meridian.Id, notice.Target.Id);
    }

    [Fact]
    public void TheOpenActionNamesTheAccountItOpens()
    {
        var household = TheMockupsHousehold();
        var account = household.Single(i => i.Name == "Evergreen Home Lending").Accounts.Single();

        var notice = AccountNotices.Build(household, Now, false).Single(n => n.Headline == "Evergreen Home Lending is a month old");

        Assert.Equal(NoticeAction.OpenAccount, notice.Target!.Action);
        Assert.Equal(account.Id, notice.Target.Id);
    }

    [Fact]
    public void ANoticeWithNoActionNamesNoTarget()
    {
        var household = TheMockupsHousehold();

        Assert.Null(AccountNotices.Build(household, Now, false).Single(n => n.Severity.ToString() == "Reference").Target);
    }

    [Fact]
    public void ABankWithSeveralStaleBalancesRaisesOneNotice_AboutTheOldest()
    {
        var bank = new Institution
        {
            Name = "Evergreen Home Lending",
            AccessToken = string.Empty,
            Accounts =
            [
                new Account { Name = "Newer", BalanceAsOfUtc = Noon(8, 29) },
                new Account { Name = "Older", BalanceAsOfUtc = Noon(8, 25) },
            ],
        };

        var notice = Assert.Single(AccountNotices.Build([bank], Now, false));

        Assert.Equal(bank.Accounts[1].Id, notice.Target!.Id);
        Assert.Equal(Noon(8, 25).AddMonths(1), notice.At);
    }

    // ---- wording the mockup does not draw ----

    [Fact]
    public void ALostConnectionThatNeverRefreshedSaysSoWithoutADate()
    {
        var neverRefreshed = new Institution
        {
            Name = "Meridian Card", AccessToken = "a", RefreshStatus = WorkStatus.Failed, LastRefreshedUtc = null,
        };

        var notice = AccountNotices.Build([neverRefreshed], Now, false).Single();

        Assert.Equal("Meridian Card needs signing in", notice.Headline);
        Assert.Equal("Reconnect Meridian Card", notice.ActionLabel);
        Assert.Null(notice.At);
        Assert.DoesNotContain(" on ", notice.Detail.Split('.')[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AtTenConnectionsTheHeadlineCountsAndTheDetailKeepsTheMockupsSentence()
    {
        var household = Enumerable.Range(0, 10)
            .Select(i => new Institution { Name = $"Bank {i}", AccessToken = $"a{i}" })
            .ToList();

        var notice = AccountNotices.Build(household, Now, false).Single();

        Assert.Equal("10 of 10 connections used", notice.Headline);
        Assert.Equal(
            "An eleventh would need a paid plan, which bills each refresh.",
            notice.Detail);
    }
}
