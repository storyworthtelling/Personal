using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

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
    public void Faster_StepsUpByOneTenth()
    {
        Assert.Equal(1.5f, new PlaybackSettings(1.4f).Faster().Speed);
    }

    [Fact]
    public void Slower_StepsDownByOneTenth()
    {
        Assert.Equal(1.3f, new PlaybackSettings(1.4f).Slower().Speed);
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
