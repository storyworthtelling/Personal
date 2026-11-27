using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// The photos the wrist has fetched (W-5.1.3), held in memory for the process only and at most a few, so a conversation
/// opened again shows them at once and a wrist that restarts asks the phone again. Nothing is written down: the phone
/// keeps the record (A9).
/// </summary>
public class HeldPhotosTests
{
    private static string Id(int index) => "a" + index.ToString("x16");

    private static byte[] Jpeg(int index) => [0xFF, 0xD8, 0xFF, (byte)index];

    [Fact]
    public void AHeldPhotoComesBack()
    {
        var held = new HeldPhotos();

        held.Hold(Id(1), Jpeg(1));

        Assert.Equal(Jpeg(1), held.Get(Id(1)));
        Assert.True(held.Has(Id(1)));
        Assert.Null(held.Get(Id(2)));
        Assert.False(held.Has(Id(2)));
    }

    [Fact]
    public void AtMostAFewAreHeld()
    {
        var held = new HeldPhotos();

        foreach (var index in Enumerable.Range(0, HeldPhotos.Most + 4))
        {
            held.Hold(Id(index), Jpeg(index));
        }

        Assert.Equal(12, HeldPhotos.Most);
        Assert.Equal(HeldPhotos.Most, held.Count);
        Assert.False(held.Has(Id(0)));
        Assert.True(held.Has(Id(HeldPhotos.Most + 3)));
    }

    [Fact]
    public void TheLongestUnseenGoesFirst()
    {
        var held = new HeldPhotos();
        foreach (var index in Enumerable.Range(0, HeldPhotos.Most))
        {
            held.Hold(Id(index), Jpeg(index));
        }

        Assert.NotNull(held.Get(Id(0)));
        held.Hold(Id(99), Jpeg(99));

        Assert.True(held.Has(Id(0)));
        Assert.False(held.Has(Id(1)));
    }

    [Fact]
    public void HoldingAgainReplaces()
    {
        var held = new HeldPhotos();

        held.Hold(Id(1), Jpeg(1));
        held.Hold(Id(1), Jpeg(2));

        Assert.Equal(1, held.Count);
        Assert.Equal(Jpeg(2), held.Get(Id(1)));
    }

    [Fact]
    public void ManyThreadsAtOnceLoseNothing()
    {
        var held = new HeldPhotos();

        Parallel.For(0, 2000, index =>
        {
            held.Hold(Id(index % HeldPhotos.Most), Jpeg(index));
            held.Get(Id((index + 1) % HeldPhotos.Most));
        });

        Assert.Equal(HeldPhotos.Most, held.Count);
    }
}
