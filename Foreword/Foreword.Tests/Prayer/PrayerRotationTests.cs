using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// A rotation: a standing list the owner wants to pray through one at a time — a missionary list,
/// say — where one member prints each morning and the list comes round again.
///
/// <para>It is a different thing from a held request and must not be modelled as one. A request has
/// a length of time attached to it and comes off the list when it is answered; a rotation member
/// has neither, and printing "held 6 weeks" beside a name that was simply next would be the page
/// making something up.</para>
///
/// <para>The pick is a pure function of the <b>date</b>, not of a stored cursor. A cursor is a
/// second thing to keep in step with the page, and it goes wrong in the one way that matters: a
/// morning that fails to deliver still moves it, so a name is skipped and nobody ever knows.</para>
/// </summary>
public class PrayerRotationTests
{
    private static readonly DateOnly Start = new(2026, 9, 6);

    private sealed class Reader : IPrayerReader
    {
    }

    private static string Read(PrayerText text) => text.ReadBy(new Reader());

    private static PrayerRotation Missionaries() => new()
    {
        Id = "missionaries",
        Name = "Missionaries",
        Members = [PrayerText.Of("the Aldens"), PrayerText.Of("the Boyds"), PrayerText.Of("the Cho family")],
        Began = Start,
    };

    [Fact]
    public void OneMemberPrintsEachMorningInOrder()
    {
        var rotation = Missionaries();

        Assert.Equal("the Aldens", Read(RotationPick.For(rotation, Start)!.Value.Member));
        Assert.Equal("the Boyds", Read(RotationPick.For(rotation, Start.AddDays(1))!.Value.Member));
        Assert.Equal("the Cho family", Read(RotationPick.For(rotation, Start.AddDays(2))!.Value.Member));
    }

    [Fact]
    public void AndThenItComesRoundAgain()
    {
        var rotation = Missionaries();

        Assert.Equal("the Aldens", Read(RotationPick.For(rotation, Start.AddDays(3))!.Value.Member));
        Assert.Equal("the Boyds", Read(RotationPick.For(rotation, Start.AddDays(31))!.Value.Member));
    }

    /// <summary>
    /// The pick is arithmetic on the date, so a morning that never delivered costs that morning's
    /// name and nothing else. A stored cursor advanced on compose would skip a name silently on
    /// every failed delivery, and the list would quietly drift out of the order the owner set.
    /// </summary>
    [Fact]
    public void AMissedMorningDoesNotSkipAName()
    {
        var rotation = Missionaries();

        // Monday delivered, Tuesday failed, Wednesday delivered. Wednesday is still the third name.
        Assert.Equal("the Cho family", Read(RotationPick.For(rotation, Start.AddDays(2))!.Value.Member));
    }

    /// <summary>
    /// A rotation that carries its name onto the page, so the section reads as the list it is
    /// rather than as an unexplained extra request.
    /// </summary>
    [Fact]
    public void ThePickCarriesTheRotationsNameAndItsPlaceInTheList()
    {
        var pick = RotationPick.For(Missionaries(), Start.AddDays(1))!.Value;

        Assert.Equal("Missionaries", pick.Rotation);
        Assert.Equal("2 of 3", pick.Position);
    }

    [Fact]
    public void AnEmptyRotationPrintsNothingRatherThanAnEmptyHeading()
    {
        var empty = Missionaries() with { Members = [] };

        Assert.Null(RotationPick.For(empty, Start));
    }

    /// <summary>
    /// A rotation paused is a rotation that prints nothing — the owner's way of putting a list down
    /// for a season without losing the list.
    /// </summary>
    [Fact]
    public void APausedRotationPrintsNothing()
    {
        var paused = Missionaries() with { Paused = true };

        Assert.Null(RotationPick.For(paused, Start));
    }

    /// <summary>
    /// A rotation whose start date is in the future has not begun. It reads as nothing rather than
    /// as a negative index, which is the shape a crash takes when a date is stored wrong.
    /// </summary>
    [Fact]
    public void ARotationThatHasNotBegunPrintsNothing()
    {
        var rotation = Missionaries() with { Began = Start.AddDays(4) };

        Assert.Null(RotationPick.For(rotation, Start));
    }

    /// <summary>
    /// Every rotation the owner keeps is picked from, not just the first. Two lists are two things
    /// to pray through, and a loop that takes only the first would be the second list silently
    /// never appearing.
    /// </summary>
    [Fact]
    public void EveryRotationGetsAPick()
    {
        var second = new PrayerRotation
        {
            Id = "elders",
            Name = "Elders",
            Members = [PrayerText.Of("Dan"), PrayerText.Of("Pete")],
            Began = Start,
        };

        var picks = RotationPick.ForAll([Missionaries(), second], Start.AddDays(1));

        Assert.Equal(["Missionaries", "Elders"], picks.Select(pick => pick.Rotation));
        Assert.Equal(["the Boyds", "Pete"], picks.Select(pick => Read(pick.Member)));
    }
}
