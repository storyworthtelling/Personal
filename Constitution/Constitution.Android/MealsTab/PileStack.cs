using Android.Content;
using Android.Views;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// One pile of recipe cards (book p.13): each card showing only its title, overlapping the next at a loose angle, no tape.
/// <see cref="PileLayout.Place"/> decides where every card lies; this measures to it and positions the card views. A picked
/// card leaves a dashed gap in its place. A tap on a card, or on its gap, is the area's to decide; a long-press raises the
/// cuisine card.
/// </summary>
internal sealed class PileStack : ViewGroup
{
    private readonly Pile pile;
    private readonly RecipeId? gap;
    private readonly IReadOnlySet<RecipeId> planned;
    private readonly List<View> cards = [];
    private PileSpread? spread;

    public PileStack(Context context, Pile pile, RecipeId? gap, IReadOnlySet<RecipeId> planned, Action<Recipe> onTap, Action<Recipe> onHold) : base(context)
    {
        this.pile = pile;
        this.gap = gap;
        this.planned = planned;
        foreach (var card in pile.Cards)
        {
            var view = card.Id == gap ? GapView(context) : CardView(context, card);
            var recipe = card;
            view.Click += (_, _) => onTap(recipe);
            view.LongClick += (_, args) =>
            {
                onHold(recipe);
                args.Handled = true;
            };
            cards.Add(view);
            AddView(view);
        }
    }

    private View CardView(Context context, Recipe card)
    {
        var row = new Android.Widget.LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.Top);
        row.Background = PaperUi.Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X3);
        row.SetPadding(pad, PaperTheme.Dp(context, DesignTokens.Space.X2), pad, 0);
        var title = PaperUi.Body(context, card.Title);
        title.SetSingleLine(true);
        title.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        row.AddView(title, new Android.Widget.LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        if (planned.Contains(card.Id))
        {
            row.AddView(PaperUi.Secondary(context, "Next week"));
        }

        return row;
    }

    private static View GapView(Context context)
    {
        var view = new View(context);
        var outline = PaperUi.Outline(context, PaperTheme.Paper, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        outline.SetStroke(PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp), PaperTheme.Hairline, PaperTheme.Dp(context, DesignTokens.Space.X1), PaperTheme.Dp(context, DesignTokens.Space.X1));
        view.Background = outline;
        return view;
    }

    private PileSpread SpreadFor(int widthPx)
    {
        var density = Context!.Resources!.DisplayMetrics!.Density;
        return PileLayout.Place(pile, (int)(widthPx / density), gap);
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var placed = SpreadFor(width);
        spread = placed;
        for (var index = 0; index < cards.Count; index++)
        {
            var at = placed.Cards[index];
            cards[index].Measure(
                MeasureSpec.MakeMeasureSpec(PaperTheme.Dp(Context!, at.WidthDp), MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec(PaperTheme.Dp(Context!, at.HeightDp), MeasureSpecMode.Exactly));
        }

        SetMeasuredDimension(width, PaperTheme.Dp(Context!, placed.HeightDp));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        if (spread is null)
        {
            return;
        }

        for (var index = 0; index < cards.Count; index++)
        {
            var at = spread.Cards[index];
            var x = PaperTheme.Dp(Context!, at.LeftDp);
            var y = PaperTheme.Dp(Context!, at.TopDp);
            cards[index].Layout(x, y, x + PaperTheme.Dp(Context!, at.WidthDp), y + PaperTheme.Dp(Context!, at.HeightDp));
            cards[index].Rotation = at.AngleDegrees;
        }
    }
}
