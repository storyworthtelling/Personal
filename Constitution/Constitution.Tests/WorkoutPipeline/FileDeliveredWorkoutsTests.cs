using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>What has gone to the tablet, kept across app restarts: it is how a later run knows a page is already there.</summary>
public sealed class FileDeliveredWorkoutsTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-delivered-");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf() => Path.Combine(folder.FullName, "nested", "delivered.json");

    private FileDeliveredWorkouts Make() => new(PathOf());

    private static DeliveredWorkout Entry(int day = 3, string person = "Sam", string fingerprint = "abc123", SessionKind kind = SessionKind.OutdoorCardio, bool doctor = false) =>
        new($"Constitution — Workout — 2026-11-{day:00} — {person}", person, new DateOnly(2026, 11, day), kind, Est(11, day, 18), fingerprint, doctor);

    [Fact]
    public void NothingRecordedIsNothingFound() => Assert.Null(Make().Find("Constitution — Workout — 2026-11-03 — Sam"));

    [Fact]
    public void AnEntryKeepsTheOffsetOfItsSlot()
    {
        var entry = Entry(doctor: true);
        Make().Record(entry);

        var back = Make().Find(entry.Name);

        Assert.Equal(entry, back);
        Assert.Equal(TimeSpan.FromHours(-5), back!.SlotStart.Offset);
        Assert.True(back.CarriesDoctorNote);
    }

    [Fact]
    public void RecordingTheSameNameAgainReplacesIt()
    {
        var store = Make();
        store.Record(Entry(fingerprint: "one"));
        store.Record(Entry(fingerprint: "two"));

        Assert.Equal("two", Make().Find(Entry().Name)!.Fingerprint);
    }

    [Fact]
    public void TwoPeopleOnTheSameDayAreTwoEntries()
    {
        var store = Make();
        store.Record(Entry(person: "Sam"));
        store.Record(Entry(person: "Lee"));

        Assert.NotNull(Make().Find(Entry(person: "Sam").Name));
        Assert.NotNull(Make().Find(Entry(person: "Lee").Name));
    }

    [Fact]
    public void RemovingOneLeavesTheOthers()
    {
        var store = Make();
        store.Record(Entry(person: "Sam"));
        store.Record(Entry(person: "Lee"));

        store.Remove(Entry(person: "Sam").Name);

        Assert.Null(Make().Find(Entry(person: "Sam").Name));
        Assert.NotNull(Make().Find(Entry(person: "Lee").Name));
    }

    [Fact]
    public void RemovingWhatIsNotThereIsNothing()
    {
        var store = Make();

        store.Remove("Constitution — Workout — 2026-11-03 — Sam");

        Assert.False(File.Exists(PathOf()));
    }

    [Fact]
    public void OldEntriesAreLetGoOnTheNextRecord()
    {
        var store = Make();
        store.Record(Entry(day: 3));
        store.Record(Entry(day: 18));

        Assert.Null(Make().Find(Entry(day: 3).Name));
        Assert.NotNull(Make().Find(Entry(day: 18).Name));
    }

    [Fact]
    public void AnEntryExactlyTwoWeeksOldIsKept()
    {
        var store = Make();
        store.Record(Entry(day: 4));
        store.Record(Entry(day: 18));

        Assert.NotNull(Make().Find(Entry(day: 4).Name));
    }

    [Fact]
    public void AnUnreadableFileIsSetAsideAndIgnored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ this is not the file");

        var store = Make();

        Assert.Null(store.Find(Entry().Name));
        Assert.True(File.Exists(PathOf() + ".unreadable"));
        store.Record(Entry());
        Assert.NotNull(Make().Find(Entry().Name));
    }

    [Fact]
    public void AnUnknownKindMakesTheFileUnreadable()
    {
        var store = Make();
        store.Record(Entry());
        File.WriteAllText(PathOf(), File.ReadAllText(PathOf()).Replace("OutdoorCardio", "Swimming", StringComparison.Ordinal));

        Assert.Null(Make().Find(Entry().Name));
        Assert.True(File.Exists(PathOf() + ".unreadable"));
    }

    [Fact]
    public void TheFileHoldsNoPageContentAndNoDose()
    {
        Make().Record(Entry());
        var text = File.ReadAllText(PathOf());

        Assert.DoesNotContain("lb", text, StringComparison.Ordinal);
        Assert.Contains("abc123", text, StringComparison.Ordinal);
    }
}
