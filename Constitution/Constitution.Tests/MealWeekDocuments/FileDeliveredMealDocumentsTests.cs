using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>What has gone to the tablet, kept across restarts: it is how a later run knows a page is already there.</summary>
public sealed class FileDeliveredMealDocumentsTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-meal-delivered-");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf() => Path.Combine(folder.FullName, "nested", "delivered-documents.json");

    private FileDeliveredMealDocuments Make() => new(PathOf());

    private static DeliveredMealDocument Entry(int day = 25, DeliveryKind kind = DeliveryKind.Meals, string person = "Lee", string fingerprint = "abc123") =>
        new($"Constitution — {kind} — 2026-10-{day:00} — {person}", kind, person, new DateOnly(2026, 10, day), Edt(10, day, 17, 15), fingerprint);

    [Fact]
    public void NothingRecordedIsNothingFound() => Assert.Null(Make().Find("Constitution — Meals — 2026-10-25"));

    [Fact]
    public void AnEntryKeepsTheOffsetOfItsSlot()
    {
        var entry = Entry();
        Make().Record(entry);

        var back = Make().Find(entry.Name);

        Assert.Equal(entry, back);
        Assert.Equal(TimeSpan.FromHours(-4), back!.SlotStart.Offset);
    }

    [Fact]
    public void RecordingTheSameNameAgainReplacesIt()
    {
        Make().Record(Entry(fingerprint: "one"));
        Make().Record(Entry(fingerprint: "two"));

        Assert.Equal("two", Make().Find(Entry().Name)!.Fingerprint);
    }

    [Fact]
    public void ARecipeAndASheetAreTwoEntries()
    {
        Make().Record(Entry(kind: DeliveryKind.Meals));
        Make().Record(Entry(kind: DeliveryKind.Recipe));

        Assert.NotNull(Make().Find(Entry(kind: DeliveryKind.Meals).Name));
        Assert.NotNull(Make().Find(Entry(kind: DeliveryKind.Recipe).Name));
    }

    [Fact]
    public void RemovingWhatIsNotThereIsNothing()
    {
        Make().Remove("Constitution — Meals — 2026-10-25");

        Assert.False(File.Exists(PathOf()));
    }

    [Fact]
    public void RemovingTakesOnlyThatEntry()
    {
        var store = Make();
        store.Record(Entry(kind: DeliveryKind.Meals));
        store.Record(Entry(kind: DeliveryKind.Recipe));

        store.Remove(Entry(kind: DeliveryKind.Meals).Name);

        Assert.Null(Make().Find(Entry(kind: DeliveryKind.Meals).Name));
        Assert.NotNull(Make().Find(Entry(kind: DeliveryKind.Recipe).Name));
    }

    [Fact]
    public void AnEntryMuchOlderThanANewOneIsLetGo()
    {
        var store = Make();
        store.Record(Entry(day: 1));
        store.Record(Entry(day: 25));

        Assert.Null(Make().Find(Entry(day: 1).Name));
        Assert.NotNull(Make().Find(Entry(day: 25).Name));
    }

    [Fact]
    public void AFileThatCannotBeReadIsSetAsideAndTheStoreCarriesOn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "this is not the record");

        Assert.Null(Make().Find(Entry().Name));

        Assert.Contains(Directory.GetFiles(Path.GetDirectoryName(PathOf())!), file => file != PathOf());
        Make().Record(Entry());
        Assert.NotNull(Make().Find(Entry().Name));
    }

    [Fact]
    public void TheFileHoldsNoLineOfThePage()
    {
        Make().Record(Entry(fingerprint: "feedface"));

        var text = File.ReadAllText(PathOf());

        Assert.Contains("feedface", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pdf", text, StringComparison.OrdinalIgnoreCase);
    }
}
