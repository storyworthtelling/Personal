using Android.App;
using Android.Views;
using Android.Widget;
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
using Pulp.Domain.Title;
using Pulp.Domain.Voice;

namespace Pulp.Favorites;

/// <summary>
/// A child's Favorites (mockup 06, D213, D230–D234) — the page behind the child's home's primary
/// button. Its header is "Favorites", the shell's line under it names the child (D179), and under that
/// the age-moderated voiced line sits, hidden until composed (D232, D233). Then big posters, two across:
/// the child's own stars, a parent's picks and what the child has watched most, each with the small
/// line saying which, and only titles every child watching may see (D12, D280). A tap plays through
/// the child gate; a long press opens the child's card, where ★ stars or takes the star away
/// (undo-first). An empty page is its own voiced seat (D264). Reads run off the UI thread and the page
/// says so in one sentence while they do.
/// </summary>
public sealed class FavoritesPage(Activity host, Func<FrameLayout> overlay, ChildViewSession session, Action invalidate)
{
    private readonly ChildVoice voice = new(host, PulpServices.For(host));
    private CancellationTokenSource? loading;
    private string? problem;
    private bool unreadable;
    private ChildPlay? play;
    private IReadOnlyList<PosterCell>? cells;
    private string? greeting;
    private VoiceLineAction emptyAction = VoiceLineAction.Compose;
    private string? emptyLine;

    public const string Title = ChildViewText.Favorites;

    /// <summary>The voiced line under the header; null until composed, and on an empty page (its seat is the page's own).</summary>
    public string? Greeting => cells is { Count: > 0 } ? greeting : null;

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

        if (cells is null || play is null)
        {
            return Status(ChildViewText.Reading);
        }

        if (cells.Count == 0)
        {
            var empty = new EmptyState(host, VoiceSeat.EmptyChildFavorites);
            empty.Present(emptyAction, emptyLine);
            return empty;
        }

        var actions = play;
        var posters = PulpServices.For(host).Posters;
        return PosterGrid.Build(
            host,
            cells,
            cell => host.StartActivity(TitleActivity.For(host, cell.Id)),
            cell => actions.ShowCard(cell.Id, cell.Name, cell.Where),
            PosterArt.Loader(host, posters));
    }

    public void Pause()
    {
        loading?.Cancel();
        loading = null;
    }

    /// <summary>Reads the page again; what is on screen stays until the new read lands.</summary>
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
                var read = await ReadAsync(services, cancel.Token);
                host.RunOnUiThread(() =>
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }

                    unreadable = false;
                    problem = read.Problem;
                    play = read.Play;
                    cells = read.Cells;
                    invalidate();
                    if (read.Facts is { } facts)
                    {
                        _ = voice.SpeakAsync(facts, Present, cancel.Token);
                    }
                });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // A newer read, or the page going away, superseded this one.
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("child", "A child's Favorites could not be read.", exception);
                host.RunOnUiThread(() =>
                {
                    unreadable = cells is null;
                    invalidate();
                });
            }
        });
    }

    private sealed record Read(string? Problem, ChildPlay? Play, IReadOnlyList<PosterCell>? Cells, VoiceFacts? Facts);

    private async Task<Read> ReadAsync(PulpServices services, CancellationToken cancellationToken)
    {
        var household = await services.HouseholdAsync(cancellationToken);
        var resolved = session.Resolve(household.Roster, household.ChildRules, household.NameOf);
        if (resolved.Audience is not { } audience)
        {
            return new Read(resolved.Problem, null, null, null);
        }

        // Every title any source could offer, looked up cache-first so the filter judges it on its facts.
        var candidates = audience.Watching
            .SelectMany(child => household.StarsOf(child).Select(star => star.Title)
                .Concat(household.SittingsOf(child).Select(sitting => sitting.Title)))
            .Concat(audience.Children.SelectMany(child => child.ParentFavorites))
            .Distinct()
            .ToList();
        var titles = new Dictionary<TitleId, CatalogueTitle>();
        foreach (var id in candidates)
        {
            if (await services.TitleAsync(id, tracked: false, opened: false, cancellationToken) is { } title)
            {
                titles[id] = title;
            }
        }

        TitleFacts? FactsFor(TitleId id) => titles.TryGetValue(id, out var title) ? DiscoverFacts.For(title, _ => []) : null;
        var cards = ChildFavorites.For(audience, household.StarsOf, household.SittingsOf, FactsFor);

        var read = new List<PosterCell>();
        var named = new List<NamedTitle>();
        foreach (var card in cards)
        {
            var title = titles[card.Title];
            var facts = FactsFor(card.Title)!;
            var home = audience.HomeFor(TitleHomes.Choose(title, household.ServicePlans, household.HomeOverrides), facts, services.Catalogue, household.Attested);
            var where = home is null ? null : ChildFavorites.WhereLine(title, home, services.Catalogue.ById(home.Service)?.Name ?? home.Service.Value);
            read.Add(new PosterCell(card.Title, title.Name, where, ChildFavorites.SourceLine(card, household.NameOf), title.PosterUrl));
            named.Add(new NamedTitle(facts, title.Name));
        }

        var child = audience.ReadingLevel;
        var voiced = read.Count > 0 ? ChildVoiceFacts.Favorites(child, named) : ChildVoiceFacts.EmptyFavorites(child);
        var actions = new ChildPlay(host, overlay, services, audience, household, Reload);
        return new Read(null, actions, read, voiced);
    }

    private void Present(VoiceLineAction action, string? line)
    {
        if (cells is { Count: > 0 })
        {
            greeting = ChildVoice.Shown(action, line);
        }
        else
        {
            emptyAction = action;
            emptyLine = line;
        }

        invalidate();
    }

    private View Status(string text)
    {
        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Secondary(host, text), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return column;
    }
}
