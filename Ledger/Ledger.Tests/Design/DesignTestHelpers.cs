namespace Index.Tests.Design;

/// <summary>
/// Release B (Guard G12) removed this head's own colour palette, along with the contrast-ratio
/// arithmetic <c>DesignTokensTests</c>/<c>TokenParityTests</c> used to check it with — the kit's own
/// tests do that job now, by reflection against <c>PaperColorTokens</c>, for every STATIC role/tier
/// pairing. <see cref="ContrastRatio"/> is back for one reason a static reflection check cannot cover:
/// D127's post-it colour is COMPUTED at runtime from a bank's own hex, not one of the kit's registered
/// tokens, so <c>PostItTintTests</c>/<c>GraphPaperContrastTests</c> need the WCAG arithmetic itself
/// rather than a fixed table to look values up in. Ported from
/// <c>FocusPhone/Codex.Tests/Design/DesignTestHelpers.cs</c> (same formula, same shape) rather than
/// referenced — a native FocusPhone test project cannot be referenced from this desktop-TFM one any
/// more than <c>Storyworthtelling.Maui</c> can, so this is the same "copy the algorithm, cite the
/// source" move <c>Index.Domain.Design.Oklch</c> makes for <c>Storyworthtelling.Paper</c>'s version.
/// </summary>
public static class DesignTestHelpers
{
    /// <summary>Walks up from the test bin directory to the folder containing Ledger.sln.</summary>
    public static string LedgerRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Ledger.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not locate Ledger.sln above " + AppContext.BaseDirectory);
        }
        return dir.FullName;
    }

    /// <summary>WCAG 2.x contrast ratio between two "#RRGGBB" colours, always ≥ 1.</summary>
    public static double ContrastRatio(string hexA, string hexB)
    {
        var la = RelativeLuminance(hexA);
        var lb = RelativeLuminance(hexB);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    private static (double R, double G, double B) ParseHex(string hex)
    {
        var value = int.Parse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber);
        var r = ((value >> 16) & 0xFF) / 255.0;
        var g = ((value >> 8) & 0xFF) / 255.0;
        var b = (value & 0xFF) / 255.0;
        return (r, g, b);
    }

    private static double Linear(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    /// <summary>
    /// Flat sRGB alpha compositing of <paramref name="foregroundHex"/> over
    /// <paramref name="backgroundHex"/> — the same math a translucent line drawn over a background
    /// actually renders as on screen (Android/MAUI alpha blending is gamma-space, not
    /// linear-light), which is what <c>GraphPaperContrastTests</c> needs: it is checking the pixel
    /// that is actually drawn, not a colour-science ideal blend.
    /// </summary>
    public static string CompositeOver(string foregroundHex, string backgroundHex, double alpha)
    {
        var fg = ParseHexBytes(foregroundHex);
        var bg = ParseHexBytes(backgroundHex);

        var r = Blend(fg.R, bg.R, alpha);
        var g = Blend(fg.G, bg.G, alpha);
        var b = Blend(fg.B, bg.B, alpha);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int Blend(int fg, int bg, double alpha) =>
        (int)Math.Round(alpha * fg + (1 - alpha) * bg);

    private static (int R, int G, int B) ParseHexBytes(string hex)
    {
        var value = int.Parse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber);
        return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }
}
