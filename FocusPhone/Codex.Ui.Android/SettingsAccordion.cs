using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Storyworthtelling.Updates;

// Android.Graphics.Path, not System.IO.Path — the implicit usings bring both in.
using Path = Android.Graphics.Path;

namespace Codex.Ui;

/// <summary>One Settings group: its heading, the quiet line beside it (or nothing), and what it holds once open.</summary>
public sealed record SettingsSection(string Title, string? Meta, Action<LinearLayout> Fill);

/// <summary>
/// Every Settings screen's anatomy (audit W8.4b; owner's rulings: folding groups in every app, drawn as
/// Pulp's ruled rows): a Serif heading with its quiet line right-aligned in the muted ink, a chevron, a
/// hairline under each group, and the group opening flat on the page in place — no card, no box, no
/// overlay. The groups come in the suite's order (<see cref="SettingsAnatomy"/>): Connections, Vault, the
/// app's own, Diagnostics, with the build line under them.
///
/// <para>The accordion keeps which groups are open across repaints, so a screen builds one per visit and
/// draws its sections through it every time. A group's body is built only while it is open.</para>
/// </summary>
public sealed class SettingsAccordion
{
    private readonly Context context;
    private readonly Action repaint;
    private readonly HashSet<string> open;

    /// <param name="repaint">Draws the screen again after a group opens or closes.</param>
    /// <param name="openAtFirst">The groups that start open.</param>
    public SettingsAccordion(Context context, Action repaint, params string[] openAtFirst)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.repaint = repaint ?? throw new ArgumentNullException(nameof(repaint));
        open = new HashSet<string>(openAtFirst, StringComparer.Ordinal);
    }

    public bool IsOpen(string title) => open.Contains(title);

    /// <summary>Opens a group without a tap — one that has something needing the owner, say.</summary>
    public void Open(string title) => open.Add(title);

    /// <summary>Draws the sections into <paramref name="column"/>. They must be in the suite's order.</summary>
    public void Draw(LinearLayout column, IReadOnlyList<SettingsSection> sections)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(sections);
        var titles = sections.Select(section => section.Title).ToList();
        if (!SettingsAnatomy.IsInOrder(titles))
        {
            throw new ArgumentException(
                "Settings groups must run Connections, Vault, the app's own, Diagnostics; these ran " + string.Join(", ", titles) + ".",
                nameof(sections));
        }

        foreach (var section in sections)
        {
            var title = section.Title;
            column.AddView(Group(context, title, section.Meta, open.Contains(title), () =>
            {
                if (!open.Remove(title))
                {
                    open.Add(title);
                }

                repaint();
            }, section.Fill));
        }
    }

    /// <summary>
    /// One folding group. <paramref name="isOpen"/> and <paramref name="toggle"/> let the caller keep which
    /// groups are open across repaints; the body is built only while the group is open.
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

        var rule = new View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        group.AddView(rule, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp)));
        return group;
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
