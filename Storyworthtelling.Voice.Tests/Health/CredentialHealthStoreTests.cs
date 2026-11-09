using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Voice.Tests.Health;

/// <summary>
/// One credential health store for every head (audit 01 §14). There were four copies, each a load, modify,
/// save with no lock, while a background worker and parallel clients marked health at the same time, so one
/// credential's mark could overwrite another's. Storage is a seam: MAUI Preferences or SharedPreferences.
/// </summary>
public class CredentialHealthStoreTests
{
    private sealed class MemoryStorage : ICredentialHealthStorage
    {
        private string text = string.Empty;

        public string Read()
        {
            var read = text;
            Thread.Yield(); // widen the window a lost update needs, so the test would see one
            return read;
        }

        public void Write(string report) => text = report;
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AMarkIsReadBack()
    {
        var store = new CredentialHealthStore(new MemoryStorage(), () => Now);

        store.MarkOk(SharedCredentialIds.Claude);
        store.MarkFailing(SharedCredentialIds.Gemini, "API key not valid");

        var report = store.Load();
        Assert.Equal(CredentialStatus.Ok, report.Get(SharedCredentialIds.Claude).Status);
        Assert.Equal(CredentialStatus.Failing, report.Get(SharedCredentialIds.Gemini).Status);
        Assert.Equal(Now, report.Get(SharedCredentialIds.Claude).CheckedAtUtc);
    }

    [Fact]
    public void MarksFromManyThreadsAtOnceAreAllKept()
    {
        var storage = new MemoryStorage();
        var ids = Enumerable.Range(0, 40).Select(index => $"credential-{index}").ToList();

        Parallel.ForEach(ids, new ParallelOptions { MaxDegreeOfParallelism = 8 }, id =>
            new CredentialHealthStore(storage, () => Now).MarkOk(id));

        var report = new CredentialHealthStore(storage, () => Now).Load();
        Assert.All(ids, id => Assert.Equal(CredentialStatus.Ok, report.Get(id).Status));
    }

    [Fact]
    public void TheSharedIdsAreTheOnesEveryAppAlreadyStores()
    {
        // Wire names: a stored report keyed by one of these is read back under the same word.
        Assert.Equal("claude", SharedCredentialIds.Claude);
        Assert.Equal("gemini", SharedCredentialIds.Gemini);
        Assert.Equal("remarkable", SharedCredentialIds.Remarkable);
        Assert.Equal("github-update-token", SharedCredentialIds.GitHubUpdateToken);
    }
}
