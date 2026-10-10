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
using Pulp.Title;

namespace Pulp.Watching;

/// <summary>
/// What the Continue watching card's four entries do (mockup 10, D256): Play the next thing through
/// the one hand-off door, mark it watched (undo-first, D160), Add to… (mockup 11), and remove it from
/// Continue watching (undo-first). <see cref="ActionCardPlan"/> decides the entries; this wires them.
/// </summary>
public sealed class WatchingActions
{
    private readonly Activity host;
    private readonly FrameLayout overlay;
    private readonly PulpServices services;
    private readonly ViewerId viewer;
    private readonly Action reload;
    private readonly Action<TitleId, bool> setHidden;

    /// <param name="setHidden">Hides a row (true) or brings it back (false) on this page.</param>
    public WatchingActions(Activity host, FrameLayout overlay, PulpServices services, ViewerId viewer, Action reload, Action<TitleId, bool> setHidden)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.viewer = viewer;
        this.reload = reload ?? throw new ArgumentNullException(nameof(reload));
        this.setHidden = setHidden ?? throw new ArgumentNullException(nameof(setHidden));
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
        var handlers = new Dictionary<CardActionKind, CardHandler>
        {
            [CardActionKind.Play] = new(() => Play(row)),
            [CardActionKind.MarkWatched] = new(
                () => Write(() => services.PutAsync(HouseholdMaps.Watched, marked, CancellationToken.None), "mark watched"),
                () => Write(() => services.RemoveAsync(HouseholdMaps.Watched, marked, CancellationToken.None), "undo the mark")),
            [CardActionKind.AddTo] = new(() => AddTo(row)),
            [CardActionKind.RemoveFromContinueWatching] = new(
                () => setHidden(row.Row.Title, true),
                () => setHidden(row.Row.Title, false)),
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
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.Diagnostics.Warn("watching", $"Play on '{title.Name}' could not reach the hand-off door.", exception);
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
        Write(() => services.PutAsync(HouseholdMaps.Watched, sitting, CancellationToken.None), "record the hand-off");
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
                host.RunOnUiThread(() => AddToPicker.Show(
                    host, overlay, model,
                    (pickerRow, change) => OnPick(playlists, pickerRow, change, entry),
                    name => NewPlaylist(name, entry)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.Diagnostics.Warn("watching", $"The playlists for '{row.Name}' could not be read.", exception);
            }
        });
    }

    private Action? OnPick(IReadOnlyList<HouseholdPlaylist> playlists, PickerRow row, PickerChange change, PlaylistEntry entry)
    {
        if (row.Group != PickerGroup.Playlists || playlists.FirstOrDefault(playlist => playlist.Id.Value == row.Key) is not { } playlist)
        {
            return null;
        }

        var added = playlist with { List = playlist.List.Add(entry) };
        var removed = playlist with { List = playlist.List.Remove(entry) };
        switch (change)
        {
            case PickerChange.Add:
            case PickerChange.AddAndAllowList:
                Write(() => services.PutAsync(HouseholdMaps.Playlist, added, CancellationToken.None), "add to a playlist");
                return null;
            case PickerChange.RemoveUndoFirst:
                Write(() => services.PutAsync(HouseholdMaps.Playlist, removed, CancellationToken.None), "take off a playlist");
                return () => Write(() => services.PutAsync(HouseholdMaps.Playlist, added, CancellationToken.None), "put back on a playlist");
            case PickerChange.Refused:
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "A picker change the home does not handle.");
        }
    }

    /// <summary>A playlist named in place, personal by default (mockup 11), with this title already on it.</summary>
    private void NewPlaylist(string name, PlaylistEntry entry)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var playlist = new HouseholdPlaylist(
            new PlaylistId("playlist-" + Guid.NewGuid().ToString("N")), viewer, Shared: false, Playlist.Named(name.Trim()).Add(entry));
        Write(() => services.PutAsync(HouseholdMaps.Playlist, playlist, CancellationToken.None), "make a playlist");
    }

    /// <summary>A household write off the UI thread; the page reloads when it lands, so the list says what is true.</summary>
    private void Write(Func<Task> write, string what) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await write();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.Diagnostics.Warn("watching", $"Could not {what}; the household is unchanged.", exception);
            }

            host.RunOnUiThread(reload);
        });
}
