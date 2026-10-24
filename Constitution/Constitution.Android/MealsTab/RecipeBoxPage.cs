using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>What the box shows at one moment: the model, the card out of its pile, and what the pile hands back.</summary>
internal sealed record BoxScreen(RecipeBoxScreenModel Model, CardFace? Face, RecipeId? Picked, IReadOnlySet<RecipeId> Planned);

/// <summary>
/// The recipe box half (book p.13): the search field under the switcher, then the piles, each under its label strip, with a
/// picked card laid on top. Typing narrows the piles in place by rebuilding only the piles, so the field keeps its focus.
/// The well ends above the pinned bar, so no card scrolls behind the button. Decides nothing: every placement is
/// <see cref="PileLayout"/>, every word is a model's.
/// </summary>
internal sealed class RecipeBoxPage : LinearLayout
{
    private readonly Action<string> onQuery;
    private readonly Action<Recipe> onTap;
    private readonly Action<Recipe> onHold;
    private readonly Action onCook;
    private readonly Action onPutBack;
    private readonly EditText? find;
    private readonly FrameLayout body;

    public RecipeBoxPage(
        Context context,
        BoxScreen screen,
        string query,
        Action<string> onQuery,
        Action<Recipe> onTap,
        Action<Recipe> onHold,
        Action onCook,
        Action onPutBack) : base(context)
    {
        this.onQuery = onQuery;
        this.onTap = onTap;
        this.onHold = onHold;
        this.onCook = onCook;
        this.onPutBack = onPutBack;
        Orientation = Orientation.Vertical;

        if (screen.Model.ShowsSearch)
        {
            find = PaperUi.Field(context, screen.Model.FindLabel);
            find.SetSingleLine(true);
            find.Text = query;
            find.TextChanged += OnTextChanged;
            AddView(find, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        body = new FrameLayout(context);
        AddView(body, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        Show(screen);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs args) => onQuery(find?.Text ?? string.Empty);

    /// <summary>Redraws the well for a new state. The search field is left alone.</summary>
    public void Show(BoxScreen screen)
    {
        body.RemoveAllViews();
        var context = Context!;
        if (screen.Model.Empty is { } empty)
        {
            body.AddView(EmptyBoxLayout.Build(context, empty));
            return;
        }

        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        if (screen.Face is { } face)
        {
            column.AddView(new PickedCardView(context, face, onCook, onPutBack), Spaced(context, DesignTokens.Space.X2));
        }

        if (screen.Model.NothingMatches is { } nothing)
        {
            column.AddView(PageFurniture.Sentence(context, nothing));
        }

        foreach (var pile in screen.Model.Piles)
        {
            column.AddView(PaperUi.Label(context, pile.Label), Spaced(context, DesignTokens.Space.X4));
            column.AddView(new PileStack(context, pile.Pile, screen.Picked, screen.Planned, TapOnPile, onHold), Spaced(context, DesignTokens.Space.X1));
        }

        var scroll = new ScrollView(context);
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        body.AddView(scroll);
    }

    private void TapOnPile(Recipe card) => onTap(card);

    private static LayoutParams Spaced(Context context, int topDp)
    {
        var parameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        parameters.TopMargin = PaperTheme.Dp(context, topDp);
        return parameters;
    }
}
