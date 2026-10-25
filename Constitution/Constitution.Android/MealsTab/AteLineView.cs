using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Meals;

namespace Constitution.MealsTab;

/// <summary>
/// The line "I ate something else" writes on a meal's card (book p.15). A status ("Estimating your dinner…") is in the teal progress
/// voice; the result is "You had something else · about" in the card's own ink with the figure in mono, and under it, smaller and muted,
/// the guess in plain words (the range, how sure). Nothing here listens for a tap and nothing is a toast.
/// </summary>
internal static class AteLineView
{
    public static View Build(Context context, AteLine line)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X2), 0, 0);
        if (line.IsStatus)
        {
            column.AddView(Text(context, line.Lead, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Secondary, PaperTheme.ProgressText));
            return column;
        }

        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.AddView(Text(context, line.Lead, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Secondary, PaperTheme.Ink));
        row.AddView(Text(context, line.Figure ?? string.Empty, PaperTheme.Mono(context), DesignTokens.TypeSp.Mono, PaperTheme.Ink));
        column.AddView(row);
        if (line.Detail is not null)
        {
            column.AddView(Text(context, line.Detail, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Label, PaperTheme.InkMuted));
        }

        return column;
    }

    private static TextView Text(Context context, string text, Android.Graphics.Typeface face, float sizeSp, Android.Graphics.Color color)
    {
        var view = new TextView(context) { Text = text };
        view.SetTextSize(Android.Util.ComplexUnitType.Sp, sizeSp);
        view.SetTextColor(color);
        view.Typeface = face;
        view.SetIncludeFontPadding(false);
        return view;
    }
}
