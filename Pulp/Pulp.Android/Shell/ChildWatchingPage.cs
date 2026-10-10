using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Library;
using Pulp.Domain.Shell;
using Pulp.Domain.Voice;
using Pulp.Domain.Watching;
using Pulp.Favorites;
using Pulp.Watching;

namespace Pulp.Shell;

/// <summary>
/// The home in a child's view (D42, D213, D27): the same Continue watching page, filtered — the
/// children's own progress, only titles every child watching may see — with the bottom bar's secondary
/// reading "Back to me" and its primary opening the child's <see cref="FavoritesPage"/> instead of
/// Recently watched. Favorites is drawn inside the shell with the binder showing Watching (mockup 06),
/// and Back from it comes home. A tap plays through the child gate; a long press opens the child's
/// card. The greeting under the header and the empty page are the child's voiced seats (D39, D294).
/// </summary>
public sealed class ChildWatchingPage
{
    private readonly Activity host;
    private readonly Func<FrameLayout> overlay;
    private readonly ChildViewSession session;
    private readonly Action invalidate;
    private readonly Action backToMe;
    private readonly FavoritesPage favorites;
    private readonly ChildVoice voice;

    private CancellationTokenSource? loading;
    private bool showingFavorites;
    private bool unreadable;
    private string? problem;
    private ChildPlay? play;
    private IReadOnlyList<WatchingRow>? rows;
    private string? greeting;
    private VoiceLineAction emptyAction = VoiceLineAction.Compose;
    private string? emptyLine;

    public ChildWatchingPage(Activity host, Func<FrameLayout> overlay, ChildViewSession session, Action invalidate, Action backToMe)
    {
        this.host = host;
        this.overlay = overlay;
        this.session = session;
        this.invalidate = invalidate;
        this.backToMe = backToMe;
        favorites = new FavoritesPage(host, overlay, session, invalidate);
        voice = new ChildVoice(host, PulpServices.For(host));
    }

    public string Title => showingFavorites ? FavoritesPage.Title : PulpAreas.For(PulpArea.Watching).Title;

    public string? Greeting => showingFavorites ? favorites.Greeting : rows is { Count: > 0 } ? greeting : null;

    public View BuildView()
    {
        if (showingFavorites)
        {
            return favorites.Build();
        }

        if (unreadable)
        {
            return Status(ChildViewText.Unreadable);
        }

        if (problem is not null)
        {
            return Status(problem);
        }

        if (rows is null || play is null)
        {
            return Status(ChildViewText.ReadingWatching);
        }

        if (rows.Count == 0)
        {
            var empty = new EmptyState(host, VoiceSeat.EmptyChildWatching);
            empty.Present(emptyAction, emptyLine);
            return empty;
        }

        var actions = play;
        var list = new LinearLayout(host) { Orientation = Orientation.Vertical };
        foreach (var row in rows)
        {
            list.AddView(ContinueRowView.Build(
                host,
                row,
                () => actions.Play(row.Row.Title),
                () => actions.ShowCard(row.Row.Title, row.Name, ContinueRowText.CardSubtitle(row.NextUp, row.ServiceName))));
        }

        var scroll = new ScrollView(host) { FillViewport = true };
        scroll.AddView(list);
        return scroll;
    }

    /// <summary>Home's bar in a child's view (D213); Favorites' bar has only the way back (mockup 06).</summary>
    public View BottomBar()
    {
        var back = PaperUi.SecondaryButton(host, ChildViewText.BackToMe, backToMe);
        if (showingFavorites)
        {
            return PaperUi.BottomBar(host, back);
        }

        return PaperUi.BottomBar(host, back, PaperUi.PrimaryButton(host, ChildViewText.Favorites, () =>
        {
            showingFavorites = true;
            favorites.Reload();
            invalidate();
        }));
    }

    /// <summary>Back from Favorites comes home; at home it is the shell's.</summary>
    public bool GoBack()
    {
        if (!showingFavorites)
        {
            return false;
        }

        showingFavorites = false;
        favorites.Pause();
        Reload();
        invalidate();
        return true;
    }

    public void Pause()
    {
        loading?.Cancel();
        loading = null;
        favorites.Pause();
    }

    public void Reload()
    {
        if (showingFavorites)
        {
            favorites.Reload();
            return;
        }

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
                    rows = read.Rows;
                    invalidate();
                    if (read.Facts is { } facts)
                    {
                        _ = voice.SpeakAsync(facts, Present, cancel.Token);
                    }
                });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // A newer read, or the screen going away, superseded this one.
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("child", "A child's Continue watching could not be read.", exception);
                host.RunOnUiThread(() =>
                {
                    unreadable = rows is null;
                    invalidate();
                });
            }
        });
    }

    private sealed record Read(string? Problem, ChildPlay? Play, IReadOnlyList<WatchingRow>? Rows, VoiceFacts? Facts);

    private async Task<Read> ReadAsync(PulpServices services, CancellationToken cancellationToken)
    {
        var household = await services.HouseholdAsync(cancellationToken);
        var resolved = session.Resolve(household.Roster, household.ChildRules);
        if (resolved.Audience is not { } audience)
        {
            return new Read(resolved.Problem, null, null, null);
        }

        // Each child's own progress (D27, D97), one row a title, most recent first.
        var continueRows = new List<ContinueRow>();
        foreach (var child in audience.Watching)
        {
            continueRows.AddRange(await services.ContinueWatchingAsync(child, cancellationToken));
        }

        var read = new List<(WatchingRow Row, TitleFacts? Facts)>();
        foreach (var row in continueRows.OrderByDescending(row => row.Since).DistinctBy(row => row.Title))
        {
            var title = await services.TitleAsync(row.Title, tracked: true, opened: false, cancellationToken);
            var home = ContinueRowText.Home(title, household.Services, household.HomeOverrides);
            var serviceName = home is null ? null : services.Catalogue.ById(home.Service)?.Name ?? home.Service.Value;
            read.Add((new WatchingRow(
                row,
                title?.Name ?? row.Title.Value,
                title,
                ContinueRowText.NextUp(row, title),
                ContinueRowText.Where(title, household.Services, household.HomeOverrides,
                    service => services.Catalogue.ById(service)?.Name ?? service.Value),
                home,
                serviceName,
                await services.Posters.PathForAsync(title?.PosterUrl, cancellationToken)),
                title is null ? null : DiscoverFacts.For(title, _ => [])));
        }

        // A title with no facts is hidden, as unrated is (D128): nothing reaches a child unjudged.
        var shown = audience.Visible(read, entry => entry.Facts).Select(entry => entry.Row).ToList();
        var reader = audience.ReadingLevel;
        var facts = shown.Count > 0
            ? ChildVoiceFacts.Greeting(reader, DateTimeOffset.Now, firstOpenOfDay: false)
            : ChildVoiceFacts.EmptyWatching(reader);
        return new Read(null, new ChildPlay(host, overlay, services, audience, household, Reload), shown, facts);
    }

    private void Present(VoiceLineAction action, string? line)
    {
        if (rows is { Count: > 0 })
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
        column.SetPadding(0, 0, 0, PaperTheme.Dp(host, DesignTokens.Space.X4));
        column.AddView(PaperUi.Secondary(host, text), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return column;
    }
}
