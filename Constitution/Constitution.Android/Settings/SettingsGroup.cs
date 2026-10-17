using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;

namespace Constitution.Settings;

/// <summary>
/// One inline accordion group (DESIGN.md section 7): a hairline-outlined header that reads as a
/// sentence, "Pillars 3 on", and opens in place, with no overlay, dialog or sheet. Built the way
/// <see cref="AccordionPicker"/> is, but holding rows rather than one choice. The page keeps which
/// group is open, so a rebuild does not close it.
/// </summary>
public sealed class SettingsGroup : LinearLayout
{
    public SettingsGroup(Context context, string title, string summary, bool open, Action onToggle, View body)
        : base(context)
    {
        Orientation = Orientation.Vertical;
        Background = PaperUi.Outline(
            context, PaperTheme.Paper, open ? PaperTheme.Accent : PaperTheme.Hairline, DesignTokens.Shape.RadiusInput);

        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X3);
        header.SetPadding(pad, 0, pad, 0);
        header.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        header.Clickable = true;
        header.Click += (_, _) => onToggle();

        header.AddView(PaperUi.Heading(context, title));
        if (summary.Length > 0)
        {
            var muted = PaperUi.Secondary(context, summary);
            muted.SetPadding(PaperTheme.Dp(context, DesignTokens.Space.X2), 0, 0, 0);
            header.AddView(muted);
        }

        AddView(header, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        if (open)
        {
            body.SetPadding(pad, 0, pad, pad);
            AddView(body, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }
    }
}
