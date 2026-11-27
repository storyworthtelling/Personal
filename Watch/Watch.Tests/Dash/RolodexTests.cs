using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// The Rolodex on the wrist (P14 B6, W-5.1.2; PC6): Post sends the whole of it, a page at a time, each contact a name
/// and an id; the wrist gathers the pages of one edition, holds them in memory for the session, and searches them by the
/// letters the owner types or says. Invented people throughout.
/// </summary>
public class RolodexTests
{
    private static readonly DashContact Lee = new("p0000000000000001", "Lee");
    private static readonly DashContact Robin = new("p0000000000000002", "Robin");
    private static readonly DashContact LeeRobinson = new("p0000000000000003", "Lee Robinson");
    private static readonly DashContact School = new("p0000000000000004", "Juniper’s school");
    private static readonly DashContact Rene = new("p0000000000000005", "René");
    private static readonly DashContact Sam = new("p0000000000000006", "Sam");

    private static readonly Rolodex Book = new([Lee, LeeRobinson, Rene, Robin, School, Sam]);

    // ---- searching ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("…?")]
    public void EveryoneWhenNothingIsAsked(string? query) => Assert.Equal(Book.Contacts, Book.Find(query));

    [Fact]
    public void ANameIsFoundWhateverItsCase() => Assert.Equal([Lee, LeeRobinson], Book.Find("lee"));

    [Fact]
    public void AccentsAreIgnored() => Assert.Equal([Rene], Book.Find("rene"));

    [Fact]
    public void ALetterFindsEveryWordThatStartsWithIt()
    {
        // "r" begins Robin, René and Robinson: whole names that begin with it come first, then the rest, each in Post's order.
        Assert.Equal([Rene, Robin, LeeRobinson], Book.Find("r"));
    }

    [Fact]
    public void EveryWordMustBeginAWord()
    {
        Assert.Equal([School], Book.Find("jun sch"));
        Assert.Equal([School], Book.Find("school"));
        Assert.Equal([LeeRobinson], Book.Find("lee rob"));
        Assert.Empty(Book.Find("ool"));
    }

    [Theory]
    [InlineData("junipers")]
    [InlineData("Juniper's")]
    [InlineData("juniper’s school")]
    public void AnApostropheJoinsAWord(string query) => Assert.Equal([School], Book.Find(query));

    [Theory]
    [InlineData("Robin.")]
    [InlineData("Robin?")]
    [InlineData(" robin, ")]
    public void DictationsPunctuationIsIgnored(string query) => Assert.Equal([Robin, LeeRobinson], Book.Find(query));

    [Fact]
    public void NobodyByThatNameIsNobody() => Assert.Empty(Book.Find("Juniper Sentinel"));

    [Fact]
    public void TheRolodexKeepsPostsOrder() => Assert.Equal([Lee, LeeRobinson, Rene, Robin, School, Sam], Book.Contacts);

    [Fact]
    public void AContactListedTwiceIsOnce() =>
        Assert.Equal([Lee, Robin], new Rolodex([Lee, Robin, Lee with { Name = "Lee again" }]).Contacts);

    // ---- gathering the pages ----

    private static DashRolodex Page(int page, int pages, string edition, params DashContact[] contacts) => new(page, pages, edition, contacts);

    private const string Other = "e1111111111111111";

    // Answers each ask from the queue, and records which pages were asked.
    private static Func<DashRolodexAsk, Task<RolodexAnswer>> Answering(List<int> asked, params RolodexAnswer[] answers)
    {
        var queue = new Queue<RolodexAnswer>(answers);
        return ask =>
        {
            asked.Add(ask.Page);
            return Task.FromResult(queue.Dequeue());
        };
    }

    private static RolodexAnswer Answer(DashRolodex page) => new(page, null);

    [Fact]
    public async Task OnePageIsTheWhole()
    {
        var asked = new List<int>();

        var fetched = await RolodexGathering.GatherAsync(Answering(asked, Answer(Page(0, 1, Edition, Lee, Robin))));

        Assert.Null(fetched.Trouble);
        Assert.Equal([Lee, Robin], fetched.Whole!.Contacts);
        Assert.Equal([0], asked);
    }

    [Fact]
    public async Task PagesAreAskedInTurn()
    {
        var asked = new List<int>();

        var fetched = await RolodexGathering.GatherAsync(Answering(asked,
            Answer(Page(0, 3, Edition, Lee)), Answer(Page(1, 3, Edition, Robin)), Answer(Page(2, 3, Edition, Sam))));

        Assert.Equal([Lee, Robin, Sam], fetched.Whole!.Contacts);
        Assert.Equal([0, 1, 2], asked);
    }

    [Fact]
    public async Task AnEmptyRolodexIsWhole()
    {
        var fetched = await RolodexGathering.GatherAsync(Answering([], Answer(Page(0, 1, Edition))));

        Assert.Null(fetched.Trouble);
        Assert.Empty(fetched.Whole!.Contacts);
    }

    [Fact]
    public async Task AChangedEditionStartsAgain()
    {
        // The phone's book changed between two pages: the pages would not belong together, so the wrist starts again.
        var asked = new List<int>();

        var fetched = await RolodexGathering.GatherAsync(Answering(asked,
            Answer(Page(0, 2, Edition, Lee)), Answer(Page(1, 2, Other, Robin)),
            Answer(Page(0, 2, Other, Lee)), Answer(Page(1, 2, Other, Sam))));

        Assert.Equal([Lee, Sam], fetched.Whole!.Contacts);
        Assert.Equal([0, 1, 0, 1], asked);
    }

    [Fact]
    public async Task AChangedPageCountStartsAgain()
    {
        var asked = new List<int>();

        var fetched = await RolodexGathering.GatherAsync(Answering(asked,
            Answer(Page(0, 3, Edition, Lee)), Answer(Page(1, 2, Edition, Robin)),
            Answer(Page(0, 1, Edition, Lee))));

        Assert.Equal([Lee], fetched.Whole!.Contacts);
        Assert.Equal([0, 1, 0], asked);
    }

    [Fact]
    public async Task ContactsThatKeepChangingAreSaidSo()
    {
        var asked = new List<int>();
        var answers = Enumerable.Range(0, RolodexGathering.Restarts + 1)
            .SelectMany(round => new[] { Answer(Page(0, 2, $"e{round:x16}", Lee)), Answer(Page(1, 2, Other, Robin)) })
            .ToArray();

        var fetched = await RolodexGathering.GatherAsync(Answering(asked, answers));

        Assert.Null(fetched.Whole);
        Assert.Equal(DashTrouble.Changing, fetched.Trouble);
        Assert.Equal(2, RolodexGathering.Restarts);
        Assert.Equal((RolodexGathering.Restarts + 1) * 2, asked.Count);
    }

    [Theory]
    [InlineData(DashTrouble.NoPhone)]
    [InlineData(DashTrouble.NoAnswer)]
    public async Task ATroubleStopsTheAsking(DashTrouble trouble)
    {
        var asked = new List<int>();

        var fetched = await RolodexGathering.GatherAsync(Answering(asked, Answer(Page(0, 3, Edition, Lee)), new RolodexAnswer(null, trouble)));

        Assert.Null(fetched.Whole);
        Assert.Equal(trouble, fetched.Trouble);
        Assert.Equal([0, 1], asked);
    }

    [Fact]
    public async Task AnAnswerThatIsNoPageIsUnreadable()
    {
        var fetched = await RolodexGathering.GatherAsync(Answering([], new RolodexAnswer(null, null)));

        Assert.Equal(DashTrouble.Unreadable, fetched.Trouble);
    }

    [Fact]
    public async Task TheAskingIsBounded()
    {
        // At most every page of the largest Rolodex, a few times over: never an ask without end.
        var asked = new List<int>();
        var pages = DashLimits.RolodexPages;
        var answers = Enumerable.Range(0, (RolodexGathering.Restarts + 1) * pages)
            .Select(index => Answer(Page(index % pages, pages, index % pages == pages - 1 ? Other : Edition, Lee)))
            .ToArray();

        var fetched = await RolodexGathering.GatherAsync(Answering(asked, answers));

        Assert.Equal(DashTrouble.Changing, fetched.Trouble);
        Assert.True(asked.Count <= (RolodexGathering.Restarts + 1) * pages, $"{asked.Count} asks");
    }

    // ---- held for the session ----

    [Fact]
    public void NothingIsHeldAtFirst() => Assert.Null(new HeldRolodex().Current);

    [Fact]
    public void TheLatestWholeRolodexIsHeld()
    {
        var held = new HeldRolodex();
        var first = new Rolodex([Lee]);
        var second = new Rolodex([Lee, Robin]);

        held.Hold(first);
        held.Hold(second);

        Assert.Same(second, held.Current);
    }

    [Fact]
    public void TheHoldKeepsNothingButMemory()
    {
        // Held in memory for the session only (the brief): nothing on the hold reads or writes a store.
        var members = typeof(HeldRolodex).GetMembers().Select(member => member.Name).ToList();

        Assert.Equal(["Hold", "get_Current"], members.Where(name => name is not ("GetType" or "ToString" or "Equals" or "GetHashCode" or ".ctor" or "Current")).Order(StringComparer.Ordinal));
    }
}
