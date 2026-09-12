using SkiaSharp;
using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// Whose face a document signs off with.
///
/// <para>It was one committed file, printed by whoever ran the app — which on a second install is a
/// photograph of somebody else, on every sheet, for ever.</para>
///
/// <para>These run in sequence rather than in parallel because they share one static: the mark is a
/// process-wide fact about which install this is, which is exactly what it should be on a phone and
/// exactly what makes it awkward in a test.</para>
/// </summary>
/// <summary>
/// The collection marker lives on its own class, not on the tests. xUnit does not discover a test
/// class that also carries <c>CollectionDefinition</c> — it simply runs nothing, silently, and a
/// suite that went from 75 tests to 74 is the only sign.
/// </summary>
[CollectionDefinition(OwnerMarkTests.Serially, DisableParallelization = true)]
public sealed class OwnerMarkCollection
{
}

[Collection(OwnerMarkTests.Serially)]
public class OwnerMarkTests : IDisposable
{
    internal const string Serially = "owner mark";

    public void Dispose() => OwnerMark.Use(null);

    private static byte[] Photo(int edge, SKColor colour)
    {
        using var bitmap = new SKBitmap(edge, edge);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(colour);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    [Fact]
    public void WithNothingChosenItIsTheEmbeddedOne()
    {
        OwnerMark.Use(null);

        Assert.Same(EmbeddedBranding.OwnerMedallion, OwnerMark.Medallion);
    }

    [Fact]
    public void AnInstallsOwnMarkWins()
    {
        OwnerMark.Use(Photo(64, SKColors.Red));

        var medallion = OwnerMark.Medallion;

        Assert.NotNull(medallion);
        Assert.NotSame(EmbeddedBranding.OwnerMedallion, medallion);
        Assert.Equal(64, medallion!.Width);
    }

    /// <summary>
    /// Clearing it hands the page back the embedded one, rather than leaving a document with no
    /// mark until the app is restarted.
    /// </summary>
    [Fact]
    public void ClearingItFallsBackAgain()
    {
        OwnerMark.Use(Photo(64, SKColors.Red));
        OwnerMark.Use(null);

        Assert.Same(EmbeddedBranding.OwnerMedallion, OwnerMark.Medallion);
    }

    /// <summary>
    /// A file that will not decode costs a signature, not a morning. It is the shape of a photo
    /// picked from a folder that turned out to be something else.
    ///
    /// <para>Load-bearing because <c>SKBitmap.Decode</c> <b>throws</b> on unreadable bytes rather
    /// than returning null — read out of the library rather than assumed, after assuming wrong.</para>
    /// </summary>
    [Fact]
    public void SomethingThatIsNotAnImageFallsBackRatherThanThrowing()
    {
        OwnerMark.Use([1, 2, 3, 4, 5]);

        Assert.Same(EmbeddedBranding.OwnerMedallion, OwnerMark.Medallion);
    }

    [Fact]
    public void AnEmptyChoiceIsTheSameAsNoChoice()
    {
        OwnerMark.Use([]);

        Assert.Same(EmbeddedBranding.OwnerMedallion, OwnerMark.Medallion);
    }

    /// <summary>
    /// Replacing one leaves the last <b>safe to draw</b>, which is the opposite of what tidying up
    /// after it would do.
    ///
    /// <para>The first version of this disposed the bitmap being replaced, and this test was
    /// written to prove it. It did: the run aborted with "Test host process crashed", because
    /// touching a disposed <c>SKBitmap</c> is not an <c>ObjectDisposedException</c> — it is a native
    /// access violation that takes the process with it, silently. A delivery composing a page while
    /// somebody picks a new photo in Settings holds exactly that reference.</para>
    /// </summary>
    [Fact]
    public void ReplacingOneLeavesTheLastSafeToDraw()
    {
        OwnerMark.Use(Photo(64, SKColors.Red));
        var first = OwnerMark.Medallion;

        OwnerMark.Use(Photo(32, SKColors.Blue));

        Assert.Equal(32, OwnerMark.Medallion!.Width);

        // The page that was already holding the old one goes on working.
        Assert.Equal(64, first!.Width);
    }
}
