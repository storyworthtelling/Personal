using Android.Content;
using Codex.Ui.Voice;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Services;
using Pulp.Components;
using Pulp.HandOffs;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Discover;
using Pulp.Infrastructure.Household;
using Storyworthtelling.Voice;

namespace Pulp.App;

/// <summary>
/// The composition root the areas get, so a screen never touches HTTP or a file itself. It builds the
/// four data clients (Streaming Availability on the Direct route, D285), the mood client, the
/// vault-backed keys, the lookup count (Diagnostics only, D261), the household store, the catalogue
/// cache and the one poster loader; and it answers the screens' reads — whose phone this is, Continue
/// Watching, a title, the playlists, Recently watched, a title search, the changes feed — cache-first,
/// fetching only when <see cref="CatalogueFreshness"/> says so. Every catalogue lookup goes through
/// the one counted client here, so the Diagnostics count is the whole month's (D261).
///
/// <para>One per process (<see cref="For"/>). Writes to the household go through
/// <see cref="PutAsync"/> / <see cref="RemoveAsync"/>, which stamp with this install's hybrid
/// logical clock and keep it on disk. Today the household is the phone's own copy
/// (<see cref="LocalHouseholdStore"/>); Release D swaps in the Drive store behind the same seam.</para>
/// </summary>
public sealed class PulpServices
{
    private static readonly object InstanceGate = new();
    private static PulpServices? instance;

    private readonly IHouseholdStore household;
    private readonly LocalInstallStore install;
    private readonly CatalogueCache cache;
    private readonly StreamingAvailabilityClient availability;
    private readonly TvMazeClient episodes;
    private readonly OmdbClient ratings;
    private readonly DriveVoiceOptions voiceOptions;
    private readonly SemaphoreSlim householdGate = new(1, 1);

    private PulpServices(Context context)
    {
        var files = context.FilesDir?.AbsolutePath ?? throw new InvalidOperationException("Pulp has no files directory to keep its household in.");
        var cacheRoot = context.CacheDir?.AbsolutePath ?? files;

        Diagnostics = new PulpDiagnostics(files);
        var account = new VoiceGoogleAccount();
        Keys = new VaultPulpKeySource(account, Diagnostics);
        var counter = new DiagnosticsLookupCounter(files, Diagnostics);
        var health = new CredentialHealthStore();
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        Posters = new Posters(http, cacheRoot, Diagnostics);

        Catalogue = ServiceCatalogue.Public;
        availability = new StreamingAvailabilityClient(http, Keys, Catalogue, counter, health, StreamingAvailabilityRoute.Direct);
        episodes = new TvMazeClient(http, counter);
        ratings = new OmdbClient(http, Keys, counter, health);
        Trailers = new YouTubeClient(http, Keys, counter, health);

        voiceOptions = VoiceOptionsAccess.ForHead(account.GetAccessToken, "Pulp");
        Mood = new MoodClient(http, voiceOptions, health);

        household = new LocalHouseholdStore(files);
        install = new LocalInstallStore(files);
        cache = new CatalogueCache(Path.Combine(cacheRoot, "catalogue"));
    }

    public static PulpServices For(Context context)
    {
        lock (InstanceGate)
        {
            return instance ??= new PulpServices(context.ApplicationContext ?? context);
        }
    }

    public PulpDiagnostics Diagnostics { get; }

    public VaultPulpKeySource Keys { get; }

    public ServiceCatalogue Catalogue { get; }

    /// <summary>Trailer ids for the title page's player.</summary>
    public YouTubeClient Trailers { get; }

    /// <summary>Discover's mood search and the model-made playlist (D104, D171).</summary>
    public IMoodClient Mood { get; }

    /// <summary>The one poster loader every screen paints from.</summary>
    public Posters Posters { get; }

    /// <summary>Restores the keys and the shared Claude key from the vault. Call once at launch, off the UI thread.</summary>
    public async Task RefreshKeysAsync(CancellationToken cancellationToken)
    {
        await Keys.RefreshAsync(cancellationToken);
        try
        {
            await voiceOptions.RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Diagnostics.Warn("vault", "The shared Claude key could not be read; mood search answers 'no idea' until it can.", exception);
        }
    }

    // ---- the household ----

    /// <summary>The household as read now. Records this build cannot read are logged, never voiced.</summary>
    public async Task<HouseholdView> HouseholdAsync(CancellationToken cancellationToken)
    {
        var view = HouseholdView.Of(await household.LoadAsync(cancellationToken));
        foreach (var record in view.Unreadable)
        {
            Diagnostics.Warn("household", record.Reason);
        }

        return view;
    }

    public Task PutAsync<T>(RecordMap<T> map, T value, CancellationToken cancellationToken)
        where T : notnull =>
        EditAsync((document, stamp, writer) => HouseholdRecords.Put(document, map, value, stamp, writer), cancellationToken);

    public Task RemoveAsync<T>(RecordMap<T> map, T value, CancellationToken cancellationToken)
        where T : notnull =>
        EditAsync((document, stamp, writer) => HouseholdRecords.Remove(document, map, value, stamp, writer), cancellationToken);

    private async Task EditAsync(Func<HouseholdDocument, HlcTimestamp, string, HouseholdDocument> edit, CancellationToken cancellationToken)
    {
        await householdGate.WaitAsync(cancellationToken);
        try
        {
            var document = await household.LoadAsync(cancellationToken);
            var state = install.Load();
            var clock = new HybridLogicalClock(state.LastStamp);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            clock.Witness(document.MaxStamp, now);
            var stamp = clock.Now(now);
            await household.SaveAsync(edit(document, stamp, state.InstallId), cancellationToken);
            install.SaveClock(clock.Last);
        }
        finally
        {
            householdGate.Release();
        }
    }

    // ---- whose phone this is ----

    /// <summary>Whose private lists this phone shows (D97), read from the household and this install's record.</summary>
    public async Task<ThisPhoneAnswer> ThisPhoneAsync(CancellationToken cancellationToken) =>
        ThisPhoneOf(await HouseholdAsync(cancellationToken));

    /// <summary><see cref="ThisPhoneAsync"/> for a household already read. Reads the install's file; off the UI thread.</summary>
    public ThisPhoneAnswer ThisPhoneOf(HouseholdView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return ThisPhone.Resolve(view.Roster, install.Load().Viewer);
    }

    /// <summary>
    /// Records whose phone this is (Release D's first run), or clears it with null. Only an adult on the
    /// roster can be recorded; anyone else is refused rather than kept as a record that resolves to nobody.
    /// </summary>
    public async Task SetThisPhoneAsync(ViewerId? viewer, CancellationToken cancellationToken)
    {
        if (viewer is { } who && !ThisPhone.CanBeRecorded((await HouseholdAsync(cancellationToken)).Roster, who))
        {
            throw new ArgumentException($"'{who}' is not an adult on the household roster, so this phone cannot be recorded as theirs.", nameof(viewer));
        }

        install.SaveViewer(viewer);
    }

    /// <summary>
    /// The one door for hand-offs, built for the household as it stands; null until the roster names
    /// anyone (first run is Release D's), since a hand-off with no roster cannot be checked for a child.
    /// </summary>
    public async Task<HandOffExecutor?> HandOffsAsync(CancellationToken cancellationToken)
    {
        var view = await HouseholdAsync(cancellationToken);
        return view.Roster is null ? null : new HandOffExecutor(view.Roster, view.ChildRules, Catalogue, view.Attested);
    }

    // ---- the screens' reads ----

    /// <summary>What the cache holds for a title, for an instant first paint; null when nothing is cached.</summary>
    public CatalogueTitle? CachedCopy(TitleId id) => ReadCache(id)?.Title;

    /// <summary>
    /// A title's detail, cache-first: fetched (availability, then the rating code and — for a show —
    /// the air dates) only when <see cref="CatalogueFreshness"/> says it is due. When the lookup fails
    /// the cached copy is returned and the failure logged; null only when there is neither.
    /// </summary>
    public async Task<CatalogueTitle?> TitleAsync(TitleId id, bool tracked, bool opened, CancellationToken cancellationToken)
    {
        var cached = ReadCache(id);
        if (!CatalogueFreshness.NeedsFetch(cached?.Stamp, tracked, opened, DateTimeOffset.UtcNow))
        {
            return cached!.Title;
        }

        CatalogueTitle? fetched;
        try
        {
            fetched = await availability.GetTitleAsync(id.Value, cancellationToken);
        }
        catch (CatalogueLookupException exception)
        {
            Diagnostics.Warn("catalogue", $"Title {id} could not be refreshed ({exception.Failure}); the cached copy stands.", exception);
            return cached?.Title;
        }

        if (fetched is null)
        {
            return cached?.Title;
        }

        fetched = await WithRatingAsync(fetched, cancellationToken);
        fetched = await WithAirDatesAsync(fetched, cancellationToken);
        cache.Store(fetched, DateTimeOffset.UtcNow);
        return fetched;
    }

    /// <summary>
    /// Continue Watching for one viewer (D96, D97), with each show's schedule from the catalogue and
    /// the titles they removed by hand left out (D31).
    /// </summary>
    public async Task<IReadOnlyList<ContinueRow>> ContinueWatchingAsync(ViewerId viewer, CancellationToken cancellationToken)
    {
        var view = await HouseholdAsync(cancellationToken);
        var sittings = view.SittingsOf(viewer);
        var schedules = new List<ShowSchedule>();
        foreach (var show in sittings.Where(sitting => sitting.Kind == WatchedKind.Show).Select(sitting => sitting.Title).Distinct())
        {
            if (await TitleAsync(show, tracked: true, opened: false, cancellationToken) is { Kind: TitleKind.Show } title)
            {
                schedules.Add(CatalogueSchedule.For(title));
            }
        }

        return ContinueWatching.Build(sittings, schedules, view.DismissalsOf(viewer), DateTimeOffset.UtcNow);
    }

    /// <summary>A title search (D209) over the one counted client. Throws <see cref="CatalogueLookupException"/> when the source fails.</summary>
    public Task<IReadOnlyList<CatalogueTitle>> SearchAsync(string title, CancellationToken cancellationToken) =>
        availability.SearchByTitleAsync(title, kind: null, cancellationToken);

    /// <summary>One page of the changes feed on the given services, over the one counted client.</summary>
    public Task<ChangesPage> ChangesAsync(
        IEnumerable<StreamingService> services,
        ChangeKind kind,
        ChangeItem item,
        string? cursor,
        CancellationToken cancellationToken) =>
        availability.GetChangesAsync(services, kind, item, cursor, cancellationToken);

    /// <summary>Recently watched for one viewer (D226–D228), each show's episode count from the cache.</summary>
    public async Task<IReadOnlyList<RecentDay>> RecentlyWatchedAsync(ViewerId viewer, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var sittings = (await HouseholdAsync(cancellationToken)).SittingsOf(viewer);
        var counts = new Dictionary<TitleId, int>();
        foreach (var show in sittings.Where(sitting => sitting.Kind == WatchedKind.Show).Select(sitting => sitting.Title).Distinct())
        {
            if (CachedCopy(show)?.EpisodeCount is { } count)
            {
                counts[show] = count;
            }
        }

        return RecentlyWatched.Build(sittings, counts, zone);
    }

    /// <summary>The viewer's own playlists and the shared ones (D97, D103).</summary>
    public async Task<IReadOnlyList<HouseholdPlaylist>> PlaylistsAsync(ViewerId viewer, CancellationToken cancellationToken) =>
        (await HouseholdAsync(cancellationToken)).PlaylistsFor(viewer);

    private CachedTitle? ReadCache(TitleId id)
    {
        var read = cache.Read(id);
        if (read.Status == CacheReadStatus.Unreadable)
        {
            Diagnostics.Warn("catalogue", read.Problem ?? $"Cached title {id} could not be read.");
        }

        return read.Entry;
    }

    private async Task<CatalogueTitle> WithRatingAsync(CatalogueTitle title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title.ImdbId))
        {
            return title;
        }

        try
        {
            return title with { Rating = await ratings.GetRatingAsync(title.ImdbId, cancellationToken) };
        }
        catch (CatalogueLookupException exception)
        {
            Diagnostics.Warn("catalogue", $"No rating for {title.Id} ({exception.Failure}); it stays unrated, which hides it from a child (D128).", exception);
            return title;
        }
    }

    private async Task<CatalogueTitle> WithAirDatesAsync(CatalogueTitle title, CancellationToken cancellationToken)
    {
        if (title.Kind != TitleKind.Show || string.IsNullOrWhiteSpace(title.ImdbId))
        {
            return title;
        }

        try
        {
            var dates = await episodes.GetAirDatesByImdbAsync(title.ImdbId, cancellationToken);
            return dates is null ? title : EpisodeAirDates.Merge(title, dates);
        }
        catch (CatalogueLookupException exception)
        {
            Diagnostics.Warn("catalogue", $"No air dates for {title.Id} ({exception.Failure}).", exception);
            return title;
        }
    }
}
