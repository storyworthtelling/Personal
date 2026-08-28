using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class VetoLedgerTests
{
    private static readonly TasteProfile Profile = new(
        "Reaches for guitars.", ["late-night ambient"], [], ["stadium country"], [], [], [], false, 12);

    private static CratePick P(string title, string artist) => new(title, title, artist);

    [Fact]
    public void Fold_RecordsEachVeto()
    {
        var folded = VetoLedger.Fold(Profile, [P("Some Song", "A Band")]);

        Assert.Contains("passed on Some Song by A Band", folded.Avoids);
    }

    // The profile is Claude's read of the owner, and a veto is a note in the margin
    // of it — never a replacement for it.
    [Fact]
    public void Fold_KeepsWhatTheProfileAlreadyAvoids()
    {
        var folded = VetoLedger.Fold(Profile, [P("Some Song", "A Band")]);

        Assert.Contains("stadium country", folded.Avoids);
    }

    [Fact]
    public void Fold_LeavesTheRestOfTheProfileAlone()
    {
        var folded = VetoLedger.Fold(Profile, [P("Some Song", "A Band")]);

        Assert.Equal(Profile with { Avoids = folded.Avoids }, folded);
    }

    [Fact]
    public void Fold_DoesNotRecordTheSameVetoTwice()
    {
        var once = VetoLedger.Fold(Profile, [P("Some Song", "A Band")]);
        var twice = VetoLedger.Fold(once, [P("some song", "a band")]);

        Assert.Single(twice.Avoids.Where(line => line.StartsWith("passed on", StringComparison.Ordinal)));
    }

    // Batched weekly, a veto list would otherwise grow without end and crowd out the
    // profile it is meant to annotate.
    [Fact]
    public void Fold_KeepsOnlyTheMostRecentVetoes()
    {
        var many = Enumerable.Range(0, VetoLedger.MaxRecorded + 5)
            .Select(i => P($"Song {i}", "A Band"))
            .ToList();

        var folded = VetoLedger.Fold(Profile, many);

        var recorded = folded.Avoids.Where(line => line.StartsWith("passed on", StringComparison.Ordinal)).ToList();
        Assert.Equal(VetoLedger.MaxRecorded, recorded.Count);
        Assert.Contains($"passed on Song {many.Count - 1} by A Band", recorded);
    }

    // Trimming must only ever drop vetoes. What Claude wrote is not ours to age out.
    [Fact]
    public void Fold_NeverTrimsTheProfilesOwnAvoids()
    {
        var many = Enumerable.Range(0, VetoLedger.MaxRecorded + 5)
            .Select(i => P($"Song {i}", "A Band"))
            .ToList();

        var folded = VetoLedger.Fold(Profile, many);

        Assert.Contains("stadium country", folded.Avoids);
    }

    [Fact]
    public void Fold_OnNothingChangesNothing()
    {
        Assert.Equal(Profile, VetoLedger.Fold(Profile, []));
    }
}
