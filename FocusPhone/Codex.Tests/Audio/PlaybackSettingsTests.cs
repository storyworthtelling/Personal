using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class PlaybackSettingsTests
{
    [Fact]
    public void DefaultSpeedIsNormal()
    {
        Assert.Equal(1.0f, new PlaybackSettings().Speed);
    }

    [Fact]
    public void WithSpeed_SetsAChosenSpeed()
    {
        Assert.Equal(1.5f, new PlaybackSettings().WithSpeed(1.5f).Speed);
    }

    [Fact]
    public void WithSpeed_ClampsBelowMinimum()
    {
        Assert.Equal(PlaybackSettings.MinSpeed, new PlaybackSettings().WithSpeed(0.1f).Speed);
    }

    [Fact]
    public void WithSpeed_ClampsAboveMaximum()
    {
        Assert.Equal(PlaybackSettings.MaxSpeed, new PlaybackSettings().WithSpeed(9.0f).Speed);
    }

    [Fact]
    public void Toggles_DefaultOff()
    {
        var settings = new PlaybackSettings();

        Assert.False(settings.SkipSilence);
        Assert.False(settings.NormalizeLoudness);
    }

    [Fact]
    public void Presets_AreTheExpectedButtons()
    {
        Assert.Equal([1.0f, 1.2f, 1.5f, 2.0f], PlaybackSettings.Presets);
    }
}
