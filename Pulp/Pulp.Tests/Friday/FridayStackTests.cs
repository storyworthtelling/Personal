using Pulp.Domain.Core;
using Pulp.Domain.Friday;

namespace Pulp.Tests.Friday;

/// <summary>
/// D106–D112, D219–D221, D22: two piles, films and shows, sized from Settings (default three and
/// two); taste first; built for the week that starts Friday 6 am; a veto strikes the card where it
/// stands until Friday and is permanent unless brought back; nothing refills a used-up stack; a
/// child gets no stack (D23, D26). Invented titles only.
/// </summary>
public class FridayStackTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly StackViewer Owner = new(Sam, IsChild: false);
    private static readonly TimeZoneInfo Harbor =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");
    private static readonly DateTimeOffset Wednesday = new(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(-5));

    private static StackCandidate Film(string id, params string[] tags) => new(new TitleId(id), TitleKind.Film, tags);

    private static StackCandidate Show(string id, params string[] tags) => new(new TitleId(id), TitleKind.Show, tags);

    private static readonly StackCandidate[] Pool =
    [
        Film("f-heist", "heist"), Film("f-noir", "noir"), Film("f-space", "space"), Film("f-farce", "farce"),
        Film("f-musical", "musical"), Show("s-noir", "noir"), Show("s-cooking", "cooking"), Show("s-space", "space"),
    ];

    private static FridayStack Build(
        TasteProfile? taste = null, VetoBook? vetoes = null, StackSizes? sizes = null, IEnumerable<StackCandidate>? pool = null) =>
        FridayStack.Build(
            Owner, pool ?? Pool, taste ?? TasteProfile.Empty, vetoes ?? VetoBook.For(Sam), sizes ?? StackSizes.Default, Wednesday, Harbor);

    [Fact]
    public void TheDefaultIsThreeFilmsAndTwoShows()
    {
        Assert.Equal(new StackSizes(3, 2), StackSizes.Default);

        var stack = Build();

        Assert.Equal(3, stack.Pile(TitleKind.Film, VetoBook.For(Sam)).Count);
        Assert.Equal(2, stack.Pile(TitleKind.Show, VetoBook.For(Sam)).Count);
    }

    [Fact]
    public void ThePilesTakeTheirSizesFromSettingsAndHoldOnlyTheirKind()
    {
        var stack = Build(sizes: new StackSizes(1, 3));

        var films = stack.Pile(TitleKind.Film, VetoBook.For(Sam));
        var shows = stack.Pile(TitleKind.Show, VetoBook.For(Sam));
        Assert.Single(films);
        Assert.Equal(3, shows.Count);
        Assert.All(shows, card => Assert.StartsWith("s-", card.Title.Value));
    }

    [Fact]
    public void ANegativeSizeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackSizes(-1, 2));
    }

    [Fact]
    public void TasteComesFirst()
    {
        var taste = TasteLearner.Learn(
            Owner, [], [new WatchedTitle(new TitleId("w-1"), ["noir"], [Sam]), new WatchedTitle(new TitleId("w-2"), ["noir", "space"], [Sam])], VetoBook.For(Sam));

        var stack = Build(taste, sizes: new StackSizes(2, 1));

        Assert.Equal(["f-noir", "f-space"], stack.Pile(TitleKind.Film, VetoBook.For(Sam)).Select(card => card.Title.Value));
        Assert.Equal(["s-noir"], stack.Pile(TitleKind.Show, VetoBook.For(Sam)).Select(card => card.Title.Value));
    }

    [Fact]
    public void TheStackIsTheWeekFromLastFridayToTheNext()
    {
        var stack = Build();

        Assert.Equal(new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.FromHours(-5)), stack.Turnover);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 6, 0, 0, TimeSpan.FromHours(-5)), stack.Until);
        Assert.False(stack.IsDue(Wednesday));
        Assert.True(stack.IsDue(stack.Until));
    }

    [Fact]
    public void AVetoStrikesTheCardWhereItStandsUntilFriday()
    {
        var stack = Build();
        var front = stack.Pile(TitleKind.Film, VetoBook.For(Sam))[0];

        var vetoes = VetoBook.For(Sam).Veto(front.Title, ["heist"]);
        var films = stack.Pile(TitleKind.Film, vetoes);

        Assert.Equal(3, films.Count);
        Assert.Equal(new StackCard(front.Title, CardState.Struck), films[0]);
    }

    [Fact]
    public void BringItBackLiftsTheStrike()
    {
        var stack = Build();
        var front = stack.Pile(TitleKind.Film, VetoBook.For(Sam))[0];

        var vetoes = VetoBook.For(Sam).Veto(front.Title, []).BringBack(front.Title);

        Assert.Equal(CardState.Fresh, stack.Pile(TitleKind.Film, vetoes)[0].State);
    }

    [Fact]
    public void AVetoIsPermanentNextFridayButBringItBackLiftsIt()
    {
        var vetoed = VetoBook.For(Sam).Veto(new TitleId("f-heist"), ["heist"]);
        var sizes = new StackSizes(5, 0);

        var nextWeek = Build(vetoes: vetoed, sizes: sizes);
        Assert.DoesNotContain(nextWeek.Pile(TitleKind.Film, vetoed), card => card.Title.Value == "f-heist");

        var lifted = vetoed.BringBack(new TitleId("f-heist"));
        var later = Build(vetoes: lifted, sizes: sizes);
        Assert.Contains(later.Pile(TitleKind.Film, lifted), card => card.Title.Value == "f-heist");
    }

    [Fact]
    public void AddingMarksTheCardAndAUsedUpStackIsNotRefilled()
    {
        var vetoes = VetoBook.For(Sam);
        var stack = Build(sizes: new StackSizes(1, 1));
        var film = stack.Pile(TitleKind.Film, vetoes)[0].Title;
        var show = stack.Pile(TitleKind.Show, vetoes)[0].Title;

        stack = stack.Add(film);
        vetoes = vetoes.Veto(show, []);

        Assert.Equal(CardState.Added, stack.Pile(TitleKind.Film, vetoes)[0].State);
        Assert.True(stack.IsUsedUp(vetoes));
        Assert.Single(stack.Pile(TitleKind.Film, vetoes));
    }

    [Fact]
    public void AddingATitleNotInTheStackIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Build().Add(new TitleId("not-in-it")));
    }

    [Fact]
    public void AChildHasNoStack()
    {
        var child = new StackViewer(new ViewerId("kit"), IsChild: true);

        var stack = FridayStack.Build(
            child, Pool, TasteProfile.Empty, VetoBook.For(child.Id), StackSizes.Default, Wednesday, Harbor);

        Assert.Empty(stack.Pile(TitleKind.Film, VetoBook.For(child.Id)));
        Assert.Empty(stack.Pile(TitleKind.Show, VetoBook.For(child.Id)));
    }

    [Fact]
    public void AnotherViewersVetoesCannotShapeThisStack()
    {
        Assert.Throws<ArgumentException>(() => Build(vetoes: VetoBook.For(new ViewerId("alex"))));
    }

    [Fact]
    public void ADuplicateCandidateTakesOneSlot()
    {
        var stack = Build(pool: [Film("f-one"), Film("f-one"), Film("f-two")], sizes: new StackSizes(3, 0));

        Assert.Equal(["f-one", "f-two"], stack.Pile(TitleKind.Film, VetoBook.For(Sam)).Select(card => card.Title.Value));
    }
}
