using Android.App;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Discover;
using Pulp.Title;
using Pulp.Domain.Catalogue;
using Pulp.Domain.ChildView;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Services;
using Pulp.Domain.Title;
using Pulp.Favorites;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Shell;

/// <summary>
/// Discover in a child's view (D42, D131, D290): no Friday stack — a child has no taste profile (D23)
/// — so the page is the search field with its Title / Mood chips and, under it, what the search found
/// that every child watching may see, as big posters two across. A mood search tells the model a child
/// is watching and nothing else; the filter, not the model, decides (D290). A tap plays through the
/// child gate; a long press opens the child's card.
/// </summary>
public sealed class ChildDiscoverPage(Activity host, Func<FrameLayout> overlay, ChildViewSession session)
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private LinearLayout? root;
    private FilterField? field;
    private LinearLayout? body;
    private DiscoverSearchMode mode = DiscoverSearchMode.Title;
    private DateTimeOffset editedAt = DateTimeOffset.MinValue;
    private Java.Lang.IRunnable? pending;
    private CancellationTokenSource? search;
    private int generation;

    private PulpServices Services => PulpServices.For(host);

    public View Build()
    {
        if (root is not null)
        {
            (root.Parent as ViewGroup)?.RemoveView(root);
            return root;
        }

        root = new LinearLayout(host) { Orientation = Orientation.Vertical };
        field = new FilterField(host, DiscoverPageInput.Hint, Android.Graphics.Color.ParseColor(DesignTokens.Identity.Pulp.Mark));
        field.QueryChanged += _ =>
        {
            editedAt = DateTimeOffset.UtcNow;
            Decide(submitted: false);
        };
        if (field.ChildCount > 0 && field.GetChildAt(0) is EditText input)
        {
            input.ImeOptions = ImeAction.Search;
            input.EditorAction += (_, args) =>
            {
                args.Handled = args.ActionId is ImeAction.Search or ImeAction.Done;
                if (args.Handled)
                {
                    Decide(submitted: true);
                }
            };
        }

        root.AddView(field, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        root.AddView(new ModeChips(host, mode, tapped =>
        {
            mode = tapped;
            Decide(submitted: field.Query.Length > 0);
        }), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(host, DesignTokens.Space.X2),
        });

        body = new LinearLayout(host) { Orientation = Orientation.Vertical };
        root.AddView(body, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        ShowStatus(ChildViewText.DiscoverStatus);
        return root;
    }

    public void Pause() => Cancel();

    private void Decide(bool submitted)
    {
        if (field is null)
        {
            return;
        }

        if (pending is not null)
        {
            handler.RemoveCallbacks(pending);
            pending = null;
        }

        var step = DiscoverPageInput.Decide(field.Query, mode, editedAt, DateTimeOffset.UtcNow, submitted);
        switch (step.Action)
        {
            case DiscoverPageAction.ShowStack:
                Cancel();
                ShowStatus(ChildViewText.DiscoverStatus);
                break;
            case DiscoverPageAction.Wait:
                if (step.AskAgainAt is { } at)
                {
                    pending = new Java.Lang.Runnable(() => Decide(submitted: false));
                    handler.PostDelayed(pending, Math.Max(0, (long)(at - DateTimeOffset.UtcNow).TotalMilliseconds));
                }

                break;
            case DiscoverPageAction.AwaitMoodDone:
                Cancel();
                ShowStatus(DiscoverPageText.MoodPrompt);
                break;
            case DiscoverPageAction.SearchTitles:
                Search(DiscoverPageText.Searching(step.Text), (audience, view, token) => SearchTitlesAsync(step.Text, audience, token));
                break;
            case DiscoverPageAction.AskMood:
                Search(DiscoverPageText.Asking(step.Text), (audience, view, token) => AskMoodAsync(step.Text, audience, view, token));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(submitted), step.Action, "A field decision the child's page does not act on.");
        }
    }

    private void Search(string status, Func<ChildAudience, HouseholdView, CancellationToken, Task<IReadOnlyList<CatalogueTitle>>> find)
    {
        Cancel();
        search = new CancellationTokenSource();
        var token = search.Token;
        var asked = ++generation;
        ShowStatus(status);

        _ = Task.Run(async () =>
        {
            try
            {
                var view = await Services.HouseholdAsync(token);
                var resolved = session.Resolve(view.Roster, view.ChildRules, view.NameOf);
                if (resolved.Audience is not { } audience)
                {
                    Land(asked, () => ShowStatus(resolved.Problem ?? ChildViewText.Unreadable));
                    return;
                }

                var found = await find(audience, view, token);

                // Judged on the cached copy when there is one, which carries the rating a search result may lack;
                // an unrated title stays hidden (D128).
                var judged = found.Select(title => Services.CachedCopy(title.Id) ?? title).DistinctBy(title => title.Id).ToList();
                var shown = audience.Visible(judged, title => DiscoverFacts.For(title, _ => []));
                var cells = shown.Select(title => Cell(audience, view, title)).ToList();
                var play = new ChildPlay(host, overlay, Services, audience, view, () => { });
                Land(asked, () => ShowResults(cells, play));
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A newer search superseded this one.
            }
            catch (Exception exception)
            {
                Services.Diagnostics.Warn("child", "A child's search failed: " + exception.Message, exception);
                Land(asked, () => ShowStatus(DiscoverPageText.SearchUnreachable));
            }
        }, token);
    }

    private async Task<IReadOnlyList<CatalogueTitle>> SearchTitlesAsync(string text, ChildAudience audience, CancellationToken token)
    {
        try
        {
            return await Services.SearchAsync(text, token);
        }
        catch (CatalogueLookupException exception)
        {
            Services.Diagnostics.Warn("child", $"A child's title search failed ({exception.Failure}).", exception);
            return [];
        }
    }

    private async Task<IReadOnlyList<CatalogueTitle>> AskMoodAsync(string mood, ChildAudience audience, HouseholdView view, CancellationToken token)
    {
        if (view.Roster is not { } roster)
        {
            return [];
        }

        // A child has no taste (D23): the context carries the household's services and that a child is watching.
        var viewer = audience.Mood;
        var services = view.Services.Select(Services.Catalogue.ById).OfType<StreamingService>().ToList();
        var query = MoodQuery.For(mood, DiscoverContext.For(viewer, roster, services, TasteProfile.Empty));
        var answer = await Services.Mood.AskMoodAsync(query, token);
        if (answer.Outcome != MoodOutcome.Answered)
        {
            Services.Diagnostics.Warn("child", $"A child's mood search had no idea ({answer.Outcome}): {answer.Detail}");
            return [];
        }

        var found = new List<CatalogueTitle>();
        foreach (var term in MoodPlan.SearchTerms(answer))
        {
            try
            {
                found.AddRange(await Services.SearchAsync(term, token));
            }
            catch (CatalogueLookupException exception)
            {
                Services.Diagnostics.Warn("child", $"A mood candidate's lookup failed ({exception.Failure}); it is dropped.", exception);
            }
        }

        return MoodPlan.Resolve(query, answer, found, viewer, _ => []).Titles;
    }

    private PosterCell Cell(ChildAudience audience, HouseholdView view, CatalogueTitle title)
    {
        var home = audience.HomeFor(TitleHomes.Choose(title, view.ServicePlans, view.HomeOverrides), DiscoverFacts.For(title, _ => []), Services.Catalogue, view.Attested);
        var where = home is null ? null : ChildFavorites.WhereLine(title, home, Services.Catalogue.ById(home.Service)?.Name ?? home.Service.Value);
        return new PosterCell(title.Id, title.Name, where, null, title.PosterUrl);
    }

    private void ShowResults(IReadOnlyList<PosterCell> cells, ChildPlay play)
    {
        if (cells.Count == 0)
        {
            ShowStatus(ChildViewText.NothingFound);
            return;
        }

        var posters = Services.Posters;
        body?.RemoveAllViews();
        body?.AddView(PosterGrid.Build(
            host,
            cells,
            cell => host.StartActivity(TitleActivity.For(host, cell.Id)),
            cell => play.ShowCard(cell.Id, cell.Name, cell.Where),
            PosterArt.Loader(host, posters)), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    private void Land(int asked, Action show) =>
        host.RunOnUiThread(() =>
        {
            if (asked == generation && field is not null && field.Query.Length > 0)
            {
                show();
            }
        });

    private void Cancel()
    {
        generation++;
        search?.Cancel();
        search?.Dispose();
        search = null;
    }

    private void ShowStatus(string text)
    {
        if (body is null)
        {
            return;
        }

        body.RemoveAllViews();
        var status = PaperUi.Secondary(host, text);
        status.SetPadding(0, PaperTheme.Dp(host, DesignTokens.Space.X2), 0, PaperTheme.Dp(host, DesignTokens.Space.X1));
        body.AddView(status);
    }
}
