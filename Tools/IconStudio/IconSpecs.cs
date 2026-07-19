using SkiaSharp;

namespace IconStudio;

/// <param name="App">Display name, used in logs and preview filenames.</param>
/// <param name="ResourceRoot">Android resource root, relative to the repo root.</param>
/// <param name="Name">Base resource name (appicon or ic_launcher).</param>
/// <param name="WritesColorsXml">Whether to (re)write values/colors.xml with the paper background color.</param>
public sealed record IconSpec(
    string App,
    string ResourceRoot,
    string Name,
    bool WritesColorsXml,
    Action<SKCanvas, float> Glyph);

public static class IconSpecs
{
    public static readonly IReadOnlyList<IconSpec> All =
    [
        new("FocusPhone.Launcher", "FocusPhone/FocusPhone.Launcher.Android/Resources", "appicon", false, Glyphs.Clock),
        new("FocusPhone.Camera", "FocusPhone/FocusPhone.Camera.Android/Resources", "appicon", false, Glyphs.Lens),
        new("FocusPhone.Audio", "FocusPhone/FocusPhone.Audio.Android/Resources", "appicon", false, Glyphs.Waveform),
        new("FocusPhone.Communication", "FocusPhone/FocusPhone.Communication.Android/Resources", "appicon", false, Glyphs.Speech),
        // MAUI heads: resources live in the plain `Resources/` folder, not `Platforms/Android/Resources`.
        // These projects don't set <SingleProject>true</SingleProject>, so MAUI never repoints the Android
        // resource prefix at Platforms/Android/Resources — the active prefix stays the .NET-for-Android
        // default (`Resources`). Emitting here is what actually gets the mipmaps packaged (see the two heads'
        // csproj comments and CLAUDE.md); the launcher icon is then wired via [Application(Icon=...)].
        new("Storyboarder", "Storyboarder/Storyboarder.Android/Resources", "appicon", true, Glyphs.PanelGrid),
        new("DailyLoader", "DailyLoader/DailyLoader.Android/Resources", "ic_launcher", true, Glyphs.FoldedPaper),
        new("Tally", "Tally/Tally.Android/Resources", "appicon", true, Glyphs.Tally),
    ];
}
