using System.Text.RegularExpressions;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// G4 — the report can be opened from the app, and the route it opens by is one Android will accept.
///
/// <para><b>The bug this reintroduces is the one the finding calls R-2:</b> drop
/// <c>ReportPage</c> — the only in-app route to the report — without building the action that
/// replaces it, and the product becomes a thing that produces a document nobody can reach. Nothing
/// fails when that happens. The pipeline still runs, the PDF is still written, the book still
/// finishes; there is simply no longer a tap that ends at it.</para>
///
/// <para><b>And a second route was missing before this release even began.</b> <c>BookPage</c> — the
/// screen that carries the open action — was built, wears the binder, passes every frame guard, and
/// was constructed by nothing at all. That is HANDOFF.md §4's trap and P9's binder strip again: a
/// component that is correct and unreached, invisible to every test that is about the component.
/// So the first assertion here is not about the button. It is about whether anything opens the
/// screen the button is on.</para>
///
/// <para><b>Why the URI assertion.</b> Android 7+ throws <c>FileUriExposedException</c> when a
/// <c>file://</c> URI crosses an app boundary, so handing a viewer a path is a crash rather than a
/// failure to open — and it is a crash that only happens on a device, in the one code path this
/// environment cannot run. The Essentials openers (<c>Launcher.OpenAsync(OpenFileRequest)</c>,
/// <c>Share.RequestAsync(ShareFileRequest)</c>) go through the <c>${applicationId}.fileProvider</c>
/// that MAUI Essentials already declares in the merged manifest, and grant the read permission
/// themselves. Measured, not assumed: the merged <c>AndroidManifest.xml</c> in this head's <c>obj</c>
/// carries <c>microsoft.maui.essentials.fileProvider</c> under an authority built from the frozen
/// application id — which is why the finding's estimate that this "is not a one-liner" was
/// overstated: it searched the head's own source, where the provider is not written down because the
/// framework contributes it.</para>
/// </summary>
public class ReportIsReachableTests
{
    /// <summary>
    /// Something in the head constructs the book page. Without this the open action is unreachable
    /// however well it is wired, and every other assertion in this file passes anyway.
    /// </summary>
    [Fact]
    public void SomethingOpensTheBookPage()
    {
        var openers = FolioInvariantsTests.HeadFiles()
            .Where(file => Path.GetFileName(file) != "BookPage.cs")
            .Where(file => File.ReadAllText(file).Contains("new BookPage(", StringComparison.Ordinal))
            .Select(FolioInvariantsTests.Relative)
            .ToList();

        Assert.True(openers.Count > 0,
            "Nothing constructs BookPage, so the screen carrying the open-the-report action cannot "
            + "be reached. A screen that no tap ends at is not a screen.");
    }

    /// <summary>The bar carries an open-the-report action, and it is wired to something.</summary>
    [Fact]
    public void TheBookPageOffersAnOpenReportAction()
    {
        var source = Read("BookPage.cs");

        Assert.Contains("Open report", source, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\bClicked\s*\+=\s*[^;]*OpenReport", RegexOptions.Singleline), source);
    }

    /// <summary>
    /// The action ends at the report rather than at a message about it. It asks the service for the
    /// rendered PDF's path and hands that path to an opener.
    /// </summary>
    [Fact]
    public void TheOpenActionAsksForTheReportAndOpensIt()
    {
        Assert.Contains("ReportFile.OpenAsync", Read("BookPage.cs"), StringComparison.Ordinal);

        var opener = Read("ReportFile.cs");
        Assert.Contains("GetAnalysisPdfPathAsync", opener, StringComparison.Ordinal);
        Assert.Contains("OpenFileRequest", opener, StringComparison.Ordinal);
        Assert.Contains("ReadOnlyFile", opener, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing in the head hands Android a path. This is the crash the finding warned about, and it
    /// is invisible until a device runs it.
    /// </summary>
    [Fact]
    public void NothingHandsAViewerAFileUri()
    {
        var offenders = FolioInvariantsTests.HeadFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"""file://|Uri\.FromFile\("))
            .Select(FolioInvariantsTests.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A file:// URI crossing an app boundary throws FileUriExposedException on Android 7+. "
            + "Open through Launcher.OpenAsync(new OpenFileRequest(...)), which goes through the "
            + "fileProvider MAUI Essentials declares:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The in-app reader is gone (D85), so nothing may quietly depend on it still being there — and
    /// the assertions above are what make dropping it safe rather than merely done.
    /// </summary>
    [Fact]
    public void TheInAppReaderIsGone()
    {
        var survivors = FolioInvariantsTests.HeadFiles()
            .Concat(Directory.EnumerateFiles(
                Path.GetDirectoryName(FolioInvariantsTests.HeadProjectFile())!, "*.xaml",
                SearchOption.AllDirectories))
            .Where(file => Path.GetFileName(file).StartsWith("ReportPage.", StringComparison.Ordinal)
                || File.ReadAllText(file).Contains("new ReportPage(", StringComparison.Ordinal))
            .Select(FolioInvariantsTests.Relative)
            .ToList();

        Assert.True(survivors.Count == 0,
            "The report is the printed document; the in-app reader was a second, thinner copy of it "
            + "that drifted from the page it was a copy of:\n" + string.Join("\n", survivors));
    }

    private static string Read(string fileName) =>
        File.ReadAllText(FolioInvariantsTests.HeadFiles()
            .Single(file => Path.GetFileName(file) == fileName));
}
