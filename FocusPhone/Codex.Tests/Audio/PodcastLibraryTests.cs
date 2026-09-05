using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class PodcastLibraryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static Episode Ep(string id, int daysAfterT0) =>
        new(id, FeedId: "feed1", Title: $"Episode {id}", PublishedUtc: T0.AddDays(daysAfterT0));

    [Fact]
    public void DefaultView_ShowsUnlistenedEpisodes()
    {
        var library = new PodcastLibrary([Ep("a", 0), Ep("b", 1)]);

        Assert.Equal(2, library.GetDefaultView().Count);
    }

    [Fact]
    public void DefaultView_ExcludesListenedEpisodes()
    {
        var library = new PodcastLibrary([Ep("a", 0), Ep("b", 1)]);

        library.MarkListened("a");

        var view = library.GetDefaultView();
        Assert.Equal("b", Assert.Single(view).Id);
    }

    [Fact]
    public void DefaultView_OrdersNewestFirst()
    {
        var library = new PodcastLibrary([Ep("old", 0), Ep("new", 10), Ep("mid", 5)]);

        Assert.Equal(["new", "mid", "old"], library.GetDefaultView().Select(e => e.Id).ToArray());
    }

    [Fact]
    public void MarkUnlistened_RestoresEpisodeToDefaultView()
    {
        var library = new PodcastLibrary([Ep("a", 0)]);
        library.MarkListened("a");

        library.MarkUnlistened("a");

        Assert.Single(library.GetDefaultView());
    }

    [Fact]
    public void ListenedState_SurvivesExportAndRestore()
    {
        var library = new PodcastLibrary([Ep("a", 0), Ep("b", 1)]);
        library.MarkListened("a");

        var restored = new PodcastLibrary([Ep("a", 0), Ep("b", 1)], library.ExportListenedIds());

        Assert.Equal("b", Assert.Single(restored.GetDefaultView()).Id);
    }

    [Fact]
    public void AllEpisodesView_IncludesListened_NewestFirst()
    {
        var library = new PodcastLibrary([Ep("a", 0), Ep("b", 1)]);
        library.MarkListened("b");

        Assert.Equal(["b", "a"], library.GetAllEpisodes().Select(e => e.Id).ToArray());
    }
}
