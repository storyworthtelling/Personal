using Constitution.Domain;
using Constitution.Domain.Notices;

namespace Constitution.Tests.Notices;

/// <summary>
/// C-9.1.3 — the brief's short list as ordinary notifications (D83, D91; PC1): a scan waiting, the box
/// under ten, and the three errors. Each carries its role as an extra and no number; nothing else
/// posts. The wire strings are literals here on purpose: Flyleaf reads them from the other side.
/// </summary>
public class NoticeNotificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 7, 30, 0, TimeSpan.Zero);

    private static Notice Waiting(NoticeKind kind) => new(kind, "cause:" + kind, Start);

    private static readonly NoticeKind[] Listed =
    [
        NoticeKind.ReMarkableDelivery, NoticeKind.HealthConnectRevoked, NoticeKind.BedOffline,
        NoticeKind.ScanToCheck, NoticeKind.BoxUnderTen,
    ];

    [Fact]
    public void OnlyTheShortList()
    {
        Assert.Equal(Listed.Order(), NoticeNotifications.ShortList.Order());
        foreach (var kind in Enum.GetValues<NoticeKind>().Except(Listed))
        {
            Assert.Null(NoticeNotifications.For(kind));
        }
        foreach (var kind in Listed)
        {
            Assert.NotNull(NoticeNotifications.For(kind));
        }
    }

    [Fact]
    public void CarriesTheRole()
    {
        Assert.Equal("problem", NoticeNotifications.ProblemRole);
        Assert.Equal("reference", NoticeNotifications.ReferenceRole);
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".NOTICE_ROLE", NoticeNotifications.RoleExtraKey);
        Assert.Equal("problem", NoticeNotifications.For(NoticeKind.ReMarkableDelivery)!.RoleWire);
        Assert.Equal("problem", NoticeNotifications.For(NoticeKind.HealthConnectRevoked)!.RoleWire);
        Assert.Equal("problem", NoticeNotifications.For(NoticeKind.BedOffline)!.RoleWire);
        // D91: a waiting scan's bookmark is slate, though its dog-ear stays teal.
        Assert.Equal("reference", NoticeNotifications.For(NoticeKind.ScanToCheck)!.RoleWire);
        Assert.Equal("reference", NoticeNotifications.For(NoticeKind.BoxUnderTen)!.RoleWire);
    }

    [Fact]
    public void NoDigits()
    {
        foreach (var kind in Listed)
        {
            var note = NoticeNotifications.For(kind)!;
            Assert.False(string.IsNullOrWhiteSpace(note.Title));
            Assert.False(string.IsNullOrWhiteSpace(note.Text));
            Assert.DoesNotContain(note.Title + note.Text, char.IsDigit);
        }
    }

    [Fact]
    public void IdsAreDistinctAndStable()
    {
        var ids = Listed.Select(kind => NoticeNotifications.For(kind)!.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids, Listed.Select(kind => NoticeNotifications.For(kind)!.Id).ToList());
    }

    [Fact]
    public void PostsWhatIsWaitingOnce()
    {
        var plan = NoticeNotifications.Plan([Waiting(NoticeKind.BedOffline), Waiting(NoticeKind.BedSilent)], new HashSet<NoticeKind>());
        Assert.Equal([NoticeKind.BedOffline], plan.Post.Select(note => note.Kind));
        Assert.Empty(plan.Cancel);

        var again = NoticeNotifications.Plan([Waiting(NoticeKind.BedOffline)], new HashSet<NoticeKind> { NoticeKind.BedOffline });
        Assert.Empty(again.Post);
        Assert.Empty(again.Cancel);
    }

    [Fact]
    public void ManyCausesOfOneKindPostOnce()
    {
        var two = new[]
        {
            new Notice(NoticeKind.ScanToCheck, "scan:a", Start),
            new Notice(NoticeKind.ScanToCheck, "scan:b", Start),
        };
        var plan = NoticeNotifications.Plan(two, new HashSet<NoticeKind>());
        Assert.Single(plan.Post);
    }

    [Fact]
    public void CancelsWhenTheCauseGoes()
    {
        var plan = NoticeNotifications.Plan([], new HashSet<NoticeKind> { NoticeKind.BoxUnderTen, NoticeKind.ScanToCheck });
        Assert.Empty(plan.Post);
        Assert.Equal(new[] { NoticeKind.BoxUnderTen, NoticeKind.ScanToCheck }.Order(), plan.Cancel.Order());
    }

    [Fact]
    public void ANoticeOffTheListIsNeverCancelledOrPosted()
    {
        var plan = NoticeNotifications.Plan([Waiting(NoticeKind.KeepHandoff)], new HashSet<NoticeKind>());
        Assert.Empty(plan.Post);
        Assert.Empty(plan.Cancel);
    }
}
