using Constitution.Domain.Body;
using Constitution.Infrastructure.Body;

namespace Constitution.Tests.Body;

/// <summary>The Body readings on the phone: file-backed, atomic, and never silently emptied.</summary>
public class JsonBodyStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "constitution-body-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(directory, "readings", "body-readings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static BodyReading Weight(int daysAgo, double value) =>
        new(BodySignal.Weight, Samples.Today.AddDays(-daysAgo), value);

    [Fact]
    public void ReadingsSurviveANewInstance()
    {
        new JsonBodyStore(FilePath).Upsert([Weight(0, 180.5), new BodyReading(BodySignal.Steps, Samples.Today, 8123)]);

        var again = new JsonBodyStore(FilePath).All();

        Assert.Equal(2, again.Count);
        Assert.Contains(Weight(0, 180.5), again);
    }

    [Fact]
    public void AFreshStoreIsEmptyWithNoInstallDay()
    {
        var store = new JsonBodyStore(FilePath);

        Assert.True(store.IsEmpty);
        Assert.Null(store.InstallDay);
        Assert.Empty(store.All());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void AnInstallDayAloneIsStillEmpty()
    {
        var store = new JsonBodyStore(FilePath);
        store.SetInstallDay(Samples.Today);

        Assert.True(store.IsEmpty);
        Assert.Equal(Samples.Today, new JsonBodyStore(FilePath).InstallDay);
    }

    [Fact]
    public void TheInstallDayIsRecordedOnceAndNeverMoved()
    {
        var store = new JsonBodyStore(FilePath);
        store.SetInstallDay(Samples.Today);

        new JsonBodyStore(FilePath).SetInstallDay(Samples.Today.AddDays(9));
        new JsonBodyStore(FilePath).SetInstallDay(Samples.Today.AddDays(-9));

        Assert.Equal(Samples.Today, new JsonBodyStore(FilePath).InstallDay);
    }

    [Fact]
    public void ALaterReadingOfTheSameSignalAndDayReplacesTheEarlier()
    {
        var store = new JsonBodyStore(FilePath);
        store.Upsert([Weight(0, 180)]);
        store.Upsert([Weight(0, 179.5), Weight(1, 181)]);

        Assert.Equal(179.5, store.All().Single(reading => reading.Day == Samples.Today).Value);
        Assert.Equal(2, store.All().Count);
    }

    [Fact]
    public void ReadingsComeOldestFirst()
    {
        var store = new JsonBodyStore(FilePath);
        store.Upsert([Weight(0, 1), Weight(5, 2), Weight(2, 3)]);

        Assert.Equal([5, 2, 0], store.All().Select(reading => Samples.Today.DayNumber - reading.Day.DayNumber));
    }

    [Fact]
    public void TwoInstancesDoNotLoseEachOthersWrites()
    {
        // The worker and the screen each hold a store on the same file.
        var worker = new JsonBodyStore(FilePath);
        var screen = new JsonBodyStore(FilePath);

        worker.Upsert([Weight(1, 181)]);
        screen.Upsert([Weight(0, 180)]);

        Assert.Equal(2, worker.All().Count);
        Assert.Equal(2, screen.All().Count);
    }

    [Fact]
    public void AWriteLeavesNoTemporaryFileBehind()
    {
        var store = new JsonBodyStore(FilePath);
        store.Upsert([Weight(0, 180)]);
        store.Upsert([Weight(1, 181)]);

        Assert.Equal([FilePath], Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public void AGarbledFileIsSetAsideNotOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ this is not json");

        var store = new JsonBodyStore(FilePath);
        Assert.True(store.IsEmpty);
        store.Upsert([Weight(0, 180)]);

        // What could not be read is kept for a person to look at; the store carries on with a clean file.
        Assert.Equal("{ this is not json", File.ReadAllText(FilePath + ".unreadable"));
        Assert.Single(new JsonBodyStore(FilePath).All());
    }

    [Fact]
    public void ASignalFromANewerVersionIsSkippedAndTheRestKept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            {"version":1,"installDay":"2026-11-10","readings":[
              {"signal":"weight","day":"2026-11-17","value":180},
              {"signal":"from-the-future","day":"2026-11-17","value":1},
              {"signal":"steps","day":"not-a-date","value":2}]}
            """);

        var store = new JsonBodyStore(FilePath);

        Assert.Equal(new DateOnly(2026, 11, 10), store.InstallDay);
        Assert.Equal([new BodyReading(BodySignal.Weight, Samples.Today, 180)], store.All());
    }

    [Fact]
    public void MergeRestoredAddsWhatIsMissingAndKeepsLocalDays()
    {
        var store = new JsonBodyStore(FilePath);
        store.SetInstallDay(Samples.Today);
        store.Upsert([Weight(2, 190)]);

        var changed = store.MergeRestored(Samples.Today.AddDays(-40), [Weight(3, 185), Weight(2, 184)]);

        Assert.True(changed);
        Assert.Equal(Samples.Today.AddDays(-40), store.InstallDay);
        var weights = store.All().ToDictionary(reading => reading.Day, reading => reading.Value);
        Assert.Equal(2, weights.Count);
        Assert.Equal(190, weights[Samples.Today.AddDays(-2)]);
        Assert.Equal(185, weights[Samples.Today.AddDays(-3)]);
    }

    [Fact]
    public void MergeRestoredNeverMovesTheInstallDayLater()
    {
        var store = new JsonBodyStore(FilePath);
        store.SetInstallDay(Samples.Today.AddDays(-60));

        var changed = store.MergeRestored(Samples.Today.AddDays(-10), []);

        Assert.False(changed);
        Assert.Equal(Samples.Today.AddDays(-60), store.InstallDay);
    }

    [Fact]
    public void MergeRestoredOntoAnUndatedStoreTakesTheBackupsDay()
    {
        var store = new JsonBodyStore(FilePath);

        Assert.True(store.MergeRestored(Samples.Today.AddDays(-5), [Weight(1, 180)]));
        Assert.Equal(Samples.Today.AddDays(-5), store.InstallDay);
    }

    [Fact]
    public void MergeRestoredWithNothingNewChangesNothingAndWritesNothing()
    {
        var store = new JsonBodyStore(FilePath);
        store.SetInstallDay(Samples.Today.AddDays(-5));
        store.Upsert([Weight(1, 180)]);
        var before = File.ReadAllText(FilePath);

        Assert.False(store.MergeRestored(Samples.Today.AddDays(-5), [Weight(1, 999)]));
        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public void TheFileHoldsReadingsAndDatesAndNoPersonalLabel()
    {
        var store = new JsonBodyStore(FilePath);
        store.SetInstallDay(Samples.Today);
        store.Upsert([Weight(0, 180)]);

        var text = File.ReadAllText(FilePath);

        Assert.DoesNotContain("example", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"signal\":\"weight\"", text, StringComparison.Ordinal);
    }
}
