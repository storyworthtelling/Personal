using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Library;
using Pulp.Domain.Services;

namespace Pulp.Domain.Sports;

/// <summary>What Play does for one game: open a service's home through the hand-off door, or show a card.</summary>
public sealed record GamePlayDecision(ServiceId? Service, Uri? Home, string? Note)
{
    public bool Opens => Service is not null && Home is not null;
}

/// <summary>
/// Game Play (D311, D312): the channel text resolved to a ticked service, then the one hand-off door (G8).
/// A game's feed carries a channel and no link, so the open is the service's home (D57: open the live section
/// and name the channel). The hand-off is usage evidence only; it never says the game was watched.
/// </summary>
public static class GamePlay
{
    public static GamePlayDecision Decide(
        string? channel, IReadOnlyCollection<ServiceId> ticked, BroadcasterTable table, ServiceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(ticked);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(catalogue);
        var line = SportsScreenText.ChannelLine(channel);
        var resolution = table.Resolve(channel, ticked);
        var service = resolution.Service is { } id ? catalogue.ById(id) : null;
        if (resolution.Kind == BroadcastKind.Unknown || service is null)
        {
            return new GamePlayDecision(null, null, SportsScreenText.PlayNote(line));
        }

        if (resolution.Kind == BroadcastKind.NotTicked)
        {
            return new GamePlayDecision(null, null, SportsScreenText.TickNote(line, service.Name));
        }

        return BroadcasterTable.HomeLink(service) is { } home
            ? new GamePlayDecision(service.Id, home, null)
            : new GamePlayDecision(null, null, SportsScreenText.PlayNote(line));
    }

    /// <summary>
    /// The request the planner judges. The game's facts are unrated, so a child anywhere on the sofa is refused at
    /// the gate unless a parent allowed the game's <paramref name="league"/> for that child (D326): the facts carry the
    /// league, never a rating, and the filter reads the child's own allowance against it. Without a league, nothing helps.
    /// </summary>
    public static HandOffRequest Request(ViewerId viewer, GameId game, StreamingService service, Uri home, string gameName, LeagueId? league = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(home);
        var title = new TitleId(game.Value);
        var facts = new Pulp.Domain.Children.TitleFacts(title, TitleKind.Show, Pulp.Domain.Ratings.ContentRating.Unrated) { Homes = [service.Id], League = league };
        return new HandOffRequest([viewer], facts, new HandOffLink(title, service.Id, home, null), [gameName]);
    }

    /// <summary>The hand-off kept as the viewer's sitting for a game: it counts toward the service's usage and nothing else (<see cref="RecordedGames"/> ignores it).</summary>
    public static WatchedSitting UsageSitting(ViewerId viewer, GameId game, PendingHandOff pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return new WatchedSitting(viewer, new WatchEvent(
            new TitleId(game.Value), WatchedKind.Game, pending.Evidence.At, pending.Service, null, WatchSource.HandOff));
    }
}
