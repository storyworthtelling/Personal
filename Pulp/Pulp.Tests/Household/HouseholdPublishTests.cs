using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>
/// Finding §2.7: Drive v3 has no conditional write, so two phones can race between read and write.
/// The publisher reads, merges, writes, re-reads and re-merges once; a write that still loses stays
/// on its writer's phone and lands on the next pass. These tests drive that plan against an
/// in-memory file whose race is injected by hand.
/// </summary>
public class HouseholdPublishTests
{
    private sealed class FakeFile
    {
        public HouseholdDocument Content { get; set; } = HouseholdDocument.Empty;

        public int Writes { get; private set; }

        public Action? BeforeNextWrite { get; set; }

        public void Write(HouseholdDocument document)
        {
            var race = BeforeNextWrite;
            BeforeNextWrite = null;
            race?.Invoke();

            // Drive overwrites: whatever another writer put there in between is simply replaced.
            Content = document;
            Writes++;
        }
    }

    private static HouseholdDocument One(string kind, string id, long wall, string writer) =>
        HouseholdDocument.Empty.With(HouseholdRecord.Write(
            new RecordKey(kind, id),
            1,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["by"] = writer },
            new HlcTimestamp(wall, 0),
            writer));

    /// <summary>The cycle as the store will run it: read → merge → write → reread → maybe once more.</summary>
    private static (HouseholdDocument Local, PublishAction Outcome) Publish(HouseholdDocument local, FakeFile file)
    {
        var writes = 0;
        var toWrite = HouseholdPublish.FirstWrite(local, file.Content);
        while (true)
        {
            file.Write(toWrite);
            writes++;
            var step = HouseholdPublish.AfterReread(local, file.Content, writes);
            local = step.Document;
            if (step.Action != PublishAction.WriteAgain)
            {
                return (local, step.Action);
            }

            toWrite = step.Document;
        }
    }

    [Fact]
    public void AQuietPublishWritesOnceAndIsDone()
    {
        var file = new FakeFile { Content = One("playlist", "film-club", 100, "phone-b") };
        var local = One("playlist", "rainy-day", 200, "phone-a");

        var (after, outcome) = Publish(local, file);

        Assert.Equal(PublishAction.Done, outcome);
        Assert.Equal(1, file.Writes);
        Assert.Equal(2, file.Content.Records.Count);
        Assert.True(after.ContentEquals(file.Content));
    }

    [Fact]
    public void AWriteOverwrittenByARaceIsNoticedOnTheRereadAndWrittenAgain()
    {
        var file = new FakeFile();
        var mine = One("playlist", "rainy-day", 200, "phone-a");
        var theirs = One("playlist", "film-club", 210, "phone-b");

        // Phone B reads the same empty file and writes just after A does, wiping A's write.
        var bRead = file.Content;
        var aWrite = HouseholdPublish.FirstWrite(mine, file.Content);
        file.Write(aWrite);
        file.Write(HouseholdPublish.FirstWrite(theirs, bRead));

        var step = HouseholdPublish.AfterReread(mine, file.Content, writesSoFar: 1);

        Assert.Equal(PublishAction.WriteAgain, step.Action);
        Assert.Equal(2, step.Document.Records.Count);
    }

    [Fact]
    public void TwoPhonesRacingBothEndUpInTheFile()
    {
        var file = new FakeFile();
        var phoneA = One("playlist", "rainy-day", 200, "phone-a");
        var phoneB = One("favorite", "film-4", 205, "phone-b");

        // B's whole first write lands between A's read and A's write; A's write then erases it.
        var bLocal = phoneB;
        file.BeforeNextWrite = () => file.Write(HouseholdPublish.FirstWrite(bLocal, file.Content));

        var (aAfter, aOutcome) = Publish(phoneA, file);
        var (bAfter, bOutcome) = Publish(bLocal, file);

        Assert.Equal(PublishAction.Done, aOutcome);
        Assert.Equal(PublishAction.Done, bOutcome);
        Assert.NotNull(file.Content.Live(new RecordKey("playlist", "rainy-day")));
        Assert.NotNull(file.Content.Live(new RecordKey("favorite", "film-4")));
        Assert.True(bAfter.ContentEquals(file.Content));
        Assert.Equal(1, aAfter.Records.Count(record => record.Writer == "phone-a"));
    }

    [Fact]
    public void AfterTheSecondWriteAStillLostEditWaitsForTheNextPassRatherThanLooping()
    {
        var mine = One("playlist", "rainy-day", 200, "phone-a");
        var reread = One("playlist", "film-club", 300, "phone-b");

        var step = HouseholdPublish.AfterReread(mine, reread, writesSoFar: HouseholdPublish.MaxWrites);

        Assert.Equal(PublishAction.LeaveForNextPass, step.Action);
        // The phone keeps its own edit alongside what it read, so the next pass carries it.
        Assert.NotNull(step.Document.Live(new RecordKey("playlist", "rainy-day")));
        Assert.NotNull(step.Document.Live(new RecordKey("playlist", "film-club")));
    }

    [Fact]
    public void ANewerEditInTheFileIsAdoptedNotFoughtOver()
    {
        var mine = One("playlist", "rainy-day", 200, "phone-a");
        var newer = One("playlist", "rainy-day", 900, "phone-b");

        var step = HouseholdPublish.AfterReread(mine, newer, writesSoFar: 1);

        Assert.Equal(PublishAction.Done, step.Action);
        Assert.Equal("phone-b", step.Document.Live(new RecordKey("playlist", "rainy-day"))!.Writer);
    }

    [Fact]
    public void TheCycleWritesAtMostTwice() => Assert.Equal(2, HouseholdPublish.MaxWrites);
}
