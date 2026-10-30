using Constitution.Domain.Delivery;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;
using static Constitution.Tests.MealWeekDocuments.DocsData;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>The trail's words, and the roster and pantry readings over the household file.</summary>
public class MealDocumentLogicTests
{
    private static MealDocumentOutcome Outcome(MealDocumentVerdict verdict, DeliveryKind kind = DeliveryKind.Recipe) =>
        new("a title that must never be printed", kind, "Lee", new DateOnly(2026, 10, 25), verdict);

    private static string Entry(string phrase) => "[Oct 21, 12:00 pm] INFO Body — " + phrase;

    [Fact]
    public void EveryVerdictHasALineThatPassesTheFilter()
    {
        foreach (var verdict in Enum.GetValues<MealDocumentVerdict>())
        {
            var lines = MealDocumentTrail.Lines(new MealDocumentsResult.Done([Outcome(verdict)], null, 0));

            var line = Assert.Single(lines);
            Assert.NotNull(TrailLines.Filter(Entry(line)));
            Assert.DoesNotContain("title", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ALineCountsWhatHappened()
    {
        var done = new MealDocumentsResult.Done(
            [Outcome(MealDocumentVerdict.Delivered), Outcome(MealDocumentVerdict.Delivered), Outcome(MealDocumentVerdict.NotHere)], null, 0);

        var lines = MealDocumentTrail.Lines(done);

        Assert.Contains("meal documents: 2 documents delivered", lines);
        Assert.Contains("meal documents: 1 documents not here", lines);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void AnEmptyPantryAndUnreadableStaplesAreSaid()
    {
        var done = new MealDocumentsResult.Done([Outcome(MealDocumentVerdict.Unchanged)], "No file yet.", 3);

        var lines = MealDocumentTrail.Lines(done);

        Assert.Contains("meal documents: pantry empty", lines);
        Assert.Contains("meal documents: 3 records unreadable", lines);
        Assert.All(lines, line => Assert.NotNull(TrailLines.Filter(Entry(line))));
    }

    [Fact]
    public void EveryStopHasALineThatPassesTheFilter()
    {
        foreach (var because in Enum.GetValues<MealDocumentsStoppedBecause>())
        {
            var line = Assert.Single(MealDocumentTrail.Lines(new MealDocumentsResult.Stopped(because, "A fixed sentence.")));

            Assert.NotNull(TrailLines.Filter(Entry(line)));
            Assert.StartsWith("meal documents not delivered", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRosterIsTheLivePeople()
    {
        var view = new RosterView(People(), []);

        var read = Assert.IsType<MealRosterRead.Ready>(MealRosterReading.From(view, null));

        Assert.Equal(4, read.People.Count);
    }

    [Fact]
    public void ABlockedFileIsNoRoster()
    {
        var read = Assert.IsType<MealRosterRead.Unavailable>(MealRosterReading.From(new RosterView(People(), []), "The file will not open."));

        Assert.Equal("The file will not open.", read.Reason);
    }

    [Fact]
    public void NoRosterYetIsNoRoster()
    {
        Assert.IsType<MealRosterRead.Unavailable>(MealRosterReading.From(null, null));
    }

    [Fact]
    public void ARosterWithAnUnreadablePersonIsNoRoster()
    {
        var view = new RosterView(People(), [new UnreadableRecord(new RecordKey(ConstitutionKinds.Person, "wren"), "bad")]);

        var read = Assert.IsType<MealRosterRead.Unavailable>(MealRosterReading.From(view, null));

        Assert.DoesNotContain("wren", read.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARosterWithNoOneIsNoRoster()
    {
        Assert.IsType<MealRosterRead.Unavailable>(MealRosterReading.From(new RosterView([], []), null));
    }

    private static HouseholdDocument WithStaple(Staple staple) =>
        HouseholdRecords.Put(HouseholdDocument.Empty, PantryMaps.Staple, staple, RecipeData.At(1), RecipeData.OwnerWriter);

    [Fact]
    public void ThePantryIsTheStaplesOfTheFile()
    {
        var document = WithStaple(new Staple("olive oil", Aisle.BakingAndSpices, true));

        var read = Assert.IsType<MealPantryRead.Ready>(MealPantryReading.From(document, fileMet: true));

        Assert.NotNull(read.Pantry.Find("olive oil"));
        Assert.Equal(0, read.UnreadableRecords);
    }

    [Fact]
    public void AFileNotYetMetHasNoPantry()
    {
        var read = Assert.IsType<MealPantryRead.NoneYet>(MealPantryReading.From(HouseholdDocument.Empty, fileMet: false));

        Assert.False(string.IsNullOrWhiteSpace(read.Sentence));
    }

    [Fact]
    public void AnEmptyFileThatWasMetIsAnEmptyPantry()
    {
        var read = Assert.IsType<MealPantryRead.Ready>(MealPantryReading.From(HouseholdDocument.Empty, fileMet: true));

        Assert.Empty(read.Pantry.Items);
    }

    [Fact]
    public void AStapleThatCannotBeReadIsCountedNotGuessed()
    {
        var document = WithStaple(new Staple("olive oil", Aisle.BakingAndSpices, true)).With(HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Staple, "mystery"),
            RecordSchema.Version,
            new Dictionary<string, string> { ["aisle"] = "nowhere" },
            RecipeData.At(2),
            RecipeData.OwnerWriter));

        var read = Assert.IsType<MealPantryRead.Ready>(MealPantryReading.From(document, fileMet: true));

        Assert.Equal(1, read.UnreadableRecords);
        Assert.Single(read.Pantry.Items);
    }

    [Fact]
    public void NoProfileMeansNobodysRatings()
    {
        var none = Constitution.Infrastructure.Meals.TasteForTheWeek.LastRatings(new MealTasteRead.None());

        Assert.Null(none.Of(new PersonId("sam"), Id("lentil-soup")));
    }

    [Fact]
    public void AHeldProfileGivesEachPersonsLastWord()
    {
        var sam = new PersonTaste(
            new PersonId("sam"), false, [Id("lentil-soup")], [Id("miso-salmon")], [Id("black-bean-chili")],
            new Dictionary<Cuisine, int>(), new Dictionary<string, decimal>(), new Dictionary<RecipeId, int>());
        var held = new MealTasteRead.Held(new TasteProfile([sam], []), []);

        var ratings = Constitution.Infrastructure.Meals.TasteForTheWeek.LastRatings(held);

        Assert.Equal(TasteVerdict.Loved, ratings.Of(new PersonId("sam"), Id("lentil-soup")));
        Assert.Equal(TasteVerdict.Fine, ratings.Of(new PersonId("sam"), Id("miso-salmon")));
        Assert.Equal(TasteVerdict.NeverAgain, ratings.Of(new PersonId("sam"), Id("black-bean-chili")));
        Assert.Null(ratings.Of(new PersonId("lee"), Id("lentil-soup")));
    }
}
