using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class DeviceAudioProfileTests
{
    [Fact]
    public void For_ReturnsRememberedProfile()
    {
        var book = new DeviceAudioProfileBook();
        book.Remember(new DeviceAudioProfile("car", 0.6f, "Bass Boost"));

        var profile = book.For("car");

        Assert.NotNull(profile);
        Assert.Equal("Bass Boost", profile!.EqPreset);
    }

    [Fact]
    public void For_ReturnsNullForUnknownDevice()
    {
        Assert.Null(new DeviceAudioProfileBook().For("headphones"));
    }

    [Fact]
    public void Remember_OverwritesTheSameDevice()
    {
        var book = new DeviceAudioProfileBook();
        book.Remember(new DeviceAudioProfile("car", 0.6f, "Flat"));
        book.Remember(new DeviceAudioProfile("car", 0.9f, "Rock"));

        Assert.Equal(0.9f, book.For("car")!.Volume);
        Assert.Single(book.Export());
    }

    [Fact]
    public void Profiles_SurviveExportAndRestore()
    {
        var book = new DeviceAudioProfileBook();
        book.Remember(new DeviceAudioProfile("car", 0.6f, "Bass Boost"));
        book.Remember(new DeviceAudioProfile("buds", 0.4f, "Flat"));

        var restored = new DeviceAudioProfileBook(book.Export());

        Assert.Equal("Bass Boost", restored.For("car")!.EqPreset);
        Assert.Equal("Flat", restored.For("buds")!.EqPreset);
    }
}
