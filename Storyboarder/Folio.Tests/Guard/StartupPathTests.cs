using System.Text.RegularExpressions;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// The two halves of a crash on open, guarded together because they are the same failure: the app
/// could not build its first screen, and then could not tell the owner why.
///
/// <para><b>What happened.</b> <c>MainPage</c>'s constructor called <c>SetBackdrop</c> and then
/// <c>Wrap</c>. The backdrop goes behind the header <i>and</i> the content, so it needs the column
/// the frame builds — which <c>Wrap</c> builds. Called first, it threw an
/// <c>ArgumentNullException</c> out of the constructor, out of
/// <c>GetRequiredService&lt;MainPage&gt;()</c>, and out of <c>CreateWindow</c>. The app opened on
/// its failure page every time.</para>
///
/// <para><b>Why nothing caught it.</b> The head compiles, and both orders compile. No test can
/// construct a MAUI page from this project, so the constructor of the app's home screen is the one
/// piece of code that runs on every launch and in no test. These assertions read the source
/// instead, which is the same answer the other invisible-invariant guards in this folder reach for.
/// </para>
/// </summary>
public class StartupPathTests
{
    /// <summary>
    /// A page must have its frame before it hangs anything on it. The ordering trap is now closed
    /// in <c>FolioBinderPage</c> as well — a backdrop set early is held and applied by
    /// <c>Wrap</c> — so this guard is about the source staying readable rather than about the
    /// crash, which cannot recur. Both are wanted: a page whose constructor reads in the wrong
    /// order is a page whose next reader will wonder which calls are safe.
    /// </summary>
    [Fact]
    public void EveryPageWrapsItsFrameBeforeItDressesIt()
    {
        var wrong = new List<string>();

        foreach (var file in FolioInvariantsTests.HeadFiles())
        {
            var source = File.ReadAllText(file);
            var backdrop = source.IndexOf("SetBackdrop(new", StringComparison.Ordinal);
            if (backdrop < 0)
            {
                continue;
            }

            var wrap = source.IndexOf("Wrap(FolioArea", StringComparison.Ordinal);
            if (wrap < 0 || wrap > backdrop)
            {
                wrong.Add(FolioInvariantsTests.Relative(file));
            }
        }

        Assert.True(wrong.Count == 0,
            "A page calls SetBackdrop before Wrap has built the frame it hangs on:\n"
            + string.Join("\n", wrong));
    }

    /// <summary>
    /// The failure page is the only screen a phone in this state can show, and its whole job is to
    /// be read back to somebody. It promised in its own copy that the detail "can be copied and
    /// sent on" — and offered a read-only <c>Editor</c>, which on Android takes no selection at
    /// all, so the one thing it said you could do was the one thing you could not. A button that
    /// puts the text on the clipboard needs no selection.
    /// </summary>
    [Fact]
    public void TheFailurePageCanActuallyHandOverWhatItShows()
    {
        var page = FolioInvariantsTests.HeadFiles()
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("Folio could not start", StringComparison.Ordinal));

        Assert.NotNull(page);
        Assert.Contains("Clipboard.SetTextAsync", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A screen nothing constructs is a screen nobody has looked at, and it rots: the dead
    /// <c>WorkDeskPage</c> carried the very ordering bug that crashed the live desk, having been
    /// copied from it. This is <c>ReportIsReachableTests</c>'s rule applied to the whole binder —
    /// every page in it is opened by something.
    /// </summary>
    [Fact]
    public void EveryPageInTheBinderIsReachable()
    {
        var head = FolioInvariantsTests.HeadFiles().ToList();
        var sources = head.ToDictionary(file => file, File.ReadAllText);

        var pages = sources
            .Where(entry => Regex.IsMatch(entry.Value, @"class\s+(\w+)\s*:\s*FolioBinderPage"))
            .Select(entry => Regex.Match(entry.Value, @"class\s+(\w+)\s*:\s*FolioBinderPage").Groups[1].Value)
            .ToList();

        Assert.NotEmpty(pages);

        var orphans = pages
            .Where(page => !sources.Any(entry =>
                !Regex.IsMatch(entry.Value, $@"class\s+{page}\s*:\s*FolioBinderPage")
                && (entry.Value.Contains($"new {page}(", StringComparison.Ordinal)
                    || entry.Value.Contains($"GetRequiredService<{page}>", StringComparison.Ordinal)
                    || entry.Value.Contains($"GetRequiredService<Pages.{page}>", StringComparison.Ordinal))))
            .OrderBy(page => page, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "These pages wear the binder and are constructed by nothing:\n" + string.Join("\n", orphans));
    }
}
