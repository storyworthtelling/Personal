using Microsoft.Maui.Handlers;

namespace Storyworthtelling.Maui;

/// <summary>
/// What a screen reader is told about the kit's labels (DESIGN.md §17, audit W9.1, report 10 §2), set once for every
/// head from the style a label wears, so no page has to remember it.
///
/// <list type="bullet">
/// <item>A Display, Title or Heading label is a heading, so TalkBack's heading navigation moves between them.</item>
/// <item>A status line or an error line is a polite live region: when its words change, TalkBack reads them, which is
/// what a spinner's arrival used to tell a sighted person (§6).</item>
/// </list>
///
/// <para>Installed from <see cref="PaperKit"/> beside <see cref="PaperMaterials"/>; a style is compared by identity with
/// the kit's own, so a page's ad-hoc style is left alone.</para>
/// </summary>
public static class PaperSemantics
{
    private static readonly string[] Headings = ["DisplayLabel", "TitleLabel", "HeadingLabel"];
    private static readonly string[] LiveLines = ["StatusLine", "ErrorLabel"];

    private static bool installed;

    public static void Install()
    {
        if (installed)
        {
            return;
        }
        installed = true;

        LabelHandler.Mapper.AppendToMapping("PaperSemantics", (handler, label) => Mark(handler.PlatformView, label as Label));
    }

    private static void Mark(global::Android.Widget.TextView platform, Label? label)
    {
        if (label?.Style is not { } style)
        {
            return;
        }

        if (Wears(style, Headings))
        {
            platform.AccessibilityHeading = true;
        }

        if (Wears(style, LiveLines))
        {
            platform.AccessibilityLiveRegion = global::Android.Views.AccessibilityLiveRegion.Polite;
        }
    }

    private static bool Wears(Style style, IEnumerable<string> keys) =>
        keys.Any(key => Application.Current?.Resources.TryGetValue(key, out var kit) == true && ReferenceEquals(kit, style));
}
