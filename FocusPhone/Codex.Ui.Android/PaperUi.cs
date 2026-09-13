using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Storyworthtelling.Updates;

namespace Codex.Ui;

/// <summary>
/// View factories implementing the DESIGN.md component recipes for
/// imperatively built screens. Every color, size, and spacing value comes
/// from DesignTokens via Theme — screens never style widgets directly.
/// </summary>
public static class PaperUi
{
    // ---- Text ----

    public static TextView Display(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Display, PaperTheme.Ink, PaperTheme.SerifBlack(context));

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

    /// <summary>
    /// Field labels, chips and tab labels (DESIGN.md §4): Sans SemiBold at the
    /// smallest step. Muted by default — a label names a thing, it is not the thing.
    /// </summary>
    public static TextView Label(Context context, string text) =>
        Text(context, text, DesignTokens.TypeSp.Label, PaperTheme.InkMuted, PaperTheme.SansSemibold(context));

    /// <summary>
    /// The owner's voice: Pinyon Script in ink-blue (DESIGN.md §4 reserves the
    /// calligraphy hand for this and nothing else). Wraps, because a voiced line is
    /// prose rather than a label.
    /// </summary>
    public static TextView Voice(Context context, string text)
    {
        var view = Text(context, text, DesignTokens.TypeSp.Voice, PaperTheme.InkBlue, PaperTheme.Script(context));
        view.SetLineSpacing(0f, 1.25f);
        return view;
    }

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

    /// <summary>Applies the paper list recipe: transparent over the grained
    /// window background, hairline dividers.</summary>
    public static void StylePaperList(Context context, ListView list)
    {
        list.SetBackgroundColor(Color.Transparent);
        list.CacheColorHint = Color.Transparent;
        list.Divider = new ColorDrawable(PaperTheme.Hairline);
        list.DividerHeight = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp);
    }

    /// <summary>
    /// A styled paper list with its taps already wired — the pairing every screen
    /// that shows rows was writing out by hand, including the bounds check that stops
    /// a stale adapter position indexing past the rows it was built from.
    ///
    /// Long-press is optional and, when given, is marked handled: §9 gives long-press
    /// to the action card, so a row must not also fire its tap.
    /// </summary>
    public static ListView List(
        Context context, IReadOnlyList<PaperListRow> rows, Action<int> onClick,
        Action<int>? onLongClick = null, bool picking = false)
    {
        var list = new ListView(context);
        StylePaperList(context, list);
        list.Adapter = new PaperListAdapter(context, rows, picking);

        list.ItemClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < rows.Count)
            {
                onClick(e.Position);
            }
        };

        if (onLongClick is not null)
        {
            list.ItemLongClick += (_, e) =>
            {
                if (e.Position >= 0 && e.Position < rows.Count)
                {
                    onLongClick(e.Position);
                }

                e.Handled = true;
            };
        }

        return list;
    }

    // ---- Structure ----

    /// <summary>
    /// The in-page chapter-heading screen title (DESIGN.md §7), without its stroke.
    /// Prefer <see cref="ScreenHeader"/> — §7 says every screen opens with the title
    /// <i>and</i> a full-width marker underline; this is the bare title for the
    /// places that compose their own.
    /// </summary>
    public static TextView PageHeader(Context context, string title)
    {
        var view = Text(context, title, DesignTokens.TypeSp.Display, PaperTheme.Ink, PaperTheme.SerifBlack(context));
        view.SetPadding(0, 0, 0, PaperTheme.Dp(context, DesignTokens.Space.X6));
        return view;
    }

    /// <summary>
    /// How every screen opens (DESIGN.md §7): a Display Serif Black title with a
    /// full-width marker underline in the area's hue, and nothing else. There is no
    /// standing top app bar — the header <i>is</i> the chrome.
    /// </summary>
    public static LinearLayout ScreenHeader(Context context, string title, Color hue)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };

        var text = Text(context, title, DesignTokens.TypeSp.Display, PaperTheme.Ink, PaperTheme.SerifBlack(context));
        column.AddView(text, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        column.AddView(new MarkerUnderline(context, hue), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, MarkerUnderline.HeightDp)));

        column.SetPadding(0, 0, 0, PaperTheme.Dp(context, DesignTokens.Space.X3));
        return column;
    }

    /// <summary>
    /// The within-area view switch (DESIGN.md §7), for any number of segments —
    /// Cover · Queue · Lyrics, Easy · Steady · Hard, Recent · Artist · Title.
    ///
    /// The selection is §8's <b>highlighter wash</b> of the hue behind full-strength
    /// ink, not coloured text: a wash reads as "this one" at arm's length, where an
    /// inked label reads as a link. Hairline-divided cells inside one rounded
    /// outline, radius 12 and never a pill.
    ///
    /// Placed directly under the screen header, which across the app means one thing:
    /// <i>change what this page is showing</i>.
    /// </summary>
    public static LinearLayout Segmented(
        Context context, IReadOnlyList<string> labels, Action<int> onSelect, int initialIndex = 0,
        Color? hue = null, IReadOnlyList<bool>? enabled = null)
    {
        var tint = hue ?? PaperTheme.Accent;
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.Background = Outline(context, PaperTheme.Paper, PaperTheme.Hairline, DesignTokens.Shape.RadiusInput);
        // Each cell paints its selection wash as a flat rectangle (SetBackgroundColor below); without
        // clipping, the first/last cell's fill squares off past the row's own rounded corners. The
        // GradientDrawable background above provides a rounded-rect outline for free.
        row.ClipToOutline = true;

        var cells = new TextView[labels.Count];
        var selected = initialIndex;

        void Paint()
        {
            for (var i = 0; i < cells.Length; i++)
            {
                var live = enabled is null || i >= enabled.Count || enabled[i];
                var active = i == selected && live;
                cells[i].SetBackgroundColor(active
                    ? PaperTheme.Wash(tint)
                    : Color.Transparent);
                cells[i].SetTextColor(active ? PaperTheme.Ink : PaperTheme.InkMuted);
                cells[i].SetTypeface(
                    active ? PaperTheme.SansSemibold(context) : PaperTheme.SansRegular(context),
                    TypefaceStyle.Normal);
            }
        }

        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var cell = Text(context, labels[i], DesignTokens.TypeSp.Label, PaperTheme.InkMuted,
                PaperTheme.SansRegular(context));
            cell.Gravity = GravityFlags.Center;
            cell.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
            var pad = PaperTheme.Dp(context, DesignTokens.Space.X2);
            cell.SetPadding(pad, pad, pad, pad);

            // §8 Disabled: a segment with nothing behind it is dimmed and inert, never
            // removed — a control that changes shape as its content arrives is worse
            // than one with a greyed-out third.
            if (enabled is not null && index < enabled.Count && !enabled[index])
            {
                cell.Alpha = DesignTokens.Alpha.DisabledOpacity;
                cell.Clickable = false;
            }
            else
            {
                cell.Clickable = true;
                cell.Click += (_, _) =>
                {
                    selected = index;
                    Paint();
                    onSelect(index);
                };
            }

            cells[i] = cell;
            row.AddView(cell, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

            if (i < labels.Count - 1)
            {
                var divider = new View(context);
                divider.SetBackgroundColor(PaperTheme.Hairline);
                row.AddView(divider, new LinearLayout.LayoutParams(
                    PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp),
                    ViewGroup.LayoutParams.MatchParent));
            }
        }

        // Paint the starting selection without firing onSelect — a switch should
        // reflect the current state on open without acting on it.
        Paint();
        return row;
    }

    /// <summary>The highlighter alpha DESIGN.md §3 gives the wash tier (~30%).</summary>

    /// <summary>
    /// A two-state segmented selector, kept for the heads that still call it.
    /// Prefer <see cref="Segmented"/> — §8 wants the selection to be the highlighter
    /// wash rather than inked text, and most switches now have more than two states.
    /// </summary>
    public static LinearLayout SegmentedToggle(
        Context context, string leftLabel, string rightLabel, Action<int> onSelect, int initialIndex = 0)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        Button left = null!;
        Button right = null!;

        void Paint(int index)
        {
            left.SetTextColor(index == 0 ? PaperTheme.Accent : PaperTheme.InkMuted);
            right.SetTextColor(index == 1 ? PaperTheme.Accent : PaperTheme.InkMuted);
        }

        left = SecondaryButton(context, leftLabel, () => { Paint(0); onSelect(0); });
        right = SecondaryButton(context, rightLabel, () => { Paint(1); onSelect(1); });

        row.AddView(left, new LinearLayout.LayoutParams(0,
            Android.Views.ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(right, new LinearLayout.LayoutParams(0,
            Android.Views.ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });

        // Paint the starting selection without firing onSelect — a toggle should reflect the current
        // state on open (e.g. a radio that is already off) without acting on it.
        Paint(initialIndex);
        return row;
    }

    /// <summary>
    /// The quiet line at the foot of a Settings screen: app name, versionName, package id, in
    /// mono, centred, at Label size.
    ///
    /// <para>It answers one question the device cannot otherwise answer. There is no adb after
    /// the cutover and the update channel installs silently, so an update that changes nothing
    /// visible — which is most of them — is indistinguishable from no update at all. Flyleaf had
    /// this line and the other heads did not; it lives here now so all of them share one
    /// implementation and one wording.</para>
    ///
    /// <para>It shows the <i>versionName</i>, not the versionCode: the code is the update
    /// channel's business, the name is the owner's. A package manager that refuses prints
    /// <c>?</c> rather than a blank, so a failed lookup does not read as an app with no version.
    /// </para>
    /// </summary>
    public static TextView BuildNumber(Activity activity, string appName)
    {
        string? versionName = null;
        try
        {
            versionName = activity.PackageManager?
                .GetPackageInfo(activity.PackageName!, (PackageInfoFlags)0)?.VersionName;
        }
        catch (Exception)
        {
            // A refusal here must never take the Settings screen down with it.
        }

        var line = Text(activity, BuildLine.Format(appName, versionName, activity.PackageName),
            DesignTokens.TypeSp.Label, PaperTheme.InkMuted, PaperTheme.Mono(activity));
        line.Gravity = GravityFlags.CenterHorizontal;
        return line;
    }

    /// <summary>
    /// The pinned bottom bar: a hairline rule over a horizontal action row,
    /// fixed at the screen bottom (append after a weight-1 content view).
    /// </summary>
    public static LinearLayout BottomBar(Context context, params Android.Views.View[] actions)
    {
        var bar = new LinearLayout(context) { Orientation = Orientation.Vertical };

        var rule = new Android.Views.View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        bar.AddView(rule, new LinearLayout.LayoutParams(
            Android.Views.ViewGroup.LayoutParams.MatchParent,
            PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp)));

        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X6);
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
        row.SetPadding(horizontal, vertical, horizontal, vertical);
        foreach (var action in actions)
        {
            row.AddView(action, new LinearLayout.LayoutParams(0,
                Android.Views.ViewGroup.LayoutParams.WrapContent)
            {
                Weight = 1,
                MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X2),
            });
        }
        bar.AddView(row, new LinearLayout.LayoutParams(
            Android.Views.ViewGroup.LayoutParams.MatchParent,
            Android.Views.ViewGroup.LayoutParams.WrapContent));

        // Under the edge-to-edge layout that Android enforces at target SDK 35+
        // the window draws behind the system navigation bar, so this pinned bar
        // would sit beneath the gesture pill and its actions look like they
        // overlap it. Pad the bar down by the navigation-bar inset so the
        // buttons always clear the system bar. The freed strip below shows the
        // paper window background, matching the paper navigationBarColor.
        bar.SetOnApplyWindowInsetsListener(new NavigationBarInsetListener(bar.PaddingBottom));
        return bar;
    }

    /// <summary>
    /// Pads a view down by the system navigation-bar inset so a bottom-pinned
    /// bar clears the gesture pill under enforced edge-to-edge.
    /// </summary>
    private sealed class NavigationBarInsetListener : Java.Lang.Object, Android.Views.View.IOnApplyWindowInsetsListener
    {
        private readonly int basePaddingBottom;

        public NavigationBarInsetListener(int basePaddingBottom) => this.basePaddingBottom = basePaddingBottom;

        public Android.Views.WindowInsets OnApplyWindowInsets(Android.Views.View view, Android.Views.WindowInsets insets)
        {
            var bottom = insets.GetInsets(Android.Views.WindowInsets.Type.SystemBars()).Bottom;
            view.SetPadding(view.PaddingLeft, view.PaddingTop, view.PaddingRight, basePaddingBottom + bottom);
            return insets;
        }
    }

    /// <summary>
    /// The icon-only affordance for the swipe-up tray: a small ink chevron
    /// with a 48dp touch target (DESIGN.md §7 gesture hint).
    /// </summary>
    public static Android.Views.View HintChevron(Context context, Action onTap)
    {
        var chevron = new ChevronView(context)
        {
            ContentDescription = "Open app tray",
        };
        chevron.Click += (_, _) => onTap();
        return chevron;
    }

    private sealed class ChevronView : Android.Views.View
    {
        private readonly Paint paint;

        public ChevronView(Context context) : base(context)
        {
            paint = new Paint
            {
                AntiAlias = true,
                StrokeWidth = PaperTheme.Dp(context, 2),
                Color = PaperTheme.InkMuted,
                StrokeCap = Paint.Cap.Round,
            };
            paint.SetStyle(Paint.Style.Stroke);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            using var path = new Android.Graphics.Path();
            path.MoveTo(Width * 0.32f, Height * 0.58f);
            path.LineTo(Width * 0.50f, Height * 0.42f);
            path.LineTo(Width * 0.68f, Height * 0.58f);
            canvas.DrawPath(path, paint);
        }
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
