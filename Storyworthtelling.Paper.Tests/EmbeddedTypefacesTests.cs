using SkiaSharp;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// Every one of the repo's fonts is embedded here and reachable by name.
///
/// <para>This library is the only thing that draws a printed page, and a face it cannot load is not
/// an error: Skia substitutes something, the page renders, and the document is delivered in the
/// wrong hand. It is the same failure the MAUI kit had — the fonts shipped inside every APK with no
/// alias registered, which is not an error in MAUI either — and it is worse on paper, because the
/// page has already been printed by the time anyone looks at it.</para>
///
/// <para>The two that were missing are the two that carry meaning rather than text.
/// <b>Pinyon Script is the owner's voice</b> (DESIGN.md §4 reserves it for exactly that, and every
/// mockup of The Day and The Brief draws the narrator in it), and <b>Cascadia is the Mono role</b>,
/// which the printed type ladder has a registered size for. Both were sitting in /Fonts, shipped in
/// every app, and unreachable from the one library that prints.</para>
/// </summary>
public class EmbeddedTypefacesTests
{
    [Fact]
    public void EveryFontInTheRepoIsEmbedded()
    {
        var fonts = Directory.EnumerateFiles(FontsFolder(), "*.ttf")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var embedded = typeof(EmbeddedTypefaces).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".ttf", StringComparison.Ordinal))
            .Select(name => name[(name.LastIndexOf('.', name.Length - 5) + 1)..])
            .ToHashSet(StringComparer.Ordinal);

        var missing = fonts.Where(font => !embedded.Contains(font!)).ToList();

        Assert.True(missing.Count == 0,
            "These ship with every app and cannot be drawn by the library that prints, so Skia "
            + "substitutes something and says nothing:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// And each is actually loadable. An embedded resource under a name nothing asks for is no
    /// better than a missing one — the accessor is what a composer calls.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFace))]
    public void EveryFaceLoads(string name, Func<SKTypeface> face)
    {
        var loaded = face();

        Assert.NotNull(loaded);
        Assert.False(string.IsNullOrWhiteSpace(loaded.FamilyName), $"{name} loaded with no family name.");
    }

    public static TheoryData<string, Func<SKTypeface>> EveryFace() => new()
    {
        { "SerifRegular", () => EmbeddedTypefaces.SerifRegular },
        { "SerifSemibold", () => EmbeddedTypefaces.SerifSemibold },
        { "SerifBlack", () => EmbeddedTypefaces.SerifBlack },
        { "SerifItalic", () => EmbeddedTypefaces.SerifItalic },
        { "SansRegular", () => EmbeddedTypefaces.SansRegular },
        { "SansSemibold", () => EmbeddedTypefaces.SansSemibold },
        { "Script", () => EmbeddedTypefaces.Script },
        { "Mono", () => EmbeddedTypefaces.Mono },
    };

    /// <summary>
    /// The owner's voice is Pinyon Script and nothing else. A substituted face here would print the
    /// narrator's sentence in the same hand as the rest of the page, which is precisely the
    /// distinction §4 makes.
    /// </summary>
    [Fact]
    public void TheVoiceIsPinyonScript()
    {
        Assert.Contains("Pinyon", EmbeddedTypefaces.Script.FamilyName, StringComparison.OrdinalIgnoreCase);
    }

    private static string FontsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Fonts")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Fonts");
    }
}
