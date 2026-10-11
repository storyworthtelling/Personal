using Storyworthtelling.Voice;
using DriveData = Google.Apis.Drive.v3.Data;

namespace Storyworthtelling.Voice.Tests;

// The rule DriveTextFile applies when a name search finds more than one non-trashed file with a well-known
// name: an owned copy beats a shared one, the most recently modified wins within each group, and the
// duplicates are reported by count only. Ids and dates here are invented.
public class DriveFileChooserTests
{
    private static readonly DateTimeOffset Older = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Newer = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingFoundChoosesNothing()
    {
        var choice = DriveFileChooser.Choose([]);

        Assert.Null(choice.Id);
        Assert.Equal(DriveFileOwnership.None, choice.Ownership);
        Assert.Equal(0, choice.SameNameCount);
        Assert.Null(DriveFileChooser.Notice(choice));
    }

    [Fact]
    public void ASingleOwnedFileIsChosenWithNoNotice()
    {
        var choice = DriveFileChooser.Choose([new DriveFileCandidate("file-owned", true, Older)]);

        Assert.Equal("file-owned", choice.Id);
        Assert.Equal(DriveFileOwnership.Owned, choice.Ownership);
        Assert.Null(DriveFileChooser.Notice(choice));
    }

    [Fact]
    public void AnOwnedCopyBeatsANewerSharedOne()
    {
        // The shared copy is listed first and is newer — the order Drive's modifiedTime-desc listing returns.
        var choice = DriveFileChooser.Choose(
        [
            new DriveFileCandidate("file-shared", false, Newer),
            new DriveFileCandidate("file-owned", true, Older),
        ]);

        Assert.Equal("file-owned", choice.Id);
        Assert.Equal(DriveFileOwnership.Owned, choice.Ownership);
        Assert.Equal(2, choice.SameNameCount);
    }

    [Fact]
    public void AmongOwnedCopiesTheMostRecentlyModifiedWinsWhateverTheListingOrder()
    {
        var choice = DriveFileChooser.Choose(
        [
            new DriveFileCandidate("file-owned-older", true, Older),
            new DriveFileCandidate("file-owned-newer", true, Newer),
        ]);

        Assert.Equal("file-owned-newer", choice.Id);
        Assert.Equal(DriveFileOwnership.Owned, choice.Ownership);
    }

    [Fact]
    public void WithNoneOwnedTheMostRecentSharedCopyIsChosenAndReportedAsShared()
    {
        var choice = DriveFileChooser.Choose(
        [
            new DriveFileCandidate("file-shared-older", false, Older),
            new DriveFileCandidate("file-shared-newer", false, Newer),
        ]);

        Assert.Equal("file-shared-newer", choice.Id);
        Assert.Equal(DriveFileOwnership.Shared, choice.Ownership);
        Assert.Equal(2, choice.SameNameCount);
    }

    [Fact]
    public void AMissingModifiedTimeSortsBehindAKnownOne()
    {
        var choice = DriveFileChooser.Choose(
        [
            new DriveFileCandidate("file-undated", true, null),
            new DriveFileCandidate("file-dated", true, Older),
        ]);

        Assert.Equal("file-dated", choice.Id);
    }

    [Fact]
    public void ATieIsBrokenByIdSoTheChoiceNeverFlipsBetweenRuns()
    {
        var forward = DriveFileChooser.Choose(
        [
            new DriveFileCandidate("file-b", true, Older),
            new DriveFileCandidate("file-a", true, Older),
        ]);
        var reversed = DriveFileChooser.Choose(
        [
            new DriveFileCandidate("file-a", true, Older),
            new DriveFileCandidate("file-b", true, Older),
        ]);

        Assert.Equal(forward.Id, reversed.Id);
    }

    [Fact]
    public void DuplicatesAreReportedByCountAndOwnershipOnlyNeverById()
    {
        var choice = DriveFileChooser.Choose(
        [
            new DriveFileCandidate("file-owned-older", true, Older),
            new DriveFileCandidate("file-owned-newer", true, Newer),
            new DriveFileCandidate("file-shared", false, Newer),
        ]);

        var notice = DriveFileChooser.Notice(choice);

        Assert.NotNull(notice);
        Assert.Contains("3", notice);
        Assert.Contains("owns", notice);
        Assert.DoesNotContain("file-", notice);
    }

    [Fact]
    public void ASingleSharedCopyIsStillReportedSoAReaderKnowsItIsNotItsOwn()
    {
        var notice = DriveFileChooser.Notice(
            DriveFileChooser.Choose([new DriveFileCandidate("file-shared", false, Older)]));

        Assert.NotNull(notice);
        Assert.Contains("shared", notice);
        Assert.DoesNotContain("file-", notice);
    }

    [Fact]
    public void FromDriveReadsOwnershipAndModifiedTimeFromTheListResult()
    {
        // A fake files.list result: the shape Google.Apis.Drive.v3 deserialises.
        var files = new List<DriveData.File>
        {
            new() { Id = "file-shared", OwnedByMe = false, ModifiedTimeDateTimeOffset = Newer },
            new() { Id = "file-owned", OwnedByMe = true, ModifiedTimeDateTimeOffset = Older },
            // ownedByMe is not populated for items in shared drives — that is not ownership.
            new() { Id = "file-shared-drive", OwnedByMe = null, ModifiedTimeDateTimeOffset = Newer },
        };

        var candidates = DriveFileChooser.FromDrive(files);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(new DriveFileCandidate("file-shared", false, Newer), candidates[0]);
        Assert.Equal(new DriveFileCandidate("file-owned", true, Older), candidates[1]);
        Assert.False(candidates[2].OwnedByMe);
        Assert.Equal("file-owned", DriveFileChooser.Choose(candidates).Id);
    }

    [Fact]
    public void TheListRequestAsksForTheFieldsTheRuleNeeds()
    {
        Assert.Contains("ownedByMe", DriveFileChooser.ListFields);
        Assert.Contains("modifiedTime", DriveFileChooser.ListFields);
        Assert.Contains("id", DriveFileChooser.ListFields);
    }

    [Fact]
    public void TheNameQueryEscapesASingleQuote()
    {
        Assert.Equal(
            @"name='an example\'s file.json' and trashed=false",
            DriveFileChooser.NameQuery("an example's file.json"));
        Assert.Equal(
            "name='example-store.json' and trashed=false",
            DriveFileChooser.NameQuery("example-store.json"));
    }
}
