using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using Pulp.Domain.People;

namespace Pulp.Tests.Discover;

/// <summary>
/// The week as Discover holds it (D22, D106–D112, D219–D221, D282): dealt once from the Friday stack
/// and kept until the next Friday 6 am, so a veto strikes the card where it stands instead of a
/// rebuild quietly dealing a replacement (D112: no refill). Watched and vetoed titles are never
/// dealt; a used-up stack is every card added or struck, and an empty one is not "used up".
/// Invented titles, people and zone only.
/// </summary>
public class DiscoverPageWeekTests
{
    private static readonly ViewerId Sam = new("adult-example-a");
    private static readonly ViewerId Ash = new("adult-example-b");
    private static readonly ViewerId Wren = new("child-example-wren");
    private static readonly HouseholdRoster Roster = new(
        [new HouseholdViewer(Sam, false), new HouseholdViewer(Ash, false), new HouseholdViewer(Wren, true)]);

    private static readonly TimeZoneInfo Harbor =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");

    private static readonly DateTimeOffset Wednesday = new(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(-5));

    private static CatalogueTitle Title(string id, TitleKind kind, params string[] genres) => new()
    {
        Id = new TitleId(id),
        Kind = kind,
        Name = "Invented " + id,
        Year = 2020,
        Genres = genres,
    };

    private static readonly CatalogueTitle[] Pool =
    [
        Title("f-heist", TitleKind.Film, "heist"), Title("f-noir", TitleKind.Film, "noir"),
        Title("f-space", TitleKind.Film, "space"), Title("f-farce", TitleKind.Film, "farce"),
        Title("s-noir", TitleKind.Show, "noir"), Title("s-cooking", TitleKind.Show, "cooking"), Title("s-space", TitleKind.Show, "space"),
    ];

    private static DiscoverPageWeek Deal(
        ViewerId? viewer = null, TasteProfile? taste = null, VetoBook? vetoes = null, IReadOnlySet<TitleId>? watched = null,
        StackSizes? sizes = null, IEnumerable<CatalogueTitle>? pool = null)
    {
        var who = viewer ?? Sam;
        return DiscoverPageWeek.Deal(
            who, Roster, pool ?? Pool, taste ?? TasteProfile.Empty, vetoes ?? VetoBook.For(who), watched ?? new HashSet<TitleId>(),
            sizes ?? StackSizes.Default, Wednesday, Harbor, [], HomeOverrides.None);
    }

    private static string[] Ids(IEnumerable<DiscoverCard> pile) => pile.Select(card => card.Row.Id.Value).ToArray();

    [Fact]
    public void TheWeekIsTheFridayStacksTwoPilesInTasteOrder()
    {
        var taste = TasteProfile.From(new Dictionary<string, int> { ["noir"] = 5, ["space"] = 3 });

        var week = Deal(taste: taste);

        Assert.Equal(["f-noir", "f-space", "f-farce"], Ids(week.Pile(TitleKind.Film, VetoBook.For(Sam))));
        Assert.Equal(["s-noir", "s-space"], Ids(week.Pile(TitleKind.Show, VetoBook.For(Sam))));
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.FromHours(-5)), week.Turnover);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 6, 0, 0, TimeSpan.FromHours(-5)), week.Until);
    }

    [Fact]
    public void AWatchedTitleIsNeverDealt()
    {
        var week = Deal(watched: new HashSet<TitleId> { new("f-heist"), new("s-cooking") });

        Assert.Equal(["f-farce", "f-noir", "f-space"], Ids(week.Pile(TitleKind.Film, VetoBook.For(Sam))));
        Assert.Equal(["s-noir", "s-space"], Ids(week.Pile(TitleKind.Show, VetoBook.For(Sam))));
    }

    [Fact]
    public void AVetoMidweekStrikesTheCardWhereItStandsAndDealsNoReplacement()
    {
        var week = Deal();
        var vetoes = VetoBook.For(Sam).Veto(new TitleId("f-farce"), ["farce"]);

        var films = week.Pile(TitleKind.Film, vetoes);

        Assert.Equal(["f-farce", "f-heist", "f-noir"], Ids(films));
        Assert.Equal(CardState.Struck, films[0].State);
        Assert.Equal(CardState.Fresh, films[1].State);
    }

    [Fact]
    public void BringItBackLiftsTheStrike()
    {
        var week = Deal();
        var vetoes = VetoBook.For(Sam).Veto(new TitleId("f-farce"), ["farce"]).BringBack(new TitleId("f-farce"));

        Assert.Equal(CardState.Fresh, week.Pile(TitleKind.Film, vetoes)[0].State);
    }

    [Fact]
    public void AnAddedCardStaysOnItsPileMarkedAdded()
    {
        var week = Deal().Add(new TitleId("f-heist"));

        var card = week.Pile(TitleKind.Film, VetoBook.For(Sam)).Single(c => c.Row.Id.Value == "f-heist");
        Assert.Equal(CardState.Added, card.State);
        Assert.Throws<ArgumentException>(() => week.Add(new TitleId("not-dealt")));
    }

    [Fact]
    public void EveryCardAddedOrStruckIsAUsedUpStack()
    {
        var week = Deal(sizes: new StackSizes(1, 1));
        var film = week.Pile(TitleKind.Film, VetoBook.For(Sam))[0].Row.Id;
        var show = week.Pile(TitleKind.Show, VetoBook.For(Sam))[0].Row.Id;

        Assert.False(week.IsUsedUp(VetoBook.For(Sam)));
        Assert.True(week.Add(film).IsUsedUp(VetoBook.For(Sam).Veto(show, [])));
    }

    [Fact]
    public void AnEmptyWeekIsNotUsedUp()
    {
        var week = Deal(pool: []);

        Assert.True(week.IsEmpty);
        Assert.False(week.IsUsedUp(VetoBook.For(Sam)));
    }

    [Fact]
    public void AChildIsDealtNothing()
    {
        Assert.True(Deal(viewer: Wren).IsEmpty);
    }

    [Fact]
    public void AnotherViewersVetoesAreRefused()
    {
        var week = Deal();

        Assert.Throws<ArgumentException>(() => week.Pile(TitleKind.Film, VetoBook.For(Ash)));
    }

    [Fact]
    public void TheWeekIsDueAtTheNextFridaySixAm()
    {
        var week = Deal();

        Assert.False(week.IsDue(week.Until.AddMinutes(-1)));
        Assert.True(week.IsDue(week.Until));
    }

    [Fact]
    public void TheWeekSurvivesBeingWrittenAndReadBack()
    {
        var week = Deal(taste: TasteProfile.From(new Dictionary<string, int> { ["noir"] = 2 })).Add(new TitleId("f-noir"));

        var read = DiscoverPageWeekCodec.Read(DiscoverPageWeekCodec.Write(week));

        Assert.NotNull(read.Week);
        Assert.Null(read.Problem);
        var back = read.Week!;
        Assert.Equal(week.Viewer, back.Viewer);
        Assert.Equal(week.Turnover, back.Turnover);
        Assert.Equal(week.Until, back.Until);
        foreach (var kind in new[] { TitleKind.Film, TitleKind.Show })
        {
            var before = week.Pile(kind, VetoBook.For(Sam));
            var after = back.Pile(kind, VetoBook.For(Sam));
            Assert.Equal(Ids(before), Ids(after));
            Assert.Equal(before.Select(card => card.State), after.Select(card => card.State));
            Assert.Equal(before.Select(card => card.Row.HomeLine), after.Select(card => card.Row.HomeLine));
            Assert.Equal(before.SelectMany(card => card.Row.Tags), after.SelectMany(card => card.Row.Tags));
            Assert.Equal(before.Select(card => card.Row.Year), after.Select(card => card.Row.Year));
            Assert.Contains(after, card => card.Row.Year is not null);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("{\"version\":99}")]
    public void AGarbledWeekReadsAsNoWeekWithTheReason(string text)
    {
        var read = DiscoverPageWeekCodec.Read(text);

        Assert.Null(read.Week);
        Assert.False(string.IsNullOrWhiteSpace(read.Problem));
    }

    // ---- a size change waits for Friday (D112, D220) ----

    [Fact]
    public void AKeptWeekIsKeptMidweekWhateverSizeIsNowChosen()
    {
        var dealt = Deal(sizes: new StackSizes(3, 2));
        var reread = DiscoverPageWeekCodec.Read(DiscoverPageWeekCodec.Write(dealt)).Week!;

        Assert.True(reread.Keeps(Sam, Wednesday.AddDays(1)));
        Assert.Equal(3, reread.Pile(TitleKind.Film, VetoBook.For(Sam)).Count);
        Assert.Equal(2, reread.Pile(TitleKind.Show, VetoBook.For(Sam)).Count);
    }

    [Fact]
    public void AKeptWeekIsDealtAgainOnlyAtTheNextFridayOrForAnotherViewer()
    {
        var dealt = Deal();

        Assert.False(dealt.Keeps(Sam, dealt.Until));
        Assert.False(dealt.Keeps(Ash, Wednesday));
        Assert.True(dealt.Keeps(Sam, dealt.Until.AddTicks(-1)));
    }

    [Fact]
    public void ABiggerSizeChosenLaterDealsMoreOnlyInTheNextWeek()
    {
        var small = Deal(sizes: new StackSizes(1, 1));
        var next = DiscoverPageWeek.Deal(
            Sam, Roster, Pool, TasteProfile.Empty, VetoBook.For(Sam), new HashSet<TitleId>(),
            new StackSizes(3, 2), small.Until, Harbor, [], HomeOverrides.None);

        Assert.Single(small.Pile(TitleKind.Film, VetoBook.For(Sam)));
        Assert.Equal(3, next.Pile(TitleKind.Film, VetoBook.For(Sam)).Count);
    }
}
