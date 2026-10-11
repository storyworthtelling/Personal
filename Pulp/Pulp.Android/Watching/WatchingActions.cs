using Android.App;
using Android.Widget;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Actions;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Domain.Watching;
using Pulp.Library;
using Pulp.Title;

namespace Pulp.Watching;

/// <summary>
/// What the Continue watching card's four entries do (mockup 10, D256): Play the next thing through
/// the one hand-off door, mark it watched (undo-first, D160), Add to… (mockup 11), and remove it from
/// Continue watching (undo-first). <see cref="ActionCardPlan"/> decides the entries; this wires them.
/// A removal is the viewer's own household record (<see cref="ContinueDismissedEntry"/>), so it sticks
/// across launches and phones until they sit down with the title again; undo writes its tombstone.
/// </summary>
public sealed class WatchingActions
{
    private readonly Activity host;
    private readonly FrameLayout overlay;
    private readonly PulpServices services;
    private readonly ViewerId viewer;
    private readonly Action reload;
    private readonly Action<TitleId, bool> setHidden;
    private readonly LibraryWrites writes;

    /// <param name="setHidden">Hides a row (true) or brings it back (false) on this page.</param>
    public WatchingActions(Activity host, FrameLayout overlay, PulpServices services, ViewerId viewer, Action reload, Action<TitleId, bool> setHidden)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.viewer = viewer;
        this.reload = reload ?? throw new ArgumentNullException(nameof(reload));
        this.setHidden = setHidden ?? throw new ArgumentNullException(nameof(setHidden));

        // One queue for every write this card makes, so an undo never lands ahead of its act (and so
        // never takes the earlier clock stamp and loses to it).
        writes = new LibraryWrites(services, () => host.RunOnUiThread(reload), "watching");
    }

    public void ShowCard(WatchingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var next = row.Row.NextUp;
        var item = new CardItem(row.Name, CardPlace.ContinueWatching)
        {
            Subtitle = ContinueRowText.CardSubtitle(row.NextUp, row.ServiceName),
            NextUp = next is null ? null : ContinueRowText.Episode(next),
            NextEpisode = next is null ? null : ContinueRowText.EpisodeShort(next),
        };

        var marked = MarkSitting(row);
        var removal = new ContinueDismissedEntry(viewer, new ContinueDismissal(row.Row.Title, DateTimeOffset.UtcNow));
        var handlers = new Dictionary<CardActionKind, CardHandler>
        {
            [CardActionKind.Play] = new(() => Play(row)),
            [CardActionKind.MarkWatched] = new(
                () => writes.Enqueue("mark watched", token => services.PutAsync(HouseholdMaps.Watched, marked, token)),
                () => writes.Enqueue("undo the mark", token => services.RemoveAsync(HouseholdMaps.Watched, marked, token))),
            [CardActionKind.AddTo] = new(() => AddTo(row)),
            [CardActionKind.RemoveFromContinueWatching] = new(
                () =>
                {
                    setHidden(row.Row.Title, true);
                    writes.Enqueue("remove from Continue watching", token => services.PutAsync(HouseholdMaps.ContinueDismissed, removal, token));
                },
                () =>
                {
                    setHidden(row.Row.Title, false);
                    writes.Enqueue("put back on Continue watching", token => services.RemoveAsync(HouseholdMaps.ContinueDismissed, removal, token));
                }),
        };

        PulpActionCard.Show(host, overlay, item, handlers);
    }

    /// <summary>The manual mark (D10, D182): the next episode for a show, the whole title for a film.</summary>
    private WatchedSitting MarkSitting(WatchingRow row) =>
        new(viewer, new WatchEvent(
            row.Row.Title,
            row.Row.Kind == TitleKind.Film ? WatchedKind.Film : WatchedKind.Show,
            DateTimeOffset.UtcNow,
            row.Home?.Service,
            row.Row.NextUp,
            WatchSource.MarkedWatched));

    private void Play(WatchingRow row)
    {
        // With no home on the household's services, or nothing cached, there is no link to hand off;
        // play lives on the title page (D151), which says where it streams instead.
        if (row.Title is not { } title || row.Home is not { } home || LinkFor(title, home.Service, row.Row.NextUp) is not { } link)
        {
            host.StartActivity(TitleActivity.For(host, row.Row.Title));
            return;
        }

        var episodeName = row.Row.NextUp is { } next ? EpisodeName(title, next) : null;
        var request = new HandOffRequest(
            [viewer],
            DiscoverFacts.For(title, _ => []),
            link,
            new[] { title.Name, episodeName ?? string.Empty });
        var serviceName = row.ServiceName ?? home.Service.Value;
        var buttons = new PulpNotices.Buttons(OpenTitle: id => host.StartActivity(TitleActivity.For(host, id)));

        _ = Task.Run(async () =>
        {
            try
            {
                var executor = await services.HandOffsAsync(CancellationToken.None)
                    ?? throw new InvalidOperationException("The household has no roster, so no hand-off can be checked.");
                host.RunOnUiThread(() => Start(executor, request, row, title, serviceName, buttons));
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("watching", "Play could not reach the hand-off door.", exception);
            }
        });
    }

    private void Start(Pulp.HandOffs.HandOffExecutor executor, HandOffRequest request, WatchingRow row, CatalogueTitle title, string serviceName, PulpNotices.Buttons buttons)
    {
        var at = DateTimeOffset.UtcNow;
        var outcome = executor.Start(host, request, at);
        PulpNotices.AfterHandOff(host, overlay, outcome, request, serviceName, title.Name, at, buttons);
        if (outcome.Recorded is not { } pending)
        {
            return;
        }

        // The hand-off is the fallback evidence (D30) until the session says otherwise.
        var sitting = new WatchedSitting(viewer, new WatchEvent(
            row.Row.Title,
            row.Row.Kind == TitleKind.Film ? WatchedKind.Film : WatchedKind.Show,
            pending.Evidence.At,
            pending.Service,
            row.Row.NextUp,
            WatchSource.HandOff));
        writes.Enqueue("record the hand-off", token => services.PutAsync(HouseholdMaps.Watched, sitting, token));
    }

    /// <summary>The next episode's own link on the home when the catalogue has one, else the title's.</summary>
    private static HandOffLink? LinkFor(CatalogueTitle title, ServiceId service, EpisodeRef? next)
    {
        var episodeOffers = next is null
            ? []
            : title.Seasons.SelectMany(season => season.Episodes).Where(episode => episode.Episode == next).SelectMany(episode => episode.Offers);
        var offer = Streamable(episodeOffers, service) ?? Streamable(title.Offers, service);
        if (offer is null || !Uri.TryCreate(offer.Link, UriKind.Absolute, out var uri))
        {
            return null;
        }

        Uri? video = null;
        if (offer.VideoLink is { } videoLink && Uri.TryCreate(videoLink, UriKind.Absolute, out var parsed))
        {
            video = parsed;
        }

        return new HandOffLink(title.Id, service, uri, video);
    }

    private static TitleOffer? Streamable(IEnumerable<TitleOffer> offers, ServiceId service) =>
        offers.FirstOrDefault(offer => offer.Service == service && offer.Kind is OfferKind.Subscription or OfferKind.Free);

    private static string? EpisodeName(CatalogueTitle title, EpisodeRef next) =>
        title.Seasons.SelectMany(season => season.Episodes).FirstOrDefault(episode => episode.Episode == next)?.Name;

    private void AddTo(WatchingRow row)
    {
        var entry = PlaylistEntry.ForTitle(row.Row.Title, row.Row.Kind);
        _ = Task.Run(async () =>
        {
            try
            {
                var playlists = await services.PlaylistsAsync(viewer, CancellationToken.None);
                var choices = playlists
                    .Select(playlist => new PickerPlaylist(playlist.Id.Value, playlist.List, playlist.Shared, LastUsed: null))
                    .ToList();

                // Each child's Favorites is the picker's second group (D254), but the roster carries no
                // child's name to head a row with, so the group stays empty until Release D names them.
                var model = AddToPickerPlan.Build(row.Name, entry, row.Title is { } title ? DiscoverFacts.For(title, _ => []) : null, choices, []);
                var picks = new PickerWrites(services, writes.Enqueue, viewer, row.Row.Title, entry);
                host.RunOnUiThread(() => AddToPicker.Show(
                    host, overlay, model,
                    picks.OnPick,
                    name => picks.NewPlaylist(name)));
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("watching", "The playlists for a row could not be read.", exception);
            }
        });
    }
}
