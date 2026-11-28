using System.Text;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;
using Storyworthtelling.Folds;
using Watch.Domain;
using Watch.Domain.Dash;
using Xunit;

namespace Codex.Tests.Dash;

/// <summary>
/// Post tells the wrist a text arrived (P14 B8), so the wrist can buzz its two short taps (WD §7, A12): named as People
/// waiting names the correspondence, with when it came and whether its sender is starred in the phone's contacts, and
/// nothing else. Invented people and numbers throughout.
/// </summary>
public class DashArrivalsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 16, 20, 0, TimeSpan.Zero);

    private const string Lee = "2125550101";
    private const string Robin = "2125550102";
    private const string LeeMail = "lee@example.com";

    private static TimelineItem Text(string who, int minute, string words = "Running late.") =>
        new(TimelineItemKind.Message, who, T0.AddMinutes(minute), words, Incoming: true, $"sms:{who}:{minute}");

    private static TimelineItem GroupText(string who, int minute) =>
        Text(who, minute) with { GroupId = "mms:17", Participants = [Lee, Robin], ParticipantAddresses = ["+12125550101", "+12125550102"] };

    private static string WaitingKey(TimelineItem item, IIdentityResolver resolver)
    {
        var relay = new DashRelay(new DashSnapshot(
            [item], new HashSet<(TimelineItemKind, string)>(), SpamList.From([]), new DeleteBuffer(), new Tombstones(), resolver,
            new ReadState(new Dictionary<string, DateTimeOffset>()), GroupNames.Empty, key => key == Lee ? "Lee" : "Robin"));
        return Assert.Single(relay.People().People).Key;
    }

    private static bool Nobody(string key) => false;

    [Fact]
    public void APersonsTextIsNamedAsPeopleWaitingNamesIt()
    {
        var text = Text(Lee, -1);

        Assert.Equal(WaitingKey(text, PerKeyIdentityResolver.Instance), DashArrivals.For(text, Nobody).Key);
    }

    [Fact]
    public void AGroupsTextIsNamedAsPeopleWaitingNamesIt()
    {
        var text = GroupText(Robin, -1);

        Assert.Equal(WaitingKey(text, PerKeyIdentityResolver.Instance), DashArrivals.For(text, Nobody).Key);
        Assert.Equal(DashIds.Correspondence(TimelineMerger.GroupThreadKey("mms:17")), DashArrivals.For(text, Nobody).Key);
    }

    [Fact]
    public void APersonKnownByTwoAddressesIsNamedAsPeopleWaitingNamesThem()
    {
        // The identity links merge Lee's number and address into one correspondence, which People waiting names by the
        // first of Lee's keys; a text comes from the number, which is that one.
        var resolver = new OneLee();
        var mail = new TimelineItem(TimelineItemKind.Email, LeeMail, T0.AddMinutes(-9), "See you.", Incoming: true, "mail:1") { Subject = "Plans" };
        var text = Text(Lee, -1);
        var relay = new DashRelay(new DashSnapshot(
            [mail, text], new HashSet<(TimelineItemKind, string)>(), SpamList.From([]), new DeleteBuffer(), new Tombstones(), resolver,
            new ReadState(new Dictionary<string, DateTimeOffset>()), GroupNames.Empty, key => "Lee"));

        Assert.Equal(Assert.Single(relay.People().People).Key, DashArrivals.For(text, Nobody).Key);
    }

    [Fact]
    public void ItIsWhenTheTextCame() =>
        Assert.Equal(T0.AddMinutes(-1), DashArrivals.For(Text(Lee, -1), Nobody).At);

    [Fact]
    public void AStarredSenderIsAFavourite()
    {
        Assert.True(DashArrivals.For(Text(Lee, -1), key => key == Lee).Favourite);
        Assert.False(DashArrivals.For(Text(Robin, -1), key => key == Lee).Favourite);
    }

    [Fact]
    public void InAGroupTheFavouriteIsWhoeverSentIt()
    {
        Assert.True(DashArrivals.For(GroupText(Robin, -1), key => key == Robin).Favourite);
        Assert.False(DashArrivals.For(GroupText(Lee, -1), key => key == Robin).Favourite);
    }

    [Fact]
    public void ASenderWithNoKeyIsNoFavourite()
    {
        // A text Post got with no address: nobody to look up, so nobody starred.
        var asked = new List<string>();

        var arrived = DashArrivals.For(Text(string.Empty, -1), key => { asked.Add(key); return true; });

        Assert.False(arrived.Favourite);
        Assert.Empty(asked);
    }

    [Fact]
    public void NoNumberNameOrWordCrosses()
    {
        var json = Encoding.UTF8.GetString(DashCodec.Encode(DashArrivals.For(GroupText(Robin, -1), Nobody)));

        foreach (var payload in new[] { Lee, Robin, "555", "Robin", "Lee", "Running", "mms:17", "group:" })
        {
            Assert.DoesNotContain(payload, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ItTravelsOnTheArrivalPath() =>
        Assert.Equal(WatchIdentity.DashArrivedPath, DashArrivals.For(Text(Lee, -1), Nobody).Path);

    private sealed class OneLee : IIdentityResolver
    {
        public string ResolveThreadKey(string contactKey) => contactKey == Lee ? LeeMail : contactKey;
    }
}
