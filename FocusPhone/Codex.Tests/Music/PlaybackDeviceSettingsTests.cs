using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PlaybackDeviceSettingsTests
{
    [Fact]
    public void Resolve_UnknownDevice_ReturnsNeutralDefault()
    {
        var setting = new PlaybackDeviceSettings().Resolve("car-bt");
        Assert.Equal("car-bt", setting.DeviceKey);
        Assert.Equal(100, setting.VolumePercent);
        Assert.Null(setting.EqualizerPreset);
        Assert.Equal(1.0f, setting.Speed);
    }

    [Fact]
    public void Set_ThenResolve_ReturnsSaved()
    {
        var settings = new PlaybackDeviceSettings();
        settings.Set(new DevicePlaybackSetting("car-bt", 60, "Bass", 1.0f));
        Assert.Equal(60, settings.Resolve("car-bt").VolumePercent);
        Assert.Equal("Bass", settings.Resolve("car-bt").EqualizerPreset);
    }

    [Fact]
    public void Set_SameDevice_Upserts()
    {
        var settings = new PlaybackDeviceSettings();
        settings.Set(new DevicePlaybackSetting("hp", 40, null, 1.0f));
        settings.Set(new DevicePlaybackSetting("hp", 55, null, 1.25f));
        Assert.Single(settings.Export());
        Assert.Equal(55, settings.Resolve("hp").VolumePercent);
    }

    [Fact]
    public void Codec_RoundTrips()
    {
        var settings = new PlaybackDeviceSettings();
        settings.Set(new DevicePlaybackSetting("hp", 40, "Vocal", 1.5f));
        var decoded = new PlaybackDeviceSettings(PlaybackDeviceSettingsCodec.Decode(
            PlaybackDeviceSettingsCodec.Encode(settings.Export())));
        Assert.Equal("Vocal", decoded.Resolve("hp").EqualizerPreset);
        Assert.Equal(1.5f, decoded.Resolve("hp").Speed);
    }

    [Fact]
    public void Codec_Garbled_ReturnsEmpty() => Assert.Empty(PlaybackDeviceSettingsCodec.Decode("{oops"));
}
