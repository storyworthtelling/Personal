using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Title;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Shell;
using Pulp.Domain.Title;
using Pulp.Domain.Voice;
using Pulp.Favorites;

namespace Pulp.Shell;

/// <summary>
/// Playlists in a child's view (D42, D231): the playlists every child watching may open, each reduced
/// to what every one of them may see (<see cref="ChildPlaylists"/>). Not drawn in the mockup set, so it
/// takes the child's own anatomy: the list is a row per playlist, and a playlist opens to big posters,
/// two across, as Favorites does — a child picks by picture. A child cannot make, edit or reorder a
/// playlist; those are a parent's. An empty page is the child's voiced seat (D294).
/// </summary>
public sealed class ChildPlaylistsPage(Activity host, Func<FrameLayout> overlay, ChildViewSession session, Action invalidate)
{
    private readonly ChildVoice voice = new(host, PulpServices.For(host));
    private CancellationTokenSource? loading;
    private bool unreadable;
    private string? problem;
    private ChildPlay? play;
    private IReadOnlyList<(ChildPlaylist List, IReadOnlyList<PosterCell> Cells)>? lists;
    private PlaylistId? open;
    private VoiceLineAction emptyAction = VoiceLineAction.Compose;
    private string? emptyLine;

    /// <summary>An open playlist's name is its page's header; the list is "Playlists".</summary>
    public string Title => Open?.List.Playlist.List.Name ?? PulpAreas.For(PulpArea.Playlists).Title;

    private (ChildPlaylist List, IReadOnlyList<PosterCell> Cells)? Open
    {
        get
        {
            if (open is not { } id || lists is null)
            {
                return null;
            }

            foreach (var entry in lists)
            {
                if (entry.List.Playlist.Id == id)
                {
                    return entry;
                }
            }

            return null;
        }
    }

    public View Build()
    {
        if (unreadable)
        {
            return Status(ChildViewText.Unreadable);
        }

        if (problem is not null)
        {
            return Status(problem);
        }

        if (lists is null || play is null)
        {
            return Status(ChildViewText.ReadingPlaylists);
        }

        if (Open is { } shown)
        {
            var actions = play;
            var posters = PulpServices.For(host).Posters;
            return PosterGrid.Build(
                host,
                shown.Cells,
                cell => host.StartActivity(TitleActivity.For(host, cell.Id)),
                cell => actions.ShowCard(cell.Id, cell.Name, cell.Where),
                (window, url, width) => _ = Task.Run(async () =>
                {
                    var bitmap = await posters.LoadAsync(url, width, CancellationToken.None);
                    host.RunOnUiThread(() => window.SetArtwork(bitmap));
                }));
        }

        if (lists.Count == 0)
        {
            var empty = new EmptyState(host, VoiceSeat.EmptyChildPlaylists);
            empty.Present(emptyAction, emptyLine);
            return empty;
        }

        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        foreach (var (list, cells) in lists)
        {
            var row = new LinearLayout(host) { Orientation = Orientation.Vertical };
            row.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.ListRowMinHeightDp));
            var pad = PaperTheme.Dp(host, DesignTokens.Space.X2);
            row.SetPadding(0, pad, 0, pad);
            row.AddView(PaperUi.Heading(host, list.Playlist.List.Name));
            row.AddView(PaperUi.Secondary(host, cells.Count == 1 ? "1 title" : $"{cells.Count} titles"));
            row.Clickable = true;
            var id = list.Playlist.Id;
            row.Click += (_, _) =>
            {
                open = id;
                invalidate();
            };
            column.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        var scroll = new ScrollView(host) { FillViewport = true };
        scroll.AddView(column);
        return scroll;
    }

    public bool GoBack()
    {
        if (open is null)
        {
            return false;
        }

        open = null;
        invalidate();
        return true;
    }

    public void Pause()
    {
        loading?.Cancel();
        loading = null;
    }

    public void Reload()
    {
        loading?.Cancel();
        var cancel = new CancellationTokenSource();
        loading = cancel;
        var services = PulpServices.For(host);

        _ = Task.Run(async () =>
        {
            try
            {
                var household = await services.HouseholdAsync(cancel.Token);
                var resolved = session.Resolve(household.Roster, household.ChildRules, household.NameOf);
                if (resolved.Audience is not { } audience)
                {
                    Land(cancel, () => problem = resolved.Problem);
                    return;
                }

                var titles = new Dictionary<TitleId, CatalogueTitle>();
                foreach (var id in audience.Watching.SelectMany(household.PlaylistsFor).SelectMany(list => list.List.Entries).Where(entry => entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show).Select(entry => new TitleId(entry.Key)).Distinct())
                {
                    if (await services.TitleAsync(id, tracked: false, opened: false, cancel.Token) is { } title)
                    {
                        titles[id] = title;
                    }
                }

                TitleFacts? FactsFor(TitleId id) => titles.TryGetValue(id, out var title) ? DiscoverFacts.For(title, _ => []) : null;
                var read = ChildPlaylists.For(audience, household.PlaylistsFor, FactsFor)
                    .Select(list => (list, (IReadOnlyList<PosterCell>)list.Titles.Select(id => Cell(services, audience, household, titles[id])).ToList()))
                    .ToList();
                var actions = new ChildPlay(host, overlay, services, audience, household, Reload);
                Land(cancel, () =>
                {
                    problem = null;
                    play = actions;
                    lists = read;
                });
                if (read.Count == 0)
                {
                    await voice.SpeakAsync(ChildVoiceFacts.EmptyPlaylists(audience.ReadingLevel), Present, cancel.Token);
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // A newer read, or the screen going away, superseded this one.
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("child", "A child's Playlists could not be read.", exception);
                host.RunOnUiThread(() =>
                {
                    unreadable = lists is null;
                    invalidate();
                });
            }
        });
    }

    private static PosterCell Cell(PulpServices services, ChildAudience audience, HouseholdView household, CatalogueTitle title)
    {
        var home = audience.HomeFor(TitleHomes.Choose(title, household.ServicePlans, household.HomeOverrides), DiscoverFacts.For(title, _ => []), services.Catalogue, household.Attested);
        var where = home is null ? null : ChildFavorites.WhereLine(title, home, services.Catalogue.ById(home.Service)?.Name ?? home.Service.Value);
        return new PosterCell(title.Id, title.Name, where, null, title.PosterUrl);
    }

    private void Land(CancellationTokenSource cancel, Action apply) =>
        host.RunOnUiThread(() =>
        {
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            unreadable = false;
            apply();
            invalidate();
        });

    private void Present(VoiceLineAction action, string? line)
    {
        emptyAction = action;
        emptyLine = line;
        invalidate();
    }

    private View Status(string text)
    {
        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Secondary(host, text), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return column;
    }
}
