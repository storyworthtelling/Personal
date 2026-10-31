using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Time;

namespace Codex.Ui;

/// <summary>
/// The dog-ear's notice fold turned into a page (DESIGN.md §13, D162), replacing the action-card
/// popup <see cref="DogEar"/>'s <c>OpenNotices</c> used to show: a Serif header, the marker rule, an
/// optional status line, and every notice written straight onto the page — no cards, no count.
///
/// <para><b>Notices carry an <see cref="Action"/> delegate</b> (<see cref="DogEar.Notice"/>'s
/// <c>OnAction</c>), which cannot be marshalled through an <see cref="Intent"/> extra, so the
/// notices to show are handed off through <see cref="Pending"/> — a one-shot static slot, the same
/// kind of hand-off <c>BinderFrame</c>'s own screens already use for switching areas (see e.g.
/// <c>ScoreActivity.PendingArea</c>) rather than a mechanism invented for this file. <see
/// cref="OnCreate"/> reads and clears it immediately, so a process death that re-creates this
/// Activity with nobody left to repopulate it fails safe to an empty list rather than replaying
/// stale delegates.</para>
///
/// <para><b>No binder tabs here.</b> <c>DogEar.OpenNotices</c> only carries a <see cref="Context"/>
/// and the overlay <see cref="FrameLayout"/> it was posted against — neither says which app or which
/// area's tab strip is on screen, and <c>BinderFrame.Wrap</c> needs both (its own
/// <c>areas</c>/<c>activeIndex</c>/<c>onAreaSelected</c>). So this shared page draws the header,
/// rule, status line and rows only, exactly as <c>ActionCard</c> before it drew a title and rows
/// with no tabs of its own. Wearing the calling app's own binder tabs — so "Accounts stays lit"
/// behind the turned page, per mockup 17 — is per-app adoption work, the same way
/// <c>EqualizerActivity</c> wraps its own content in <c>BinderFrame.Wrap</c> with its own area
/// table; that wiring is explicitly later follow-up (one small commit per app), not this shared
/// file's job.</para>
///
/// <para><b>Each notice prints its date</b> when it has one (<see cref="DogEar.Notice.At"/>), in the
/// suite's relative-time rule, and the list arrives newest-first from the fold's <c>NoticeOrder</c> —
/// the same ordering the MAUI kit's page uses.</para>
/// </summary>
[Activity(Label = "Waiting", Theme = "@style/Theme.FocusPhone.Paper")]
public class WaitingActivity : Activity
{
    // One-shot hand-off: set immediately before StartActivity, consumed once in OnCreate.
    internal static IReadOnlyList<DogEar.Notice>? Pending;
    internal static string? PendingSubtitle;
    internal static Color? PendingHue;

    /// <summary>
    /// Opens the Waiting page. <paramref name="subtitle"/> and <paramref name="hue"/> are optional
    /// — <c>DogEar.OpenNotices</c> itself supplies neither today (its old <c>ActionCard.Show</c>
    /// call passed <c>subtitle: null</c> too, and had no area hue to give a rule), so both default
    /// to that same "nothing extra" shape; a future per-app adoption commit can supply them once it
    /// has an area to ask for one.
    /// </summary>
    public static void Launch(
        Context context, IReadOnlyList<DogEar.Notice> notices, string? subtitle = null, Color? hue = null)
    {
        Pending = notices;
        PendingSubtitle = subtitle;
        PendingHue = hue;

        var intent = new Intent(context, typeof(WaitingActivity));
        if (context is not Activity)
        {
            intent.AddFlags(ActivityFlags.NewTask);
        }

        context.StartActivity(intent);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);

        // The ordinary screen-open transition every Activity in the suite already uses. There is
        // no page-curl anywhere in the repo, native or MAUI, to match instead (confirmed by grep:
        // no OverrideActivityTransition/overridePendingTransition call anywhere names a curl, and
        // "curl" appears only in doc comments describing the dog-ear's own folded-corner drawing).
        // A real curl is DESIGN.md's stated aspiration, not yet built anywhere, so this page uses
        // the same slide every other screen already gets rather than inventing one of its own.
        PaperTransitions.Apply(this);

        var notices = Pending ?? Array.Empty<DogEar.Notice>();
        var subtitle = PendingSubtitle;
        var hue = PendingHue ?? PaperTheme.Ink;
        Pending = null;
        PendingSubtitle = null;
        PendingHue = null;

        SetContentView(BuildLayout(notices, subtitle, hue));
    }

    private View BuildLayout(IReadOnlyList<DogEar.Notice> notices, string? subtitle, Color hue)
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        page.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);

        // Serif header + marker rule (DESIGN.md §5, §7) — the same component every other screen's
        // header goes through, so "Waiting" opens exactly the way any other page does.
        page.AddView(PaperUi.ScreenHeader(this, "Waiting", hue));

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var status = PaperUi.Secondary(this, subtitle);
            status.SetPadding(0, 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X4));
            page.AddView(status);
        }

        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };

        if (notices.Count == 0)
        {
            // §13's other half of "one fold, no count": once the last notice is gone, the page
            // says so in words rather than sitting blank.
            var empty = PaperUi.EmptyState(this, "Nothing waiting.");
            empty.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X12), 0, 0);
            body.AddView(empty);
        }
        else
        {
            for (var i = 0; i < notices.Count; i++)
            {
                body.AddView(BuildRow(notices[i]));
                if (i < notices.Count - 1)
                {
                    body.AddView(Hairline());
                }
            }
        }

        var scroll = new ScrollView(this);
        scroll.AddView(body);
        page.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        return PaperUi.WrapForTopInset(this, page);
    }

    // One row: a role-colour bar on the left, the headline with its date, the detail, and at most one
    // secondary-button action (mockup 17's anatomy).
    private View BuildRow(DogEar.Notice notice)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var vertical = PaperTheme.Dp(this, DesignTokens.Space.X3);
        row.SetPadding(0, vertical, 0, vertical);

        var bar = new View(this);
        bar.SetBackgroundColor(notice.Hue);
        row.AddView(bar, new LinearLayout.LayoutParams(
            PaperTheme.Dp(this, RoleBarWidthDp), ViewGroup.LayoutParams.MatchParent)
        {
            RightMargin = PaperTheme.Dp(this, DesignTokens.Space.X3),
        });

        var text = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.AddView(PaperUi.Heading(this, notice.Headline), new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (notice.At is { } at)
        {
            // The suite's relative-time rule (just now, 2h ago, yesterday, Tuesday, Sep 26).
            var when = PaperUi.Secondary(this, RelativeTime.Format(at.LocalDateTime, DateTime.Now));
            when.SetPadding(PaperTheme.Dp(this, DesignTokens.Space.X2), 0, 0, 0);
            top.AddView(when, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        }

        text.AddView(top);
        text.AddView(PaperUi.BodyMuted(this, notice.Detail));

        if (!string.IsNullOrWhiteSpace(notice.ActionLabel))
        {
            // A notice's action is a secondary button, never the primary style — there is no
            // pinned bar here, because nothing on this page is THE primary action (§7).
            var button = PaperUi.SecondaryButton(this, notice.ActionLabel!, () =>
            {
                notice.OnAction?.Invoke();
                Finish();
            });
            text.AddView(button, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X2),
            });
        }

        row.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        return row;
    }

    private View Hairline()
    {
        var rule = new View(this);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(this, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    // Mockup 17 draws the bar at 4dp.
    private const int RoleBarWidthDp = 4;
}
