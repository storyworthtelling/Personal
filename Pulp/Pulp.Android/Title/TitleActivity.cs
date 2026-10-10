using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Actions;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Ratings;
using Pulp.Domain.Shell;
using Pulp.Domain.Title;
using Pulp.Domain.Voice;
using Pulp.HandOffs;
using Pulp.Shell;
using HomeChoice = Pulp.Domain.Availability.HomeChoice;
using HomeOverride = Pulp.Domain.Household.Records.HomeChoice;

namespace Pulp.Title;

/// <summary>
/// A title's page (mockup 02, D218; a film's page, D272). Every screen that lists a title opens it
/// through <see cref="For"/>.
///
/// <para>As drawn: the title as the Serif header over the identity's rule; the poster with the kind ·
/// seasons · episodes line (a film: its runtime), the rating chip (D215, teen in the reference slate
/// by D276, no reasons by D207), the best home and the others beneath it with "open there instead"
/// (D28, D29, D216), and the quiet Trailer button (D34, D77); the why-this line in the owner's hand,
/// hidden until composed (D36, D41); the synopsis; and for a show the season accordion (D159, D217).
/// The pinned bar is Add to… and a Play that names what it will play (D158). The binder gutter is
/// drawn with Watching active, since the page is reached from inside the binder.</para>
///
/// <para>Play goes only through <see cref="HandOffExecutor"/>, the one door past the child gate (G13):
/// this page asks for a hand-off and lays the executor's report on the dog-ear; it never builds the
/// intent itself. Success is the service's app opening, and the page repaints with the hand-off
/// counted when it comes back — no toast. Everything that reads a file or the network runs off the
/// UI thread, and while the title is being looked up the page says so in one sentence.</para>
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class TitleActivity : Activity
{
    public const string ExtraTitleId = "titleId";

    private readonly CancellationTokenSource lifetime = new();

    private PulpServices services = null!;
    private TitleId id;
    private FrameLayout overlay = null!;
    private LinearLayout headerSlot = null!;
    private TextView pageStatus = null!;
    private LinearLayout body = null!;
    private TextView barStatus = null!;
    private Button addTo = null!;
    private Button play = null!;
    private ArtworkWindow poster = null!;
    private TextView voice = null!;
    private Button trailer = null!;
    private SeasonAccordion accordion = null!;

    private PageState? state;
    private Task writes = Task.CompletedTask;
    private Intent? trailerIntent;
    private string? posterShown;
    private bool extrasAsked;

    public static Intent For(Context context, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(TitleActivity));
        intent.PutExtra(ExtraTitleId, title.Value);
        return intent;
    }

    /// <summary>
    /// What the page knows. <see cref="Fresh"/> is set once the source has been asked this visit, so
    /// "none of your services streams this" is never said of a stale or missing copy.
    /// </summary>
    private sealed record PageState(
        CatalogueTitle Title,
        HouseholdView? Household,
        ViewerId? Viewer,
        IReadOnlyList<WatchEvent> Sittings,
        HandOffExecutor? Executor,
        string? PlayBlocked,
        bool Fresh);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        var raw = Intent?.GetStringExtra(ExtraTitleId);
        if (string.IsNullOrWhiteSpace(raw))
        {
            Android.Util.Log.Warn("Pulp", "TitleActivity opened without a title id; closing.");
            Finish();
            return;
        }

        id = new TitleId(raw);
        services = PulpServices.For(this);
        SetContentView(BuildLayout());
        ShowStatus(TitlePageText.Looking);
        _ = LoadAsync();
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Back from a service's app or another screen: the sittings may have moved (a hand-off counted,
        // a mark made elsewhere), so the ticks and the Play label are read again.
        if (state is not null)
        {
            _ = RefreshHouseholdAsync();
        }
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }

    public override void OnBackPressed()
    {
        if (overlay is not null && AddToPicker.IsOpen(overlay))
        {
            AddToPicker.Fold(overlay);
            addTo.Text = AddToPicker.ButtonLabel(open: false);
            return;
        }

        base.OnBackPressed();
    }

    // ---- layout ----

    private View BuildLayout()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, PaperTheme.Dp(this, DesignTokens.Space.X6));

        headerSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(headerSlot);

        pageStatus = PaperUi.Secondary(this, string.Empty);
        pageStatus.Visibility = ViewStates.Gone;
        content.AddView(pageStatus);

        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(body);

        // Built once and moved between paints, so the poster, the voiced line, the trailer and the
        // accordion's folds survive a repaint rather than flashing back to empty.
        poster = new ArtworkWindow(this);
        voice = PaperUi.Voice(this, string.Empty);
        voice.Visibility = ViewStates.Gone;
        trailer = PaperUi.QuietButton(this, "▷  " + TitlePageText.TrailerLabel, OpenTrailer);
        trailer.SetTextColor(PaperTheme.Ink);
        trailer.Gravity = GravityFlags.Start | GravityFlags.CenterVertical;
        trailer.SetPadding(0, trailer.PaddingTop, trailer.PaddingRight, trailer.PaddingBottom);
        trailer.Visibility = ViewStates.Gone;
        accordion = new SeasonAccordion(this, MarkEpisode);
        accordion.Repaint += () => Paint();

        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(content);

        overlay = PaperUi.WrapForTopInset(this, scroll);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(overlay, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        barStatus = PaperUi.Secondary(this, string.Empty);
        barStatus.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X2), edge, 0);
        barStatus.Visibility = ViewStates.Gone;
        column.AddView(barStatus);

        addTo = PaperUi.SecondaryButton(this, AddToPicker.ButtonLabel(open: false), ToggleAddTo);
        play = PaperUi.PrimaryButton(this, "Play", Play);
        SetEnabled(addTo, false);
        SetEnabled(play, false);
        column.AddView(PaperUi.BottomBar(this, addTo, play));

        var spread = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        spread.AddView(column, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent) { Weight = 1 });

        // The gutter as drawn, Watching active. A tab opens the shell on the area tapped.
        var binder = new BinderTabs(this);
        binder.SetAreas(PulpAreas.All.Select(Tab), 0);
        binder.AreaSelected += index => MainActivity.OpenOn(this, index);
        spread.AddView(binder, new LinearLayout.LayoutParams(PaperTheme.Dp(this, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));
        return spread;
    }

    private static BinderArea Tab(PulpAreaSpec spec)
    {
        var (ink, cloth, _) = AreaPalette.For(spec.Hue);
        return new BinderArea(spec.Label, AreaPalette.Glyph(spec.Area), ink, cloth);
    }

    // ---- loading ----

    private async Task LoadAsync()
    {
        var token = lifetime.Token;
        try
        {
            // The cached copy first, for an instant paint; then the source, when it is due.
            var cached = await Task.Run(() => services.CachedCopy(id), token);
            var household = await Task.Run(() => ReadHouseholdAsync(token), token);
            if (cached is not null)
            {
                Post(() => Apply(Compose(cached, household, fresh: false)));
            }

            var tracked = household.Sittings.Any(sitting => sitting.Title.Equals(id));
            var fetched = await Task.Run(() => services.TitleAsync(id, tracked, opened: true, token), token);
            if (fetched is null)
            {
                Post(() =>
                {
                    if (state is null)
                    {
                        ShowStatus(TitlePageText.NotFound);
                    }
                });
                return;
            }

            Post(() => Apply(Compose(fetched, household, fresh: true)));
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The page closed; nothing is waiting for the answer.
        }
        catch (Exception exception)
        {
            services.Diagnostics.Warn("title", $"Title {id} could not be loaded.", exception);
            Post(() =>
            {
                if (state is null)
                {
                    ShowStatus(TitlePageText.NotFound);
                }
            });
        }
    }

    private sealed record HouseholdRead(HouseholdView? View, ViewerId? Viewer, IReadOnlyList<WatchEvent> Sittings, HandOffExecutor? Executor, string? PlayBlocked);

    private async Task<HouseholdRead> ReadHouseholdAsync(CancellationToken token)
    {
        var view = await services.HouseholdAsync(token);
        var viewer = services.ThisPhoneOf(view).Viewer;
        var sittings = viewer is { } who ? view.SittingsOf(who) : [];

        HandOffExecutor? executor;
        string? blocked;
        try
        {
            executor = await services.HandOffsAsync(token);
            blocked = executor is null ? TitlePageText.NoHousehold : viewer is null ? ThisPhone.UnmatchedSentence : null;
        }
        catch (ArgumentException exception)
        {
            // The roster and the child rules disagree; the gate refuses to be built rather than judge a
            // child by nothing, so Play waits and Diagnostics says why.
            services.Diagnostics.Warn("title", "The hand-off gate could not be built from the household.", exception);
            executor = null;
            blocked = TitlePageText.HouseholdUnreadable;
        }

        return new HouseholdRead(view, viewer, sittings, executor, blocked);
    }

    private static PageState Compose(CatalogueTitle title, HouseholdRead household, bool fresh) =>
        new(title, household.View, household.Viewer, household.Sittings, household.Executor, household.PlayBlocked, fresh);

    private async Task RefreshHouseholdAsync()
    {
        var token = lifetime.Token;
        try
        {
            var household = await Task.Run(() => ReadHouseholdAsync(token), token);
            Post(() =>
            {
                if (state is { } current)
                {
                    Apply(Compose(current.Title, household, current.Fresh));
                }
            });
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The page closed.
        }
        catch (Exception exception)
        {
            services.Diagnostics.Warn("title", $"The household could not be read again for title {id}.", exception);
        }
    }

    private void Apply(PageState next)
    {
        // A fresh answer is never replaced by the cached copy arriving late.
        if (state is { Fresh: true } && !next.Fresh)
        {
            return;
        }

        state = next;
        Paint();
        AskForExtras(next);
    }

    /// <summary>The poster, the trailer and the why-this line: asked once the title is known, each off the UI thread.</summary>
    private void AskForExtras(PageState current)
    {
        if (current.Title.PosterUrl != posterShown)
        {
            posterShown = current.Title.PosterUrl;
            _ = LoadPosterAsync(current.Title.PosterUrl);
        }

        if (!current.Fresh || extrasAsked)
        {
            return;
        }

        extrasAsked = true;
        _ = LoadTrailerAsync(current.Title);
        if (current.Viewer is { } viewer)
        {
            _ = LoadVoiceAsync(current.Title, viewer, current.Sittings);
        }
    }

    private async Task LoadPosterAsync(string? url)
    {
        var token = lifetime.Token;
        try
        {
            var size = PaperTheme.Dp(this, PosterHeightDp);
            var bitmap = await Task.Run(() => services.Posters.LoadAsync(url, size, token), token);
            Post(() => poster.SetArtwork(bitmap));
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The page closed.
        }
    }

    private async Task LoadTrailerAsync(CatalogueTitle title)
    {
        var token = lifetime.Token;
        try
        {
            var lookup = new TrailerLookup(this, services);
            var intent = await Task.Run(() => lookup.FindAsync(this, title, token), token);
            Post(() =>
            {
                trailerIntent = intent;
                trailer.Visibility = intent is null ? ViewStates.Gone : ViewStates.Visible;
            });
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The page closed.
        }
        catch (Exception exception)
        {
            services.Diagnostics.Warn("trailer", $"The trailer for {title.Id} could not be looked up; the button stays hidden.", exception);
        }
    }

    private async Task LoadVoiceAsync(CatalogueTitle title, ViewerId viewer, IReadOnlyList<WatchEvent> sittings)
    {
        var token = lifetime.Token;
        try
        {
            var line = await Task.Run(async () =>
            {
                var past = WhyThisPast.Alike(viewer, title, sittings, services.CachedCopy);
                var facts = VoiceFactBuilder.WhyThis(viewer, ViewerStanding.DeviceOwner, title.Name, past);
                return await new TitleVoiceLine(this).LineAsync(title.Id.Value, facts, token);
            }, token);
            Post(() =>
            {
                voice.Text = line ?? string.Empty;
                voice.Visibility = string.IsNullOrWhiteSpace(line) ? ViewStates.Gone : ViewStates.Visible;
            });
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The page closed.
        }
        catch (Exception exception)
        {
            // Never voiced and never on the page (§14): the seat simply stays hidden.
            services.Diagnostics.Warn("voice", $"The why-this line for {title.Id} could not be composed.", exception);
        }
    }

    // ---- painting ----

    private const int PosterWidthDp = DesignTokens.Space.X24;
    private const int PosterHeightDp = DesignTokens.Space.X24 + DesignTokens.Space.X12;

    private void Paint()
    {
        if (state is not { } current)
        {
            return;
        }

        var title = current.Title;
        pageStatus.Visibility = ViewStates.Gone;

        headerSlot.RemoveAllViews();
        headerSlot.AddView(PaperUi.ScreenHeader(this, title.Name, AreaPalette.For(PulpAreas.RuleHue(PulpArea.Watching)).Rule));

        Detach(poster, voice, trailer, accordion);
        body.RemoveAllViews();
        var homes = TitleHomes.Choose(title, current.Household?.Services ?? [], current.Household?.HomeOverrides ?? HomeOverrides.None);

        body.AddView(TopRow(current, homes));

        voice.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X3), 0, PaperTheme.Dp(this, DesignTokens.Space.X2));
        body.AddView(voice);

        if (!string.IsNullOrWhiteSpace(title.Synopsis))
        {
            var synopsis = PaperUi.Body(this, title.Synopsis.Trim());
            synopsis.Typeface = PaperTheme.SerifRegular(this);
            synopsis.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X2), 0, PaperTheme.Dp(this, DesignTokens.Space.X3));
            body.AddView(synopsis);
        }

        // A film's page is the same anatomy without the accordion (D272).
        if (title.Kind == TitleKind.Show && title.Seasons.Count > 0)
        {
            var watched = TitleProgress.WatchedEpisodes(title.Id, current.Sittings);
            var next = TitleProgress.NextUp(title, current.Sittings, DateTimeOffset.UtcNow);
            accordion.Show(title, watched, next, TitleProgress.OpenSeason(title, next, watched));
            body.AddView(accordion);
        }

        PaintBar(current, homes);
    }

    private View TopRow(PageState current, HomeChoice homes)
    {
        var title = current.Title;
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.AddView(poster, new LinearLayout.LayoutParams(PaperTheme.Dp(this, PosterWidthDp), PaperTheme.Dp(this, PosterHeightDp))
        {
            MarginEnd = PaperTheme.Dp(this, DesignTokens.Space.X3),
        });

        var facts = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var kind = TitlePageText.KindLine(title);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            var line = PaperUi.Secondary(this, kind);
            line.SetTextColor(PaperTheme.InkSoft);
            facts.AddView(line);
        }

        var chip = new RatingChipView(this, RatingChip.For(title.Rating));
        facts.AddView(chip, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X1),
        });

        if (homes.Best is { } best)
        {
            var where = PaperUi.Secondary(this, TitlePageText.WhereLine(best, ServiceName(best.Service)));
            where.SetTextColor(PaperTheme.Ink);
            where.Typeface = PaperTheme.SansSemibold(this);
            where.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X2), 0, 0);
            facts.AddView(where);

            foreach (var other in homes.Others)
            {
                facts.AddView(AlsoLine(other));
            }
        }
        else if (current.Fresh && current.Household is not null)
        {
            var none = PaperUi.Secondary(this, TitlePageText.NotOnOurServices);
            none.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X2), 0, 0);
            facts.AddView(none);
        }

        facts.AddView(trailer, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        row.AddView(facts, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        return row;
    }

    /// <summary>"Also on Lanternbox, with ads · open there instead" — the whole line is the target, the link underlined as drawn.</summary>
    private TextView AlsoLine(Home other)
    {
        var lead = TitlePageText.AlsoLine(other, ServiceName(other.Service)) + " · ";
        var text = new SpannableString(lead + TitlePageText.OpenThereInstead);
        text.SetSpan(new UnderlineSpan(), lead.Length, text.Length(), SpanTypes.ExclusiveExclusive);

        var line = PaperUi.Label(this, string.Empty);
        line.Typeface = PaperTheme.SansRegular(this);
        line.TextFormatted = text;
        line.Gravity = GravityFlags.CenterVertical;
        line.SetMinHeight(PaperTheme.Dp(this, DesignTokens.Shape.TouchTargetDp));
        line.Click += (_, _) => OpenThereInstead(other.Service);
        return line;
    }

    private void PaintBar(PageState current, HomeChoice homes)
    {
        var title = current.Title;
        var episode = TitleProgress.ToPlay(title, current.Sittings, DateTimeOffset.UtcNow);
        play.Text = TitlePageText.PlayLabel(title, episode);

        var blocked = current.PlayBlocked;
        if (blocked is null && homes.Best is null)
        {
            blocked = current.Fresh ? TitlePageText.NotOnOurServices : TitlePageText.Looking;
        }

        if (blocked is null && homes.Best is { } best && TitleHomes.LinkFor(title, best.Service, episode) is null)
        {
            blocked = TitlePageText.NoLink;
        }

        SetEnabled(play, blocked is null);
        SetEnabled(addTo, current.Viewer is not null && current.Household is not null);

        // The "none of your services" sentence already sits by the poster; the bar does not repeat it.
        var sentence = blocked == TitlePageText.NotOnOurServices ? null : blocked;
        barStatus.Text = sentence ?? string.Empty;
        barStatus.Visibility = sentence is null ? ViewStates.Gone : ViewStates.Visible;
    }

    private void ShowStatus(string sentence)
    {
        pageStatus.Text = sentence;
        pageStatus.Visibility = ViewStates.Visible;
    }

    private string ServiceName(ServiceId service) => services.Catalogue.ById(service)?.Name ?? service.Value;

    private static void SetEnabled(Button button, bool enabled)
    {
        button.Enabled = enabled;
        button.Alpha = enabled ? 1f : DisabledAlpha;
    }

    // As the mockups draw a locked thing: the same ink, faded.
    private const float DisabledAlpha = 0.45f;

    private static void Detach(params View[] views)
    {
        foreach (var view in views)
        {
            (view.Parent as ViewGroup)?.RemoveView(view);
        }
    }

    private void Post(Action action) =>
        RunOnUiThread(() =>
        {
            if (!IsFinishing && !IsDestroyed)
            {
                action();
            }
        });

    // ---- actions ----

    private void OpenTrailer()
    {
        if (trailerIntent is { } intent)
        {
            StartActivity(intent);
        }
    }

    /// <summary>D216: the other home becomes this title's home from now on; the page repaints with it on top.</summary>
    private void OpenThereInstead(ServiceId service)
    {
        Write($"The home for {id}", token => services.PutAsync(HouseholdMaps.HomeOverride, new HomeOverride(id, service), token));
    }

    private void Play()
    {
        if (state is not { Executor: { } executor, Viewer: { } viewer } current)
        {
            return;
        }

        var title = current.Title;
        var homes = TitleHomes.Choose(title, current.Household?.Services ?? [], current.Household?.HomeOverrides ?? HomeOverrides.None);
        if (homes.Best is not { } best)
        {
            return;
        }

        var episode = TitleProgress.ToPlay(title, current.Sittings, DateTimeOffset.UtcNow);
        if (TitleHomes.LinkFor(title, best.Service, episode) is not { } link)
        {
            return;
        }

        // An adult's hand-off: the catalogue carries no franchises, and a franchise block is a child's
        // rule, which the gate applies only to a child — none is on this request (ThisPhone never answers a child).
        var request = new HandOffRequest([viewer], DiscoverFacts.For(title, _ => []), link, TitleHomes.SessionNames(title, episode));
        var at = DateTimeOffset.UtcNow;
        var outcome = executor.Start(this, request, at);
        var buttons = new PulpNotices.Buttons(OpenTitle: other => StartActivity(For(this, other)));
        PulpNotices.AfterHandOff(this, overlay, outcome, request, ServiceName(best.Service), title.Name, at, buttons);

        // D10, D30: the hand-off is the fallback evidence of watching, kept once the app has opened.
        if (outcome.Recorded is { } pending)
        {
            var sitting = new WatchEvent(title.Id, KindOf(title), pending.Evidence.At, pending.Service, episode, WatchSource.HandOff);
            Write($"The hand-off of {id}", token => services.PutAsync(HouseholdMaps.Watched, new WatchedSitting(viewer, sitting), token));
        }
    }

    private static WatchedKind KindOf(CatalogueTitle title) => title.Kind == TitleKind.Film ? WatchedKind.Film : WatchedKind.Show;

    /// <summary>D160: a long-press on an episode opens the card; every mark is undo-first.</summary>
    private void MarkEpisode(CatalogueEpisode episode)
    {
        if (state is not { Viewer: { } viewer } current)
        {
            return;
        }

        var title = current.Title;
        var watched = TitleProgress.WatchedEpisodes(title.Id, current.Sittings);
        var item = new CardItem(title.Name, CardPlace.Episode)
        {
            Subtitle = string.IsNullOrWhiteSpace(episode.Name)
                ? TitlePageText.Episode(episode.Episode)
                : $"{TitlePageText.Episode(episode.Episode)} · {episode.Name.Trim()}",
            EpisodeWatched = watched.Contains(episode.Episode),
        };

        CardHandler Handler(CardActionKind kind)
        {
            IReadOnlyList<WatchEvent> made = [];
            return new CardHandler(
                Do: () =>
                {
                    made = EpisodeMarks.For(kind, title, episode.Episode, watched, DateTimeOffset.UtcNow);
                    var marks = made;
                    Mark(marks, add: true);
                    Write($"A mark on {id}", async token =>
                    {
                        foreach (var mark in marks)
                        {
                            await services.PutAsync(HouseholdMaps.Watched, new WatchedSitting(viewer, mark), token);
                        }
                    });
                },
                Undo: () =>
                {
                    var marks = made;
                    Mark(marks, add: false);
                    Write($"Undoing a mark on {id}", async token =>
                    {
                        foreach (var mark in marks)
                        {
                            await services.RemoveAsync(HouseholdMaps.Watched, new WatchedSitting(viewer, mark), token);
                        }
                    });
                });
        }

        var handlers = new Dictionary<CardActionKind, CardHandler>
        {
            [CardActionKind.Watched] = Handler(CardActionKind.Watched),
            [CardActionKind.Unwatched] = Handler(CardActionKind.Unwatched),
            [CardActionKind.WatchedUpToHere] = Handler(CardActionKind.WatchedUpToHere),
        };
        PulpActionCard.Show(this, overlay, item, handlers);
    }

    /// <summary>The inline state change (§5): the ticks move at once; the household read after the save is the truth.</summary>
    private void Mark(IReadOnlyList<WatchEvent> marks, bool add)
    {
        if (state is not { } current)
        {
            return;
        }

        var sittings = add ? [.. current.Sittings, .. marks] : current.Sittings.Where(sitting => !marks.Contains(sitting)).ToList();
        state = current with { Sittings = sittings };
        Paint();
    }

    // ---- Add to… ----

    private void ToggleAddTo()
    {
        if (AddToPicker.IsOpen(overlay))
        {
            AddToPicker.Fold(overlay);
            addTo.Text = AddToPicker.ButtonLabel(open: false);
            return;
        }

        ShowPicker();
    }

    private void ShowPicker()
    {
        if (state is not { Viewer: { } viewer, Household: { } household } current)
        {
            return;
        }

        var title = current.Title;
        var entry = PlaylistEntry.ForTitle(title.Id, title.Kind);
        var playlists = household.PlaylistsFor(viewer).Select(list => new PickerPlaylist(list.Id.Value, list.List, list.Shared, LastUsed: null));
        var children = household.ChildRules.Select(rules => new PickerChild(rules.Child.Value, rules));
        var model = AddToPickerPlan.Build(title.Name, entry, DiscoverFacts.For(title, _ => []), playlists, children);

        AddToPicker.Show(this, overlay, model,
            (row, change) => OnPick(household, viewer, entry, row, change),
            name => NewPlaylist(viewer, entry, name),
            onFolded: () => addTo.Text = AddToPicker.ButtonLabel(open: false));
        addTo.Text = AddToPicker.ButtonLabel(open: true);
    }

    private Action? OnPick(HouseholdView household, ViewerId viewer, PlaylistEntry entry, PickerRow row, PickerChange change)
    {
        if (change == PickerChange.Refused)
        {
            return null;
        }

        if (row.Group == PickerGroup.Playlists)
        {
            var list = household.Playlist(new PlaylistId(row.Key))
                ?? throw new InvalidOperationException($"The picker offered playlist '{row.Key}', which the household no longer holds.");
            if (change == PickerChange.RemoveUndoFirst)
            {
                Write($"Removing {id} from a playlist", token => services.PutAsync(HouseholdMaps.Playlist, list with { List = list.List.Remove(entry) }, token));
                return () => Write($"Putting {id} back on a playlist", token => services.PutAsync(HouseholdMaps.Playlist, list, token));
            }

            Write($"Adding {id} to a playlist", token => services.PutAsync(HouseholdMaps.Playlist, list with { List = list.List.Add(entry) }, token));
            return null;
        }

        var child = new ViewerId(row.Key);
        if (change == PickerChange.RemoveUndoFirst)
        {
            var picks = household.ChildRules.Where(rules => rules.Child == child)
                .SelectMany(rules => rules.ParentPicks)
                .Where(pick => pick.Title == id)
                .Select(pick => new ParentPickEntry(child, pick))
                .ToList();
            Write($"Removing {id} from a child's Favorites", async token =>
            {
                foreach (var pick in picks)
                {
                    await services.RemoveAsync(HouseholdMaps.ParentPick, pick, token);
                }
            });
            return () => Write($"Putting {id} back on a child's Favorites", async token =>
            {
                foreach (var pick in picks)
                {
                    await services.PutAsync(HouseholdMaps.ParentPick, pick, token);
                }
            });
        }

        // A parent's pick allow-lists its title for the child (D255), so Add and AddAndAllowList write the same record.
        var added = new ParentPickEntry(child, new ParentPick(id, viewer, DateTimeOffset.UtcNow));
        Write($"Adding {id} to a child's Favorites", token => services.PutAsync(HouseholdMaps.ParentPick, added, token));
        return null;
    }

    private void NewPlaylist(ViewerId viewer, PlaylistEntry entry, string name)
    {
        // Personal by default (mockup 11); sharing is the playlist's own setting.
        var list = new HouseholdPlaylist(new PlaylistId(Guid.NewGuid().ToString("N")), viewer, Shared: false, Playlist.Named(name).Add(entry));
        Write($"A new playlist with {id}", token => services.PutAsync(HouseholdMaps.Playlist, list, token), reopenPicker: true);
    }

    // ---- writes ----

    /// <summary>
    /// Household writes, one after another so an undo can never land before the act it undoes. A failed
    /// write is logged with what it was; the household is read again afterwards either way, so the page
    /// shows what was actually kept.
    /// </summary>
    private void Write(string what, Func<CancellationToken, Task> work, bool reopenPicker = false)
    {
        var token = lifetime.Token;
        writes = writes.ContinueWith(async _ =>
        {
            try
            {
                await work(token);
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("household", $"{what} could not be saved.", exception);
            }

            await RefreshHouseholdAsync();
            if (reopenPicker)
            {
                Post(() =>
                {
                    if (AddToPicker.IsOpen(overlay))
                    {
                        ShowPicker();
                    }
                });
            }
        }, token, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }
}
