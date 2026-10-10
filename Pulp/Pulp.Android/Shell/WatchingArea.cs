using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Shell;
using Pulp.Domain.Voice;
using Pulp.Domain.Watching;
using Pulp.Library;
using Pulp.Title;
using Pulp.Watching;

namespace Pulp.Shell;

/// <summary>
/// Continue watching — Pulp's home, the area it opens on (mockup 01, D212). Most recently watched
/// first, a new episode jumping its show to the top with a chip (D96, D21), a caught-up show gone
/// (D152); each row a poster, title, what's next, where it plays and how, and the pencil mark (D150).
/// A tap opens the title page (D151); a long press, the action card (mockup 10).
///
/// <para>Whose list it is: the phone's viewer, as <see cref="ContinueWatchingViewer"/> answers it. With
/// nobody to answer for — no roster yet, which is a fresh install before Release D's first run — the
/// page is its empty state, never somebody else's rows (D97). Reading happens off the UI thread and
/// the page says so in a status sentence while it does, never a spinner.</para>
///
/// <para>The voiced greeting sits under the header only once something has been composed (D118); the
/// empty page carries its own voiced invitation instead (D264), so the two never show together.</para>
///
/// <para>The bottom bar is drawn as approved and never changes (D213, D263): the secondary switches
/// to a child's view, the primary opens Recently watched.</para>
/// </summary>
public sealed class WatchingArea(Activity host) : PulpAreaPage(host, PulpArea.Watching)
{
    /// <summary>The status sentence while the household and the catalogue are read.</summary>
    public const string ReadingStatus = "Finding where you left off.";

    /// <summary>The status sentence when the household could not be read; Diagnostics has why.</summary>
    public const string UnreadableStatus = "Continue watching could not be read just now.";

    private readonly HashSet<TitleId> hidden = [];
    private WatchingVoice? voice;
    private CancellationTokenSource? loading;
    private IReadOnlyList<WatchingRow>? rows;
    private ViewerId? viewer;
    private bool unreadable;
    private string? greeting;
    private VoiceLineAction emptyAction = VoiceLineAction.ShowNeutral;
    private string? emptyLine;

    /// <summary>
    /// "Watch as a child" was tapped (D213). Nothing handles it yet: the child's view ("Back to me",
    /// Favorites as the primary) is Release D's.
    /// </summary>
    public event Action? ChildViewRequested;

    /// <summary>"Recently watched" was tapped (D213); the page opens it itself, and raises this as well.</summary>
    public event Action? RecentlyWatchedRequested;

    public override string? Greeting =>
        viewer is not null && rows is not null && rows.Any(row => !hidden.Contains(row.Row.Title)) ? greeting : null;

    private WatchingVoice Voice => voice ??= new WatchingVoice(Host);

    public override View BuildView()
    {
        if (unreadable)
        {
            return Column(PaperUi.Secondary(Host, UnreadableStatus));
        }

        if (rows is null)
        {
            return Column(PaperUi.Secondary(Host, ReadingStatus));
        }

        var visible = rows.Where(row => !hidden.Contains(row.Row.Title)).ToList();
        if (visible.Count == 0 || viewer is not { } who)
        {
            var empty = new EmptyState(Host, VoiceSeat.EmptyContinueWatching);
            empty.Present(emptyAction, emptyLine);
            return empty;
        }

        var actions = new WatchingActions(Host, Overlay, PulpServices.For(Host), who, Reload, SetHidden);
        var list = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        foreach (var row in visible)
        {
            list.AddView(ContinueRowView.Build(
                Host,
                row,
                () => Host.StartActivity(TitleActivity.For(Host, row.Row.Title)),
                () => actions.ShowCard(row)));
        }

        var scroll = new ScrollView(Host) { FillViewport = true };
        scroll.AddView(list);
        return scroll;
    }

    public override View? BuildBottomBar() =>
        PaperUi.BottomBar(
            Host,
            PaperUi.SecondaryButton(Host, "Watch as a child", () => ChildViewRequested?.Invoke()),
            PaperUi.PrimaryButton(Host, "Recently watched", () =>
            {
                Host.StartActivity(RecentlyWatchedActivity.For(Host));
                RecentlyWatchedRequested?.Invoke();
            }));

    public override void Resume() => Reload();

    public override void Pause()
    {
        loading?.Cancel();
        loading = null;
    }

    private void SetHidden(TitleId title, bool hide)
    {
        if (hide)
        {
            hidden.Add(title);
        }
        else
        {
            hidden.Remove(title);
        }

        Invalidate();
    }

    /// <summary>Reads the list again off the UI thread; the rows on screen stay until the new ones arrive.</summary>
    private void Reload()
    {
        loading?.Cancel();
        var cancel = new CancellationTokenSource();
        loading = cancel;
        var services = PulpServices.For(Host);
        var posters = new PosterFiles(Host, (message, exception) => services.Diagnostics.Warn("watching", message, exception));

        _ = Task.Run(async () =>
        {
            try
            {
                var (who, read) = await ReadAsync(services, posters, cancel.Token);
                Host.RunOnUiThread(() =>
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }

                    unreadable = false;
                    viewer = who;
                    rows = read;
                    Invalidate();
                    _ = SpeakAsync(services, read, cancel.Token);
                });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // A newer read, or the screen going away, superseded this one.
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("watching", "Continue watching could not be read.", exception);
                Host.RunOnUiThread(() =>
                {
                    unreadable = rows is null;
                    Invalidate();
                });
            }
        });
    }

    private static async Task<(ViewerId? Viewer, IReadOnlyList<WatchingRow> Rows)> ReadAsync(
        PulpServices services, PosterFiles posters, CancellationToken cancellationToken)
    {
        var household = await services.HouseholdAsync(cancellationToken);
        if (ContinueWatchingViewer.Resolve(household.Roster) is not { } who)
        {
            return (null, []);
        }

        var continueRows = await services.ContinueWatchingAsync(who, cancellationToken);
        var read = new List<WatchingRow>();
        foreach (var row in continueRows)
        {
            // Shows were looked up by ContinueWatchingAsync already; a film is asked for here, cache-first.
            var title = row.Kind == TitleKind.Film
                ? await services.TitleAsync(row.Title, tracked: true, opened: false, cancellationToken)
                : services.CachedCopy(row.Title);
            var home = ContinueRowText.Home(title, household.Services, household.HomeOverrides);
            var serviceName = home is null ? null : services.Catalogue.ById(home.Service)?.Name ?? home.Service.Value;
            read.Add(new WatchingRow(
                row,
                title?.Name ?? row.Title.Value,
                title,
                ContinueRowText.NextUp(row, title),
                ContinueRowText.Where(title, household.Services, household.HomeOverrides,
                    service => services.Catalogue.ById(service)?.Name ?? service.Value),
                home,
                serviceName,
                await posters.PathForAsync(title?.PosterUrl, cancellationToken)));
        }

        return (who, read);
    }

    /// <summary>
    /// The page's one voiced seat for what it is showing: the greeting over rows, the invitation on an
    /// empty page. <see cref="VoiceLinePlan"/> decides whether to compose, reuse, or stay quiet.
    /// </summary>
    private async Task SpeakAsync(PulpServices services, IReadOnlyList<WatchingRow> read, CancellationToken cancellationToken)
    {
        try
        {
            await SpeakCoreAsync(read, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page moved on before the line came back; the next paint asks again.
        }
        catch (Exception exception)
        {
            // A voice is never an error on the page (DESIGN.md §14): the seat stays as it was.
            services.Diagnostics.Warn("watching", "The home's voiced line could not be decided.", exception);
        }
    }

    private async Task SpeakCoreAsync(IReadOnlyList<WatchingRow> read, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var showing = read.Count(row => !hidden.Contains(row.Row.Title));

        // Friday's stack is Discover's; nothing here can count what it brought, so the fact is left out
        // rather than said as none (VoiceFactBuilder omits a zero).
        var facts = showing > 0 && viewer is not null
            ? VoiceFactBuilder.Greeting(now, Voice.FirstOpenOfDay(today), showing, arrivedFriday: 0)
            : VoiceFactBuilder.EmptyContinueWatching(arrivedFriday: 0);
        var remembered = Voice.Remembered(facts.Seat);
        var action = VoiceLinePlan.Decide(facts, WatchingVoice.Ready, remembered, today);

        Present(facts.Seat, action, action == VoiceLineAction.Reuse ? remembered?.Line : null);
        if (action != VoiceLineAction.Compose)
        {
            return;
        }

        var line = await Voice.ComposeAsync(facts, today, cancellationToken);
        if (!cancellationToken.IsCancellationRequested)
        {
            Host.RunOnUiThread(() => Present(facts.Seat, string.IsNullOrWhiteSpace(line) ? Quiet(facts.Seat) : VoiceLineAction.Reuse, line));
        }
    }

    private static VoiceLineAction Quiet(VoiceSeat seat) =>
        VoicedOccasions.For(seat).Fallback == VoiceFallback.Hide ? VoiceLineAction.Hide : VoiceLineAction.ShowNeutral;

    private void Present(VoiceSeat seat, VoiceLineAction action, string? line)
    {
        if (seat == VoiceSeat.Greeting)
        {
            greeting = action == VoiceLineAction.Reuse && !string.IsNullOrWhiteSpace(line) ? line.Trim() : null;
        }
        else
        {
            emptyAction = action;
            emptyLine = line;
        }

        Invalidate();
    }

    private View Column(View child)
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.SetPadding(0, 0, 0, PaperTheme.Dp(Host, DesignTokens.Space.X4));
        column.AddView(child, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return column;
    }
}
