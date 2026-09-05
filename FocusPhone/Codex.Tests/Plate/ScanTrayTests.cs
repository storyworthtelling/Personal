using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The page tray that lets a scan run longer than Google's scanner does.
///
/// <para>ML Kit's document scanner caps a single run at fifty pages, and the owner asked to
/// be able to scan "much more if needed". So Plate stops taking the scanner's finished PDF
/// and takes the per-page images instead — which the shipped head already asks for and then
/// throws away — accumulating them across as many runs as it takes and composing the
/// document itself.</para>
///
/// <para>A page is an opaque reference here: a content URI on the phone, a plain string in
/// these tests. The Domain never opens one, so it does not need to know.</para>
/// </summary>
public class ScanTrayTests
{
    private static readonly DateTime Noon = new(2026, 9, 4, 12, 34, 56, DateTimeKind.Local);

    [Fact]
    public void GooglesPerRunCapIsTheReasonThisTypeExists()
    {
        // Recorded as a constant so the head asks the scanner for exactly what it will give
        // and the number is not typed twice.
        Assert.Equal(50, ScanTray.PagesPerScannerRun);
    }

    [Fact]
    public void AFreshTrayRemembersTheDoorAndHoldsNothing()
    {
        // The destination is picked before scanning starts — it is the door the owner came
        // through — so a tray cannot exist without one.
        var tray = ScanTray.For(ScanDestination.Remarkable);

        Assert.Equal(ScanDestination.Remarkable, tray.Destination);
        Assert.Empty(tray.Pages);
        Assert.False(tray.HasPages);
    }

    [Fact]
    public void PagesKeepTheOrderTheyWereScannedIn()
    {
        var tray = ScanTray.For(ScanDestination.Drive).Add(["a.jpg", "b.jpg", "c.jpg"]);

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], References(tray));
        Assert.Equal(3, tray.Count);
        Assert.True(tray.HasPages);
    }

    [Fact]
    public void ASecondRunAppendsAfterTheFirst()
    {
        // This is the whole point: page 51 arrives from a second trip through the scanner
        // and has to land after page 50, not replace it.
        var tray = ScanTray.For(ScanDestination.Post)
            .Add(["1.jpg", "2.jpg"])
            .Add(["3.jpg"]);

        Assert.Equal(["1.jpg", "2.jpg", "3.jpg"], References(tray));
    }

    [Fact]
    public void TheTrayItselfIsNotCapped()
    {
        var tray = ScanTray.For(ScanDestination.Drive);
        for (var run = 0; run < 3; run++)
        {
            tray = tray.Add(Enumerable.Range(0, ScanTray.PagesPerScannerRun)
                .Select(page => $"run{run}-page{page}.jpg"));
        }

        Assert.Equal(150, tray.Count);
        Assert.Equal("run0-page0.jpg", tray.Pages[0].Reference);
        Assert.Equal("run2-page49.jpg", tray.Pages[149].Reference);
    }

    [Fact]
    public void BackingOutOfTheScannerChangesNothing()
    {
        var tray = ScanTray.For(ScanDestination.Drive).Add(["a.jpg"]);

        Assert.Equal(References(tray), References(tray.Add([])));
    }

    [Fact]
    public void EachPageRemembersWhenItWasScanned()
    {
        // The tray screen lists the pages as "Page 2 · scanned 4:39 PM", which is the only
        // way to tell two near-identical sheets apart when a long scan goes wrong.
        var tray = ScanTray.For(ScanDestination.Drive).Add(["a.jpg"], Noon);

        Assert.Equal("a.jpg", tray.Pages[0].Reference);
        Assert.Equal(Noon, tray.Pages[0].ScannedAt);
    }

    [Fact]
    public void APageCanBeMovedEarlierOrLater()
    {
        // Scanning a stack out of order is ordinary. Reordering has to be possible without
        // rescanning, or a single misfed sheet costs the whole document.
        var tray = ScanTray.For(ScanDestination.Drive).Add(["a.jpg", "b.jpg", "c.jpg"]);

        Assert.Equal(["b.jpg", "a.jpg", "c.jpg"], References(tray.Move(0, 1)));
        Assert.Equal(["a.jpg", "c.jpg", "b.jpg"], References(tray.Move(2, 1)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    [InlineData(1, 1)]
    public void MovingAPageNowhereLeavesTheTrayAlone(int from, int to)
    {
        // Moving the first page up, or the last page down, is a tap people make constantly.
        // It has to do nothing rather than throw away a scan in progress.
        var tray = ScanTray.For(ScanDestination.Drive).Add(["a.jpg", "b.jpg", "c.jpg"]);

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], References(tray.Move(from, to)));
    }

    [Fact]
    public void MovingAPageCarriesItsTimeWithIt()
    {
        var tray = ScanTray.For(ScanDestination.Drive)
            .Add(["a.jpg"], Noon)
            .Add(["b.jpg"], Noon.AddMinutes(1))
            .Move(1, 0);

        Assert.Equal("b.jpg", tray.Pages[0].Reference);
        Assert.Equal(Noon.AddMinutes(1), tray.Pages[0].ScannedAt);
    }

    [Fact]
    public void ADroppedPageLeavesTheRestInOrder()
    {
        var tray = ScanTray.For(ScanDestination.Drive)
            .Add(["a.jpg", "b.jpg", "c.jpg"])
            .RemoveAt(1);

        Assert.Equal(["a.jpg", "c.jpg"], References(tray));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void DroppingAPageThatIsNoLongerThereIsNotAnError(int index)
    {
        // Two taps on the same page, or a tap that arrives after the list has already been
        // rebuilt. Losing the whole tray to an exception would be far worse than a no-op.
        var tray = ScanTray.For(ScanDestination.Drive).Add(["a.jpg", "b.jpg"]);

        Assert.Equal(["a.jpg", "b.jpg"], References(tray.RemoveAt(index)));
    }

    [Fact]
    public void AddingDoesNotDisturbTheTrayItGrewFrom()
    {
        // The head holds the tray in a field and reassigns it. If Add mutated in place, a
        // caller that forgot to reassign would still appear to work — until an undo, when
        // the pages it thought it had kept would be gone.
        var first = ScanTray.For(ScanDestination.Drive).Add(["a.jpg"]);
        var second = first.Add(["b.jpg"]);

        Assert.Single(first.Pages);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void ANullRunIsTreatedAsNoPages()
    {
        // ML Kit hands back a result object whose page list is nullable, and a cancelled
        // scan is the ordinary way to see it.
        var tray = ScanTray.For(ScanDestination.Drive).Add(null);

        Assert.Empty(tray.Pages);
    }

    private static IReadOnlyList<string> References(ScanTray tray) =>
        [.. tray.Pages.Select(page => page.Reference)];
}
