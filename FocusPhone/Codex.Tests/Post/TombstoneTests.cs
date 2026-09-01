using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The load-bearing invariant of the whole phase.
///
/// Filing means: delete at the source, keep a ghost here. That directly contradicts how
/// reconcile works — it mirrors the source, so anything the source still has comes back.
/// Ghosts and mirroring cannot both be naive, and getting this wrong does not look like a
/// bug: a filed message simply reappears in the Mailbox, over and over, and the owner
/// concludes filing does not work.
///
/// A tombstone is the resolution, and it is deliberately TEMPORARY — it exists exactly as
/// long as Post and the source disagree about whether something is gone.
/// </summary>
public class TombstoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private static Tombstones Filed(params string[] ids)
    {
        var book = new Tombstones();
        foreach (var id in ids)
        {
            book.Mark(id, Now);
        }
        return book;
    }

    // Step one: the item goes at once. Waiting for the source to confirm would make filing
    // feel broken on a slow network, which is most of the time on a phone.
    [Fact]
    public void FilingHidesTheItemImmediately()
    {
        Assert.True(Filed("m1").IsFiled("m1"));
    }

    [Fact]
    public void SomethingNeverFiledIsNotHidden()
    {
        Assert.False(Filed("m1").IsFiled("m2"));
    }

    // Step three, and the reason any of this exists: reconcile mirrors the source EXCEPT
    // where a tombstone says the owner filed it. Without this the source's copy — which is
    // still there, because the delete has not landed yet — walks straight back in.
    [Fact]
    public void ReconcileDoesNotResurrectAFiledItem()
    {
        var book = Filed("m1");

        var kept = book.Reconcile(["m1", "m2"]);

        Assert.Equal(["m2"], kept);
    }

    // Step four: once the source confirms, the disagreement is over. The tombstone goes,
    // because reconcile can no longer bring the item back — the source does not have it.
    [Fact]
    public void AConfirmedDeleteDropsTheTombstone()
    {
        var book = Filed("m1");

        book.SourceConfirmed("m1");

        Assert.False(book.IsFiled("m1"));
        Assert.Empty(book.Outstanding());
    }

    // And having dropped it, reconcile must still not bring the item back — because the
    // source no longer offers it. This is the test that says the tombstone was temporary
    // rather than load-bearing forever.
    [Fact]
    public void AfterConfirmationTheSourceNoLongerOffersIt()
    {
        var book = Filed("m1");
        book.SourceConfirmed("m1");

        Assert.Empty(book.Reconcile([]));
    }

    // Step two: the delete is retried while the tombstone stands. Anything still
    // outstanding is work to redo on the next sync.
    [Fact]
    public void AnUnconfirmedDeleteStaysOutstanding()
    {
        Assert.Equal(["m1"], Filed("m1").Outstanding());
    }

    [Fact]
    public void ConfirmedDeletesAreNotRetried()
    {
        var book = Filed("m1", "m2");
        book.SourceConfirmed("m1");

        Assert.Equal(["m2"], book.Outstanding());
    }

    // A source delete that keeps failing raises a dog-ear, but the item STAYS filed
    // regardless: the owner's intent stands whatever the server does. The retry budget is
    // about when to stop being quiet, never about giving the message back.
    [Fact]
    public void GivingUpOnTheSourceDoesNotUnfileTheItem()
    {
        var book = Filed("m1");

        for (var attempt = 0; attempt < Tombstones.QuietAttempts + 3; attempt++)
        {
            book.AttemptFailed("m1", Now);
        }

        Assert.True(book.IsFiled("m1"));
        Assert.Empty(book.Reconcile(["m1"]));   // still suppressed, however loudly it failed
        Assert.True(book.ShouldSpeak("m1"));
    }

    // Until the budget is spent it stays quiet: a phone loses its network constantly, and
    // a fold for every blip is a fold nobody reads.
    [Fact]
    public void OneFailedAttemptSaysNothing()
    {
        var book = Filed("m1");
        book.AttemptFailed("m1", Now);

        Assert.False(book.ShouldSpeak("m1"));
    }

    // Filing something twice is not two tombstones, and must not reset a retry count that
    // is close to giving up.
    [Fact]
    public void FilingTwiceIsIdempotent()
    {
        var book = Filed("m1");
        book.AttemptFailed("m1", Now);
        book.AttemptFailed("m1", Now);
        book.Mark("m1", Now);

        Assert.Single(book.Outstanding());
        Assert.Equal(2, book.FailedAttempts("m1"));
    }
}
