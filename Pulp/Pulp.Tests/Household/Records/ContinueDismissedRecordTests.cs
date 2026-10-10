using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household.Records;

/// <summary>
/// "Remove from Continue watching" as a household record: one per viewer and title, private to that
/// viewer (D97), undone by its tombstone, and removable again after an undo.
/// </summary>
public class ContinueDismissedRecordTests
{
    private static readonly ContinueDismissedEntry RobinsOrchard = new(Robin, new ContinueDismissal(Orchard, Evening));

    [Fact]
    public void ARemovalRoundTrips()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ContinueDismissed, RobinsOrchard, Next(), PhoneA);

        var read = HouseholdRecords.Read(document, HouseholdMaps.ContinueDismissed);

        Assert.Empty(read.Unreadable);
        var back = Assert.Single(read.Items);
        Assert.Equal(RobinsOrchard, back);
        Assert.Equal(Evening.Offset, back.Dismissal.At.Offset);
    }

    [Fact]
    public void AViewerSeesOnlyTheirOwnRemovals()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ContinueDismissed, RobinsOrchard, Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.ContinueDismissed, new ContinueDismissedEntry(Avery, new ContinueDismissal(Lantern, Evening)), Next(), PhoneB);

        var view = HouseholdView.Of(document);

        Assert.Equal([new ContinueDismissal(Orchard, Evening)], view.DismissalsOf(Robin));
        Assert.Equal([new ContinueDismissal(Lantern, Evening)], view.DismissalsOf(Avery));
        Assert.Empty(view.DismissalsOf(Pip));
    }

    [Fact]
    public void TwoViewersRemovingOneTitleAreTwoRecords()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ContinueDismissed, RobinsOrchard, Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.ContinueDismissed, RobinsOrchard with { Viewer = Avery }, Next(), PhoneB);

        Assert.Equal(2, HouseholdRecords.Read(document, HouseholdMaps.ContinueDismissed).Items.Count);
    }

    [Fact]
    public void UndoIsATombstoneThatOutranksTheOtherPhonesCopy()
    {
        var phoneA = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ContinueDismissed, RobinsOrchard, Next(), PhoneA);
        var phoneB = phoneA;
        phoneA = HouseholdRecords.Remove(phoneA, HouseholdMaps.ContinueDismissed, RobinsOrchard, Next(), PhoneA);

        var merged = HouseholdMerge.Merge(phoneB, phoneA);

        Assert.Empty(HouseholdView.Of(merged).DismissalsOf(Robin));
        Assert.True(Assert.Single(merged.Records).IsTombstone);
    }

    [Fact]
    public void ATitleCanBeRemovedAgainAfterAnUndo()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ContinueDismissed, RobinsOrchard, Next(), PhoneA);
        document = HouseholdRecords.Remove(document, HouseholdMaps.ContinueDismissed, RobinsOrchard, Next(), PhoneA);
        var later = RobinsOrchard with { Dismissal = new ContinueDismissal(Orchard, Evening.AddDays(2)) };
        document = HouseholdRecords.Put(document, HouseholdMaps.ContinueDismissed, later, Next(), PhoneA);

        Assert.Equal([later.Dismissal], HouseholdView.Of(document).DismissalsOf(Robin));
    }
}
