using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Actions;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;

namespace Pulp.Discover;

/// <summary>What a pile's controls ask the page to do with the front card.</summary>
internal sealed record PileHandlers(
    Action<DiscoverRow> Open,
    Action<DiscoverRow> AddTo,
    Action<DiscoverRow> NotForMe,
    Action<DiscoverRow> BringBack,
    Action<DiscoverRow> Trailer,
    Action<PosterDeck, string, int> WantsPoster);

/// <summary>
/// One of the two piles (mockup 03, D108, D219–D221): "Films  1 of 3" over a literal stack — the front
/// poster with the rest fanned behind — and beside it the title in Serif, "Film · 1h 44m" / "On … · 4K",
/// the three small buttons (+ Add to…, × Not for me, ▷ Trailer; D221), and "Swipe for the next". A
/// struck card dims, takes the ink-blue strike, and offers "Bring it back" where the buttons were
/// (D220). A sideways drag on the poster flips to the next card; a tap opens the title page.
/// </summary>
internal sealed class StackPileView : LinearLayout
{
    private readonly PileHandlers handlers;
    private readonly TextView position;
    private readonly PosterDeck deck;
    private readonly StruckTitleView title;
    private readonly TextView kindLine;
    private readonly TextView homeLine;
    private readonly LinearLayout buttons;
    private readonly Button bringBack;
    private IReadOnlyList<DiscoverCard> cards = [];
    private int index;

    public StackPileView(Context context, string heading, PileHandlers handlers) : base(context)
    {
        this.handlers = handlers;
        Orientation = Orientation.Vertical;

        // "Films  1 of 3": the Serif pile heading with its position in muted Sans beside it.
        var head = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.Bottom);
        head.AddView(PaperUi.Heading(context, heading));
        position = PaperUi.Label(context, string.Empty);
        position.SetTypeface(PaperTheme.SansRegular(context), TypefaceStyle.Normal);
        head.AddView(position, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });
        AddView(head, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X3),
            BottomMargin = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });

        var stack = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        deck = new PosterDeck(context);
        stack.AddView(deck, new LayoutParams(PaperTheme.Dp(context, PosterDeck.WidthDp), PaperTheme.Dp(context, PosterDeck.HeightDp))
        {
            MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X3),
            MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X4),
        });
        WireDeck(context);

        var info = new LinearLayout(context) { Orientation = Orientation.Vertical };
        info.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X1), 0, 0);
        title = new StruckTitleView(context)
        {
            TextSize = DesignTokens.TypeSp.Heading,
            Typeface = PaperTheme.SerifSemibold(context),
        };
        title.SetTextColor(PaperTheme.Ink);
        title.Click += (_, _) => Front(handlers.Open);
        info.AddView(title);

        kindLine = PaperUi.Secondary(context, string.Empty);
        homeLine = PaperUi.Secondary(context, string.Empty);
        info.AddView(kindLine, Gap(context, DesignTokens.Space.X1));
        info.AddView(homeLine);

        buttons = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        buttons.AddView(Small(context, "+", ActionCardPlan.AddToLabel, () => Front(handlers.AddTo)));
        buttons.AddView(Small(context, "×", "Not for me", () => Front(handlers.NotForMe)), Beside(context));
        buttons.AddView(Small(context, "▷", "Trailer", () => Front(handlers.Trailer)), Beside(context));
        info.AddView(buttons, Gap(context, DesignTokens.Space.X2));

        // D287: the kit's own quiet button, as it is.
        bringBack = PaperUi.QuietButton(context, DiscoverPageText.BringItBack, () => Front(handlers.BringBack));
        bringBack.SetPadding(0, 0, 0, 0);
        bringBack.Gravity = GravityFlags.CenterVertical | GravityFlags.Start;
        info.AddView(bringBack, Gap(context, DesignTokens.Space.X2));

        var hint = PaperUi.Label(context, DiscoverPageText.SwipeHint);
        hint.SetTypeface(PaperTheme.SansRegular(context), TypefaceStyle.Normal);
        info.AddView(hint, Gap(context, DesignTokens.Space.X2));

        stack.AddView(info, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        AddView(stack, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });
    }

    /// <summary>Shows the pile as it stands; the front card stays where it was unless the pile shrank.</summary>
    public void Bind(IReadOnlyList<DiscoverCard> pile)
    {
        cards = pile;
        if (cards.Count == 0)
        {
            Visibility = ViewStates.Gone;
            return;
        }

        Visibility = ViewStates.Visible;
        index = Math.Min(index, cards.Count - 1);
        Paint();
    }

    private void Paint()
    {
        var card = cards[index];
        var struck = card.State == CardState.Struck;

        position.Text = DiscoverPagePile.Position(index, cards.Count);
        deck.Show(card.Row.PosterUrl, DiscoverPagePile.FannedBehind(cards.Count), struck);
        deck.ContentDescription = card.Row.Name;

        title.Text = card.Row.Name;
        title.Struck = struck;
        title.SetTextColor(struck ? PaperTheme.InkMuted : PaperTheme.Ink);
        title.Invalidate();
        kindLine.Text = card.Row.KindLine;
        homeLine.Text = card.Row.HomeLine;

        buttons.Visibility = struck ? ViewStates.Gone : ViewStates.Visible;
        bringBack.Visibility = struck ? ViewStates.Visible : ViewStates.Gone;

        if (card.Row.PosterUrl is { } url)
        {
            handlers.WantsPoster(deck, url, PaperTheme.Dp(Context!, PosterDeck.WidthDp));
        }
    }

    private void Turn(DiscoverSwipe swipe)
    {
        if (cards.Count < 2)
        {
            return;
        }

        index = swipe == DiscoverSwipe.Next
            ? DiscoverPagePile.Next(index, cards.Count)
            : DiscoverPagePile.Previous(index, cards.Count);
        Paint();
    }

    private void Front(Action<DiscoverRow> act)
    {
        if (cards.Count > 0)
        {
            act(cards[index].Row);
        }
    }

    // A sideways drag flips the pile; a tap opens the title; a mostly vertical drag is the page's scroll.
    private void WireDeck(Context context)
    {
        var slop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
        var threshold = PaperTheme.Dp(context, DesignTokens.Space.X12);
        float downX = 0, downY = 0;

        deck.Touch += (_, args) =>
        {
            var motion = args.Event!;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    downX = motion.RawX;
                    downY = motion.RawY;
                    args.Handled = true;
                    break;
                case MotionEventActions.Move:
                    var dx = motion.RawX - downX;
                    var dy = motion.RawY - downY;
                    if (Math.Abs(dx) > slop && Math.Abs(dx) > Math.Abs(dy))
                    {
                        deck.Parent?.RequestDisallowInterceptTouchEvent(true);
                    }

                    args.Handled = true;
                    break;
                case MotionEventActions.Up:
                    var swipe = DiscoverPagePile.Swipe(motion.RawX - downX, motion.RawY - downY, threshold);
                    if (swipe != DiscoverSwipe.None)
                    {
                        Turn(swipe);
                    }
                    else if (Math.Abs(motion.RawX - downX) < slop && Math.Abs(motion.RawY - downY) < slop)
                    {
                        Front(handlers.Open);
                    }

                    args.Handled = true;
                    break;
                default:
                    args.Handled = false;
                    break;
            }
        };
    }

    private static Button Small(Context context, string glyph, string label, Action onClick)
    {
        var button = PaperUi.SecondaryButton(context, glyph, onClick);
        button.ContentDescription = label;
        button.SetPadding(0, 0, 0, 0);
        var size = PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp);
        button.SetMinimumWidth(size);
        button.SetMinimumHeight(size);
        button.LayoutParameters = new LayoutParams(size, size);
        return button;
    }

    private static LayoutParams Beside(Context context)
    {
        var size = PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp);
        return new LayoutParams(size, size) { MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2) };
    }

    private static LayoutParams Gap(Context context, int topDp) =>
        new(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { TopMargin = PaperTheme.Dp(context, topDp) };
}
