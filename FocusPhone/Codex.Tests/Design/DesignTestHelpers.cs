using System.Globalization;

namespace Codex.Tests.Design;

public static class DesignTestHelpers
{
    /// <summary>Walks up from the test bin directory to the folder containing Codex.sln.</summary>
    public static string FocusPhoneRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Codex.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not locate Codex.sln above " + AppContext.BaseDirectory);
        }
        return dir.FullName;
    }

    public static (int R, int G, int B) ParseHex(string hex)
    {
        var value = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
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
        var (r, g, b) = ParseHex(hex);
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    private static double Linear(int channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
