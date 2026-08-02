using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class EqualizerTests
{
    [Fact]
    public void Flat_HasZeroedBandsAndDisabled()
    {
        var settings = EqualizerSettings.Flat(5);
        Assert.False(settings.Enabled);
        Assert.Equal(5, settings.BandGains.Count);
        Assert.All(settings.BandGains, g => Assert.Equal((short)0, g));
    }

    [Fact]
    public void WithBand_SetsOneBand_OutOfRangeIsNoOp()
    {
        var settings = EqualizerSettings.Flat(3).WithBand(1, 600);
        Assert.Equal([(short)0, (short)600, (short)0], settings.BandGains.ToArray());
        Assert.Equal(settings, settings.WithBand(9, 100));
    }

    [Fact]
    public void Clamp_ConstrainsEveryBand()
    {
        var settings = new EqualizerSettings(true, [(short)-2000, (short)0, (short)2000]).Clamp(-1500, 1500);
        Assert.Equal([(short)-1500, (short)0, (short)1500], settings.BandGains.ToArray());
    }

    [Fact]
    public void SavePreset_ThenApply_LoadsGainsAndEnables()
    {
        var library = EqualizerLibrary.Flat(3);
        library = (library with { Active = library.Active.WithBand(0, 700) }).SavePreset("Bass");

        var applied = EqualizerLibrary.Flat(3).ApplyPreset("bass") with { }; // no such preset in a fresh library
        Assert.False(applied.Active.Enabled); // apply of unknown preset is a no-op

        var reloaded = library.ApplyPreset("Bass");
        Assert.True(reloaded.Active.Enabled);
        Assert.Equal((short)700, reloaded.Active.BandGains[0]);
    }

    [Fact]
    public void SavePreset_ReplacesSameName()
    {
        var library = EqualizerLibrary.Flat(2).SavePreset("Mine");
        library = (library with { Active = library.Active.WithBand(0, 300) }).SavePreset("Mine");
        Assert.Single(library.Presets);
        Assert.Equal((short)300, library.ApplyPreset("Mine").Active.BandGains[0]);
    }

    [Fact]
    public void Codec_RoundTrips()
    {
        var library = EqualizerLibrary.Flat(3).SavePreset("Flat");
        var decoded = EqualizerCodec.Decode(EqualizerCodec.Encode(library));
        Assert.NotNull(decoded);
        Assert.Single(decoded!.Presets);
        Assert.Equal("Flat", decoded.Presets[0].Name);
    }

    [Fact]
    public void Codec_NullAndGarbled_ReturnNull()
    {
        Assert.Null(EqualizerCodec.Decode(null));
        Assert.Null(EqualizerCodec.Decode("  "));
        Assert.Null(EqualizerCodec.Decode("{bad"));
    }
}
