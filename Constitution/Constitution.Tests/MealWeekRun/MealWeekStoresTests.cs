using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Screens;
using Constitution.Infrastructure.Meals;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealWeekRun;

/// <summary>The two files the run keeps beside the household's other records: the cooked history and the planned weeks.</summary>
public sealed class MealWeekStoresTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-meals-");

    public void Dispose() => folder.Delete(recursive: true);

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);

    private static StoredMealWeek Stored(DateOnly start, string dish = "saffron-rice") => new(
        MealWeek.Planned(
            start,
            Enumerable.Range(0, 7).Select(offset => offset == 1
                ? DinnerChoice.Cooked(start.AddDays(1), Id(dish), Sam, Edt(10, 26, 17, 15), Edt(10, 26, 18), false, false, "From the box.")
                : DinnerChoice.NothingPlanned(start.AddDays(offset), "Nothing is planned."))),
        Edt(10, 21, 12),
        [new StoredDish(Id(dish), "Invented " + dish)]);

    // ---- the cooked history ----

    [Fact]
    public void NoHistoryYetIsAnEmptyOneThatCanBeRead()
    {
        var read = new MealHistoryStore(Path("history.json")).Load();

        Assert.True(read.IsReadable);
        Assert.Empty(read.Meals);
    }

    [Fact]
    public void AHistoryThatCannotBeReadIsNotAnEmptyOne()
    {
        File.WriteAllText(Path("history.json"), "garbage");

        var read = new MealHistoryStore(Path("history.json")).Load();

        Assert.False(read.IsReadable);
        Assert.Equal(MealHistoryCodec.Unusable, read.UnreadableBecause);
    }

    [Fact]
    public void AHistoryThatIsADirectoryIsUnreadableToo()
    {
        Directory.CreateDirectory(Path("history.json"));

        Assert.False(new MealHistoryStore(Path("history.json")).Load().IsReadable);
    }

    [Fact]
    public void AppendedDinnersAreReadBackOnceEach()
    {
        var store = new MealHistoryStore(Path("history.json"));
        var meal = new CookedMeal(Id("tomato-pasta"), Sunday.AddDays(-2));

        store.Append([meal], Sunday);
        store.Append([meal, new CookedMeal(Id("corn-tacos"), Sunday.AddDays(-1))], Sunday);

        Assert.Equal(2, store.Load().Meals.Count);
    }

    [Fact]
    public void DinnersOlderThanTheRetentionAreLetGo()
    {
        var store = new MealHistoryStore(Path("history.json"));

        store.Append([new CookedMeal(Id("tomato-pasta"), Sunday.AddDays(-MealHistoryRetention.Days - 1)), new CookedMeal(Id("corn-tacos"), Sunday.AddDays(-MealHistoryRetention.Days))], Sunday);

        Assert.Equal(Id("corn-tacos"), Assert.Single(store.Load().Meals).Recipe);
    }

    [Fact]
    public void AppendingToAnUnreadableHistoryIsRefusedAndLeavesItBe()
    {
        File.WriteAllText(Path("history.json"), "garbage");
        var store = new MealHistoryStore(Path("history.json"));

        Assert.Throws<InvalidOperationException>(() => store.Append([new CookedMeal(Id("tomato-pasta"), Sunday)], Sunday));

        Assert.Equal("garbage", File.ReadAllText(Path("history.json")));
    }

    // ---- the planned weeks ----

    [Fact]
    public void NoFileIsNothingYet()
    {
        Assert.IsType<MealWeekRecordRead.NothingYet>(new FileMealWeekRecords(Path("weeks.json")).Load());
    }

    [Fact]
    public void ASavedRecordIsReadBack()
    {
        var store = new FileMealWeekRecords(Path("weeks.json"));

        store.Save(MealWeekRecord.Empty.With(Stored(Sunday)));

        var read = Assert.IsType<MealWeekRecordRead.Held>(store.Load());
        Assert.Equal(Sunday, Assert.Single(read.Record.Weeks).WeekStart);
    }

    [Fact]
    public void AFileThatCannotBeReadIsSetAsideNotWrittenOver()
    {
        File.WriteAllText(Path("weeks.json"), "garbage the owner might want");
        var store = new FileMealWeekRecords(Path("weeks.json"));
        Assert.IsType<MealWeekRecordRead.Unreadable>(store.Load());

        store.Save(MealWeekRecord.Empty.With(Stored(Sunday)));

        Assert.Equal("garbage the owner might want", File.ReadAllText(Path("weeks.json.unreadable")));
        Assert.IsType<MealWeekRecordRead.Held>(store.Load());
    }

    [Fact]
    public void ASecondDamageNeverReplacesTheFirstCopy()
    {
        var store = new FileMealWeekRecords(Path("weeks.json"));
        File.WriteAllText(Path("weeks.json"), "first damage");
        store.Save(MealWeekRecord.Empty.With(Stored(Sunday)));
        File.WriteAllText(Path("weeks.json"), "second damage");

        store.Save(MealWeekRecord.Empty.With(Stored(Sunday)));

        Assert.Equal("first damage", File.ReadAllText(Path("weeks.json.unreadable")));
        Assert.Equal("second damage", File.ReadAllText(Path("weeks.json.unreadable.2")));
    }

    [Fact]
    public void ADirectoryWhereTheFileBelongsIsUnreadable()
    {
        Directory.CreateDirectory(Path("weeks.json"));

        Assert.IsType<MealWeekRecordRead.Unreadable>(new FileMealWeekRecords(Path("weeks.json")).Load());
    }

    // ---- the source the screens read ----

    [Fact]
    public void NothingSavedIsNoPlanYet()
    {
        var source = new MealWeekRecordSource(new FileMealWeekRecords(Path("weeks.json")));

        Assert.IsType<MealWeekScreenRead.NoPlanYet>(source.Read(Edt(10, 27, 9)));
    }

    [Fact]
    public void AnUnreadableRecordIsSaidNotHidden()
    {
        File.WriteAllText(Path("weeks.json"), "garbage");

        var read = Assert.IsType<MealWeekScreenRead.Unavailable>(new MealWeekRecordSource(new FileMealWeekRecords(Path("weeks.json"))).Read(Edt(10, 27, 9)));

        Assert.Equal(MealWeekRecordCodec.NotJson, read.Reason);
    }

    [Fact]
    public void AWeekThatIsOverIsNoPlanYet()
    {
        var store = new FileMealWeekRecords(Path("weeks.json"));
        store.Save(MealWeekRecord.Empty.With(Stored(Sunday)));

        Assert.IsType<MealWeekScreenRead.NoPlanYet>(new MealWeekRecordSource(store).Read(Edt(11, 3, 9)));
    }

    [Fact]
    public void TheSourceReadsTheDayOnTheClockItIsGiven()
    {
        var store = new FileMealWeekRecords(Path("weeks.json"));
        store.Save(MealWeekRecord.Empty.With(Stored(Sunday)));

        var late = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.FromHours(-4));

        Assert.IsType<MealWeekScreenRead.Ready>(new MealWeekRecordSource(store).Read(late));
    }
}
