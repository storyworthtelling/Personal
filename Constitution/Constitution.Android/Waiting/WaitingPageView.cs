using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Waiting;

namespace Constitution.Waiting;

/// <summary>
/// The Waiting page's body (book p.18), drawn from a <see cref="WaitingPageModel"/> and deciding
/// nothing itself: each notice written straight onto the page — a role-colour bar, what happened
/// with its relative date, what to do, and either one button or the two big chips of a
/// low-confidence read. No cards, no count, no spinner; a pick or a tap is a plain callback and the
/// shell redraws. It is transparent so the crinkled ground the shell paints behind it shows through.
/// </summary>
internal static class WaitingPageView
{
    // The role bar's width in the mockup.
    private const int RoleBarWidthDp = 4;

    public static View Build(
        Context context,
        WaitingPageModel model,
        Action<WaitingActionId> onAction,
        Action<WaitingItem, ChipChoice> onChip)
    {
        var edge = PaperTheme.Dp(context, DesignTokens.Shape.ScreenEdgeDp);
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.SetPadding(edge, 0, edge, PaperTheme.Dp(context, DesignTokens.Space.X8));

        var subhead = PaperUi.Secondary(context, "For Constitution · newest first");
        subhead.SetPadding(0, 0, 0, PaperTheme.Dp(context, DesignTokens.Space.X2));
        column.AddView(subhead);

        if (model.Items.Count == 0)
        {
            // The other half of "no count": once the last notice goes, the page says so in words.
            var empty = PaperUi.EmptyState(context, "Nothing waiting.");
            empty.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X12), 0, 0);
            column.AddView(empty);
        }

        foreach (var item in model.Items)
        {
            column.AddView(BuildRow(context, item, onAction, onChip));
        }

        var scroll = new ScrollView(context);
        scroll.AddView(column);
        return scroll;
    }

    private static View BuildRow(
        Context context, WaitingItem item, Action<WaitingActionId> onAction, Action<WaitingItem, ChipChoice> onChip)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
        row.SetPadding(0, vertical, 0, vertical);

        var bar = new View(context);
        bar.SetBackgroundColor(FoldView.HueOf(item.Role));
        row.AddView(bar, new LinearLayout.LayoutParams(
            PaperTheme.Dp(context, RoleBarWidthDp), ViewGroup.LayoutParams.MatchParent)
        {
            RightMargin = PaperTheme.Dp(context, DesignTokens.Space.X3),
        });

        var text = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var top = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        top.AddView(PaperUi.Heading(context, item.Title), new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var when = PaperUi.Secondary(context, item.DateText);
        when.SetPadding(PaperTheme.Dp(context, DesignTokens.Space.X2), 0, 0, 0);
        top.AddView(when, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        text.AddView(top);
        text.AddView(PaperUi.BodyMuted(context, item.Body));

        var below = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X2),
        };

        if (item.Chips.Count > 0)
        {
            text.AddView(BuildChips(context, item, onChip), below);
        }
        else if (item.Button is { } button)
        {
            // Never the primary style: nothing on this page is THE primary action (§7).
            text.AddView(PaperUi.SecondaryButton(context, button.Label, () => onAction(button.Action)), below);
        }

        row.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        return row;
    }

    // Two big chips; a pick is the answer, so there is nothing after them to confirm.
    private static View BuildChips(Context context, WaitingItem item, Action<WaitingItem, ChipChoice> onChip)
    {
        var chips = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        var big = PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp);
        foreach (var choice in item.Chips)
        {
            var picked = choice;
            var chip = PaperUi.SecondaryButton(context, picked.Value, () => onChip(item, picked));
            chip.SetMinimumWidth(big * 2);
            chip.SetMinimumHeight(big);
            chips.AddView(chip, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                RightMargin = PaperTheme.Dp(context, DesignTokens.Space.X3),
            });
        }

        return chips;
    }
}
