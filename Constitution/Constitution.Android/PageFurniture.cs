using Android.Content;
using Android.Graphics;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;

namespace Constitution;

/// <summary>
/// The text every tab's page is made of (C-3.2.4): a plain Serif sentence for what the page
/// says, and a teal status sentence for while it loads. Loading is a sentence, never a spinner
/// (DESIGN.md section 8). Sizes and colours come from the design tokens, nothing local.
/// </summary>
public static class PageFurniture
{
    /// <summary>A plain Serif sentence: no voice, no styling beyond the page's ink.</summary>
    public static TextView Sentence(Context context, string text) =>
        Build(context, text, PaperTheme.Ink);

    /// <summary>The loading line: the in-progress teal (waiting, not wrong).</summary>
    public static TextView StatusSentence(Context context, string text) =>
        Build(context, text, PaperTheme.ProgressText);

    private static TextView Build(Context context, string text, Color color)
    {
        var view = new TextView(context) { Text = text };
        view.SetTextSize(Android.Util.ComplexUnitType.Sp, DesignTokens.TypeSp.Body);
        view.SetTextColor(color);
        view.Typeface = PaperTheme.SerifRegular(context);
        view.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X3), 0, 0);
        return view;
    }
}
