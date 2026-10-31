using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Constitution.Domain.Household.Records;
using Constitution.Tests.HouseholdFile;
using Storyworthtelling.Household;

namespace Constitution.Tests.PersonSheet;

public class RosterRemovalTests
{
    private static readonly RosterEditor Owner = new(RosterRole.Owner);
    private static readonly RosterEditor Partner = new(RosterRole.Partner);

    private static PersonRemovalResult Remove(HouseholdDocument document, PersonId id, RosterEditor? editor = null) =>
        RosterRemoval.Remove(editor ?? Owner, document, id, SheetData.After(document), RosterData.OwnerWriter);

    private static PersonRestoreResult Restore(HouseholdDocument document, RemovedPerson removed, RosterEditor? editor = null) =>
        RosterRemoval.Restore(editor ?? Owner, document, removed, SheetData.After(document), RosterData.OwnerWriter);

    private static IEnumerable<string> Names(HouseholdDocument document) => RosterView.From(document).Entries.Select(entry => entry.Name);

    [Fact]
    public void OwnerRemovesAChildAtOnce()
    {
        var result = Remove(SheetData.Full(), SheetData.Robin);

        Assert.True(result.Accepted);
        Assert.Equal("Robin", result.Removed!.Name);
        Assert.DoesNotContain("Robin", Names(result.Document));
        Assert.True(result.Document.Find(new RecordKey(ConstitutionKinds.Person, "robin"))!.IsTombstone);
    }

    [Fact]
    public void ThePartnerCannotRemove()
    {
        var before = SheetData.Full();

        var result = Remove(before, SheetData.Robin, Partner);

        Assert.False(result.Accepted);
        Assert.Equal(RemovalRefusalReason.PartnerInstallIsReadOnly, result.Refusal!.Reason);
        Assert.Equal(RosterEditor.PartnerReadOnlySentence, result.Refusal.Sentence);
        Assert.Same(before, result.Document);
    }

    [Fact]
    public void TheLastAdultStays()
    {
        var before = SheetData.OneAdult();

        var result = Remove(before, SheetData.Sam);

        Assert.Equal(RemovalRefusalReason.LastAdult, result.Refusal!.Reason);
        Assert.Contains("Sam", result.Refusal.Sentence);
        Assert.Equal(RosterRemoval.LastAdultSentence("Sam"), result.Refusal.Sentence);
        Assert.Same(before, result.Document);
    }

    [Fact]
    public void OneOfTwoAdultsMayGo()
    {
        var result = Remove(SheetData.Full(), SheetData.Lee);

        Assert.True(result.Accepted);
        Assert.Equal(3, Names(result.Document).Count());
        Assert.DoesNotContain("Lee", Names(result.Document));
    }

    [Fact]
    public void ALastAdultAmongUnreadableRecordsStillStays()
    {
        var broken = SheetData.OneAdult().With(HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Person, "odd"), RecordSchema.Version,
            new Dictionary<string, string> { ["person"] = "odd", ["name"] = "Odd", ["role"] = "newer-word" },
            RosterData.At(1500), RosterData.PartnerWriter));

        var result = Remove(broken, SheetData.Sam);

        Assert.Equal(RemovalRefusalReason.LastAdult, result.Refusal!.Reason);
    }

    [Fact]
    public void SomeoneNotThereIsSaidSo()
    {
        var before = SheetData.Full();

        var result = Remove(before, new PersonId("nobody"));

        Assert.Equal(RemovalRefusalReason.NotOnTheRoster, result.Refusal!.Reason);
        Assert.Equal(RosterRemoval.NotOnTheRosterSentence, result.Refusal.Sentence);
        Assert.Same(before, result.Document);
    }

    [Fact]
    public void ARemovedChildTakesTheirThingsWithThem()
    {
        var before = SheetData.Full();

        var after = Remove(before, SheetData.Robin).Document;

        foreach (var key in new[] { SheetData.RobinsRating, SheetData.RobinsNote, SheetData.RobinsAnswer })
        {
            Assert.True(after.Find(key)!.IsTombstone, key.Value);
        }

        Assert.NotNull(after.Live(SheetData.RatingKey("sam")));
        Assert.NotNull(after.Live(SheetData.RatingKey("lee")));
        Assert.Equal(before.Live(new RecordKey(ConstitutionKinds.Person, "juniper"))!.Stamp, after.Live(new RecordKey(ConstitutionKinds.Person, "juniper"))!.Stamp);
    }

    [Fact]
    public void ARemovedAdultKeepsTheirRecords()
    {
        var after = Remove(SheetData.Full(), SheetData.Lee).Document;

        Assert.NotNull(after.Live(SheetData.RatingKey("lee")));
    }

    [Fact]
    public void RemovingNeverTouchesTheLease()
    {
        var before = SheetData.Full();
        var now = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        var after = Remove(before, SheetData.Lee).Document;

        var kept = after.Records.Where(record => record.Key.Kind == ConstitutionKinds.PlanningLease).ToList();
        var was = before.Records.Where(record => record.Key.Kind == ConstitutionKinds.PlanningLease).ToList();
        Assert.NotEmpty(was);
        Assert.Equal(was.Count, kept.Count);
        Assert.All(was, record => Assert.True(record.ContentEquals(after.Find(record.Key)!)));
        Assert.Equal(
            PlanningLease.Decide(before, "install-lee", RosterRole.Partner, now, TimeZoneInfo.Utc, LeaseTerms.DefaultLeaseLength),
            PlanningLease.Decide(after, "install-lee", RosterRole.Partner, now, TimeZoneInfo.Utc, LeaseTerms.DefaultLeaseLength));
    }

    [Fact]
    public void AnOlderCopyCannotBringThemBackOnAMerge()
    {
        var before = SheetData.Full();
        var after = Remove(before, SheetData.Robin).Document;

        Assert.DoesNotContain("Robin", Names(HouseholdMerge.Merge(after, before)));
        Assert.DoesNotContain("Robin", Names(HouseholdMerge.Merge(before, after)));
        Assert.True(HouseholdMerge.Merge(before, after).Find(SheetData.RobinsRating)!.IsTombstone);
    }

    [Fact]
    public void UndoWritesANewerRecordNotTheOldStamp()
    {
        var before = SheetData.Full();
        var removed = Remove(before, SheetData.Robin);
        var tombstone = removed.Document.Find(new RecordKey(ConstitutionKinds.Person, "robin"))!;

        var restored = Restore(removed.Document, removed.Removed!);

        Assert.Equal(PersonRestoreOutcome.Restored, restored.Outcome);
        var record = restored.Document.Live(new RecordKey(ConstitutionKinds.Person, "robin"))!;
        Assert.True(record.Stamp.CompareTo(tombstone.Stamp) > 0);
        Assert.Contains("Robin", Names(restored.Document));
    }

    [Fact]
    public void UndoWinsTheMergeInEitherOrder()
    {
        var removed = Remove(SheetData.Full(), SheetData.Robin);
        var restored = Restore(removed.Document, removed.Removed!).Document;

        Assert.Contains("Robin", Names(HouseholdMerge.Merge(restored, removed.Document)));
        Assert.Contains("Robin", Names(HouseholdMerge.Merge(removed.Document, restored)));
    }

    [Fact]
    public void UndoBringsBackTheBirthdateAndTheThingsTied()
    {
        var removed = Remove(SheetData.Full(), SheetData.Robin);

        var restored = Restore(removed.Document, removed.Removed!).Document;

        Assert.Equal(new Birthdate(new DateOnly(1900, 3, 3)), SheetData.People(restored).Single(person => person.Id == SheetData.Robin).Birthdate);
        foreach (var key in new[] { SheetData.RobinsRating, SheetData.RobinsNote, SheetData.RobinsAnswer })
        {
            Assert.NotNull(restored.Live(key));
        }
    }

    [Fact]
    public void UndoKeepsAFieldOnlyANewerWriterKnew()
    {
        var newer = HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Person, "robin"), 7,
            new Dictionary<string, string> { ["person"] = "robin", ["name"] = "Robin", ["role"] = "child", ["nickname"] = "kept" },
            RosterData.At(3000), RosterData.PartnerWriter);
        var removed = Remove(SheetData.OneAdult().With(newer), SheetData.Robin);

        var restored = Restore(removed.Document, removed.Removed!).Document;

        Assert.Equal("kept", restored.Live(newer.Key)!.Fields["nickname"]);
        Assert.Equal(7, restored.Live(newer.Key)!.SchemaVersion);
    }

    [Fact]
    public void UndoLeavesAPersonAddedAgainAlone()
    {
        var removed = Remove(SheetData.Full(), SheetData.Robin);
        var again = Owner.Save(
            removed.Document,
            new RosterPerson(SheetData.Robin, "Robin Again", MemberRole.Child, null, false, null),
            SheetData.After(removed.Document), RosterData.OwnerWriter).Document;

        var result = Restore(again, removed.Removed!);

        Assert.Equal(PersonRestoreOutcome.AlreadyBack, result.Outcome);
        Assert.Same(again, result.Document);
    }

    [Fact]
    public void ThePartnerCannotUndoEither()
    {
        var removed = Remove(SheetData.Full(), SheetData.Robin);

        var result = Restore(removed.Document, removed.Removed!, Partner);

        Assert.Equal(PersonRestoreOutcome.Refused, result.Outcome);
        Assert.Same(removed.Document, result.Document);
    }

    [Fact]
    public void TheHandleNamesNoBirthdate()
    {
        var removed = Remove(SheetData.Full(), SheetData.Robin).Removed!;

        Assert.DoesNotContain("1900", removed.ToString());
        Assert.DoesNotContain("1900", string.Join(' ', removed.GetType().GetProperties().Select(property => property.GetValue(removed)?.ToString())));
    }
}
