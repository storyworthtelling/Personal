using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class PodcastPlaybackDiagnosticsTests
{
    [Fact]
    public void ControllerNotReady_TellsTheUserToRetry()
    {
        Assert.Contains("moment", PodcastPlaybackDiagnostics.ControllerNotReady, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoPlayableAudio_NamesTheEpisode()
    {
        var message = PodcastPlaybackDiagnostics.NoPlayableAudio("The Pilot");

        Assert.Contains("The Pilot", message);
    }

    [Fact]
    public void NoPlayableAudio_FallsBackWhenTitleMissing()
    {
        var message = PodcastPlaybackDiagnostics.NoPlayableAudio(string.Empty);

        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void NothingToQueue_WhileRefreshing_SaysStillFetching()
    {
        Assert.Contains("fetching", PodcastPlaybackDiagnostics.NothingToQueue(isRefreshing: true),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NothingToQueue_WhenIdle_SaysNoUnplayedEpisodes()
    {
        var message = PodcastPlaybackDiagnostics.NothingToQueue(isRefreshing: false);

        Assert.Contains("unplayed", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetching", message, StringComparison.OrdinalIgnoreCase);
    }
}
