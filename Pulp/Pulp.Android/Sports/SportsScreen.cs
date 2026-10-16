using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Actions;
using Pulp.Infrastructure.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Services;
using Pulp.Domain.Sports;
using Pulp.Domain.Voice;
using Pulp.HandOffs;
using Pulp.Infrastructure.Sports;
using Pulp.Library;
using Pulp.Trailer;

namespace Pulp.Sports;

/// <summary>
/// Everything behind the Sports tab (mockup 07, D67, D235 to D243): leagues in order, each folding to its
/// teams and each team to its games; the championship at league level; one Spoilers tag on a held league
/// that peeks once; "Find a team to follow"; and, behind an open league, standings, the bracket and the
/// schedule (no mockup, D272). Pages are read through <see cref="PulpServices"/> as sheets only, and every
/// word on a row comes from <see cref="SportsScreenPlan"/>.
///
/// <para><b>Play (D311, D312).</b> A game carries a channel name and no link, so Play resolves the channel to a
/// service the household ticked (<see cref="GamePlay"/> over <see cref="BroadcasterTable"/>) and opens that
/// service's home through <see cref="HandOffExecutor"/>, the one door (G8). The hand-off is kept as the viewer's
/// usage evidence only, through <see cref="GamePlay.UsageSitting"/>; it never says the game was watched
/// (<see cref="RecordedGames"/> ignores it), so only "Mark watched" or a session that ended can lift a hold. An
/// unknown or unticked broadcaster keeps the honest card and names what to tick.</para>
///
/// <para><b>Highlights (D313, D314).</b> A quiet line on the row of an over game in an open league with an official
/// channel; tapping it looks the video up, once, and plays it in the shared player. The line and the player's
/// header are the generic label with the teams and the date, never the video's own title.</para>
/// </summary>
internal sealed class SportsScreen
{
    private enum Showing
    {
        List,
        Page,
    }

    private const string Loading = "Reading the leagues…";

    private readonly Activity host;
    private readonly Func<FrameLayout> overlay;
    private readonly Action invalidate;
    private readonly PulpServices services;
    private readonly LibraryWrites writes;
    private readonly SportsFoldStore foldStore;

    private Showing showing = Showing.List;
    private LeagueId? openLeague;
    private SportsLeaguePage openPage;
    private ThisPhoneAnswer? me;
    private HouseholdView? household;
    private SportsPage? page;
    private FoldMemory overlayFold = FoldMemory.Empty;
    private string? status = Loading;
    private int generation;
    private bool foldLoaded;
    private CancellationTokenSource paint = new();
    private FilterField? finder;
    private LinearLayout? matchesHost;
    private LinearLayout? leaguesHost;
    private IReadOnlyList<(League League, Team Team)>? candidates;
    private string? findStatus;
    private readonly HashSet<TeamId> followedNow = [];
    private readonly HashSet<TeamId> unfollowedNow = [];

    public SportsScreen(Activity host, Func<FrameLayout> overlay, Action invalidate)
    {
        this.host = host;
        this.overlay = overlay;
        this.invalidate = invalidate;
        services = PulpServices.For(host);
        writes = new LibraryWrites(services, () => host.RunOnUiThread(Reload), "sports");
        foldStore = new SportsFoldStore(host);
    }

    /// <summary>The header: the sub-page's own name behind a league; otherwise the area's.</summary>
    public string? Title => showing == Showing.Page
        ? openPage switch
        {
            SportsLeaguePage.Standings => "Standings",
            SportsLeaguePage.Bracket => "Playoff bracket",
            SportsLeaguePage.Schedule => "Schedule",
            _ => null,
        }
        : null;

    public bool GoBack()
    {
        if (showing == Showing.List)
        {
            return false;
        }

        showing = Showing.List;
        openLeague = null;
        invalidate();
        return true;
    }

    public void Pause() => paint.Cancel();

    /// <summary>Reads the tab: the cache paints at once, then the refreshed page replaces it.</summary>
    public void Reload()
    {
        var mine = ++generation;
        _ = Task.Run(async () =>
        {
            try
            {
                if (!foldLoaded)
                {
                    services.SportsFold = foldStore.Load();
                    foldLoaded = true;
                }

                var view = await services.HouseholdAsync(CancellationToken.None);
                var answer = services.ThisPhoneOf(view);
                if (answer.Viewer is not { } viewer)
                {
                    host.RunOnUiThread(() => Show(mine, view, answer, null));
                    return;
                }

                var cached = await services.CachedSportsAsync(viewer, CancellationToken.None);
                host.RunOnUiThread(() => Show(mine, view, answer, cached));
                var fresh = await services.SportsAsync(viewer, CancellationToken.None);
                host.RunOnUiThread(() => Show(mine, view, answer, fresh));
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("sports", "The Sports tab could not be read.", exception);
                host.RunOnUiThread(() =>
                {
                    if (mine == generation && page is null)
                    {
                        status = "The leagues couldn't be read just now.";
                        invalidate();
                    }
                });
            }
        });
    }

    private void Show(int mine, HouseholdView view, ThisPhoneAnswer answer, SportsPage? read)
    {
        if (mine != generation)
        {
            return;
        }

        household = view;
        me = answer;
        page = read;
        status = null;
        invalidate();
    }

    public View Build()
    {
        paint.Cancel();
        paint = new CancellationTokenSource();

        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(host) { FillViewport = true };
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        if (status is not null || me is null)
        {
            column.AddView(PaperUi.Secondary(host, status ?? Loading));
            return scroll;
        }

        switch (me.Status)
        {
            case ThisPhoneStatus.NoRoster:
                column.AddView(Empty(primary: null));
                return scroll;
            case ThisPhoneStatus.Unmatched:
                column.AddView(PaperUi.Secondary(host, ThisPhone.UnmatchedSentence));
                return scroll;
            case ThisPhoneStatus.Known:
                break;
            default:
                throw new InvalidOperationException($"A viewer status the Sports page does not know: {me.Status}.");
        }

        var plan = page is null ? [] : SportsScreenPlan.For(page, overlayFold, DateTimeOffset.Now, TimeZoneInfo.Local);
        if (showing == Showing.Page && page is not null && PageLeague() is { } opened)
        {
            BuildPage(column, opened);
            return scroll;
        }

        showing = Showing.List;
        BuildList(column, plan);
        return scroll;
    }

    private OpenLeague? PageLeague() =>
        page?.Sheets.OfType<OpenLeague>().FirstOrDefault(league => league.Line.Id == openLeague);

    // ---- the list (mockup 07) ----

    private void BuildList(LinearLayout column, IReadOnlyList<LeaguePlan> plan)
    {
        var viewer = me!.Viewer!.Value;
        column.AddView(Finder());

        matchesHost = new LinearLayout(host) { Orientation = Orientation.Vertical };
        column.AddView(matchesHost);

        leaguesHost = new LinearLayout(host) { Orientation = Orientation.Vertical };
        column.AddView(leaguesHost);

        if (plan.Count == 0)
        {
            leaguesHost.AddView(Empty(() => finder?.RequestFocus()));
        }

        foreach (var league in plan)
        {
            leaguesHost.AddView(BuildLeague(league, viewer));
        }

        ShowMatches();
    }

    private View Finder()
    {
        if (finder is null)
        {
            finder = new FilterField(host, "Find a team to follow", Android.Graphics.Color.ParseColor(DesignTokens.Identity.Pulp.Mark));
            finder.QueryChanged += _ => OnQuery();
        }

        (finder.Parent as ViewGroup)?.RemoveView(finder);
        finder.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(host, DesignTokens.Space.X3),
            BottomMargin = PaperTheme.Dp(host, DesignTokens.Space.X2),
        };
        return finder;
    }

    private View BuildLeague(LeaguePlan league, ViewerId viewer)
    {
        var block = new LinearLayout(host) { Orientation = Orientation.Vertical };
        var badge = SportsRows.Badge(host, league.Name, league.BadgeArt, shield: false, letters: 3, LoadBadge);
        block.AddView(SportsRows.LeagueLine(host, league, badge, () => ToggleLeague(league), () => Peek(league)));

        if (league.Open)
        {
            foreach (var team in league.Teams)
            {
                block.AddView(BuildTeam(league, team, viewer));
            }

            if (league.Championship is { } champ)
            {
                block.AddView(SportsRows.Championship(host, champ));
            }

            foreach (var link in league.Links)
            {
                var kind = link.Kind;
                block.AddView(SportsRows.LinkLine(host, link.Label, () => OpenPage(league.Id, kind)));
            }
        }

        block.AddView(SportsRows.Rule(host, strong: league.Open));
        block.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(host, DesignTokens.Space.X1),
        };
        return block;
    }

    private View BuildTeam(LeaguePlan league, TeamPlan team, ViewerId viewer)
    {
        var block = SportsRows.TeamBlock(host);
        var badge = SportsRows.Badge(host, team.Name, team.BadgeArt, shield: true, letters: 2, LoadBadge);
        block.AddView(SportsRows.TeamLine(host, team, badge, () => ToggleTeam(league.Id, team), () => ShowTeamCard(team, viewer)));
        if (!team.Open)
        {
            return block;
        }

        foreach (var game in team.Games)
        {
            block.AddView(SportsRows.GameRow(host, game, () => Play(game), () => ShowGameCard(game, viewer, watched: false), () => PlayHighlights(game)));
            block.AddView(LibraryRows.Hairline(host));
        }

        // D330: watched, finished games stay reachable behind a quiet line; it starts folded.
        if (team.EarlierLine is { } earlierLine)
        {
            block.AddView(SportsRows.EarlierLine(host, earlierLine, team.EarlierOpen, () => ToggleEarlier(league.Id, team)));
            if (team.EarlierOpen)
            {
                foreach (var game in team.Earlier)
                {
                    block.AddView(SportsRows.GameRow(host, game, () => Play(game), () => ShowGameCard(game, viewer, watched: true), () => PlayHighlights(game)));
                    block.AddView(LibraryRows.Hairline(host));
                }
            }
        }

        if (team.EmptyLine is { } empty)
        {
            block.AddView(PaperUi.Secondary(host, empty));
        }

        return block;
    }

    // ---- folds and the peek ----

    private void ToggleLeague(LeaguePlan league)
    {
        var open = !league.Open;
        overlayFold = overlayFold.WithLeague(league.Id, open);
        services.SportsFold = services.SportsFold.WithLeague(league.Id, open);
        foldStore.Save(services.SportsFold);
        invalidate();
    }

    private void ToggleTeam(LeagueId league, TeamPlan team)
    {
        var open = !team.Open;
        overlayFold = overlayFold.WithTeam(league, team.Id, open);
        services.SportsFold = services.SportsFold.WithTeam(league, team.Id, open);
        foldStore.Save(services.SportsFold);
        invalidate();
    }

    private void ToggleEarlier(LeagueId league, TeamPlan team)
    {
        var open = !team.EarlierOpen;
        overlayFold = overlayFold.WithEarlier(league, team.Id, open);
        services.SportsFold = services.SportsFold.WithEarlier(league, team.Id, open);
        foldStore.Save(services.SportsFold);
        invalidate();
    }

    /// <summary>
    /// The Spoilers tag was tapped (D183, D242): the cache is drawn once more with that league's one peek
    /// spent, and no refresh follows to put the hold back before the viewer has looked.
    /// </summary>
    private void Peek(LeaguePlan league)
    {
        var mine = ++generation;
        overlayFold = overlayFold.WithLeague(league.Id, true);
        services.TapSpoilersTag(league.Id);
        _ = Task.Run(async () =>
        {
            try
            {
                var viewer = me!.Viewer!.Value;
                var cached = await services.CachedSportsAsync(viewer, CancellationToken.None);
                host.RunOnUiThread(() =>
                {
                    if (mine == generation)
                    {
                        page = cached;
                        invalidate();
                    }
                });
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("sports", "A league could not be drawn for its peek.", exception);
            }
        });
    }

    private void OpenPage(LeagueId league, SportsLeaguePage kind)
    {
        openLeague = league;
        openPage = kind;
        showing = Showing.Page;
        invalidate();
    }

    // ---- standings, bracket, schedule (D272) ----

    private void BuildPage(LinearLayout column, OpenLeague league)
    {
        column.AddView(PaperUi.Secondary(host, league.Line.Name));
        var now = DateTimeOffset.Now;
        switch (openPage)
        {
            case SportsLeaguePage.Standings:
                foreach (var row in SportsScreenPages.Standings(league))
                {
                    column.AddView(StandingLine(row));
                    column.AddView(LibraryRows.Hairline(host));
                }

                break;
            case SportsLeaguePage.Bracket:
                var bracket = SportsScreenPages.Bracket(league, now, TimeZoneInfo.Local);
                if (bracket.Championship is { } champ)
                {
                    column.AddView(SportsRows.Championship(host, champ));
                    column.AddView(LibraryRows.Hairline(host));
                }

                AddGames(column, bracket.Playoffs, "No playoff games yet.");
                break;
            case SportsLeaguePage.Schedule:
                AddGames(column, SportsScreenPages.Schedule(league, now, TimeZoneInfo.Local), "Nothing scheduled.");
                break;
            default:
                throw new InvalidOperationException($"A league page the Sports screen does not know: {openPage}.");
        }
    }

    private void AddGames(LinearLayout column, IReadOnlyList<GameRow> games, string none)
    {
        if (games.Count == 0)
        {
            column.AddView(PaperUi.Secondary(host, none));
            return;
        }

        var viewer = me!.Viewer!.Value;
        foreach (var game in games)
        {
            column.AddView(SportsRows.GameRow(host, game, () => Play(game), () => ShowGameCard(game, viewer, watched: false), () => PlayHighlights(game)));
            column.AddView(LibraryRows.Hairline(host));
        }
    }

    private View StandingLine(StandingRow row)
    {
        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp));
        var rank = PaperUi.Mono(host, row.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture));
        rank.SetTextColor(PaperTheme.InkMuted);
        line.AddView(rank, new LinearLayout.LayoutParams(PaperTheme.Dp(host, DesignTokens.Space.X8), ViewGroup.LayoutParams.WrapContent));
        line.AddView(SportsRows.Badge(host, row.Name, row.BadgeArt, shield: true, letters: 2, LoadBadge));
        var name = PaperUi.Body(host, row.Name);
        if (row.Followed)
        {
            name.SetTypeface(PaperTheme.SansSemibold(host), Android.Graphics.TypefaceStyle.Normal);
        }

        line.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        return line;
    }

    // ---- a game: Play, the card, marks ----

    /// <summary>Play: the ticked service's home through the one door, else the card (see the class note).</summary>
    private void Play(GameRow game)
    {
        // Until the household has loaded nothing is known of what is ticked, so "Tick Peacock in Settings" would be a guess.
        if (household is null)
        {
            ActionCard.Show(host, overlay(), game.Title, SportsScreenText.ServicesLoading);
            return;
        }

        var decision = GamePlay.Decide(game.Channel, household.Services, BroadcasterTable.Public, ServiceCatalogue.Public);
        if (!decision.Opens || me?.Viewer is not { } viewer)
        {
            ActionCard.Show(host, overlay(), game.Title, decision.Note ?? SportsScreenText.PlayNote(game.ChannelLine));
            return;
        }

        var service = ServiceCatalogue.Public.ById(decision.Service!.Value)!;
        var request = GamePlay.Request(viewer, game.Id, service, decision.Home!, game.Title);
        _ = Task.Run(async () =>
        {
            try
            {
                var executor = await services.HandOffsAsync(CancellationToken.None)
                    ?? throw new InvalidOperationException("The household has no roster, so no hand-off can be checked.");
                host.RunOnUiThread(() => StartGame(executor, request, viewer, game, service.Name));
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("sports", "Play could not reach the hand-off door.", exception);
            }
        });
    }

    private void StartGame(HandOffExecutor executor, HandOffRequest request, ViewerId viewer, GameRow game, string serviceName)
    {
        var at = DateTimeOffset.UtcNow;
        var outcome = executor.Start(host, request, at);
        PulpNotices.AfterHandOff(host, overlay(), outcome, request, serviceName, game.Title, at, new PulpNotices.Buttons());
        if (outcome.Recorded is not { } pending)
        {
            return;
        }

        // Usage only: the sitting counts the service's openings and never marks the game watched (D312).
        var sitting = GamePlay.UsageSitting(viewer, game.Id, pending);
        writes.Enqueue("record a game's hand-off", token => services.PutAsync(HouseholdMaps.Watched, sitting, token));
    }

    private readonly HighlightVideoCache highlightVideos = new();

    /// <summary>The one-tap peek at a game's highlights: asked again of the gate, looked up until found, played in the shared player.</summary>
    private void PlayHighlights(GameRow game)
    {
        var found = page?.Sheets.OfType<OpenLeague>()
            .SelectMany(league => league.Teams.SelectMany(team => team.Games)
                .Concat(league.Earlier.SelectMany(entry => entry.Games))
                .Select(line => (League: league, Line: line)))
            .FirstOrDefault(entry => entry.Line.Id == game.Id);
        if (found is not { } entry
            || !HighlightsOffer.For(entry.League, entry.Line, DateTimeOffset.Now)
            || HighlightChannels.Default.ChannelOf(entry.League.Line.Id) is not { } channel)
        {
            return;
        }

        var label = game.HighlightsLabel ?? "Highlights";
        var request = new HighlightsRequest(entry.League.Line.Id, channel, entry.Line.Home.Name, entry.Line.Away.Name, entry.Line.StartsAt);
        _ = Task.Run(async () =>
        {
            try
            {
                if (!highlightVideos.TryGet(game.Id, out var video))
                {
                    video = await new YouTubeHighlightsSource(services.Trailers).FindVideoIdAsync(request, CancellationToken.None);
                    highlightVideos.Remember(game.Id, video);
                }

                host.RunOnUiThread(() =>
                {
                    if (video is null)
                    {
                        ActionCard.Show(host, overlay(), game.Title, "No highlights found for this game yet.");
                        return;
                    }

                    host.StartActivity(TrailerActivity.ForHighlights(host, video, label));
                });
            }
            catch (CatalogueLookupException exception)
            {
                services.Diagnostics.Warn("sports", $"No highlights lookup ({exception.Failure}).", exception);
                host.RunOnUiThread(() => ActionCard.Show(host, overlay(), game.Title, "Highlights couldn't be looked up just now."));
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("sports", "A game's highlights could not be looked up.", exception);
            }
        });
    }

    private void ShowGameCard(GameRow game, ViewerId viewer, bool watched)
    {
        var item = new CardItem(game.Title, CardPlace.SportsGame) { Subtitle = game.Detail, GameWatched = watched };
        var handlers = new Dictionary<CardActionKind, CardHandler>
        {
            [CardActionKind.Play] = new(() => Play(game)),
            [CardActionKind.MarkWatched] = new(
                () => writes.Enqueue("mark a game watched", token => services.MarkGameAsync(viewer, game.Id, true, token)),
                () => writes.Enqueue("undo marking a game watched", token => services.MarkGameAsync(viewer, game.Id, false, token))),
            [CardActionKind.MarkUnwatched] = new(
                () => writes.Enqueue("mark a game unwatched", token => services.MarkGameAsync(viewer, game.Id, false, token)),
                () => writes.Enqueue("undo marking a game unwatched", token => services.MarkGameAsync(viewer, game.Id, true, token))),
            [CardActionKind.AddTo] = new(() => AddTo(game, viewer)),
        };
        PulpActionCard.Show(host, overlay(), item, handlers);
    }

    private void AddTo(GameRow game, ViewerId viewer)
    {
        var entry = new PlaylistEntry(PlaylistEntryKind.Game, game.Id.Value);
        _ = Task.Run(async () =>
        {
            try
            {
                var playlists = await services.PlaylistsAsync(viewer, CancellationToken.None);
                var choices = playlists
                    .Select(playlist => new PickerPlaylist(playlist.Id.Value, playlist.List, playlist.Shared, LastUsed: null))
                    .ToList();
                var model = AddToPickerPlan.Build(game.Title, entry, null, choices, []);
                var picks = new PickerWrites(services, writes.Enqueue, viewer, new TitleId(game.Id.Value), entry);
                host.RunOnUiThread(() => AddToPicker.Show(host, overlay(), model, picks.OnPick, name => picks.NewPlaylist(name)));
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("sports", "The playlists for a game could not be read.", exception);
            }
        });
    }

    /// <summary>A team's card: stop following it, undone from the corner (§13).</summary>
    private void ShowTeamCard(TeamPlan team, ViewerId viewer)
    {
        var id = team.Id;
        var name = team.Name;
        ActionCard.Show(host, overlay(), name, "Followed", new CardAction("Stop following", () =>
        {
            writes.Enqueue("unfollow a team", token => services.UnfollowTeamAsync(viewer, id, token));
            DogEar.Show(host, overlay(), $"Undo stop following, {name}.", () =>
                writes.Enqueue("follow a team again", token => services.FollowTeamAsync(viewer, id, token)));
        }));
    }

    // ---- find a team to follow (D67) ----

    private void OnQuery()
    {
        if (candidates is null && findStatus is null)
        {
            findStatus = "Looking through the leagues…";
            _ = Task.Run(async () =>
            {
                try
                {
                    var leagues = await services.BrowseLeaguesAsync(CancellationToken.None);
                    var found = leagues.SelectMany(entry => entry.Teams.Select(team => (entry.League, team))).ToList();
                    host.RunOnUiThread(() =>
                    {
                        candidates = found;
                        findStatus = null;
                        ShowMatches();
                    });
                }
                catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
                {
                    services.Diagnostics.Warn("sports", "The leagues could not be browsed.", exception);
                    host.RunOnUiThread(() =>
                    {
                        findStatus = "The leagues couldn't be reached just now.";
                        ShowMatches();
                    });
                }
            });
        }

        ShowMatches();
    }

    private void ShowMatches()
    {
        if (matchesHost is null || leaguesHost is null || finder is null)
        {
            return;
        }

        matchesHost.RemoveAllViews();
        var query = finder.Query;
        leaguesHost.Visibility = query.Length == 0 ? ViewStates.Visible : ViewStates.Gone;
        if (query.Length == 0)
        {
            return;
        }

        if (candidates is null)
        {
            matchesHost.AddView(PaperUi.Secondary(host, findStatus ?? "Looking through the leagues…"));
            return;
        }

        var viewer = me!.Viewer!.Value;
        var following = household!.FollowsOf(viewer).Select(follow => follow.Team).Concat(followedNow).Where(team => !unfollowedNow.Contains(team)).ToHashSet();
        var rows = SportsScreenFind.Matches(candidates, query, following);
        if (rows.Count == 0)
        {
            matchesHost.AddView(PaperUi.Secondary(host, "No team by that name."));
            return;
        }

        foreach (var row in rows)
        {
            matchesHost.AddView(FindLine(row, viewer));
            matchesHost.AddView(LibraryRows.Hairline(host));
        }
    }

    private View FindLine(FindRow row, ViewerId viewer)
    {
        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.ListRowMinHeightDp));
        line.AddView(SportsRows.Badge(host, row.Team.Name, row.Team.BadgeArt, shield: true, letters: 2, LoadBadge));
        var lines = LibraryRows.Lines(host);
        lines.AddView(PaperUi.Body(host, row.Team.Name));
        lines.AddView(PaperUi.Secondary(host, row.LeagueName));
        line.AddView(lines, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        var state = PaperUi.Label(host, row.Following ? "Following" : "Follow");
        state.SetTextColor(row.Following ? PaperTheme.InkMuted : PaperTheme.ActionText);
        line.AddView(state);

        var team = row.Team;
        line.Click += (_, _) =>
        {
            if (row.Following)
            {
                Unfollow(team, viewer);
            }
            else
            {
                Follow(team, viewer);
            }
        };
        return line;
    }

    private void Follow(Team team, ViewerId viewer)
    {
        unfollowedNow.Remove(team.Id);
        followedNow.Add(team.Id);
        ShowMatches();
        writes.Enqueue("follow a team", token => services.FollowTeamAsync(viewer, team.Id, token));
    }

    private void Unfollow(Team team, ViewerId viewer)
    {
        followedNow.Remove(team.Id);
        unfollowedNow.Add(team.Id);
        ShowMatches();
        writes.Enqueue("unfollow a team", token => services.UnfollowTeamAsync(viewer, team.Id, token));
        var name = team.Name;
        DogEar.Show(host, overlay(), $"Undo stop following, {name}.", () =>
        {
            unfollowedNow.Remove(team.Id);
            followedNow.Add(team.Id);
            ShowMatches();
            writes.Enqueue("follow a team again", token => services.FollowTeamAsync(viewer, team.Id, token));
        });
    }

    // ---- small parts ----

    private void LoadBadge(SportsBadgeView badge, string url)
    {
        var token = paint.Token;
        var target = PaperTheme.Dp(host, SportsBadgeView.HeightDp(shield: true));
        _ = Task.Run(async () =>
        {
            try
            {
                var bitmap = await services.Posters.LoadAsync(url, target, token);
                if (bitmap is not null && !host.IsFinishing && !host.IsDestroyed)
                {
                    host.RunOnUiThread(() => badge.SetArt(bitmap));
                }
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, token))
            {
                services.Diagnostics.Warn("sports", "A badge could not be loaded.", exception);
            }
        });
    }

    private EmptyState Empty(Action? primary)
    {
        var empty = new EmptyState(host, VoiceSeat.EmptySports, primary);

        // No composer is wired in the head yet, so the seat shows its neutral line (D264).
        empty.Present(VoiceLineAction.ShowNeutral, null);
        empty.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        return empty;
    }
}
