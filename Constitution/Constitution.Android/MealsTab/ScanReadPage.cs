using Android.Content;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The scanned card, shown for checking (book p.14, "After a scan"): the line that says the picture is not kept, the card as it was
/// read with its hard words under a Caution highlighter wash, then one choice row per hard word with the best reading already
/// chosen, two alternatives and "Other…" for typing. Nothing else on the card is asked about, except a title when the card gave
/// none. A clean scan has no rows. Success is the card going into the pile, not a message: the actions live in the pinned bar.
/// Decides nothing: every word, line and choice is <see cref="ScanConfirmModel"/>'s.
/// </summary>
internal sealed class ScanReadPage : ScrollView
{
    private const float Tilt = -0.4f;

    private readonly Action<int, int> onChoose;
    private readonly Action<int, string> onOther;
    private readonly Action<string> onTitle;
    private readonly HashSet<int> typing = [];
    private readonly LinearLayout column;
    private ScanConfirmModel model;

    public ScanReadPage(Context context, ScanConfirmModel model, Action<int, int> onChoose, Action<int, string> onOther, Action<string> onTitle) : base(context)
    {
        this.onChoose = onChoose;
        this.onOther = onOther;
        this.onTitle = onTitle;
        this.model = model;
        column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.SetPadding(0, 0, 0, PaperTheme.Dp(context, DesignTokens.Space.X8));
        AddView(column, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        Show(model);
    }

    /// <summary>Redraws for the model as it stands now. Typing a title does not redraw; a choice does.</summary>
    public void Show(ScanConfirmModel current)
    {
        model = current;
        column.RemoveAllViews();
        var context = Context!;
        column.AddView(PaperUi.Secondary(context, model.StatusSentence));
        column.AddView(Card(context, model), Spaced(context, DesignTokens.Space.X4));
        if (model.Questions.Count > 0)
        {
            column.AddView(Questions(context, model), Spaced(context, DesignTokens.Space.X4));
        }
    }

    private View Card(Context context, ScanConfirmModel model)
    {
        var card = new LinearLayout(context) { Orientation = Orientation.Vertical };
        card.Background = PaperUi.Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        card.SetPadding(pad, pad, pad, pad);
        card.Rotation = Tilt;

        card.AddView(PaperUi.Label(context, model.Label));
        if (model.NeedsTitle)
        {
            var title = PaperUi.Field(context, ScanConfirmModel.TitleSentence);
            title.SetSingleLine(true);
            title.TextChanged += (_, _) => onTitle(title.Text ?? string.Empty);
            card.AddView(title, Spaced(context, DesignTokens.Space.X2));
        }
        else
        {
            card.AddView(PaperUi.Heading(context, model.Title));
        }

        foreach (var line in model.Lines)
        {
            card.AddView(Line(context, line), Spaced(context, DesignTokens.Space.X1));
        }

        return card;
    }

    private static TextView Line(Context context, ScanCardLine line)
    {
        var view = PaperUi.Body(context, line.Text);
        var text = new SpannableString(line.Text);
        var wash = PaperTheme.Wash(PaperTheme.CautionMark);
        var at = 0;
        foreach (var segment in line.Segments)
        {
            if (segment.Marked)
            {
                text.SetSpan(new BackgroundColorSpan(wash), at, at + segment.Text.Length, SpanTypes.ExclusiveExclusive);
            }

            at += segment.Text.Length;
        }

        view.SetText(text, TextView.BufferType.Spannable);
        return view;
    }

    private View Questions(Context context, ScanConfirmModel model)
    {
        var box = new LinearLayout(context) { Orientation = Orientation.Vertical };
        box.Background = PaperUi.Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        box.SetPadding(pad, pad, pad, pad);

        box.AddView(PaperUi.Heading(context, model.QuestionHeading!));
        var questions = model.Questions;
        for (var index = 0; index < questions.Count; index++)
        {
            box.AddView(Row(context, index, questions[index]), Spaced(context, DesignTokens.Space.X2));
            if (questions[index].IsOther || typing.Contains(index))
            {
                box.AddView(OtherField(context, index, questions[index]), Spaced(context, DesignTokens.Space.X1));
            }
        }

        return box;
    }

    private View Row(Context context, int question, ScanQuestion asked)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        var label = PaperUi.Secondary(context, asked.Label);
        row.AddView(label, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });

        for (var option = 0; option < asked.Options.Count; option++)
        {
            var chosen = option;
            row.AddView(Chip(context, asked.Options[option], asked.Selected == option, () => onChoose(question, chosen)));
        }

        row.AddView(Chip(context, ScanQuestionText.OtherOption, asked.IsOther, () => OpenOther(question)));
        return row;
    }

    private void OpenOther(int question)
    {
        typing.Add(question);
        Show(model);
    }

    private TextView Chip(Context context, string text, bool on, Action onTap)
    {
        var chip = PaperUi.Label(context, text);
        chip.SetTextColor(PaperTheme.Ink);
        chip.Background = PaperUi.Outline(
            context,
            on ? PaperTheme.Wash(PaperTheme.PositiveMark) : PaperTheme.PaperBright,
            on ? PaperTheme.Wash(PaperTheme.PositiveMark) : PaperTheme.Hairline,
            DesignTokens.Shape.RadiusInput);
        var sideways = PaperTheme.Dp(context, DesignTokens.Space.X3);
        var upright = PaperTheme.Dp(context, DesignTokens.Space.X2);
        chip.SetPadding(sideways, upright, sideways, upright);
        chip.SetMinHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        chip.Gravity = GravityFlags.Center;
        chip.Click += (_, _) => onTap();
        chip.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X1),
        };
        return chip;
    }

    private EditText OtherField(Context context, int question, ScanQuestion asked)
    {
        var field = PaperUi.Field(context, ScanQuestionText.OtherOption);
        field.SetSingleLine(true);
        field.ImeOptions = ImeAction.Done;
        field.Text = asked.OtherText ?? string.Empty;
        field.EditorAction += (_, args) =>
        {
            if (args.ActionId == ImeAction.Done)
            {
                onOther(question, field.Text ?? string.Empty);
            }
        };
        field.FocusChange += (_, args) =>
        {
            if (!args.HasFocus)
            {
                onOther(question, field.Text ?? string.Empty);
            }
        };
        return field;
    }

    private static LinearLayout.LayoutParams Spaced(Context context, int topDp)
    {
        var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        parameters.TopMargin = PaperTheme.Dp(context, topDp);
        return parameters;
    }
}
