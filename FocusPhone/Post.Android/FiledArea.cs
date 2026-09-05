using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Post;
using Codex.Domain.Time;
using Codex.Domain.Timeline;
using Codex.Ui;

namespace Post;

/// <summary>
/// Filed: correspondence that has been dealt with — deleted at the source, kept here as a
/// ghost. The other half of the to-do-list frame the Mailbox is the front of.
///
/// Ordered by when the owner dealt with something rather than by when it arrived; on a
/// to-do list those are different questions and the first is the useful one. Per the
/// owner's note on the mock-ups there is no explanatory preamble at the top — they know
/// how it works.
/// </summary>
public sealed class FiledArea(Activity host) : PostArea(host)
{
    private IReadOnlyList<Ghost> ghosts = [];
    private string query = string.Empty;
    private FilterField? filter;

    public override BinderArea Tab =>
        new("Filed", Resource.Drawable.area_filed, PaperTheme.CautionMark, PaperTheme.CautionFill);

    public override void Resume() => Reload();

    private void Reload()
    {
        try
        {
            using var database = TimelineDatabase.Open(Host);
            var archive = database.Filing.LoadArchive();
            var window = TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs);
            var now = DateTimeOffset.UtcNow;

            // Settled, not merely archived: a message filed seconds ago is deliberately in
            // neither list while its undo window is open. Arriving in Filed under a fold
            // still offering to put it back reads as the app having already decided.
            ghosts = archive.Settled(query, now, window);

            ShowWhatIsStillSettling(archive, now, window);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("Post", $"Could not read the archive: {e}");
            PostDiagnostics.LogWarning("Filing", "Could not read the archive", e);
            ghosts = [];
        }
    }

    // Something filed while this screen is open would otherwise not appear until the owner
    // navigated away and back — they would watch it vanish from the Mailbox and never see
    // it land, which is the same "where did it go" the settling rule exists to prevent.
    // So when anything is still inside its window, come back once the earliest one settles.
    private void ShowWhatIsStillSettling(GhostArchive archive, DateTimeOffset now, TimeSpan window)
    {
        var earliest = archive.All
            .Select(ghost => ghost.FiledAtUtc + window)
            .Where(settlesAt => settlesAt > now)
            .DefaultIfEmpty(default)
            .Min();

        if (earliest == default)
        {
            return;
        }

        // Bounded by the window itself: a ghost stamped in the future (a clock that moved,
        // a restored backup) would otherwise reschedule this for ever.
        var wait = (long)(earliest - now).TotalMilliseconds + 1;
        if (wait > (long)window.TotalMilliseconds)
        {
            return;
        }

        new Android.OS.Handler(Android.OS.Looper.MainLooper!).PostDelayed(Invalidate, wait);
    }

    public override View BuildView()
    {
        Reload();
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        filter = new FilterField(Host, "Find something filed", PaperTheme.CautionMark);
        filter.SetQuery(query);
        filter.QueryChanged += text =>
        {
            query = text;
            Invalidate();
        };
        filter.Visibility = FilterMatch.IsWarranted(ghosts.Count) || query.Length > 0
            ? ViewStates.Visible
            : ViewStates.Gone;
        column.AddView(filter, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var frame = new FrameLayout(Host);
        var list = new ListView(Host);
        PaperUi.StylePaperList(Host, list);
        list.Adapter = new PaperListAdapter(Host, ghosts.Select(Row).ToList());
        list.ItemLongClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < ghosts.Count)
            {
                ShowGhostActions(ghosts[e.Position]);
            }
        };
        frame.AddView(list, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var empty = PaperUi.EmptyState(Host, query.Length > 0 ? "Nothing by that name." : "Nothing filed.");
        frame.AddView(empty, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        list.EmptyView = empty;

        column.AddView(frame, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return column;
    }

    private PaperListRow Row(Ghost ghost)
    {
        var title = ContactNameResolver.DisplayName(Host, ghost.Item.ContactKey);
        var when = RelativeTime.Format(ghost.FiledAtUtc.LocalDateTime, DateTime.Now);
        return new PaperListRow(title, $"Filed {when} · {TimelineItemFormatter.Describe(ghost.Item)}");
    }

    // Un-filing is allowed at any time (the owner asked for it explicitly). What comes back
    // is the ghost, because the source original is already gone — there is nothing else it
    // could be, and saying so plainly is better than implying a round trip that cannot
    // happen.
    private void ShowGhostActions(Ghost ghost)
    {
        var who = ContactNameResolver.DisplayName(Host, ghost.Item.ContactKey);
        ActionCard.Show(Host, Overlay, who, TimelineItemFormatter.Describe(ghost.Item),
            new CardAction("Put back in the Mailbox", () => Unfile(ghost), Lead: true));
    }

    private void Unfile(Ghost ghost)
    {
        if (Filing.Unfile(Host, ghost.Item.SourceId) is null)
        {
            return;
        }

        Invalidate();
        DogEar.Show(Host, Overlay, $"Put back {ContactNameResolver.DisplayName(Host, ghost.Item.ContactKey)}.",
            onUndo: () =>
            {
                Filing.File(Host, ghost.Item, ghost.FiledAtUtc);
                Invalidate();
            });
    }
}
