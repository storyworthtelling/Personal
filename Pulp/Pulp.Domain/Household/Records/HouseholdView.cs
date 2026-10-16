using Pulp.Domain.Availability;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Household.Records;

/// <summary>
/// One household document read into the Domain's own inputs, once: the roster, each child's rules
/// with the parents' picks on them, every viewer's sittings, vetoes and stars, the playlists, the
/// home overrides and the services. Records that could not be read are gathered in
/// <see cref="Unreadable"/> for Diagnostics — never dropped without a word, never allowed to blank
/// the rest of the household.
/// </summary>
public sealed class HouseholdView
{
    private readonly IReadOnlyList<WatchedSitting> sittings;
    private readonly IReadOnlyList<HouseholdPlaylist> playlists;
    private readonly IReadOnlyList<ViewerVeto> vetoes;
    private readonly IReadOnlyList<ChildStarEntry> stars;
    private readonly IReadOnlyList<ContinueDismissedEntry> dismissals;
    private readonly IReadOnlyList<ViewerNameEntry> names;
    private readonly IReadOnlyList<AdultAccountEntry> accounts;

    private HouseholdView(HouseholdDocument document)
    {
        var unreadable = new List<UnreadableRecord>();

        IReadOnlyList<T> Gather<T>(RecordMap<T> map)
            where T : notnull
        {
            var set = HouseholdRecords.Read(document, map);
            unreadable.AddRange(set.Unreadable);
            return set.Items;
        }

        sittings = Gather(HouseholdMaps.Watched);
        playlists = Gather(HouseholdMaps.Playlist);
        vetoes = Gather(HouseholdMaps.Veto);
        stars = Gather(HouseholdMaps.ChildStar);
        dismissals = Gather(HouseholdMaps.ContinueDismissed);
        names = Gather(HouseholdMaps.ViewerName);
        Plans = Gather(HouseholdMaps.ServicePlan);
        SportsFollows = Gather(HouseholdMaps.SportsFollow);
        CalendarPlans = Gather(HouseholdMaps.Plan);
        accounts = Gather(HouseholdMaps.AdultAccount);
        PulpCalendarId = Gather(HouseholdMaps.PulpCalendar).FirstOrDefault()?.CalendarId;
        var picks = Gather(HouseholdMaps.ParentPick);
        var rules = Gather(HouseholdMaps.ChildRules);
        var viewers = Gather(HouseholdMaps.Viewer);
        var allowances = Gather(HouseholdMaps.ChildLeague);

        HomeOverrides = Gather(HouseholdMaps.HomeOverride)
            .Aggregate(Availability.HomeOverrides.None, (chosen, choice) => chosen.OpenThereInstead(choice.Title, choice.Service));
        Attested = new AttestedServices(Gather(HouseholdMaps.AttestedService).Select(entry => entry.Service));
        Services = Gather(HouseholdMaps.HouseholdService).Select(entry => entry.Service).ToList();

        // Each record has one key per viewer, so the roster cannot be handed a viewer twice.
        Roster = viewers.Count == 0 ? null : new HouseholdRoster(viewers);

        // A league tick belongs to a child who is on the roster as a child now and has live rules: a record left
        // behind by a removed child (or one made an adult) must not be read for a later child with the same id.
        var liveChildren = rules.Select(child => child.Child).Where(child => Roster?.IsChild(child) == true).ToHashSet();
        ChildLeagueAllowances = allowances.Where(entry => liveChildren.Contains(entry.Child)).ToList();
        ChildRules = rules
            .Select(child => child with
            {
                ParentPicks = picks.Where(pick => pick.Child == child.Child).Select(pick => pick.Pick).ToList(),
                AllowedLeagues = ChildLeagueAllowances.Where(entry => entry.Child == child.Child).Select(entry => entry.League).ToHashSet(),
            })
            .ToList();
        Unreadable = unreadable;
    }

    public static HouseholdView Of(HouseholdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new HouseholdView(document);
    }

    /// <summary>The household's viewers; null until anyone is on the roster (first run is Release D's).</summary>
    public HouseholdRoster? Roster { get; }

    /// <summary>Each child's one filter, with the parents' picks read from their own records.</summary>
    public IReadOnlyList<ChildRules> ChildRules { get; }

    /// <summary>Every plan (D68); the calendar reads them through <c>CalendarPlans.ToSources</c>.</summary>
    public IReadOnlyList<HouseholdPlan> CalendarPlans { get; }

    /// <summary>An adult's recorded Google address, trimmed; null when none is recorded or it is not shaped like one.</summary>
    public string? GoogleAccountOf(ViewerId viewer) =>
        accounts.Where(entry => entry.Viewer == viewer && GoogleAddress.Check(entry.Email) is null)
            .Select(entry => GoogleAddress.Clean(entry.Email)).FirstOrDefault();

    /// <summary>The recorded addresses of the roster's adults: who the Pulp calendar is shared with (D304). Children never.</summary>
    public IReadOnlyList<string> AdultGoogleAccounts =>
        Roster is null
            ? []
            : Roster.Viewers.Where(viewer => !Roster.IsChild(viewer.Id)).Select(viewer => GoogleAccountOf(viewer.Id)).OfType<string>().ToList();

    /// <summary>The household's Pulp calendar id (D145); null until the owner's install has made it.</summary>
    public string? PulpCalendarId { get; }

    public HomeOverrides HomeOverrides { get; }

    public AttestedServices Attested { get; }

    public IReadOnlyList<ServiceId> Services { get; }

    public IReadOnlyList<UnreadableRecord> Unreadable { get; }

    /// <summary>Every adult's followed teams (D60).</summary>
    public IReadOnlyList<SportsFollow> SportsFollows { get; }

    /// <summary>Every parent's allowance of a league to a child (D72, D317).</summary>
    public IReadOnlyList<ChildLeagueAllowance> ChildLeagueAllowances { get; }

    /// <summary>The leagues a parent has allowed this child; empty when none.</summary>
    public IReadOnlySet<LeagueId> LeaguesAllowedFor(ViewerId child) => ChildSports.AllowedFor(ChildLeagueAllowances, child);

    /// <summary>The teams one adult follows.</summary>
    public IReadOnlyList<SportsFollow> FollowsOf(ViewerId viewer) => SportsFollows.Where(follow => follow.Viewer == viewer).ToList();

    /// <summary>What Settings recorded about each service's plan (D29, D86, D8), had or not.</summary>
    public IReadOnlyList<ServicePlanEntry> Plans { get; }

    /// <summary>The recorded plan for one service; null when Settings has recorded nothing about it.</summary>
    public ServicePlanEntry? PlanOf(ServiceId service) => Plans.FirstOrDefault(plan => plan.Service == service);

    /// <summary>
    /// The household's services as <see cref="BestHome"/> reads them (D28, D29): each with its recorded
    /// tier. A service whose tier is not set is read as ad-free — what every screen assumed before the
    /// tier was recorded — so setting nothing changes nothing, and only a recorded "with ads" plan says so.
    /// </summary>
    public IReadOnlyList<ServicePlan> ServicePlans =>
        Services.Distinct().Select(service => new ServicePlan(service, AdFree: PlanOf(service)?.Tier != PlanTier.WithAds, Picture: PlanOf(service)?.Picture)).ToList();

    /// <summary>The name a viewer goes by: their name record, or their id until one is written.</summary>
    public string NameOf(ViewerId viewer) => names.FirstOrDefault(entry => entry.Viewer == viewer)?.Name ?? viewer.Value;

    /// <summary>Everyone's sittings, for what is about the household and not one viewer: the usage count and which hand-off a session belongs to.</summary>
    public IReadOnlyList<WatchedSitting> AllSittings => sittings;

    /// <summary>One viewer's own sittings (D97): what Continue Watching and Recently watched are built from.</summary>
    public IReadOnlyList<WatchEvent> SittingsOf(ViewerId viewer) =>
        sittings.Where(sitting => sitting.Viewer == viewer).Select(sitting => sitting.Sitting).ToList();

    /// <summary>The viewer's own playlists and the shared ones, by name.</summary>
    public IReadOnlyList<HouseholdPlaylist> PlaylistsFor(ViewerId viewer) =>
        playlists
            .Where(playlist => playlist.IsVisibleTo(viewer))
            .OrderBy(playlist => playlist.List.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(playlist => playlist.Id.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public HouseholdPlaylist? Playlist(PlaylistId id) => playlists.FirstOrDefault(playlist => playlist.Id == id);

    public VetoBook VetoesOf(ViewerId viewer) =>
        VetoBook.From(viewer, vetoes.Where(veto => veto.Viewer == viewer).Select(veto => veto.Veto));

    public IReadOnlyList<ChildStar> StarsOf(ViewerId child) =>
        stars.Where(star => star.Child == child).Select(star => star.Star).ToList();

    /// <summary>One viewer's own removals from Continue watching (D31, D97).</summary>
    public IReadOnlyList<ContinueDismissal> DismissalsOf(ViewerId viewer) =>
        dismissals.Where(entry => entry.Viewer == viewer).Select(entry => entry.Dismissal).ToList();
}
