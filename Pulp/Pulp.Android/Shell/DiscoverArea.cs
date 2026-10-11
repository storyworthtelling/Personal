using Android.App;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Discover;
using Pulp.Domain.Actions;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Services;
using Pulp.Domain.Shell;
using Pulp.Domain.Voice;
using Pulp.Infrastructure.Catalogue;
using Pulp.Library;
using Pulp.Settings;
using Pulp.Title;

namespace Pulp.Shell;

/// <summary>
/// Discover (mockup 03, D222): the search field pinned under the header with its Title / Mood chips
/// (D170, D171), and under them this week's stack — films above, shows below (D108), each a literal
/// pile (D219) with its small buttons (D221) and veto strike (D220). Typing replaces the stack with
/// results in place; emptying the field brings it back. Results have no mockup (D272) and take the
/// stack's row anatomy.
///
/// <para>The page is built once and kept, so the field keeps its focus and keyboard while the content
/// under it changes; every read and write runs off the UI thread and lands back on it. Whose stack it
/// is comes from <see cref="ThisPhone"/>: with no roster, or no way yet to tell which adult holds this
/// phone, the page says so in a status sentence and deals nothing — never invented rows. Every catalogue
/// lookup goes through <see cref="PulpServices"/>' one counted client (D261).</para>
///
/// <para>In a child's view the page is the <see cref="ChildDiscoverPage"/>'s: no stack (D42), and a
/// search shows only what every child watching may see. The adult page and its week are not built.</para>
/// </summary>
public sealed class DiscoverArea(Activity host) : PulpAreaPage(host, PulpArea.Discover)
{
    /// <summary>What a failed Add to… says above the stack; the stack itself stays.</summary>
    private const string PlaylistsUnreadable = "Your playlists could not be read just now.";

    private readonly Handler handler = new(Looper.MainLooper!);

    // One kept-week file, so one store: two would interleave their saves on one temporary file.
    private readonly Lazy<DiscoverWeekStore> weekStore = new(() => new DiscoverWeekStore(host.FilesDir!.AbsolutePath, PulpServices.For(host).Diagnostics));
    private readonly Lazy<TrailerLookup> trailers = new(() => new TrailerLookup(host, PulpServices.For(host)));
    private LibraryWrites? writes;
    private LinearLayout? root;
    private FilterField? field;
    private ModeChips? chips;
    private LinearLayout? body;

    private DiscoverSearchMode mode = DiscoverSearchMode.Title;
    private DateTimeOffset editedAt = DateTimeOffset.MinValue;
    private int generation;
    private CancellationTokenSource? search;
    private Java.Lang.IRunnable? pending;
    private IReadOnlyList<DiscoverRow> results = [];
    private int resultPages = 1;

    private HouseholdView? household;
    private ViewerId? me;
    private VetoBook? vetoes;
    private DiscoverPageWeek? week;
    private string stackStatus = DiscoverPageText.Dealing;
    private bool loading;
    private string? note;
    private StackPileView? filmPile;
    private StackPileView? showPile;
    private ChildDiscoverPage? childPage;

    private PulpServices Services => PulpServices.For(Host);

    /// <summary>
    /// Every household write this page makes, one after another: an undo of a veto waits for the veto,
    /// so it can never take the earlier clock stamp and lose to it. The page keeps its own state, so
    /// nothing is re-read after a write.
    /// </summary>
    private LibraryWrites Writes => writes ??= new LibraryWrites(Services, () => { }, "discover");

    /// <summary>The changes feed is paged; two pages of new arrivals is a week's worth to deal from.</summary>
    private const int ChangePages = 2;

    public override void ViewAs(ChildViewSession? session)
    {
        base.ViewAs(session);
        childPage?.Pause();
        childPage = session is null ? null : new ChildDiscoverPage(Host, () => Overlay, session);
        CancelSearch();
    }

    public override View BuildView()
    {
        if (childPage is not null)
        {
            return childPage.Build();
        }

        if (root is not null)
        {
            (root.Parent as ViewGroup)?.RemoveView(root);
            return root;
        }

        root = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        field = new FilterField(Host, DiscoverPageInput.Hint, Android.Graphics.Color.ParseColor(DesignTokens.Identity.Pulp.Mark));
        field.QueryChanged += _ =>
        {
            editedAt = DateTimeOffset.UtcNow;
            Decide(submitted: false);
        };
        WireDone(field);
        root.AddView(field, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        chips = new ModeChips(Host, mode, tapped =>
        {
            mode = tapped;
            // A chip tapped with words in the field is an explicit ask (D171).
            Decide(submitted: field.Query.Length > 0);
        });
        root.AddView(chips, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(Host, DesignTokens.Space.X2),
        });

        body = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(Host) { FillViewport = true };
        scroll.AddView(body);
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        ShowStack();
        Load(deal: true);
        return root;
    }

    public override void Resume()
    {
        if (childPage is not null)
        {
            return;
        }

        // Vetoes may have changed elsewhere; the kept week is re-read, and dealt again only when due.
        if (root is not null)
        {
            Load(deal: true);
        }
    }

    public override void Pause()
    {
        childPage?.Pause();
        CancelSearch();
    }

    // ---- loading and dealing the week ----

    private void Load(bool deal)
    {
        if (loading)
        {
            return;
        }

        loading = true;
        Run(async cancellationToken =>
        {
            try
            {
                await LoadAsync(deal, cancellationToken);
            }
            finally
            {
                Host.RunOnUiThread(() =>
                {
                    loading = false;
                    if (field is not null && field.Query.Length == 0)
                    {
                        ShowStack();
                    }
                });
            }
        }, DiscoverPageText.StackUnreachable, CancellationToken.None);
    }

    private async Task LoadAsync(bool deal, CancellationToken cancellationToken)
    {
        var view = await Services.HouseholdAsync(cancellationToken);
        var viewer = Services.ThisPhoneOf(view).Viewer;
        if (view.Roster is not { } roster || viewer is not { } who)
        {
            Publish(view, null, null, null, view.Roster is null ? DiscoverPageText.NoHousehold : ThisPhone.UnmatchedSentence);
            return;
        }

        var book = view.VetoesOf(who);
        var store = weekStore.Value;
        var kept = store.Load();
        var now = DateTimeOffset.UtcNow;
        if (kept is not null && kept.Keeps(who, now))
        {
            Publish(view, who, book, kept, DiscoverPageText.StackStatus);
            return;
        }

        if (!deal)
        {
            Publish(view, who, book, null, DiscoverPageText.StackStatus);
            return;
        }

        var services = view.Services.Select(Services.Catalogue.ById).OfType<StreamingService>().ToList();
        if (services.Count == 0)
        {
            Publish(view, who, book, null, DiscoverPageText.NoServices);
            return;
        }

        Publish(view, who, book, null, DiscoverPageText.Dealing);
        IReadOnlyList<CatalogueTitle> pool;
        try
        {
            pool = await NewOnServicesAsync(services, cancellationToken);
        }
        catch (CatalogueLookupException exception)
        {
            Services.Diagnostics.Warn("discover", $"The week could not be dealt ({exception.Failure}).", exception);
            Publish(view, who, book, null, DiscoverPageText.StackUnreachable);
            return;
        }

        var sittings = roster.Viewers.ToDictionary(viewer => viewer.Id, viewer => view.SittingsOf(viewer.Id));
        var watched = DiscoverPageWatched.WatchedTitles(sittings, id => Services.CachedCopy(id)?.Genres ?? []);
        var taste = TasteLearner.Learn(who, roster, watched, book);
        var dealt = DiscoverPageWeek.Deal(
            who, roster, pool, taste, book, DiscoverPageWatched.AlreadyWatched(view.SittingsOf(who)),
            new StackSizeStore(Host).Load(), now, TimeZoneInfo.Local, Plans(view), view.HomeOverrides);
        try
        {
            store.Save(dealt);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The week was dealt; only keeping it failed. It is shown, and dealt again next time.
            Services.Diagnostics.Warn("discover", "The dealt week could not be kept on this phone; it is shown, and dealt again next time.", exception);
        }

        Publish(view, who, book, dealt, DiscoverPageText.StackStatus);
    }

    private void Publish(HouseholdView view, ViewerId? viewer, VetoBook? book, DiscoverPageWeek? kept, string status) =>
        Host.RunOnUiThread(() =>
        {
            household = view;
            me = viewer;
            vetoes = book;
            week = kept;
            stackStatus = status;
            if (field is not null && field.Query.Length == 0)
            {
                ShowStack();
            }
        });

    // Each household service with the tier Settings recorded for it (D29); an unset tier reads as ad-free.
    private static IReadOnlyList<ServicePlan> Plans(HouseholdView? view) => view?.ServicePlans ?? [];

    // ---- the stack ----

    private void ShowStack()
    {
        if (body is null)
        {
            return;
        }

        body.RemoveAllViews();
        if (week is not { } kept || vetoes is not { } book)
        {
            body.AddView(Status(stackStatus));
            return;
        }

        if (kept.IsEmpty)
        {
            body.AddView(Status(DiscoverPageText.NothingToDeal));
            return;
        }

        if (kept.IsUsedUp(book))
        {
            // D112: the used-up stack's empty state until Friday, no refill. No composer is wired in
            // this head yet, so the seat shows its neutral line.
            var empty = new EmptyState(Host, VoiceSeat.StackUsedUp);
            empty.Present(VoiceLineAction.ShowNeutral, null);
            body.AddView(empty, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            return;
        }

        body.AddView(Status(note ?? DiscoverPageText.StackStatus));
        note = null;

        // The pile views are kept, so a strike or an add leaves the front card where it stands (D220).
        var handlers = new PileHandlers(Open, AddTo, NotForMe, BringBack, Trailer, WantPoster);
        filmPile ??= new StackPileView(Host, DiscoverPageText.FilmsHeading, handlers);
        showPile ??= new StackPileView(Host, DiscoverPageText.ShowsHeading, handlers);
        foreach (var (pile, kind) in new[] { (filmPile, TitleKind.Film), (showPile, TitleKind.Show) })
        {
            (pile.Parent as ViewGroup)?.RemoveView(pile);
            pile.Bind(kept.Pile(kind, book));
            body.AddView(pile);
        }
    }

    private TextView Status(string text)
    {
        var status = PaperUi.Secondary(Host, text);
        status.SetPadding(0, PaperTheme.Dp(Host, DesignTokens.Space.X2), 0, PaperTheme.Dp(Host, DesignTokens.Space.X1));
        return status;
    }

    private void Open(DiscoverRow row) => Host.StartActivity(TitleActivity.For(Host, row.Id));

    private void NotForMe(DiscoverRow row)
    {
        if (me is not { } who || vetoes is null)
        {
            return;
        }

        var veto = new ViewerVeto(who, new Veto(row.Id, row.Tags));
        vetoes = vetoes.Veto(row.Id, row.Tags);
        ShowStack();
        Writes.Enqueue($"veto {row.Id}", token => Services.PutAsync(HouseholdMaps.Veto, veto, token));

        // Undo-first (§13): the strike is made at once and the corner folds back to lift it.
        DogEar.Show(Host, Overlay, $"Undo not for me, {row.Name}.", () => BringBack(row));
    }

    private void BringBack(DiscoverRow row)
    {
        if (me is not { } who || vetoes is null)
        {
            return;
        }

        var veto = new ViewerVeto(who, new Veto(row.Id, row.Tags));
        vetoes = vetoes.BringBack(row.Id);
        ShowStack();
        Writes.Enqueue($"bring back {row.Id}", token => Services.RemoveAsync(HouseholdMaps.Veto, veto, token));
    }

    // The one trailer lookup (the title page's): a found id is kept, so the key's daily quota is spent
    // once per title, and an answer that is not a video id is refused there rather than crashing here.
    private void Trailer(DiscoverRow row)
    {
        var lookup = trailers.Value;
        RunOnStack(async cancellationToken =>
        {
            var intent = await lookup.FindAsync(Host, row.Id, row.Name, row.Year, cancellationToken);
            Host.RunOnUiThread(() =>
            {
                if (intent is null)
                {
                    Note(DiscoverPageText.NoTrailer);
                    return;
                }

                Host.StartActivity(intent);
            });
        }, DiscoverPageText.NoTrailer);
    }

    // D109: "Add to…" opens the inline playlist picker, last-used first. The picker's tick is the
    // success state (D257); the card stays on its pile, marked added.
    private void AddTo(DiscoverRow row)
    {
        if (me is not { } who)
        {
            return;
        }

        var entry = PlaylistEntry.ForTitle(row.Id, row.Kind);
        var picks = new PickerWrites(Services, Writes.Enqueue, who, row.Id, entry);
        RunOnStack(async cancellationToken =>
        {
            var lists = await Services.PlaylistsAsync(who, cancellationToken);
            var model = AddToPickerPlan.Build(
                row.Name, entry, title: null,
                lists.Select(list => new PickerPlaylist(list.Id.Value, list.List, list.Shared, LastUsed: null)),
                []);
            Host.RunOnUiThread(() => AddToPicker.Show(
                Host, Overlay, model,
                (picked, change) =>
                {
                    var undo = picks.OnPick(picked, change);
                    if (change is PickerChange.Add or PickerChange.AddAndAllowList)
                    {
                        MarkAdded(row);
                    }

                    return undo;
                },
                name =>
                {
                    if (picks.NewPlaylist(name) is null)
                    {
                        return;
                    }

                    AddToPicker.Fold(Overlay);
                    MarkAdded(row);
                }));
        }, PlaylistsUnreadable);
    }

    private void MarkAdded(DiscoverRow row)
    {
        if (week is null)
        {
            return;
        }

        week = week.Add(row.Id);
        var kept = week;
        var store = weekStore.Value;
        Writes.Enqueue("keep the week's added card", _ =>
        {
            store.Save(kept);
            return Task.CompletedTask;
        });
    }

    private void WantPoster(PosterDeck deck, string url, int targetPx)
    {
        var posters = Services.Posters;
        Run(async cancellationToken =>
        {
            var bitmap = await posters.LoadAsync(url, targetPx, cancellationToken);
            Host.RunOnUiThread(() => deck.SetPoster(url, bitmap));
        }, null, CancellationToken.None);
    }

    /// <summary>
    /// What is new on the household's services, whole titles only: the pool the week is dealt from.
    /// A title that turns up on several pages, or on several services, is one candidate.
    /// </summary>
    private async Task<IReadOnlyList<CatalogueTitle>> NewOnServicesAsync(
        IReadOnlyList<StreamingService> services, CancellationToken cancellationToken)
    {
        var titles = new Dictionary<TitleId, CatalogueTitle>();
        string? cursor = null;
        for (var page = 0; page < ChangePages; page++)
        {
            var changes = await Services.ChangesAsync(services, ChangeKind.New, ChangeItem.Title, cursor, cancellationToken);
            foreach (var (id, title) in changes.Titles)
            {
                titles.TryAdd(id, title);
            }

            if (!changes.HasMore)
            {
                break;
            }

            cursor = changes.NextCursor;
        }

        return titles.Values.ToList();
    }

    // ---- the search field ----

    // The kit's FilterField raises no submit, so Done is read from its own input.
    private void WireDone(FilterField filter)
    {
        if (filter.ChildCount == 0 || filter.GetChildAt(0) is not EditText input)
        {
            Services.Diagnostics.Warn("discover", "The filter field has no input to read Done from; a mood is asked from the chip only.");
            return;
        }

        input.ImeOptions = ImeAction.Search;
        input.EditorAction += (_, args) =>
        {
            args.Handled = args.ActionId is ImeAction.Search or ImeAction.Done;
            if (args.Handled)
            {
                Decide(submitted: true);
            }
        };
    }

    private void Decide(bool submitted)
    {
        if (field is null)
        {
            return;
        }

        if (pending is not null)
        {
            handler.RemoveCallbacks(pending);
            pending = null;
        }

        var step = DiscoverPageInput.Decide(field.Query, mode, editedAt, DateTimeOffset.UtcNow, submitted);
        switch (step.Action)
        {
            case DiscoverPageAction.ShowStack:
                CancelSearch();
                ShowStack();
                break;
            case DiscoverPageAction.Wait:
                if (step.AskAgainAt is { } at)
                {
                    pending = new Java.Lang.Runnable(() => Decide(submitted: false));
                    handler.PostDelayed(pending, Math.Max(0, (long)(at - DateTimeOffset.UtcNow).TotalMilliseconds));
                }

                break;
            case DiscoverPageAction.AwaitMoodDone:
                CancelSearch();
                ShowStatus(DiscoverPageText.MoodPrompt);
                break;
            case DiscoverPageAction.SearchTitles:
                SearchTitles(step.Text);
                break;
            case DiscoverPageAction.AskMood:
                AskMood(step.Text);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(submitted), step.Action, "A field decision the page does not act on.");
        }
    }

    private void SearchTitles(string text)
    {
        var token = Begin(DiscoverPageText.Searching(text), out var asked);
        var plans = Plans(household);
        var overrides = household?.HomeOverrides ?? HomeOverrides.None;
        Run(async cancellationToken =>
        {
            IReadOnlyList<CatalogueTitle> found;
            try
            {
                found = await Services.SearchAsync(text, cancellationToken);
            }
            catch (CatalogueLookupException exception)
            {
                Services.Diagnostics.Warn("discover", $"A title search failed ({exception.Failure}).", exception);
                Land(asked, () => ShowStatus(DiscoverPageText.SearchUnreachable));
                return;
            }

            var rows = found.Select(title => DiscoverPageRows.For(title, plans, overrides)).ToList();
            Land(asked, () => ShowResults(rows, DiscoverPageText.NoTitles(text)));
        }, DiscoverPageText.SearchUnreachable, token);
    }

    private void AskMood(string mood)
    {
        if (household?.Roster is not { } roster || me is not { } who)
        {
            ShowStatus(me is null && household?.Roster is not null ? ThisPhone.UnmatchedSentence : DiscoverPageText.NoHousehold);
            return;
        }

        var token = Begin(DiscoverPageText.Asking(mood), out var asked);
        var view = household;
        var book = vetoes ?? VetoBook.For(who);
        Run(async cancellationToken =>
        {
            var services = view.Services.Select(Services.Catalogue.ById).OfType<StreamingService>().ToList();
            var sittings = roster.Viewers.ToDictionary(viewer => viewer.Id, viewer => view.SittingsOf(viewer.Id));
            var taste = TasteLearner.Learn(who, roster, DiscoverPageWatched.WatchedTitles(sittings, id => Services.CachedCopy(id)?.Genres ?? []), book);
            var viewer = MoodViewer.Adult(who);
            var query = MoodQuery.For(mood, DiscoverContext.For(viewer, roster, services, taste));

            var answer = await Services.Mood.AskMoodAsync(query, cancellationToken);
            if (answer.Outcome != MoodOutcome.Answered)
            {
                // "No idea" is a quiet sentence, never an error and never voiced (D99, G12).
                Services.Diagnostics.Warn("discover", $"Mood search had no idea ({answer.Outcome}): {answer.Detail}");
                Land(asked, () => ShowStatus(DiscoverPageText.MoodNoIdea(answer.Outcome)));
                return;
            }

            var found = new List<CatalogueTitle>();
            foreach (var term in MoodPlan.SearchTerms(answer))
            {
                try
                {
                    found.AddRange(await Services.SearchAsync(term, cancellationToken));
                }
                catch (CatalogueLookupException exception)
                {
                    Services.Diagnostics.Warn("discover", $"A mood candidate's lookup failed ({exception.Failure}); it is dropped.", exception);
                }
            }

            // No franchise source exists yet; for an adult on their own the filter is not consulted.
            var result = MoodPlan.Resolve(query, answer, found, viewer, _ => []);
            if (result.Dropped > 0)
            {
                Services.Diagnostics.Warn("discover",
                    $"Mood search dropped {result.Unresolved} unresolved, {result.OffServices} off services, {result.Hidden} hidden.");
            }

            var plans = Plans(view);
            var rows = result.Titles.Select(title => DiscoverPageRows.For(title, plans, view.HomeOverrides)).ToList();
            Land(asked, () => ShowResults(rows, DiscoverPageText.NothingForMood));
        }, DiscoverPageText.MoodNoIdea(MoodOutcome.Unavailable), token);
    }

    private CancellationToken Begin(string status, out int asked)
    {
        CancelSearch();
        search = new CancellationTokenSource();
        asked = ++generation;
        resultPages = 1;
        ShowStatus(status);
        return search.Token;
    }

    // Only the latest ask may change the page; a slower earlier one lands on nothing.
    private void Land(int asked, Action show) =>
        Host.RunOnUiThread(() =>
        {
            if (asked == generation && field is not null && field.Query.Length > 0)
            {
                show();
            }
        });

    private void CancelSearch()
    {
        generation++;
        search?.Cancel();
        search?.Dispose();
        search = null;
    }

    private void ShowStatus(string text)
    {
        body?.RemoveAllViews();
        body?.AddView(Status(text));
    }

    private void ShowResults(IReadOnlyList<DiscoverRow> rows, string none)
    {
        results = rows;
        var page = DiscoverPageResults.Build(rows, resultPages);
        if (page.IsEmpty)
        {
            ShowStatus(none);
            return;
        }

        body?.RemoveAllViews();
        body?.AddView(DiscoverResultsView.Build(Host, page, Open, WantPoster, () =>
        {
            resultPages++;
            ShowResults(results, none);
        }));
    }

    // ---- off the UI thread ----

    private void Run(Func<CancellationToken, Task> work, string? failureStatus, CancellationToken cancellationToken) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work(cancellationToken);
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, cancellationToken))
            {
                Services.Diagnostics.Warn("discover", "Discover's background work failed: " + exception.Message, exception);
                if (failureStatus is not null)
                {
                    Host.RunOnUiThread(() => ShowStatus(failureStatus));
                }
            }
        }, cancellationToken);

    /// <summary>
    /// Work behind a pile button. A failure says so in the line above the stack and leaves the stack
    /// where it is — a button that failed never takes the page away.
    /// </summary>
    private void RunOnStack(Func<CancellationToken, Task> work, string failureNote) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work(CancellationToken.None);
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                Services.Diagnostics.Warn("discover", "A stack button's work failed: " + exception.Message, exception);
                Host.RunOnUiThread(() => Note(failureNote));
            }
        });

    private void Note(string text)
    {
        note = text;
        if (field is not null && field.Query.Length == 0)
        {
            ShowStack();
        }
    }
}
