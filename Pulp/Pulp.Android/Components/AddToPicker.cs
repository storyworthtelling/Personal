using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Actions;
using DomainPicker = Pulp.Domain.Actions.AddToPickerPlan;

namespace Pulp.Components;

/// <summary>
/// What the page does when a row is ticked or unticked. The picker has already shown the new state
/// (the filled box is the success, with no toast — D257); for <see cref="PickerChange.RemoveUndoFirst"/>
/// the callback returns the act's undo, which the picker hands to the dog-ear.
/// </summary>
public delegate Action? PickerChangeHandler(PickerRow row, PickerChange change);

/// <summary>
/// Add to… (mockup 11, D259): the inline picker that grows up out of the page's bar, over the lower
/// half — no dialog (§7). Two groups (D254): the viewer's playlists, last-used first, and each child's
/// Favorites; each row carries the §7 checkbox (D257) and a child's row one neutral badge (D258).
/// <see cref="AddToPickerPlan"/> decides the rows; this draws and wires them.
///
/// <para>Not the kit's <c>AccordionPicker</c>: that field is single-choice and closes on a pick, while
/// this one is several ticks at once and stays open until folded.</para>
/// </summary>
public static class AddToPicker
{
    /// <summary>The bar's button label: drawn "Add to… ▴" while the picker is open.</summary>
    public static string ButtonLabel(bool open) => open ? ActionCardPlan.AddToLabel + " ▴" : ActionCardPlan.AddToLabel;

    public static bool IsOpen(FrameLayout overlay) => overlay.FindViewWithTag(HostTag) is not null;

    /// <summary>Folds the picker (tap outside, or the button again).</summary>
    public static void Fold(FrameLayout overlay)
    {
        if (overlay.FindViewWithTag(HostTag) is View existing)
        {
            overlay.RemoveView(existing);
        }
    }

    /// <param name="onFolded">Called when the picker folds by a tap outside, so the bar's button can say so.</param>
    /// <param name="onNewPlaylist">A name typed in place under "New playlist…" (personal by default).</param>
    public static void Show(
        Context context,
        FrameLayout overlay,
        PickerModel model,
        PickerChangeHandler onChange,
        Action<string> onNewPlaylist,
        Action? onFolded = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(onChange);
        ArgumentNullException.ThrowIfNull(onNewPlaylist);

        Fold(overlay);

        // Tapping the page above the panel folds it; the panel swallows its own taps.
        var catcher = new FrameLayout(context) { Tag = HostTag };
        catcher.Click += (_, _) =>
        {
            Fold(overlay);
            onFolded?.Invoke();
        };

        var panel = BuildPanel(context, overlay, model, onChange, onNewPlaylist);
        var scroll = new ScrollView(context);
        scroll.AddView(panel);

        // A sheet of bright paper with a hairline along its top edge, as drawn.
        var sheet = new LinearLayout(context) { Orientation = Orientation.Vertical };
        sheet.SetBackgroundColor(PaperTheme.PaperBright);
        sheet.Clickable = true;
        sheet.AddView(Hairline(context));

        // Over the page's lower half, as drawn; before the frame is measured, as tall as it needs.
        var measured = overlay.Height > 0;
        var height = measured ? overlay.Height / 2 : ViewGroup.LayoutParams.WrapContent;
        sheet.AddView(scroll, measured
            ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 }
            : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        catcher.AddView(sheet, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, height, GravityFlags.Bottom));

        overlay.AddView(catcher, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    private static LinearLayout BuildPanel(
        Context context, FrameLayout overlay, PickerModel model, PickerChangeHandler onChange, Action<string> onNewPlaylist)
    {
        var panel = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(context, DesignTokens.Shape.ScreenEdgeDp);
        panel.SetPadding(edge, PaperTheme.Dp(context, DesignTokens.Space.X4), edge, PaperTheme.Dp(context, DesignTokens.Space.X2));

        panel.AddView(PaperUi.Heading(context, model.Heading));

        panel.AddView(GroupLabel(context, DomainPicker.PlaylistsLabel));
        foreach (var row in model.Playlists)
        {
            panel.AddView(BuildRow(context, overlay, row, onChange));
            panel.AddView(Hairline(context));
        }

        panel.AddView(NewPlaylistRow(context, onNewPlaylist));

        if (model.Favorites.Count > 0)
        {
            panel.AddView(Hairline(context));
            panel.AddView(GroupLabel(context, DomainPicker.FavoritesLabel));
            foreach (var row in model.Favorites)
            {
                panel.AddView(BuildRow(context, overlay, row, onChange));
                panel.AddView(Hairline(context));
            }
        }

        return panel;
    }

    private static View BuildRow(Context context, FrameLayout overlay, PickerRow row, PickerChangeHandler onChange)
    {
        var line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));

        var box = new Checkbox(context);
        box.SetPicked(row.Ticked);
        var boxSize = PaperTheme.Dp(context, DesignTokens.Shape.CheckboxDp);
        line.AddView(box, new LinearLayout.LayoutParams(boxSize, boxSize)
        {
            MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X3),
        });

        var name = PaperUi.Body(context, row.Label);
        name.SetSingleLine(true);
        name.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        line.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (row.Tag is { } tag)
        {
            line.AddView(PaperUi.Secondary(context, tag));
        }
        else if (DomainPicker.BadgeLabel(row.Badge) is { } badge)
        {
            line.AddView(Badge(context, badge));
        }

        if (!row.Enabled)
        {
            // §8 Disabled: dimmed, never removed — the row says why with its badge.
            line.Alpha = DesignTokens.Alpha.DisabledOpacity;
            return line;
        }

        var current = row;
        line.Clickable = true;
        line.Click += (_, _) =>
        {
            var change = DomainPicker.Tap(current);
            if (change == PickerChange.Refused)
            {
                return;
            }

            var before = current;
            var ticked = change != PickerChange.RemoveUndoFirst;
            current = current with { Ticked = ticked };
            box.SetPicked(ticked);
            var undo = onChange(before, change);

            if (change != PickerChange.RemoveUndoFirst)
            {
                return;
            }

            var reverse = undo ?? throw new InvalidOperationException(
                $"Unticking '{before.Label}' is undo-first (D257), but the page returned no undo.");
            DogEar.Show(context, overlay, $"Put {before.Label} back.", () =>
            {
                current = before;
                box.SetPicked(true);
                reverse();
            });
        };

        return line;
    }

    // "New playlist…" names one in place: the row becomes a field, and Done adds it.
    private static View NewPlaylistRow(Context context, Action<string> onNewPlaylist)
    {
        var holder = new FrameLayout(context);
        holder.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));

        var label = PaperUi.Body(context, DomainPicker.NewPlaylistLabel);
        label.SetTypeface(PaperTheme.SansSemibold(context), TypefaceStyle.Normal);
        label.Gravity = GravityFlags.CenterVertical;
        holder.AddView(label, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp)));

        label.Click += (_, _) =>
        {
            var field = PaperUi.Field(context, "Name the playlist");
            field.SetSingleLine(true);
            field.ImeOptions = ImeAction.Done;
            field.EditorAction += (_, args) =>
            {
                args.Handled = false;
                if (args.ActionId != ImeAction.Done)
                {
                    return;
                }

                var name = field.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }

                args.Handled = true;
                onNewPlaylist(name);
            };

            holder.RemoveAllViews();
            holder.AddView(field, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            field.RequestFocus();
        };

        return holder;
    }

    private static TextView GroupLabel(Context context, string text)
    {
        var label = PaperUi.Label(context, text);
        label.SetAllCaps(true);
        label.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X4), 0, PaperTheme.Dp(context, DesignTokens.Space.X1));
        return label;
    }

    // The one neutral badge (D258): a hairline pill, label type, muted ink — no colour.
    private static TextView Badge(Context context, string text)
    {
        var badge = PaperUi.Label(context, text);
        badge.Background = PaperUi.Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusButton);
        var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X2);
        badge.SetPadding(horizontal, 0, horizontal, 0);
        return badge;
    }

    private static View Hairline(Context context)
    {
        var rule = new View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    private static readonly Java.Lang.String HostTag = new("pulp-add-to-picker");
}
