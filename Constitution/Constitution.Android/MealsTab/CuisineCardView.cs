using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The card a long-press on a recipe card raises (C-6.2.4, DESIGN.md sections 7 and 8): a centred paper card over a PaperDeep
/// haze that covers the whole screen, binder included, offering the cuisines. The current one is greyed with its reason and
/// none is hidden. A row does its move and the card goes; a tap on the haze dismisses it. Nothing is asked twice: the move
/// is undo-first through the dog-ear.
/// </summary>
internal sealed class CuisineCardView
{
    private const byte HazeAlpha = 219;
    private const float DimmedRow = 0.45f;
    private const int BinderGutterDp = 66;
    private static readonly Java.Lang.String HostTag = new("constitution-cuisine-card");

    private readonly Activity host;
    private readonly Action onClosed;
    private readonly FrameLayout frame;

    public CuisineCardView(Activity host, CuisineCard model, Action<Cuisine> chosen, Action onClosed)
    {
        this.host = host;
        this.onClosed = onClosed;
        frame = host.FindViewById<FrameLayout>(Android.Resource.Id.Content)!;
        Remove(frame);

        var scrim = new FrameLayout(host) { Tag = HostTag };
        scrim.SetBackgroundColor(new Color(PaperTheme.PaperDeep.R, PaperTheme.PaperDeep.G, PaperTheme.PaperDeep.B, HazeAlpha));
        scrim.Click += (_, _) => Dismiss();

        var card = new LinearLayout(host) { Orientation = Orientation.Vertical };
        card.Background = PaperUi.Outline(host, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        card.Click += (_, _) => { };

        var pad = PaperTheme.Dp(host, DesignTokens.Space.X4);
        var header = new LinearLayout(host) { Orientation = Orientation.Vertical };
        header.SetPadding(pad, pad, pad, PaperTheme.Dp(host, DesignTokens.Space.X3));
        header.AddView(PaperUi.Heading(host, model.Title));
        header.AddView(PaperUi.Secondary(host, model.Subtitle));
        card.AddView(header);
        card.AddView(Hairline());

        foreach (var choice in model.Rows)
        {
            card.AddView(Row(choice, chosen));
        }

        var edge = PaperTheme.Dp(host, DesignTokens.Shape.ScreenEdgeDp);
        var layout = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center);
        layout.SetMargins(edge, 0, edge + PaperTheme.Dp(host, BinderGutterDp), 0);
        scrim.AddView(card, layout);
        scrim.Alpha = 0f;
        frame.AddView(scrim, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        scrim.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();
    }

    public void Dismiss()
    {
        Remove(frame);
        onClosed();
    }

    private LinearLayout Row(CuisineRow choice, Action<Cuisine> chosen)
    {
        var pad = PaperTheme.Dp(host, DesignTokens.Space.X4);
        var row = new LinearLayout(host) { Orientation = Orientation.Vertical };
        row.SetPadding(pad, PaperTheme.Dp(host, DesignTokens.Space.X3), pad, PaperTheme.Dp(host, DesignTokens.Space.X3));
        row.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp));
        row.SetGravity(GravityFlags.CenterVertical);
        var label = PaperUi.Body(host, choice.Label);
        row.AddView(label);
        if (choice.IsEnabled)
        {
            var cuisine = choice.Cuisine;
            row.Click += (_, _) => chosen(cuisine);
        }
        else
        {
            label.Alpha = DimmedRow;
            row.AddView(PaperUi.Secondary(host, choice.Reason!));
        }

        return row;
    }

    private View Hairline()
    {
        var rule = new View(host);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(host, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    private static void Remove(FrameLayout frame)
    {
        if (frame.FindViewWithTag(HostTag) is { } existing)
        {
            frame.RemoveView(existing);
        }
    }
}
