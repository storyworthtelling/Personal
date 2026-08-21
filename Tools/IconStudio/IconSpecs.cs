using SkiaSharp;

namespace IconStudio;

/// <param name="App">Display name, used in logs and preview filenames.</param>
/// <param name="ResourceRoot">Android resource root, relative to the repo root.</param>
/// <param name="Name">Base resource name (appicon or ic_launcher).</param>
/// <param name="WritesColorsXml">Whether to (re)write values/colors.xml with the paper background color.</param>
/// <param name="BackgroundHex">Adaptive-icon background color (default flat paper). Flyleaf's is
/// its warm identity ground, over which the foreground draws a cream bookplate cover.</param>
public sealed record IconSpec(
    string App,
    string ResourceRoot,
    string Name,
    bool WritesColorsXml,
    Action<SKCanvas, float> Glyph,
    string BackgroundHex = Tokens.PaperHex);

public static class IconSpecs
{
    public static readonly IReadOnlyList<IconSpec> All =
    [
        new("FocusPhone.Launcher", "FocusPhone/Flyleaf.Android/Resources", "appicon", false, Glyphs.Bookplate, Tokens.IdentityWarmHex),
        new("FocusPhone.Camera", "FocusPhone/FocusPhone.Camera.Android/Resources", "appicon", false, Glyphs.Lens),
        new("FocusPhone.Audio", "FocusPhone/FocusPhone.Audio.Android/Resources", "appicon", false, Glyphs.Waveform),
        new("FocusPhone.Communication", "FocusPhone/FocusPhone.Communication.Android/Resources", "appicon", false, Glyphs.Speech),
        // MAUI heads: resources live in the plain `Resources/` folder, not `Platforms/Android/Resources`.
        // These projects don't set <SingleProject>true</SingleProject>, so MAUI never repoints the Android
        // resource prefix at Platforms/Android/Resources — the active prefix stays the .NET-for-Android
        // default (`Resources`). Emitting here is what actually gets the mipmaps packaged (see the two heads'
        // csproj comments and CLAUDE.md); the launcher icon is then wired via [Application(Icon=...)].
        new("Canonizer", "Storyboarder/Storyboarder.Android/Resources", "appicon", true, Glyphs.PenNib),
        new("DailyLoader", "DailyLoader/DailyLoader.Android/Resources", "ic_launcher", true, Glyphs.FoldedPaper),
        new("Ledger", "Ledger/Ledger.Android/Resources", "appicon", true, Glyphs.Ledger),
    ];
}
