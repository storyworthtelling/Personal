using Android.App;
using Android.Widget;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Actions;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Settings;
using Pulp.Domain.Title;
using Pulp.Library;

namespace Pulp.Favorites;

/// <summary>
/// What a title does in a child's view: Play, and the child's ★ (mockup 10's child card, D230, D253).
/// Play goes only through the one hand-off door (G13) with the children watching as the request's
/// audience, so the gate judges them — the title must pass every child's filter and the service must
/// be attested or kids-only (D210, D44) — and the hand-off's evidence is recorded as each child's own
/// sitting (progress only, D27), never an adult's, so a child's viewing never reaches adult taste (D23).
/// The home asked for is the best one every child may start on (D282). A refusal lands on the dog-ear.
///
/// <para>The star is the child's own household record (<see cref="ChildStarEntry"/>), written through
/// the services' one write path; taking it away is undo-first (§13).</para>
/// </summary>
public sealed class ChildPlay
{
    private readonly Activity host;
    private readonly Func<FrameLayout> overlay;
    private readonly PulpServices services;
    private readonly ChildAudience audience;
    private readonly HouseholdView household;
    private readonly LibraryWrites writes;

    /// <param name="changed">Runs on the UI thread once a write has landed, so the page re-reads.</param>
    public ChildPlay(Activity host, Func<FrameLayout> overlay, PulpServices services, ChildAudience audience, HouseholdView household, Action changed)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.audience = audience ?? throw new ArgumentNullException(nameof(audience));
        this.household = household ?? throw new ArgumentNullException(nameof(household));
        ArgumentNullException.ThrowIfNull(changed);
        writes = new LibraryWrites(services, () => host.RunOnUiThread(changed), "child");
    }

    /// <summary>Whether a child watching has starred the title.</summary>
    public bool IsStarred(TitleId title) => ChildFavorites.IsStarred(audience, household.StarsOf, title);

    /// <summary>The child's card: Play, and ★ Favorite or Remove from Favorites (undo-first).</summary>
    public void ShowCard(TitleId title, string name, string? subtitle)
    {
        var item = new CardItem(name, CardPlace.ContinueWatching)
        {
            Subtitle = subtitle,
            ChildView = true,
            ChildStarred = IsStarred(title),
        };

        var unstarred = ChildFavorites.ToUnstar(audience, household.StarsOf, title);
        var handlers = new Dictionary<CardActionKind, CardHandler>
        {
            [CardActionKind.Play] = new(() => Play(title)),
            [CardActionKind.Favorite] = new(() => Star(title)),
            [CardActionKind.Unfavorite] = new(
                () => Write("take away the star", unstarred, put: false),
                () => Write("put the star back", unstarred, put: true)),
        };

        PulpActionCard.Show(host, overlay(), item, handlers);
    }

    /// <summary>Plays the title for the children watching, read cache-first off the UI thread.</summary>
    public void Play(TitleId id)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var title = await services.TitleAsync(id, tracked: false, opened: true, CancellationToken.None);
                var executor = await services.HandOffsAsync(CancellationToken.None)
                    ?? throw new InvalidOperationException("The household has no roster, so no hand-off can be checked.");
                if (title is not null)
                {
                    host.RunOnUiThread(() => Start(executor, title));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.Diagnostics.Warn("child", $"Play on '{id}' could not reach the hand-off door.", exception);
            }
        });
    }

    /// <summary>
    /// Plays <paramref name="title"/> for the children watching, through the executor as them (G13):
    /// the title page, which already holds the title, starts here.
    /// </summary>
    public void Start(Pulp.HandOffs.HandOffExecutor executor, CatalogueTitle title)
    {
        var facts = DiscoverFacts.For(title, _ => []);
        if (!audience.Shows(facts))
        {
            // Never offered a hidden title in the first place; the gate would refuse it too.
            return;
        }

        var at = DateTimeOffset.UtcNow;
        var home = audience.HomeFor(TitleHomes.Choose(title, household.ServicePlans, household.HomeOverrides), facts, services.Catalogue, household.Attested);
        var sittings = household.SittingsOf(audience.Watching[0]);
        var episode = TitleProgress.ToPlay(title, sittings, at);
        if (home is null || TitleHomes.LinkFor(title, home.Service, episode) is not { } link)
        {
            var nowhere = home is null ? "your services" : services.Catalogue.ById(home.Service)?.Name ?? home.Service.Value;
            PulpNotices.Raise(host, overlay(), WaitingNotices.Refused(StartRefusal.NotAHome, nowhere, title.Name, title.Id, at), new PulpNotices.Buttons());
            return;
        }

        var request = new HandOffRequest(audience.Watching, facts, link, TitleHomes.SessionNames(title, episode));
        var serviceName = services.Catalogue.ById(home.Service)?.Name ?? home.Service.Value;
        var outcome = executor.Start(host, request, at);
        PulpNotices.AfterHandOff(host, overlay(), outcome, request, serviceName, title.Name, at, new PulpNotices.Buttons());
        if (outcome.Recorded is not { } pending)
        {
            return;
        }

        // D10, D30: the hand-off is the fallback evidence of watching — each child's own, never an adult's (D23).
        var kind = title.Kind == TitleKind.Film ? WatchedKind.Film : WatchedKind.Show;
        foreach (var child in audience.Watching)
        {
            var sitting = new WatchedSitting(child, new WatchEvent(title.Id, kind, pending.Evidence.At, pending.Service, episode, WatchSource.HandOff));
            writes.Enqueue("record the child's hand-off", token => services.PutAsync(HouseholdMaps.Watched, sitting, token));
        }
    }

    /// <summary>The child's star on a title page: starred, it is taken away undo-first (§13); not starred, it is given.</summary>
    public void ToggleStar(TitleId title, string name)
    {
        if (!IsStarred(title))
        {
            Star(title);
            return;
        }

        var unstarred = ChildFavorites.ToUnstar(audience, household.StarsOf, title);
        Write("take away the star", unstarred, put: false);
        DogEar.Show(host, overlay(), HouseholdText.Undo("remove star", name), () => Write("put the star back", unstarred, put: true));
    }

    private void Star(TitleId title) =>
        Write("star", ChildFavorites.ToStar(audience, household.StarsOf, title, DateTimeOffset.UtcNow), put: true);

    private void Write(string what, IReadOnlyList<ChildStarEntry> entries, bool put)
    {
        foreach (var entry in entries)
        {
            writes.Enqueue(what, token => put
                ? services.PutAsync(HouseholdMaps.ChildStar, entry, token)
                : services.RemoveAsync(HouseholdMaps.ChildStar, entry, token));
        }
    }
}
