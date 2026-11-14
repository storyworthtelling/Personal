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
    }

    [Fact]
    public void Set_ThenResolve_ReturnsSaved()
    {
        var settings = new PlaybackDeviceSettings();
        settings.Set(new DevicePlaybackSetting("car-bt", 60, "Bass"));
        Assert.Equal(60, settings.Resolve("car-bt").VolumePercent);
        Assert.Equal("Bass", settings.Resolve("car-bt").EqualizerPreset);
    }

    [Fact]
    public void Set_SameDevice_Upserts()
    {
        var settings = new PlaybackDeviceSettings();
        settings.Set(new DevicePlaybackSetting("hp", 40, null));
        settings.Set(new DevicePlaybackSetting("hp", 55, null));
        Assert.Single(settings.Export());
        Assert.Equal(55, settings.Resolve("hp").VolumePercent);
    }

    [Fact]
    public void Codec_RoundTrips()
    {
        var settings = new PlaybackDeviceSettings();
        settings.Set(new DevicePlaybackSetting("hp", 40, "Vocal"));
        var decoded = new PlaybackDeviceSettings(PlaybackDeviceSettingsCodec.Decode(
            PlaybackDeviceSettingsCodec.Encode(settings.Export())));
        Assert.Equal("Vocal", decoded.Resolve("hp").EqualizerPreset);
    }

    [Fact]
    public void Codec_Garbled_ReturnsEmpty() => Assert.Empty(PlaybackDeviceSettingsCodec.Decode("{oops"));
}
