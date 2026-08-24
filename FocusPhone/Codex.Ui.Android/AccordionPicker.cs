using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>One choice: what it is called, and what it means if that is not obvious.</summary>
public sealed record PickerOption(string Label, string? Detail = null);

/// <summary>
/// The picker (DESIGN.md §7): the field <b>expands in place</b> — no overlay, no
/// dialog, no bottom sheet. Collapsed it reads as a sentence with the current value
/// in it; open, the choices appear beneath it and the selected one carries §8's
/// highlighter wash.
///
/// This is what replaces the two AlertDialogs the audio head still uses for the
/// queue arrangement and the playback speed, and it is what the playlist rule editor
/// is built from.
/// </summary>
public class AccordionPicker : LinearLayout
{
    private readonly LinearLayout header;
    private readonly TextView value;
    private readonly LinearLayout options;
    private readonly List<PickerOption> choices = [];
    private int selectedIndex;
    private bool open;

    public AccordionPicker(Context context, string prefix, IEnumerable<PickerOption> items, int selected)
        : base(context)
    {
        Orientation = Orientation.Vertical;
        choices.AddRange(items);
        selectedIndex = selected;

        Background = PaperUi.Outline(
            context, PaperTheme.Paper, PaperTheme.Hairline, DesignTokens.Shape.RadiusInput);

        header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X3);
        header.SetPadding(pad, 0, pad, 0);
        header.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        header.Click += (_, _) => Toggle();

        var label = PaperUi.Body(context, prefix);
        label.SetTextColor(PaperTheme.InkMuted);
        header.AddView(label);

        value = PaperUi.Body(context, Current.Label);
        value.SetSingleLine(true);
        value.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        var gap = PaperTheme.Dp(context, DesignTokens.Space.X1);
        value.SetPadding(gap, 0, 0, 0);
        header.AddView(value, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        AddView(header, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        options = new LinearLayout(context) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        options.SetBackgroundColor(PaperTheme.PaperDeep);
        AddView(options, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        Rebuild();
    }

    /// <summary>Fires with the chosen index. Choosing closes the field.</summary>
    public event Action<int>? Selected;

    public PickerOption Current => choices[selectedIndex];

    public void Toggle()
    {
        open = !open;
        options.Visibility = open ? ViewStates.Visible : ViewStates.Gone;
        Background = PaperUi.Outline(
            Context!, PaperTheme.Paper,
            open ? PaperTheme.Accent : PaperTheme.Hairline,
            DesignTokens.Shape.RadiusInput);
    }

    private void Rebuild()
    {
        options.RemoveAllViews();
        for (var i = 0; i < choices.Count; i++)
        {
            options.AddView(BuildOption(i));
        }
    }

    private View BuildOption(int index)
    {
        var choice = choices[index];
        var row = new LinearLayout(Context!) { Orientation = Orientation.Vertical };
        var pad = PaperTheme.Dp(Context!, DesignTokens.Space.X3);
        var vertical = PaperTheme.Dp(Context!, DesignTokens.Space.X2);
        row.SetPadding(pad, vertical, pad, vertical);
        row.SetMinimumHeight(PaperTheme.Dp(Context!, DesignTokens.Shape.TouchTargetDp));

        var chosen = index == selectedIndex;
        if (chosen)
        {
            // §8 Selected: the highlighter wash of the functional hue, behind
            // full-strength ink — never a dimmed row, which would mean disabled.
            row.SetBackgroundColor(Wash(PaperTheme.Accent));
        }

        var label = PaperUi.Body(Context!, choice.Label);
        if (chosen)
        {
            label.SetTypeface(PaperTheme.SansSemibold(Context!), Android.Graphics.TypefaceStyle.Normal);
        }

        row.AddView(label);

        if (!string.IsNullOrWhiteSpace(choice.Detail))
        {
            row.AddView(PaperUi.Secondary(Context!, choice.Detail));
        }

        row.Clickable = true;
        row.Click += (_, _) =>
        {
            selectedIndex = index;
            value.Text = choice.Label;
            Rebuild();
            Toggle();
            Selected?.Invoke(index);
        };

        return row;
    }

    /// <summary>The highlighter alpha DESIGN.md §3 gives the wash tier (~30%).</summary>
    private static Android.Graphics.Color Wash(Android.Graphics.Color hue) =>
        new(hue.R, hue.G, hue.B, 77);
}
