using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Widget;
using FocusPhone.Domain.Design;

namespace FocusPhone.Ui;

/// <summary>
/// View factories implementing the DESIGN.md component recipes for
/// imperatively built screens. Every color, size, and spacing value comes
/// from DesignTokens via Theme — screens never style widgets directly.
/// </summary>
public static class PaperUi
{
    // ---- Text ----

    public static TextView Clock(Context context)
    {
        var view = Text(context, "", DesignTokens.TypeSp.Clock, PaperTheme.Ink, PaperTheme.SerifRegular(context));
        return view;
    }

    public static TextView Display(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Display, PaperTheme.Ink, PaperTheme.SerifSemibold(context));

    public static TextView Title(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Title, PaperTheme.Ink, PaperTheme.SerifSemibold(context));

    public static TextView Heading(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Heading, PaperTheme.Ink, PaperTheme.SerifSemibold(context));

    public static TextView Body(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Body, PaperTheme.Ink, PaperTheme.SansRegular(context));

    public static TextView BodyMuted(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Body, PaperTheme.InkSoft, PaperTheme.SansRegular(context));

    public static TextView Secondary(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Secondary, PaperTheme.InkMuted, PaperTheme.SansRegular(context));

    public static TextView Mono(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Mono, PaperTheme.Ink, PaperTheme.Mono(context));

    /// <summary>The unlock countdown: HH:MM:SS in Cascadia, the one oversized mono use.</summary>
    public static TextView Countdown(Context context) =>
        Text(context, "", DesignTokens.TypeSp.Countdown, PaperTheme.Ink, PaperTheme.Mono(context));

    /// <summary>Centered Serif-Italic invitation per the empty-state recipe.</summary>
    public static TextView EmptyState(Context context, string text)
    {
        var view = Text(context, text, DesignTokens.TypeSp.Body, PaperTheme.InkMuted, PaperTheme.SerifItalic(context));
        view.Gravity = Android.Views.GravityFlags.Center;
        return view;
    }

    private static TextView Text(Context context, string text, float sizeSp, Color color, Typeface typeface)
    {
        var view = new TextView(context) { Text = text, TextSize = sizeSp, Typeface = typeface };
        view.SetTextColor(color);
        return view;
    }

    // ---- Buttons ----

    public static Button PrimaryButton(Context context, string label, Action onClick)
    {
        var button = ButtonBase(context, label, onClick, PaperTheme.PaperBright, PaperTheme.SansSemibold(context));
        button.Background = PressableFill(context, PaperTheme.Accent, PaperTheme.AccentDeep);
        return button;
    }

    public static Button SecondaryButton(Context context, string label, Action onClick)
    {
        var button = ButtonBase(context, label, onClick, PaperTheme.Ink, PaperTheme.SansSemibold(context));
        button.Background = PressableOutline(context, PaperTheme.Hairline);
        return button;
    }

    public static Button QuietButton(Context context, string label, Action onClick)
    {
        var button = ButtonBase(context, label, onClick, PaperTheme.InkMuted, PaperTheme.SansSemibold(context));
        button.Background = null;
        return button;
    }

    public static Button DestructiveButton(Context context, string label, Action onClick)
    {
        var button = ButtonBase(context, label, onClick, PaperTheme.Accent, PaperTheme.SansSemibold(context));
        button.Background = PressableOutline(context, PaperTheme.Accent);
        return button;
    }

    private static Button ButtonBase(Context context, string label, Action onClick, Color textColor, Typeface typeface)
    {
        var button = new Button(context)
        {
            Text = label,
            TextSize = DesignTokens.TypeSp.Label,
            Typeface = typeface,
            TransformationMethod = null, // sentence case, never ALL CAPS
            StateListAnimator = null,    // flat paper, no elevation shadow
        };
        button.SetTextColor(textColor);
        button.SetPadding(
            PaperTheme.Dp(context, DesignTokens.Space.X4), PaperTheme.Dp(context, DesignTokens.Space.X3),
            PaperTheme.Dp(context, DesignTokens.Space.X4), PaperTheme.Dp(context, DesignTokens.Space.X3));
        button.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---- Inputs ----

    public static EditText Field(Context context, string hint)
    {
        var field = new EditText(context)
        {
            Hint = hint,
            TextSize = DesignTokens.TypeSp.Body,
            Typeface = PaperTheme.SansRegular(context),
        };
        StyleField(context, field, PaperTheme.PaperBright);
        return field;
    }

    /// <summary>Machine-text input (keys, tokens, URLs): Cascadia on deep paper.</summary>
    public static EditText MonoField(Context context, string hint)
    {
        var field = new EditText(context)
        {
            Hint = hint,
            TextSize = DesignTokens.TypeSp.Mono,
            Typeface = PaperTheme.Mono(context),
        };
        StyleField(context, field, PaperTheme.PaperDeep);
        return field;
    }

    private static void StyleField(Context context, EditText field, Color fill)
    {
        field.SetTextColor(PaperTheme.Ink);
        field.SetHintTextColor(PaperTheme.InkMuted);
        field.Background = Outline(context, fill, PaperTheme.Hairline, DesignTokens.Shape.RadiusInput);
        var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X4);
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
        field.SetPadding(horizontal, vertical, horizontal, vertical);
    }

    // ---- Cards ----

    /// <summary>Warning card: ink text on deep paper with a terracotta left rule.</summary>
    public static LinearLayout WarningCard(Context context, string text)
    {
        var card = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        card.Background = Fill(context, PaperTheme.PaperDeep, DesignTokens.Shape.RadiusCard);

        var rule = new Android.Views.View(context);
        rule.SetBackgroundColor(PaperTheme.Accent);
        card.AddView(rule, new LinearLayout.LayoutParams(
            PaperTheme.Dp(context, DesignTokens.Shape.WarningRuleDp),
            Android.Views.ViewGroup.LayoutParams.MatchParent));

        var body = Text(context, text, DesignTokens.TypeSp.Secondary, PaperTheme.Ink, PaperTheme.SansRegular(context));
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        body.SetPadding(pad, pad, pad, pad);
        card.AddView(body, new LinearLayout.LayoutParams(
            Android.Views.ViewGroup.LayoutParams.MatchParent,
            Android.Views.ViewGroup.LayoutParams.WrapContent));
        return card;
    }

    public static LinearLayout HairlineCard(Context context)
    {
        var card = new LinearLayout(context) { Orientation = Orientation.Vertical };
        card.Background = Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        card.SetPadding(pad, pad, pad, pad);
        return card;
    }

    // ---- Lists ----

    /// <summary>Applies the paper list recipe: paper background, hairline dividers.</summary>
    public static void StylePaperList(Context context, ListView list)
    {
        list.SetBackgroundColor(PaperTheme.Paper);
        list.Divider = new ColorDrawable(PaperTheme.Hairline);
        list.DividerHeight = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp);
    }

    // ---- Drawables ----

    public static GradientDrawable Fill(Context context, Color color, int radiusDp)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(PaperTheme.Dp(context, radiusDp));
        return drawable;
    }

    public static GradientDrawable Outline(Context context, Color fill, Color stroke, int radiusDp)
    {
        var drawable = Fill(context, fill, radiusDp);
        drawable.SetStroke(PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp), stroke);
        return drawable;
    }

    private static StateListDrawable PressableFill(Context context, Color normal, Color pressed)
    {
        var states = new StateListDrawable();
        states.AddState([Android.Resource.Attribute.StatePressed],
            Fill(context, pressed, DesignTokens.Shape.RadiusButton));
        states.AddState([], Fill(context, normal, DesignTokens.Shape.RadiusButton));
        return states;
    }

    private static StateListDrawable PressableOutline(Context context, Color stroke)
    {
        var states = new StateListDrawable();
        states.AddState([Android.Resource.Attribute.StatePressed],
            Outline(context, PaperTheme.PaperShade, stroke, DesignTokens.Shape.RadiusButton));
        states.AddState([], Outline(context, PaperTheme.Paper, stroke, DesignTokens.Shape.RadiusButton));
        return states;
    }
}
