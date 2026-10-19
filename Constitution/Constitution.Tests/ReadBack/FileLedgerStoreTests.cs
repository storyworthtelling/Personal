using Constitution.Domain.Ink;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>The skip ledger's file: read and written whole, atomically, and never written over when it cannot be read.</summary>
public sealed class FileLedgerStoreTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-ledger-");

    private string PathOf(string file = "ledger.json") => Path.Combine(folder.FullName, "nested", file);

    public void Dispose() => folder.Delete(recursive: true);

    private static SkipLedger Settled()
    {
        var marks = new SessionMarks("Juniper", new DateOnly(2026, 11, 3), SessionKind.Strength, ["done"]);
        return SkipLedger.Empty.Settle(marks, [new InkResolution.Ticked("done")]);
    }

    [Fact]
    public void NothingStoredIsAnEmptyLedgerWithNoProblem()
    {
        var read = new FileLedgerStore(PathOf()).Load();

        Assert.Empty(read.Ledger.Records);
        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(0, read.UnreadableEntries);
    }

    [Fact]
    public void WhatWasSavedIsWhatIsLoaded()
    {
        var store = new FileLedgerStore(PathOf());

        store.Save(Settled());

        var again = new FileLedgerStore(PathOf()).Load();
        Assert.Equal(SessionOutcome.Done, again.Ledger.OutcomeOf("Juniper", new DateOnly(2026, 11, 3)));
        Assert.False(File.Exists(PathOf() + ".tmp"));
    }

    [Fact]
    public void AnUnreadableLedgerIsLeftAlone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "this is not json");
        var store = new FileLedgerStore(PathOf());

        var read = store.Load();

        Assert.True(read.WholeFileUnreadable);
        Assert.Equal("this is not json", File.ReadAllText(PathOf()));
    }

    [Fact]
    public void SavingReplacesTheWholeLedger()
    {
        var store = new FileLedgerStore(PathOf());
        store.Save(Settled());

        store.Save(SkipLedger.Empty);

        Assert.Empty(store.Load().Ledger.Records);
    }

    [Fact]
    public void ABlankPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new FileLedgerStore(" "));
    }
}
