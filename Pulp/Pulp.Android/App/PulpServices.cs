using Android.Content;
using Codex.Ui.Voice;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Pulp.Calendar;
using Pulp.Domain.Calendar;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.FirstRun;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Services;
using Pulp.Domain.Sports;
using Pulp.Components;
using Pulp.HandOffs;
using Pulp.Infrastructure.Calendar;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Discover;
using Pulp.Infrastructure.Household;
using Pulp.Infrastructure.Sports;
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
/// <see cref="PutAsync"/> / <see cref="RemoveAsync"/> and, for a playlist or a child's pick, the edits
/// that read the record as it stands inside the same gate (<see cref="EditPlaylistAsync"/>), which stamp with this install's hybrid
/// logical clock and keep it on disk. The phone's own copy (<see cref="LocalHouseholdStore"/>) is the
/// working copy every read and edit uses; the shared file in Drive (<see cref="DriveHouseholdStore"/>,
/// finding §2.7) is reached only by the sync passes — on open, after each edit once
/// <see cref="HouseholdSyncSchedule.Debounce"/> has passed quietly, and on <see cref="SyncNowAsync"/> —
/// so a phone without Drive keeps working and loses nothing. <see cref="SyncStatus"/> says where it
/// stands.</para>
/// </summary>
public sealed class PulpServices
{
    private static readonly object InstanceGate = new();
    private static PulpServices? instance;

    private readonly IHouseholdStore household;
    private readonly DriveHouseholdStore sharedHousehold;
    private readonly HouseholdSyncLoop syncLoop;
    private readonly SemaphoreSlim passGate = new(1, 1);
    private RememberedRole? rememberedRole;
    private HouseholdSyncStatus syncStatus = HouseholdSyncStatus.LocalOnly("The household has not been compared with Drive yet.");
    private readonly LocalInstallStore install;
    private readonly CatalogueCache cache;
    private readonly StreamingAvailabilityClient availability;
    private readonly TvMazeClient episodes;
    private readonly OmdbClient ratings;
    private readonly DriveVoiceOptions voiceOptions;
    private readonly SportsFeeds sportsFeeds;
    private readonly SpoilerPeeks peeks = new();
    private FoldMemory foldMemory = FoldMemory.Empty;
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

        // The nightly calendar writer (D145, D149). Its Google account asks for only the two scopes the client
        // names; the consent lives on the Vault page (CalendarConnection). The hooks are set here so the worker,
        // which WorkManager can start with no shell, finds them once it asks for these services.
        var calendarAccount = new VoiceGoogleAccount(GoogleCalendarClient.Scopes.ToArray());
        CalendarConnection = new CalendarConnection(calendarAccount);
        CalendarSyncHooks.Calendar = new GoogleCalendarClient(http, calendarAccount.GetAccessToken);
        CalendarSyncHooks.Feed = new PulpCalendarFeed(this);
        CalendarSyncHooks.Warn = (message, exception) =>
        {
            if (exception is not null && CalendarFailures.ConsentWithdrawn(exception))
            {
                CalendarConnection.Disconnected();
            }

            Diagnostics.Warn("calendar", message, exception);
        };
        CalendarSchedule.Schedule(context);

        household = new LocalHouseholdStore(files);
        install = new LocalInstallStore(files);
        cache = new CatalogueCache(Path.Combine(cacheRoot, "catalogue"));

        // Sport (D208): MLB's own feed and ESPN's JSON over the one client, counted into Diagnostics only (D261).
        Sports = RoutedSportsSource.Shipped(http, counter);
        sportsFeeds = new SportsFeeds(
            Sports,
            SportsSourceTable.Default,
            new SportsCache(Path.Combine(cacheRoot, "sports")),
            (message, exception) => Diagnostics.Warn("sports", message, exception),
            () => DateTimeOffset.Now);

        sharedHousehold = new DriveHouseholdStore(
            new DriveTextFile(DriveFactory(account), DriveHouseholdStore.FileName, SharedDriveFileIds.Store),
            () => VaultPassphraseStore.Passphrase,
            () => HouseholdSync.MayCreateFile(HouseholdFileRole));
        rememberedRole = ReadRememberedRole();

        // The loop outlives any one pass: a failure is logged and backed off, never fatal (see HouseholdSyncLoop).
        syncLoop = new HouseholdSyncLoop(
            RunPassAsync,
            exception => Diagnostics.Warn("household", "A sync pass failed unexpectedly; the loop carries on and tries again later.", exception),
            () => DateTimeOffset.UtcNow);
        syncLoop.Opened();
        _ = Task.Run(() => syncLoop.RunAsync(CancellationToken.None));

        // The keys and the household role come from the vault, which nothing else reads at a cold start.
        _ = Task.Run(RefreshAtLaunchAsync);
    }

    // A damaged install file must not stop Pulp opening; it just means no role is remembered.
    private RememberedRole? ReadRememberedRole()
    {
        try
        {
            return install.Load().Role;
        }
        catch (HouseholdFormatException exception)
        {
            Diagnostics.Warn("household", "The install file could not be read for the remembered household role.", exception);
            return null;
        }
    }

    /// <summary>
    /// Reads the vault once at launch (the role and the keys are empty until it is read), off the UI
    /// thread. Never throws: a failure is logged and the phone works from what it remembers.
    /// </summary>
    private async Task RefreshAtLaunchAsync()
    {
        try
        {
            await RefreshKeysAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Diagnostics.Warn("vault", "The vault could not be read at launch; keys and the household role stay as remembered.", exception);
        }
    }

    /// <summary>
    /// The Drive service for the household file, over the same device-account token the vault reads
    /// with (<see cref="VoiceGoogleAccount"/>, full <c>drive</c> scope — a file shared to this account
    /// is found by name only with it).
    /// </summary>
    private static Func<CancellationToken, Task<DriveService>> DriveFactory(VoiceGoogleAccount account) => async _ =>
    {
        var token = await account.GetAccessToken();
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token),
            ApplicationName = "Pulp",
            HttpClientFactory = new GoogleHttpClientFactory(),
        });
    };

    public static PulpServices For(Context context)
    {
        lock (InstanceGate)
        {
            return instance ??= new PulpServices(context.ApplicationContext ?? context);
        }
    }

    public PulpDiagnostics Diagnostics { get; }

    /// <summary>
    /// Whether this phone's account owns the household file or has it shared to it (D166): what the vault
    /// says now, else the role remembered from its last read for this account
    /// (<see cref="HouseholdRoleMemory.Effective"/>). Unknown only when neither exists, which
    /// <see cref="HouseholdPermissions"/> does not restrict and which never creates the file.
    /// </summary>
    public HouseholdFileRole HouseholdFileRole =>
        HouseholdRoleMemory.Effective(Keys.HouseholdRole, Volatile.Read(ref rememberedRole), VoiceGoogleAccount.Email);

    /// <summary>What this phone may change in the household (D166).</summary>
    public HouseholdPermissions Rights => HouseholdPermissions.For(HouseholdFileRole);

    public VaultPulpKeySource Keys { get; }

    /// <summary>
    /// Where the owner's calendar consent stands, for the Settings and Vault pages: <see cref="CalendarConnection.Status"/>
    /// is the sentence ("Calendar not connected yet") and <see cref="CalendarConnection.ConnectAsync"/> is the consent button's action.
    /// </summary>
    public CalendarConnection CalendarConnection { get; }

    public ServiceCatalogue Catalogue { get; }

    /// <summary>Trailer ids for the title page's player.</summary>
    public YouTubeClient Trailers { get; }

    /// <summary>
    /// The sports feeds (D208), routed by <see cref="SportsSources"/>. Nothing a screen draws comes from here
    /// directly: <see cref="SportsAsync"/> hands it held or open sheets, and the contract carries no result (G15).
    /// </summary>
    public ISportsSource Sports { get; }

    /// <summary>Which source each league is read from (D208), for Settings → About to print (D140).</summary>
    public SportsSourceTable SportsSources => SportsSourceTable.Default;

    /// <summary>Discover's mood search and the model-made playlist (D104, D171).</summary>
    public IMoodClient Mood { get; }

    /// <summary>The one poster loader every screen paints from.</summary>
    public Posters Posters { get; }

    /// <summary>Restores the keys and the shared Claude key from the vault. Call once at launch, off the UI thread.</summary>
    public async Task RefreshKeysAsync(CancellationToken cancellationToken)
    {
        await Keys.RefreshAsync(cancellationToken);
        RememberRole();

        // The vault says whose Drive holds the household, and a passphrase may just have been entered:
        // either can turn a waiting or refused pass into one that lands, so ask for one now.
        RequestSync();
        try
        {
            await voiceOptions.RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, cancellationToken))
        {
            Diagnostics.Warn("vault", "The shared Claude key could not be read; mood search answers 'no idea' until it can.", exception);
        }
    }

    // The role the vault just said is kept on this install, under the account it was read for, so the next
    // cold start has it before the vault is read again.
    private void RememberRole()
    {
        var role = Keys.HouseholdRole;
        var account = VoiceGoogleAccount.Email;
        if (role == HouseholdFileRole.Unknown || string.IsNullOrWhiteSpace(account))
        {
            return;
        }

        var remembered = new RememberedRole(role, account);
        if (Volatile.Read(ref rememberedRole) == remembered)
        {
            return;
        }

        install.SaveRole(role, account);
        Volatile.Write(ref rememberedRole, remembered);
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

    /// <summary>
    /// Writes the people typed at first run (D142), each with a name record. The roster is read and the
    /// ids minted inside the same gate the sync's adoption takes, so a roster Drive brought in while the
    /// owner typed is the one the ids are checked against — a draft id can never land on someone already
    /// there (see <see cref="HouseholdDraft.Commit"/>). Only the owner's phone adds people (D166).
    /// </summary>
    public async Task<DraftCommit> AddPeopleAsync(HouseholdDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!Rights.CanAddOrRemoveViewers)
        {
            throw new InvalidOperationException("Adding people is the owner's, on their phone; this phone has the household shared to it.");
        }

        DraftCommit? made = null;
        await EditAsync((document, stamp, writer) =>
        {
            // One edit writes the viewers, their names and each child's rules together: a child with no
            // rules record would make the hand-off planner refuse the whole household.
            var written = HouseholdDraftWrite.Apply(document, draft, stamp, writer);
            made = written.Commit;
            return written.Document;
        }, cancellationToken);
        return made ?? throw new InvalidOperationException("The household edit ran without producing the people it was to add.");
    }

    // ---- playlist and pick edits: read-modify-write against the household as it stands ----

    /// <summary>A brand-new playlist under a fresh id (<see cref="HouseholdEdits.NewPlaylistId"/>), which no other writer can have edited.</summary>
    public Task CreatePlaylistAsync(HouseholdPlaylist playlist, CancellationToken cancellationToken) =>
        PutAsync(HouseholdMaps.Playlist, playlist, cancellationToken);

    /// <summary>
    /// Changes one playlist as the household holds it when the write lands — never a copy a screen read
    /// earlier, so a tick, a move or an undo made in between is kept. Throws when the playlist is gone.
    /// </summary>
    public Task EditPlaylistAsync(PlaylistId id, Func<Playlist, Playlist> change, CancellationToken cancellationToken) =>
        EditAsync((document, stamp, writer) => HouseholdEdits.EditPlaylist(document, id, change, stamp, writer), cancellationToken);

    /// <summary>A parent's pick for a child, which allow-lists its title for them (D255).</summary>
    public Task AddParentPickAsync(ViewerId child, ParentPick pick, CancellationToken cancellationToken) =>
        PutAsync(HouseholdMaps.ParentPick, new ParentPickEntry(child, pick), cancellationToken);

    /// <summary>Takes a title off a child's Favorites by key, against the household as it stands.</summary>
    public Task RemoveParentPickAsync(ParentPickRemoval removal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(removal);
        return EditAsync(removal.Apply, cancellationToken);
    }

    /// <summary>The undo of <see cref="RemoveParentPickAsync"/>: the removed pick, or <paramref name="fallback"/> when it found none.</summary>
    public Task PutBackParentPickAsync(ParentPickRemoval removal, ParentPick fallback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(removal);
        return EditAsync((document, stamp, writer) => removal.Undo(document, fallback, stamp, writer), cancellationToken);
    }

    /// <summary>
    /// One edit of the household document as it stands when the write lands, applied whole — for a change
    /// that touches several records and must never be seen half-done by a sync pass (a child and their
    /// rules, D12).
    /// </summary>
    public Task EditHouseholdAsync(Func<HouseholdDocument, HlcTimestamp, string, HouseholdDocument> edit, CancellationToken cancellationToken) =>
        EditAsync(edit, cancellationToken);

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

        syncLoop.Edited();
    }

    // ---- the shared household file ----

    /// <summary>Where this phone's household stands against Drive: local only, synced at, offline, waiting for the share, or unreadable and why.</summary>
    public HouseholdSyncStatus SyncStatus => Volatile.Read(ref syncStatus);

    /// <summary>
    /// Raised (off the UI thread) after every sync pass. A pass may have brought the other phone's
    /// records home, so a screen showing the household re-reads it.
    /// </summary>
    public event EventHandler? SyncStatusChanged;

    /// <summary>
    /// Runs a pass now, whatever the schedule was holding — including after an unreadable file, which
    /// otherwise stops passes until asked (a new passphrase is the usual reason to ask). Never throws:
    /// the outcome is in <see cref="SyncStatus"/>, and a pass that fails for any reason is logged.
    /// </summary>
    public async Task<HouseholdSyncStatus> SyncNowAsync(CancellationToken cancellationToken)
    {
        syncLoop.Update(schedule => schedule.Asked(DateTimeOffset.UtcNow));
        try
        {
            await RunPassAsync(cancellationToken);
        }
        catch (Exception exception) when (CancellationVerdict.IsUserCancel(exception, cancellationToken))
        {
            return SyncStatus;
        }
        catch (Exception exception)
        {
            Diagnostics.Warn("household", "Sync now failed unexpectedly; the phone keeps its own copy.", exception);
        }

        return SyncStatus;
    }

    /// <summary>Asks the background loop for a pass at once (coming back to the app, a vault just opened). Returns immediately.</summary>
    public void RequestSync() => syncLoop.Ask();

    /// <summary>
    /// One pass (finding §2.7): the phone's file as it stands, published through the Drive store
    /// (read → merge → write if needed → re-read, <see cref="HouseholdPublish"/>), and what that settles on
    /// merged back into the phone's file as it stands <i>then</i> (<see cref="HouseholdSync.Adopt"/>), so an
    /// edit made while the pass was on the network is kept. Every failure is caught here, logged with its
    /// reason and turned into <see cref="SyncStatus"/>; the phone's own file is never the casualty.
    /// </summary>
    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        await passGate.WaitAsync(cancellationToken);
        try
        {
            syncLoop.Update(schedule => schedule.Started(DateTimeOffset.UtcNow));

            if (HouseholdSync.Readiness(VaultPassphraseStore.IsSet, VoiceGoogleAccount.IsSignedIn) is { } localOnly)
            {
                SetStatus(localOnly);
                return;
            }

            var result = await sharedHousehold.PublishAsync(await household.LoadAsync(cancellationToken), cancellationToken);
            var brought = await AdoptAsync(result.Held, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            syncLoop.Update(schedule =>
            {
                switch (result.Outcome)
                {
                    case HouseholdPublishOutcome.AlreadyCurrent or HouseholdPublishOutcome.Written:
                        schedule.Succeeded();
                        break;
                    default:
                        schedule.Failed(now);
                        break;
                }
            });

            var settled = result.Outcome switch
            {
                HouseholdPublishOutcome.AlreadyCurrent or HouseholdPublishOutcome.Written => HouseholdSyncStatus.Synced(now),
                HouseholdPublishOutcome.NotYetShared => HouseholdSyncStatus.WaitingForShare(
                    SyncStatus.LastSynced,
                    $"No household file is shared with this account yet. The owner shares '{DriveHouseholdStore.FileName}' from their Drive; until then this phone keeps its household to itself."),
                _ => SyncStatus.WentOffline("The other phone was saving at the same moment; this phone's changes are kept and go up on the next pass."),
            };

            // A screen re-reads when records came home (SyncRefresh), so the pass says whether any did.
            SetStatus(settled with { BroughtRecords = brought });
        }
        catch (HouseholdUnreadableException exception)
        {
            Diagnostics.Warn("household", exception.Message, exception);
            Block(HouseholdSyncStatus.Unreadable(exception.Reason, exception.Message, SyncStatus.LastSynced));
        }
        catch (HouseholdFormatException exception)
        {
            // This phone's own file is damaged: nothing it holds can be merged up without guessing.
            Diagnostics.Warn("household", "This phone's household file could not be read, so it was not synced: " + exception.Message, exception);
            Block(HouseholdSyncStatus.Unreadable(HouseholdUnreadableReason.Damaged, exception.Message, SyncStatus.LastSynced));
        }
        catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, cancellationToken))
        {
            // Drive unreachable, a token refused, a timeout (a TaskCanceledException is one): the edits wait on the phone and the pass is retried.
            Diagnostics.Warn("household", "Drive could not be reached for the household; this phone keeps working from its own copy and tries again later.", exception);
            syncLoop.Update(schedule => schedule.Failed(DateTimeOffset.UtcNow));

            SetStatus(SyncStatus.WentOffline(exception.Message));
        }
        finally
        {
            passGate.Release();
        }
    }

    /// <summary>Merges what the pass settled on into the phone's file; true when that brought anything the phone lacked.</summary>
    private async Task<bool> AdoptAsync(HouseholdDocument published, CancellationToken cancellationToken)
    {
        await householdGate.WaitAsync(cancellationToken);
        try
        {
            var now = await household.LoadAsync(cancellationToken);
            var kept = HouseholdSync.Adopt(now, published);
            if (kept.ContentEquals(now))
            {
                return false;
            }

            await household.SaveAsync(kept, cancellationToken);
            return true;
        }
        finally
        {
            householdGate.Release();
        }
    }

    private void Block(HouseholdSyncStatus status)
    {
        syncLoop.Update(schedule => schedule.Blocked());
        SetStatus(status);
    }

    private void SetStatus(HouseholdSyncStatus status)
    {
        Volatile.Write(ref syncStatus, status);
        try
        {
            SyncStatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            // A screen's handler failing must not stop the sync loop that raised it.
            Diagnostics.Warn("household", "A screen's handler for the household sync status failed.", exception);
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
            Diagnostics.Warn("catalogue", $"A title could not be refreshed ({exception.Failure}); the cached copy stands.", exception);
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

    // ---- the calendar ----

    /// <summary>
    /// The Calendar page for one viewer (mockup 8): the next two weeks of their own and shared items only
    /// (D244, D245), a held league's revealing games absent (D242), grouped by day. Reads off the same
    /// sources the nightly writer uses, so the page and the household calendar agree.
    /// </summary>
    public async Task<CalendarScreenPage> CalendarPageAsync(ViewerId viewer, CancellationToken cancellationToken)
    {
        var read = await new CalendarReads(this).ReadAsync(cancellationToken);
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, read.Zone).DateTime);
        IReadOnlySet<LeagueId> held = read.HeldByAdult.TryGetValue(viewer, out var mine) ? mine : new HashSet<LeagueId>();
        var items = CalendarItems.ForViewer(read.Sources, viewer, today, read.Zone, held);
        var context = new CalendarScreenContext(viewer, read.Household.NameOf, service => Catalogue.ById(service)?.Name, read.KindOf);
        return CalendarScreen.Build(items, context, now, read.Zone);
    }

    // ---- sport ----

    /// <summary>
    /// The Sports tab for one viewer from the cache alone — no network — for an instant first paint
    /// (<see cref="SportsAsync"/> follows). One held or open sheet per followed league, in league order,
    /// and the fold it starts from; never a raw feed. Drawing a league spends its peek.
    /// </summary>
    public async Task<SportsPage> CachedSportsAsync(ViewerId viewer, CancellationToken cancellationToken) =>
        await SportsPageAsync(viewer, refresh: false, cancellationToken);

    /// <summary>
    /// <see cref="CachedSportsAsync"/> after the cache has been brought up to date: a league is fetched only
    /// when <see cref="SportsFreshness"/> says it is due, and one that fails keeps its cached copy (logged to
    /// Diagnostics, never voiced). Which games are recordings still waiting comes from the viewer's own
    /// sittings of kind Game (<see cref="RecordedGames"/>).
    /// </summary>
    public async Task<SportsPage> SportsAsync(ViewerId viewer, CancellationToken cancellationToken) =>
        await SportsPageAsync(viewer, refresh: true, cancellationToken);

    private async Task<SportsPage> SportsPageAsync(ViewerId viewer, bool refresh, CancellationToken cancellationToken)
    {
        var view = await HouseholdAsync(cancellationToken);
        var follows = view.SportsFollows;
        var feeds = await sportsFeeds.FollowedAsync(follows.Where(follow => follow.Viewer == viewer).ToList(), refresh, cancellationToken);
        return SportsPage.Build(feeds, follows, viewer, view.SittingsOf(viewer), DateTimeOffset.Now, peeks, Volatile.Read(ref foldMemory));
    }

    /// <summary>The Spoilers tag was tapped on a league's line: the next draw of that league is a peek, once (D183, D242).</summary>
    public void TapSpoilersTag(LeagueId league) => peeks.Tap(league);

    /// <summary>What the viewer has opened or closed on the Sports tab (D241); the screen reads <see cref="FoldMemory.Choices"/> to keep it.</summary>
    public FoldMemory SportsFold
    {
        get => Volatile.Read(ref foldMemory);
        set => Volatile.Write(ref foldMemory, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Every league with its teams, for the "add a team" picker (D67): the cache, else a one-day look. A league that cannot be reached is left out and logged.</summary>
    public Task<IReadOnlyList<LeagueTeams>> BrowseLeaguesAsync(CancellationToken cancellationToken) =>
        sportsFeeds.BrowseAsync(cancellationToken);

    /// <summary>Follows a team for one adult; its whole league comes in with it (D60, D67). Following twice is one follow.</summary>
    public async Task FollowTeamAsync(ViewerId viewer, TeamId team, CancellationToken cancellationToken)
    {
        // Following is an adult's act: a child's follow would reach the shared calendar and name the child.
        if (CalendarFollows.RefusesFollow((await HouseholdAsync(cancellationToken)).Roster, viewer))
        {
            throw new InvalidOperationException($"'{viewer}' is a child on this household's roster, and only an adult follows a team.");
        }

        await PutAsync(HouseholdMaps.SportsFollow, new SportsFollow(viewer, team), cancellationToken);
    }

    /// <summary>Stops following a team; the follow's tombstone, so the other phone drops it too.</summary>
    public Task UnfollowTeamAsync(ViewerId viewer, TeamId team, CancellationToken cancellationToken) =>
        RemoveAsync(HouseholdMaps.SportsFollow, new SportsFollow(viewer, team), cancellationToken);

    /// <summary>
    /// "Mark watched" / "Mark unwatched" on a game (D55, D278): a manual mark in the viewer's own sittings,
    /// which is the evidence <see cref="RecordedGames"/> reads. Pulp learns of a game from nothing else yet.
    /// </summary>
    public Task MarkGameAsync(ViewerId viewer, GameId game, bool watched, CancellationToken cancellationToken) =>
        PutAsync(
            HouseholdMaps.Watched,
            new WatchedSitting(viewer, new WatchEvent(
                new TitleId(game.Value), WatchedKind.Game, DateTimeOffset.UtcNow, Service: null,
                Evidence: watched ? WatchSource.MarkedWatched : WatchSource.MarkedUnwatched)),
            cancellationToken);

    /// <summary>
    /// The Calendar's door to the feeds: the followed leagues' schedules for <paramref name="follows"/> (every
    /// adult's, since the household calendar carries everyone's, D60), brought up to date when
    /// <paramref name="refresh"/>. Internal on purpose — a fixture built from a feed carries a name, a time and a
    /// channel, never a standing or a highlight, which is why only <c>SportsFollowing.Fixtures</c> should touch one;
    /// no screen is handed a feed (<c>SportsScreenGetsOnlySheetsTests</c>).
    /// </summary>
    internal Task<IReadOnlyList<LeagueFeed>> FollowedFeedsAsync(IReadOnlyCollection<SportsFollow> follows, bool refresh, CancellationToken cancellationToken) =>
        sportsFeeds.FollowedAsync(follows, refresh, cancellationToken);

    private CachedTitle? ReadCache(TitleId id)
    {
        var read = cache.Read(id);
        if (read.Status == CacheReadStatus.Unreadable)
        {
            Diagnostics.Warn("catalogue", read.Problem ?? "A cached title could not be read.");
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
            Diagnostics.Warn("catalogue", $"A title has no rating ({exception.Failure}); it stays unrated, which hides it from a child (D128).", exception);
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
            Diagnostics.Warn("catalogue", $"A show has no air dates ({exception.Failure}).", exception);
            return title;
        }
    }
}
