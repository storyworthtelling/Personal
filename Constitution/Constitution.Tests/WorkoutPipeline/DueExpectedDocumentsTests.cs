using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The read-back reads every document it has been told about, whatever its day. A page delivered this morning is blank
/// until the owner has done the workout, and a blank page read after ink has been proven is a skip: so the read-back
/// must be shown only documents whose day has ended. This is the view it is shown.
/// </summary>
public sealed class DueExpectedDocumentsTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-due-");

    public void Dispose() => folder.Delete(recursive: true);

    private static readonly DateTimeOffset Morning7 = new(2026, 11, 4, 7, 15, 0, TimeSpan.FromHours(-5));

    private FileExpectedDocuments Store() => new(Path.Combine(folder.FullName, "expected.json"));

    [Fact]
    public void YesterdaysDocumentIsDue()
    {
        var store = Store();
        store.Record(Workout(day: new DateOnly(2026, 11, 3)));

        var due = new DueExpectedDocuments(store, () => Morning7);

        Assert.Single(due.Outstanding());
    }

    [Fact]
    public void TodaysDocumentIsNotYetDue()
    {
        var store = Store();
        store.Record(Workout(day: new DateOnly(2026, 11, 4)));
        store.Record(Workout(day: new DateOnly(2026, 11, 5)));

        var due = new DueExpectedDocuments(store, () => Morning7);

        Assert.Empty(due.Outstanding());
        Assert.Equal(2, store.Outstanding().Count);
    }

    [Fact]
    public void ADocumentFromLastWeekIsStillDue()
    {
        var store = Store();
        store.Record(Workout(day: new DateOnly(2026, 10, 28)));

        Assert.Single(new DueExpectedDocuments(store, () => Morning7).Outstanding());
    }

    [Fact]
    public void TheDayTurnsOnTheClocksOwnOffsetNotUtc()
    {
        // 7:15 am at UTC-5 is 12:15 UTC on the 4th; 11 pm at UTC-5 on the 3rd is already the 4th in UTC.
        var store = Store();
        store.Record(Workout(day: new DateOnly(2026, 11, 3)));
        var lateOnTheThird = new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.FromHours(-5));

        Assert.Empty(new DueExpectedDocuments(store, () => lateOnTheThird).Outstanding());
    }

    [Fact]
    public void RecordingSavingAndRetiringGoStraightToTheStore()
    {
        var store = Store();
        var due = new DueExpectedDocuments(store, () => Morning7);
        var today = Workout(day: new DateOnly(2026, 11, 4));

        due.Record(today);
        Assert.Single(store.Outstanding());

        due.MarkSaved(today.Name);
        Assert.True(Assert.Single(store.Outstanding()).Saved);

        due.Retire(today.Name);
        Assert.Empty(store.Outstanding());
    }
}
