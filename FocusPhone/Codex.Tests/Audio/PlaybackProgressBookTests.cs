using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class PlaybackProgressBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resume_ReturnsRecordedPositionForInProgressItem()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), Now);

        Assert.Equal(TimeSpan.FromMinutes(5), book.Resume("ep"));
    }

    [Fact]
    public void Resume_ReturnsNullForUnknownItem()
    {
        Assert.Null(new PlaybackProgressBook().Resume("nope"));
    }

    [Fact]
    public void Resume_ReturnsNullOnceComplete()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(40), Now);

        Assert.True(book.IsComplete("ep"));
        Assert.Null(book.Resume("ep"));
    }

    [Fact]
    public void IsComplete_TrueWithinFinalThirtySeconds()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(39.6), TimeSpan.FromMinutes(40), Now);

        Assert.True(book.IsComplete("ep"));
    }

    [Fact]
    public void IsComplete_FalseWellBeforeEnd()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(40), Now);

        Assert.False(book.IsComplete("ep"));
    }

    [Fact]
    public void ResumeWithRewind_NoRewindWhenJustAway()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), Now);

        var resumed = book.ResumeWithRewind("ep", Now.AddSeconds(20));

        Assert.Equal(TimeSpan.FromMinutes(5), resumed);
    }

    [Fact]
    public void ResumeWithRewind_TenSecondsWithinTheHour()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), Now);

        var resumed = book.ResumeWithRewind("ep", Now.AddMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(10), resumed);
    }

    [Fact]
    public void ResumeWithRewind_FifteenSecondsAfterAnHour()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), Now);

        var resumed = book.ResumeWithRewind("ep", Now.AddDays(1));

        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(15), resumed);
    }

    [Fact]
    public void ResumeWithRewind_NeverGoesNegative()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(40), Now);

        var resumed = book.ResumeWithRewind("ep", Now.AddDays(1));

        Assert.Equal(TimeSpan.Zero, resumed);
    }

    [Fact]
    public void ImportListened_MarksLegacyIdsComplete()
    {
        var book = new PlaybackProgressBook();

        book.ImportListened(["old1", "old2"], Now);

        Assert.True(book.IsComplete("old1"));
        Assert.True(book.IsComplete("old2"));
        Assert.Null(book.Resume("old1"));
    }

    [Fact]
    public void ImportListened_DoesNotOverwriteExistingProgress()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), Now);

        book.ImportListened(["ep"], Now);

        Assert.Equal(TimeSpan.FromMinutes(5), book.Resume("ep"));
    }

    [Fact]
    public void Progress_SurvivesExportAndRestore()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), Now);

        var restored = new PlaybackProgressBook(book.Export());

        Assert.Equal(TimeSpan.FromMinutes(5), restored.Resume("ep"));
    }
}
