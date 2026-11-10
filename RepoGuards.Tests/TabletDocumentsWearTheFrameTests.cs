using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Every document sent to the tablet wears Foreword's frame and carries Folio's maker's mark, by the owner's decision:
/// the header signed in the owner's hand, the binder tabs as links, and the folio led by the mark — all drawn by
/// <c>Storyworthtelling.Paper.Binder.SheetFrame</c>. A file that begins a tablet page must draw through it.
/// </summary>
public class TabletDocumentsWearTheFrameTests
{
    /// <summary>
    /// Painters still on a frame of their own, each named with the step that moves it. The list only shrinks: a new
    /// painter is not added here, it draws the shared frame.
    /// </summary>
    private static readonly HashSet<string> NotYetFramed = new(StringComparer.Ordinal)
    {
        "Storyboarder/Folio.Infrastructure/Imaging/PageWriter.cs",            // step 4, Folio
    };

    /// <summary>The one place a page begins: the sink itself, and Foreword's layout, whose callers draw the frame.</summary>
    private static readonly HashSet<string> PageMachinery = new(StringComparer.Ordinal)
    {
        "Storyworthtelling.Paper/VectorPdfSink.cs",
        "Foreword/Foreword.Infrastructure/Pdf/SkiaPageLayout.cs",
        "Constitution/Constitution.Infrastructure/Documents/ConstitutionPageSink.cs",
    };

    [Fact]
    public void ConstitutionsRendererFramesEveryPageItBegins()
    {
        // Constitution begins its pages through its sink; the renderer that calls it draws the frame on each.
        var renderer = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), "Constitution", "Constitution.Infrastructure", "Documents", "ConstitutionDocumentRenderer.cs"));
        Assert.Contains("ConstitutionPageSink.Begin(", renderer, StringComparison.Ordinal);
        Assert.Contains("ConstitutionSheet.Draw(", renderer, StringComparison.Ordinal);
        var callers = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("ConstitutionPageSink.Begin(", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();
        Assert.Equal(["Constitution/Constitution.Infrastructure/Documents/ConstitutionDocumentRenderer.cs"], callers);
    }

    [Fact]
    public void EveryPainterThatBeginsATabletPageDrawsTheSharedFrame()
    {
        var unframed = Painters()
            .Where(file => !PageMachinery.Contains(Relative(file)) && !NotYetFramed.Contains(Relative(file)))
            .Where(file => !Frames(File.ReadAllText(file)))
            .Select(Relative)
            .ToList();

        Assert.True(unframed.Count == 0, "Draw the page through SheetFrame (or Foreword's PageFurniture):\n" + string.Join("\n", unframed));
    }

    [Fact]
    public void ThePendingListOnlyNamesPaintersThatStillNeedIt()
    {
        var stale = NotYetFramed
            .Where(path => !File.Exists(Path.Combine(GuardScan.RepoRoot(), path)) || Frames(File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), path))))
            .ToList();

        Assert.True(stale.Count == 0, "Take these off NotYetFramed:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void TheFolioAndTheCoverCarryTheMakersMark()
    {
        var frame = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), "Storyworthtelling.Paper", "Binder", "SheetFrame.cs"));
        var folio = Regex.Match(frame, @"public static void Folio\(SKCanvas canvas, TabletSheet sheet, string name, string\? right\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        var colophon = Regex.Match(frame, @"public static float Colophon\(.*?\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);

        Assert.Contains("Mark(", folio.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("Mark(", colophon.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("OwnersMark.Draw(", frame, StringComparison.Ordinal);
    }

    private static bool Frames(string text) =>
        text.Contains("SheetFrame.", StringComparison.Ordinal) || text.Contains("PageFurniture.", StringComparison.Ordinal)
        || text.Contains("DayPageFurniture.", StringComparison.Ordinal);

    private static IEnumerable<string> Painters() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\.BeginPage\(|new SkiaPageLayout\("));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
