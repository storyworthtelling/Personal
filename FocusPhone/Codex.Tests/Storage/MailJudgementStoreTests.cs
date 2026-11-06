using Codex.Domain.Post;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Codex.Tests.Storage;

/// <summary>
/// What each email was judged to be, kept apart from the timeline cache. Aged-out mail is
/// deleted from the cache but is still at the source, and every reconcile re-fetches thirty
/// days of it — after every file and delete. Without this record each re-fetched message came
/// back unsorted and would be sent to Claude again, and billed again, on every one of those.
/// </summary>
public class MailJudgementStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection;
    private readonly MailJudgementStore store;

    public MailJudgementStoreTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        store = new MailJudgementStore(connection);
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public void AJudgementSurvivesAReopen()
    {
        store.Remember("m1", MailClass.Marketing, Now);

        Assert.Equal(MailClass.Marketing, new MailJudgementStore(connection).Find("m1"));
        Assert.Null(store.Find("m2"));
    }

    // The first judgement stands: the owner's corrections are applied over it at read time,
    // never written into it.
    [Fact]
    public void TheFirstJudgementStands()
    {
        store.Remember("m1", MailClass.Newsletter, Now);
        store.Remember("m1", MailClass.Human, Now.AddHours(1));

        Assert.Equal(MailClass.Newsletter, store.Find("m1"));
    }

    // Kept a while past the reconcile window, so nothing still re-fetched is forgotten.
    [Fact]
    public void JudgementsOlderThanTheKeepAreForgotten()
    {
        store.Remember("old", MailClass.Marketing, Now.AddDays(-61));
        store.Remember("recent", MailClass.Marketing, Now.AddDays(-59));

        store.ForgetBefore(Now - MailJudgementStore.Keep);

        Assert.Null(store.Find("old"));
        Assert.Equal(MailClass.Marketing, store.Find("recent"));
    }
}
