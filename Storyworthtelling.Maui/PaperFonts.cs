using Microsoft.Maui.Hosting;

namespace Storyworthtelling.Maui;

/// <summary>
/// Registers the eight shared Paper &amp; Ink faces under the aliases the role table names.
///
/// <para>Every MAUI head called <c>AddFont</c> itself, and every one of them registered the same
/// six of the eight fonts in <c>/Fonts</c>. The other two — Source Serif 4 <b>Black</b> and
/// <b>Pinyon Script</b> — shipped inside every APK and could not be used by anything, because a
/// face with no alias is unreachable from XAML. Nothing failed: MAUI silently substitutes the
/// system face for an unknown family, so Display quietly drew in whatever the head had asked for
/// instead, and the owner's voice had nowhere to live at all.</para>
/// </summary>
public static class PaperFonts
{
    public static void Register(IFontCollection fonts)
    {
        fonts.AddFont("SourceSerif4-Regular.ttf", "Serif");
        fonts.AddFont("SourceSerif4-Semibold.ttf", "SerifSemibold");
        fonts.AddFont("SourceSerif4-Italic.ttf", "SerifItalic");
        fonts.AddFont("SourceSans3-Regular.ttf", "Sans");
        fonts.AddFont("SourceSans3-Semibold.ttf", "SansSemibold");
        fonts.AddFont("SourceSerif4-Black.ttf", "SerifBlack");
        fonts.AddFont("CascadiaCode-Regular.ttf", "Mono");
        fonts.AddFont("PinyonScript-Regular.ttf", "Script");
    }
}
