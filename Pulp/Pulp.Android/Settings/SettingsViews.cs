using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Shell;
using Pulp.Shell;

// Android.Graphics.Path, not System.IO.Path — the implicit usings bring both in.
using Path = Android.Graphics.Path;

namespace Pulp.Settings;

/// <summary>
/// The pieces Settings and its two pages are drawn from (mockup 09): a group that folds — Serif
/// heading, its quiet line in the muted ink, a chevron, a hairline under it — the pine tick a service
/// the household has carries, the one neutral badge (D249), a checkbox row, and the binder frame with
/// the Settings tab lit. Everything takes the neutral; nothing here wears the identity or a role (§9).
/// </summary>
internal static class SettingsViews
{
    /// <summary>
    /// One folding group. <paramref name="isOpen"/> and <paramref name="toggle"/> let the area keep
    /// which groups are open across repaints; the body is built only while the group is open.
    /// </summary>
    public static View Group(Context context, string title, string? meta, bool isOpen, Action toggle, Action<LinearLayout>? fill)
    {
        var group = new LinearLayout(context) { Orientation = Orientation.Vertical };

        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.ListRowMinHeightDp));
        header.AddView(PaperUi.Heading(context, title), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (!string.IsNullOrWhiteSpace(meta))
        {
            var line = PaperUi.Label(context, meta);
            line.Typeface = PaperTheme.SansRegular(context);
            line.SetTextColor(PaperTheme.InkMuted);
            line.SetSingleLine(true);
            line.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            header.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X3),
            });
        }

        var size = PaperTheme.Dp(context, DesignTokens.Space.X4);
        header.AddView(new ChevronView(context, up: isOpen), new LinearLayout.LayoutParams(size, size)
        {
            MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });
        header.ContentDescription = $"{title}{(string.IsNullOrWhiteSpace(meta) ? string.Empty : ", " + meta)}, {(isOpen ? "open" : "closed")}";
        header.Clickable = true;
        header.Click += (_, _) => toggle();
        group.AddView(header);

        if (isOpen && fill is not null)
        {
            var body = new LinearLayout(context) { Orientation = Orientation.Vertical };
            body.SetPadding(0, 0, 0, PaperTheme.Dp(context, DesignTokens.Space.X3));
            fill(body);
            group.AddView(body);
        }

        group.AddView(Rule(context, PaperTheme.Hairline));
        return group;
    }

    /// <summary>A two-line row: the name (with an optional tick and badge) over its quiet detail. Tappable when given an action.</summary>
    public static LinearLayout Row(Context context, string name, string? detail, bool ticked = false, string? badge = null, Action? onTap = null)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X2);
        row.SetPadding(PaperTheme.Dp(context, DesignTokens.Space.X1), vertical, 0, vertical);
        row.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));

        var top = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);
        top.AddView(PaperUi.Body(context, name));
        if (ticked)
        {
            var size = PaperTheme.Dp(context, DesignTokens.Space.X4);
            top.AddView(new TickView(context), new LinearLayout.LayoutParams(size, size)
            {
                MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X1),
            });
        }

        row.AddView(top);

        if (!string.IsNullOrWhiteSpace(detail) || badge is not null)
        {
            var bottom = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            bottom.SetGravity(GravityFlags.CenterVertical);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                var line = PaperUi.Label(context, detail);
                line.Typeface = PaperTheme.SansRegular(context);
                line.SetTextColor(PaperTheme.InkMuted);
                bottom.AddView(line);
            }

            if (badge is not null)
            {
                bottom.AddView(Badge(context, badge), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
                {
                    MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2),
                });
            }

            row.AddView(bottom);
        }

        row.ContentDescription = string.Join(", ", new[] { name, ticked ? "yours" : null, detail, badge }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (onTap is not null)
        {
            row.Clickable = true;
            row.Click += (_, _) => onTap();
        }

        return row;
    }

    /// <summary>A label beside the kit's checkbox; the whole row is the target (§7). <paramref name="onChange"/> gets the new state.</summary>
    public static LinearLayout CheckRow(Context context, string label, bool picked, Action<bool> onChange, bool enabled = true)
    {
        var line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));

        var box = new Checkbox(context);
        box.SetPicked(picked);
        var boxSize = PaperTheme.Dp(context, DesignTokens.Shape.CheckboxDp);
        line.AddView(box, new LinearLayout.LayoutParams(boxSize, boxSize) { MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X3) });
        line.AddView(PaperUi.Body(context, label), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var state = picked;
        line.ContentDescription = $"{label}, {(picked ? "ticked" : "not ticked")}";
        if (!enabled)
        {
            line.Alpha = DesignTokens.Alpha.DisabledOpacity;
            return line;
        }

        line.Clickable = true;
        line.Click += (_, _) =>
        {
            state = !state;
            box.SetPicked(state);
            line.ContentDescription = $"{label}, {(state ? "ticked" : "not ticked")}";
            onChange(state);
        };
        return line;
    }

    /// <summary>The one neutral badge (D249): the label rung on bright paper inside a hairline — no warning text, no colour.</summary>
    public static TextView Badge(Context context, string text)
    {
        var badge = PaperUi.Label(context, text);
        badge.Background = PaperUi.Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusButton);
        var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X2);
        badge.SetPadding(horizontal, 0, horizontal, 0);
        return badge;
    }

    /// <summary>A muted sub-heading inside a page: the label rung, sans semibold.</summary>
    public static TextView Subheading(Context context, string text)
    {
        var heading = PaperUi.Label(context, text);
        heading.SetTextColor(PaperTheme.InkSoft);
        heading.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X4), 0, PaperTheme.Dp(context, DesignTokens.Space.X1));
        return heading;
    }

    public static View Rule(Context context, Color colour)
    {
        var rule = new View(context);
        rule.SetBackgroundColor(colour);
        rule.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    public static LinearLayout.LayoutParams Spaced(Context context, int topDp)
    {
        var spaced = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        spaced.TopMargin = PaperTheme.Dp(context, topDp);
        return spaced;
    }

    /// <summary>
    /// A Settings page (a service's own page, Household): the binder with the Settings tab lit, its
    /// header ruled in the neutral like the area's (§9), and the overlay a dog-ear folds into. Any tab
    /// opens the shell on the area tapped.
    /// </summary>
    public static (View Spread, FrameLayout Overlay) Frame(Activity activity, string title, View content)
    {
        var areas = PulpAreas.All.Select(spec =>
        {
            var (ink, cloth, _) = AreaPalette.For(spec.Hue);
            return new BinderArea(spec.Label, AreaPalette.Glyph(spec.Area), ink, cloth);
        }).ToList();
        var settings = PulpAreas.All.ToList().FindIndex(spec => spec.Area == PulpArea.Settings);
        var rule = AreaPalette.For(PulpAreas.RuleHue(PulpArea.Settings)).Rule;
        return BinderFrame.WrapWithOverlay(activity, title, rule, content, areas, settings, index => MainActivity.OpenOn(activity, index));
    }

    /// <summary>The pine tick of a service the household has (mockup 09): the Action role's mark, drawn.</summary>
    private sealed class TickView : View
    {
        private readonly Paint paint = new() { AntiAlias = true };

        public TickView(Context context) : base(context)
        {
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = PaperTheme.Dp(context, 2);
            paint.StrokeCap = Paint.Cap.Round;
            paint.StrokeJoin = Paint.Join.Round;
            paint.Color = PaperTheme.Accent;
            ContentDescription = "Yours";
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            using var path = new Path();
            path.MoveTo(Width * 0.22f, Height * 0.53f);
            path.LineTo(Width * 0.41f, Height * 0.72f);
            path.LineTo(Width * 0.78f, Height * 0.28f);
            canvas.DrawPath(path, paint);
        }
    }

    /// <summary>The fold's chevron in the muted ink: up when the group is open, down when it is closed.</summary>
    private sealed class ChevronView : View
    {
        private readonly Paint paint = new() { AntiAlias = true };
        private readonly bool up;

        public ChevronView(Context context, bool up) : base(context)
        {
            this.up = up;
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = PaperTheme.Dp(context, 2);
            paint.StrokeCap = Paint.Cap.Round;
            paint.Color = PaperTheme.InkMuted;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var (from, to) = up ? (0.62f, 0.4f) : (0.4f, 0.62f);
            using var path = new Path();
            path.MoveTo(Width * 0.28f, Height * from);
            path.LineTo(Width * 0.5f, Height * to);
            path.LineTo(Width * 0.72f, Height * from);
            canvas.DrawPath(path, paint);
        }
    }
}
