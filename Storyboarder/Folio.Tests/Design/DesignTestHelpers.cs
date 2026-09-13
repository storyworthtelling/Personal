using System.Globalization;

namespace Folio.Tests.Design;

public static class DesignTestHelpers
{
    /// <summary>Walks up from the test bin directory to the folder containing Folio.sln.</summary>
    public static string FolioRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Folio.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not locate Folio.sln above " + AppContext.BaseDirectory);
        }
        return dir.FullName;
    }

    /// <summary>
    /// Every C# source file in this project, and nothing outside it.
    ///
    /// <para>Scoped to the folder holding <c>Folio.sln</c> rather than to the repository, which is the
    /// lesson a P10 guard taught twice: a scan that walks up to the monorepo root flags FocusPhone and
    /// <c>Storyworthtelling.Updates</c> for naming Folio's package, which they legitimately do.</para>
    /// </summary>
    public static IEnumerable<string> SourceFiles()
    {
        foreach (var file in Directory.EnumerateFiles(FolioRoot(), "*.cs", SearchOption.AllDirectories))
        {
            var parts = Path.GetRelativePath(FolioRoot(), file).Split(Path.DirectorySeparatorChar);
            if (!parts.Any(part => part is "obj" or "bin"))
            {
                yield return file;
            }
        }
    }

    /// <summary>WCAG 2.x contrast ratio between two hex colors.</summary>
    public static double ContrastRatio(string hexA, string hexB)
    {
        var la = RelativeLuminance(hexA);
        var lb = RelativeLuminance(hexB);
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return 0.2126 * Linear((value >> 16) & 0xFF)
             + 0.7152 * Linear((value >> 8) & 0xFF)
             + 0.0722 * Linear(value & 0xFF);
    }

    private static double Linear(int channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
