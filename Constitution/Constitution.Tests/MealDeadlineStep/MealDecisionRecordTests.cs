using System.Text.Json;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Meals;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>The decided weeks: JSON with frozen names, unreadable is typed and never empty, and the file keeps nothing it cannot read.</summary>
public class MealDecisionRecordTests
{
    private static MealDecisionEntry Entry(DateOnly week, bool pending = false, params string[] cards) =>
        new(week, Edt(10, 25, 15), 5, 2, 3, true, cards.Select(card => new RecipeId(card)).ToList(), pending);

    [Fact]
    public void ARecordComesBackAsItWas()
    {
        var record = MealDecisionRecord.Empty.With(Entry(Sunday, true, "lentil-curry", "corn-tacos")).With(Entry(Sunday.AddDays(7)));

        var back = Assert.IsType<MealDecisionRecordRead.Held>(MealDecisionRecordCodec.Decode(MealDecisionRecordCodec.Encode(record))).Record;

        Assert.Equal(2, back.Entries.Count);
        var first = back.For(Sunday)!;
        Assert.Equal(5, first.Approved);
        Assert.Equal(2, first.Denied);
        Assert.Equal(3, first.ApprovedUnread);
        Assert.True(first.NeedsRevisedSheet);
        Assert.True(first.EvidencePending);
        Assert.Equal(["lentil-curry", "corn-tacos"], first.ReplanWithout.Select(card => card.Value));
        Assert.Equal(Edt(10, 25, 15), first.DecidedAt);
    }

    [Fact]
    public void TheStoredNamesAreFrozen()
    {
        var json = MealDecisionRecordCodec.Encode(MealDecisionRecord.Empty.With(Entry(Sunday, true, "lentil-curry")));

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        var entry = document.RootElement.GetProperty("decisions")[0];
        Assert.Equal(
            ["week", "decidedAt", "approved", "denied", "approvedUnread", "needsRevisedSheet", "replanWithout", "evidencePending"],
            entry.EnumerateObject().Select(property => property.Name));
        Assert.Equal("2026-10-25", entry.GetProperty("week").GetString());
        Assert.Equal("lentil-curry", entry.GetProperty("replanWithout")[0].GetString());
    }

    [Fact]
    public void AHandWrittenFileOfTheFirstVersionReads()
    {
        const string json = """{"version":1,"decisions":[{"week":"2026-10-25","decidedAt":"2026-10-25T15:00:00.0000000-04:00","approved":6,"denied":1,"approvedUnread":0,"needsRevisedSheet":true,"replanWithout":["corn-tacos"],"evidencePending":false}]}""";

        var entry = Assert.Single(Assert.IsType<MealDecisionRecordRead.Held>(MealDecisionRecordCodec.Decode(json)).Record.Entries);

        Assert.Equal(new RecipeId("corn-tacos"), Assert.Single(entry.ReplanWithout));
        Assert.False(entry.EvidencePending);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{"version":2,"decisions":[]}""")]
    [InlineData("""{"version":1,"decisions":[{"week":"not a date"}]}""")]
    [InlineData("""{"version":1,"decisions":[{"week":"2026-10-24","decidedAt":"2026-10-25T15:00:00.0000000-04:00","approved":1,"denied":0,"approvedUnread":0,"needsRevisedSheet":false,"replanWithout":[],"evidencePending":false}]}""")]
    public void WhatIsNotThisRecordIsUnreadableAndQuotesNothing(string json)
    {
        var read = Assert.IsType<MealDecisionRecordRead.Unreadable>(MealDecisionRecordCodec.Decode(json));

        Assert.True(read.Reason is MealDecisionRecordCodec.NotJson or MealDecisionRecordCodec.WrongShape);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingSavedIsNothingYet(string? json) =>
        Assert.IsType<MealDecisionRecordRead.NothingYet>(MealDecisionRecordCodec.Decode(json));

    [Fact]
    public void OnlyTheLatestEightWeeksAreKept()
    {
        var record = Enumerable.Range(0, 11).Aggregate(MealDecisionRecord.Empty, (held, index) => held.With(Entry(Sunday.AddDays(7 * index))));

        Assert.Equal(MealDecisionRecord.Kept, record.Entries.Count);
        Assert.Null(record.For(Sunday));
        Assert.NotNull(record.For(Sunday.AddDays(70)));
    }

    [Fact]
    public void ANewEntryForAWeekReplacesTheOldOne()
    {
        var record = MealDecisionRecord.Empty.With(Entry(Sunday, true)).With(Entry(Sunday, false));

        Assert.False(Assert.Single(record.Entries).EvidencePending);
    }

    // ---- the file ----

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "meal-decisions-" + Guid.NewGuid().ToString("N"), "decisions.json");

    [Fact]
    public void AFileKeepsWhatWasSaved()
    {
        var path = TempFile();
        var store = new FileMealDecisionRecords(path);
        Assert.IsType<MealDecisionRecordRead.NothingYet>(store.Load());

        store.Save(MealDecisionRecord.Empty.With(Entry(Sunday)));

        Assert.Equal(Sunday, Assert.Single(Assert.IsType<MealDecisionRecordRead.Held>(new FileMealDecisionRecords(path).Load()).Record.Entries).Week);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void ADamagedFileIsUnreadableAndSetAsideBeforeANewOneIsWritten()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ damaged");
        var store = new FileMealDecisionRecords(path);

        Assert.IsType<MealDecisionRecordRead.Unreadable>(store.Load());
        store.Save(MealDecisionRecord.Empty.With(Entry(Sunday)));

        Assert.IsType<MealDecisionRecordRead.Held>(store.Load());
        var folder = Path.GetDirectoryName(path)!;
        Assert.Contains(Directory.GetFiles(folder), file => File.ReadAllText(file) == "{ damaged");
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void AFolderWhereTheFileShouldBeIsUnreadable()
    {
        var path = TempFile();
        Directory.CreateDirectory(path);
        var store = new FileMealDecisionRecords(path);

        Assert.IsType<MealDecisionRecordRead.Unreadable>(store.Load());
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}
