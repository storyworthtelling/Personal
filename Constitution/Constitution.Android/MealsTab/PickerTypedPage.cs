using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The three boxes (C-6.5.4, D176): a title, the ingredients one to a line and the steps one to a line, on the page in place of the
/// box, empty for a card typed in or filled with what was read off a scan's photographs, editable either way.
/// What is typed is written straight into the <see cref="TypedEntry"/> the area holds, so a redraw or a turn of the fold loses
/// nothing. The actions are in the pinned bar. Decides nothing: the words, the labels and what the three fields come to are the
/// entry's.
/// </summary>
internal sealed class PickerTypedPage : ScrollView
{
    private const int StepLines = 6;

    public PickerTypedPage(Context context, TypedEntry entry) : base(context)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.SetPadding(0, 0, 0, PaperTheme.Dp(context, DesignTokens.Space.X8));
        AddView(column, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        column.AddView(PaperUi.Heading(context, entry.PageHeading));
        if (entry.FromPhotos)
        {
            column.AddView(PaperUi.BodyMuted(context, TypedEntry.ReadNote));
        }

        column.AddView(Labelled(context, TypedEntry.TitleLabel, entry.Title, 1, text => entry.Title = text));
        column.AddView(Labelled(context, TypedEntry.IngredientsLabel, entry.IngredientsText, StepLines, text => entry.IngredientsText = text));
        column.AddView(Labelled(context, TypedEntry.StepsLabel, entry.StepsText, StepLines, text => entry.StepsText = text));
    }

    private static LinearLayout Labelled(Context context, string label, string text, int lines, Action<string> onText)
    {
        var box = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var parameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X4),
        };
        box.LayoutParameters = parameters;
        box.AddView(PaperUi.Label(context, label));

        var field = PaperUi.Field(context, label);
        if (lines > 1)
        {
            field.SetSingleLine(false);
            field.InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine | InputTypes.TextFlagCapSentences;
            field.SetMinLines(lines);
            field.Gravity = GravityFlags.Top | GravityFlags.Start;
        }
        else
        {
            field.SetSingleLine(true);
        }

        field.Text = text;
        field.TextChanged += (_, _) => onText(field.Text ?? string.Empty);
        box.AddView(field);
        return box;
    }
}
