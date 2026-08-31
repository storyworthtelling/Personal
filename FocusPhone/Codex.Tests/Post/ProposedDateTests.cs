using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// A date is found in the background sync, where there is no screen to offer it on. So it
/// waits, and the correspondence folds its corner the next time the owner opens it —
/// proposed, never written silently, and never a buzz.
/// </summary>
public class ProposedDateTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static ProposedDate Offer(int month, int day, TimeOnly? at = null, bool? clashed = null) =>
        new(new ProposedEvent("Recital", new DateOnly(2026, month, day), at), clashed);

    [Fact]
    public void AnOfferRoundTrips()
    {
        var offer = Offer(9, 12, new TimeOnly(19, 30), clashed: true);

        var back = ProposedDateCodec.Decode(ProposedDateCodec.Encode(offer), Today);

        Assert.Equal("Recital", back!.Event.Title);
        Assert.Equal(new DateOnly(2026, 9, 12), back.Event.StartsOn);
        Assert.Equal(new TimeOnly(19, 30), back.Event.StartsAt);
        Assert.True(back.Clashed);
    }

    // An all-day proposal has no time, and a day nobody could check has no clash answer.
    // Both must survive as absences rather than becoming midnight and "clear".
    [Fact]
    public void AbsencesSurviveAsAbsences()
    {
        var back = ProposedDateCodec.Decode(
            ProposedDateCodec.Encode(Offer(9, 12, at: null, clashed: null)), Today);

        Assert.Null(back!.Event.StartsAt);
        Assert.Null(back.Clashed);
    }

    // An offer to add last week's recital is worse than no offer at all.
    [Fact]
    public void AnOfferForAPastDayIsGone()
    {
        Assert.Null(ProposedDateCodec.Decode(ProposedDateCodec.Encode(Offer(8, 30)), Today));
    }

    // Today still counts — something this evening is exactly what is worth offering.
    [Fact]
    public void AnOfferForTodayStands()
    {
        Assert.NotNull(ProposedDateCodec.Decode(ProposedDateCodec.Encode(Offer(9, 1)), Today));
    }

    // A title with a separator in it must not break the record. A subject line is written
    // by whoever sent the mail, so any delimiter that is not escaped is one they can use.
    [Fact]
    public void ATitleWithAwkwardCharactersSurvives()
    {
        var awkward = new ProposedDate(
            new ProposedEvent("Recital | \"the good one\" \n 7:30", new DateOnly(2026, 9, 12), null), null);

        var back = ProposedDateCodec.Decode(ProposedDateCodec.Encode(awkward), Today);

        Assert.Equal(awkward.Event.Title, back!.Event.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    public void GarbledOffersAreSimplyGone(string stored)
    {
        Assert.Null(ProposedDateCodec.Decode(stored, Today));
    }
}
