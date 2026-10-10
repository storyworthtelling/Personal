using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Actions;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Shell;
using Pulp.Domain.Voice;
using Pulp.Shell;
using Pulp.Title;

namespace Pulp.Library;

/// <summary>
/// Recently watched (mockup 05, D226-D229), the adult page behind home's primary button (D213). The
/// viewer's own record, kept forever (D97, D226): days newest first under relative headings
/// (<see cref="RecentDayLabel"/>), one row per show with its last episode and a pencil mark across the
/// whole show until it is complete (D227), a film's ending, films and shows only (D228). A tap opens
/// the title page; a long-press opens the action card's Recently watched entries — Mark unwatched ·
/// Watch again · Remove from history — the two writes undo-first on the dog-ear (D160, §13).
///
/// <para>It is a page inside home, so the Watching tab stays lit and tapping it returns to Continue
/// watching (§9); there is no bottom bar. Loading is a status sentence (§6), every read is off the UI
/// thread, and with no roster the page is its empty state — never invented rows.</para>
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class RecentlyWatchedActivity : Activity
{
    private const string PageTitle = "Recently watched";
    private const string Loading = "Reading what you've watched…";

    private readonly CancellationTokenSource lifetime = new();
    private PulpServices services = null!;
    private LibraryWrites writes = null!;
    private FrameLayout overlay = null!;
    private LinearLayout column = null!;
    private int loadGeneration;

    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Intent(context, typeof(RecentlyWatchedActivity));
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        services = PulpServices.For(this);
        writes = new LibraryWrites(services, () => RunOnUiThread(Reload));

        column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var areas = PulpAreas.All.Select(spec =>
        {
            var (ink, cloth, _) = AreaPalette.For(spec.Hue);
            return new BinderArea(spec.Label, AreaPalette.Glyph(spec.Area), ink, cloth);
        }).ToList();
        var watching = PulpAreas.All.ToList().FindIndex(spec => spec.Area == PulpArea.Watching);
        var rule = AreaPalette.For(PulpAreas.RuleHue(PulpArea.Watching)).Rule;

        // Watching is the active tab (§9); any tab opens the shell on the area tapped.
        var (spread, pageOverlay) = BinderFrame.WrapWithOverlay(this, PageTitle, rule, scroll, areas, watching, index => MainActivity.OpenOn(this, index));
        overlay = pageOverlay;
        SetContentView(spread);

        ShowStatus(Loading);
        Reload();
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }

    private void Reload()
    {
        var generation = ++loadGeneration;
        var token = lifetime.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var snapshot = await LoadAsync(token);
                RunOnUiThread(() =>
                {
                    if (generation == loadGeneration && !IsFinishing && !IsDestroyed)
                    {
                        Render(snapshot);
                    }
                });
            }
            catch (System.OperationCanceledException)
            {
                // The page closed while it was reading; nothing is left to paint.
                return;
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("recently-watched", "Recently watched could not be read.", exception);
                RunOnUiThread(() => ShowStatus("Recently watched couldn't be read just now."));
            }
        }, token);
    }

    private sealed record Snapshot(
        ThisPhoneStatus Status,
        ViewerId? Viewer,
        IReadOnlyList<RecentDay> Days,
        IReadOnlyList<WatchEvent> Sittings,
        IReadOnlyDictionary<TitleId, CatalogueTitle> Titles);

    private async Task<Snapshot> LoadAsync(CancellationToken token)
    {
        var view = await services.HouseholdAsync(token);
        var me = services.ThisPhoneOf(view);
        if (me.Viewer is not { } viewer)
        {
            return new Snapshot(me.Status, null, [], [], new Dictionary<TitleId, CatalogueTitle>());
        }

        var days = await services.RecentlyWatchedAsync(viewer, TimeZoneInfo.Local, token);
        var titles = new Dictionary<TitleId, CatalogueTitle>();
        foreach (var id in days.SelectMany(day => day.Rows).Select(row => row.Title).Distinct())
        {
            var title = services.CachedCopy(id) ?? await services.TitleAsync(id, tracked: true, opened: false, token);
            if (title is not null)
            {
                titles[id] = title;
            }
        }

        return new Snapshot(ThisPhoneStatus.Known, viewer, days, view.SittingsOf(viewer), titles);
    }

    private void ShowStatus(string sentence)
    {
        column.RemoveAllViews();
        column.AddView(PaperUi.Secondary(this, sentence));
    }

    private void Render(Snapshot snapshot)
    {
        column.RemoveAllViews();

        switch (snapshot.Status)
        {
            case ThisPhoneStatus.NoRoster:
                ShowEmpty();
                return;
            case ThisPhoneStatus.Unmatched:
                ShowStatus(ThisPhone.UnmatchedSentence);
                return;
            case ThisPhoneStatus.Known:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Status, "A viewer status the page does not know.");
        }

        if (snapshot.Days.Count == 0)
        {
            ShowEmpty();
            return;
        }

        column.AddView(PaperUi.Secondary(this, "Yours only · newest first"));
        var now = DateTimeOffset.Now;
        foreach (var day in snapshot.Days)
        {
            var heading = PaperUi.Heading(this, RecentDayLabel.For(day.Day, day.Rows[0].LastWatchedAt, now, TimeZoneInfo.Local));
            heading.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X3), 0, 0);
            column.AddView(heading);
            foreach (var row in day.Rows)
            {
                column.AddView(BuildRow(row, snapshot));
                column.AddView(LibraryRows.Hairline(this));
            }
        }
    }

    private void ShowEmpty()
    {
        var empty = new EmptyState(this, VoiceSeat.EmptyRecentlyWatched);

        // No composer is wired in the head yet, so the seat shows its neutral line (D264).
        empty.Present(VoiceLineAction.ShowNeutral, null);
        column.AddView(empty, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
    }

    private View BuildRow(RecentRow row, Snapshot snapshot)
    {
        var title = snapshot.Titles.GetValueOrDefault(row.Title);
        var episodeName = row.LastEpisode is { } last
            ? title?.Seasons.SelectMany(season => season.Episodes).FirstOrDefault(episode => episode.Episode == last)?.Name
            : null;
        var serviceName = row.Service is { } service ? services.Catalogue.ById(service)?.Name : null;
        var text = RecentRowText.For(row, episodeName, serviceName);
        var name = title?.Name ?? "A title not yet looked up";

        var lines = LibraryRows.Lines(this);
        lines.AddView(LibraryRows.TitleLine(this, name));
        lines.AddView(LibraryRows.KindLine(this, text.Subtitle));
        if (text.Bar is { } bar)
        {
            lines.AddView(LibraryRows.ProgressMark(this, bar));
        }

        if (text.ProgressLine is { } progress)
        {
            var mono = PaperUi.Mono(this, progress);
            mono.SetTextColor(PaperTheme.InkMuted);
            lines.AddView(mono);
        }

        if (text.Detail is { } detail)
        {
            lines.AddView(LibraryRows.WhereLine(this, detail, emphasis: false));
        }

        var poster = LibraryRows.Poster(this, title?.PosterUrl, LibraryRows.RecentPosterWidthDp, lifetime.Token);
        var view = LibraryRows.Row(this, null, poster, LibraryRows.RecentPosterWidthDp, lines, DesignTokens.Space.X2);
        view.Clickable = true;
        view.Click += (_, _) => StartActivity(TitleActivity.For(this, row.Title));
        view.LongClick += (_, args) =>
        {
            args.Handled = true;
            ShowCard(row, name, text.Subtitle, snapshot);
        };
        return view;
    }

    private void ShowCard(RecentRow row, string name, string subtitle, Snapshot snapshot)
    {
        var viewer = snapshot.Viewer ?? throw new InvalidOperationException("A row was pressed on a page with no viewer.");
        var history = RecentlyWatchedMarks.History(snapshot.Sittings, row.Title)
            .Select(sitting => new WatchedSitting(viewer, sitting))
            .ToList();
        var marks = RecentlyWatchedMarks.Unwatch(snapshot.Sittings, row.Title, DateTimeOffset.UtcNow)
            .Select(sitting => new WatchedSitting(viewer, sitting))
            .ToList();

        var handlers = new Dictionary<CardActionKind, CardHandler>
        {
            [CardActionKind.MarkUnwatched] = new(
                () => writes.Enqueue($"mark {name} unwatched", token => PutAll(marks, token)),
                () => writes.Enqueue($"undo marking {name} unwatched", token => RemoveAll(marks, token))),

            // Play lives on the title page (D151); "again" is what the title page's Play then does.
            [CardActionKind.WatchAgain] = new(() => StartActivity(TitleActivity.For(this, row.Title))),

            [CardActionKind.RemoveFromHistory] = new(
                () => writes.Enqueue($"remove {name} from history", token => RemoveAll(history, token)),
                () => writes.Enqueue($"undo removing {name} from history", token => PutAll(history, token))),
        };

        PulpActionCard.Show(this, overlay, new CardItem(name, CardPlace.RecentlyWatched) { Subtitle = subtitle }, handlers);
    }

    private async Task PutAll(IReadOnlyList<WatchedSitting> sittings, CancellationToken token)
    {
        foreach (var sitting in sittings)
        {
            await services.PutAsync(HouseholdMaps.Watched, sitting, token);
        }
    }

    private async Task RemoveAll(IReadOnlyList<WatchedSitting> sittings, CancellationToken token)
    {
        foreach (var sitting in sittings)
        {
            await services.RemoveAsync(HouseholdMaps.Watched, sitting, token);
        }
    }
}
