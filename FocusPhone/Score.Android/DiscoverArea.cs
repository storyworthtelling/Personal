using Codex.Domain.Design;
using Android.Views;
using Codex.Domain.Audio;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Infrastructure.Podcasts;
using Codex.Ui;

namespace Score;

/// <summary>
/// Discovery: this week's crate, and the Listening Genius control with its honest
/// data note.
///
/// The crate is one record at a time with the rest of the run behind it — not a list.
/// A list of fourteen recommendations is a wall to get through; a stack you can see
/// the depth of is something you flick, and it visibly shrinks as you do. Chevrons
/// step both ways, which is what makes a veto safe to make.
///
/// Two rules from the approved screens hold here. <b>Discover never touches your
/// queues</b> — Play auditions the record, leaving the assembled music queue exactly
/// where it was — and <b>Friday replaces Friday</b>: an unfinished crate does not
/// carry over, because the run has to keep room for what is new.
///
/// Search sits beside the crate and <b>replaces</b> it while there are results:
/// curated-and-finite and literal-and-open-ended are different shapes, and filtering
/// a crate of fourteen would be a search that could only ever lose.
///
/// It searches podcasts. Every other list in Score narrows music the owner already
/// owns, so finding a <i>show</i> they have never heard of is a capability nothing
/// else in the app has — and it is the half of Discover's search that does not wait
/// on a streaming provider, since the open podcast web has a keyless directory and
/// hands back the show's own feed.
///
/// A second, separate search finds a TIDAL <i>track</i> — kept apart from the podcast
/// field rather than merged into one box, since a show search subscribes to something
/// and a track search auditions it (the same one-record-at-a-time Play the crate
/// uses, so a search cannot cost you the queue you assembled either). Hidden, with a
/// quiet note in its place, until the owner has signed in under Settings — a search
/// field that always comes back empty would look broken rather than merely unset.
///
/// Generating a playlist from a prompt used to live here and now lives under
/// Playlists, where the result is kept rather than played.
/// </summary>
public sealed class DiscoverArea : ScoreArea
{
    private readonly MusicLibrary library;
    private readonly FridayPicksStore picksStore;
    private readonly IPodcastSearch search = new PodcastSearchClient(
        new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()));
    private readonly SubscriptionStore subscriptionStore;

    // The dealer's note. One line per record, composed in the owner's voice and kept
    // for as long as the crate is — a note is worth having, not worth paying for
    // twice because a chevron went forward and back.
    private readonly Codex.Ui.Voice.VoiceGreeting voice;
    private readonly Dictionary<string, string> notes = [];

    private DiscoveryCrate? crate;
    private string? crateKey;
    private string query = string.Empty;
    private string? searchStatus;
    private IReadOnlyList<PodcastSearchResult> results = [];

    private string trackQuery = string.Empty;
    private string? trackSearchStatus;
    private IReadOnlyList<Track> trackResults = [];

    public DiscoverArea(Activity host, MusicLibrary library) : base(host)
    {
        this.library = library;
        picksStore = new FridayPicksStore(host);
        subscriptionStore = new SubscriptionStore(host);
        voice = new Codex.Ui.Voice.VoiceGreeting(
            new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score");
        library.Changed += Invalidate;
    }

    public override BinderArea Tab => new("Discover", Resource.Drawable.area_discover, PaperTheme.PositiveMark, PaperTheme.PositiveFill);

    public override bool GoBack()
    {
        // A search replaced the crate, so leaving it is coming back up a level.
        if (results.Count > 0 || query.Length > 0)
        {
            ClearSearch();
            return true;
        }

        if (trackResults.Count > 0 || trackQuery.Length > 0)
        {
            ClearTrackSearch();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Discover carries the hourly voice line as well as the crate's own note (owner's
    /// call). The mockups had settled the seat on Music and Podcasts only, on the
    /// reasoning that one pick at a time means one line of calligraphy — so this is a
    /// deliberate change to that, not a drift from it.
    /// </summary>
    public override bool ShowsVoice => results.Count == 0 && trackResults.Count == 0;

    public override View BuildView()
    {
        var layout = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        Section(layout, BuildSearch());
        Section(layout, results.Count > 0 ? BuildResults() : BuildCrate());
        Section(layout, BuildTrackSearch());
        if (trackResults.Count > 0)
        {
            Section(layout, BuildTrackResults());
        }

        Section(layout, BuildGenius());

        var scroll = new ScrollView(Host);
        scroll.AddView(layout);
        return scroll;
    }

    // Two search fields, a crate and a disclosure stacked with no air between them is
    // what made this screen read as squeezed. §5's rhythm, applied once per section
    // rather than left to whatever each block happened to pad itself with.
    private void Section(LinearLayout layout, View view) =>
        layout.AddView(view, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = PaperTheme.Dp(Host, DesignTokens.Space.X6),
        });

    private View BuildSearch()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        var field = PaperUi.Field(Host, "Find a podcast");
        field.Text = query;
        field.TextChanged += (_, _) => query = field.Text ?? string.Empty;
        row.AddView(field, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        // A button rather than a live filter: this one leaves the phone, and running
        // it per keystroke would be a request a letter.
        row.AddView(PaperUi.QuietButton(Host, "Search", () => _ = SearchAsync()));
        if (results.Count > 0 || query.Length > 0)
        {
            row.AddView(PaperUi.QuietButton(Host, "Clear", ClearSearch));
        }

        column.AddView(row);
        if (!string.IsNullOrEmpty(searchStatus))
        {
            column.AddView(PaperUi.Secondary(Host, searchStatus));
        }

        return column;
    }

    private async Task SearchAsync()
    {
        var asked = query.Trim();
        if (asked.Length == 0)
        {
            return;
        }

        searchStatus = "Looking…"; // §6: loading is a sentence
        results = [];
        Invalidate();

        var found = await search.SearchAsync(asked, CancellationToken.None);
        results = found;
        searchStatus = found.Count == 0 ? $"No shows found for \u201c{asked}\u201d." : null;
        Invalidate();
    }

    private void ClearSearch()
    {
        query = string.Empty;
        searchStatus = null;
        results = [];
        Invalidate();
    }

    private View BuildResults()
    {
        var subscribed = subscriptionStore.Load().Select(feed => feed.FeedUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = results
            .Select(show => new PaperListRow(
                show.Title,
                subscribed.Contains(show.FeedUrl) ? "Following" : show.Author))
            .ToList();

        return PaperUi.List(Host, rows, position => Subscribe(results[position]));
    }

    // Subscribing is an inline state change — the row says Following and the shelf has
    // it on its next refresh. Undo-first, because a mis-tapped row is the only way
    // this can be wrong.
    private void Subscribe(PodcastSearchResult show)
    {
        var subscriptions = new SubscriptionList(subscriptionStore.Load());
        if (subscriptions.Ordered.Any(feed =>
            string.Equals(feed.FeedUrl, show.FeedUrl, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        subscriptions.Add(show.FeedUrl, show.Title, DateTimeOffset.UtcNow);
        subscriptionStore.Save(subscriptions.Export());
        Invalidate();

        DogEar.Show(Host, Overlay, $"Following {show.Title}.", onUndo: () =>
        {
            var current = new SubscriptionList(subscriptionStore.Load());
            current.Remove(show.FeedUrl);
            subscriptionStore.Save(current.Export());
            Invalidate();
        });
    }

    // Signed-out is the default state on every fresh install (Settings' Sign in to
    // TIDAL row), so this checks the same local store that gates
    // PlaybackService.BuildTidalSessionAsync rather than assuming a search is safe to
    // attempt.
    private View BuildTrackSearch()
    {
        if (new TidalAccountStore(Host).Load() is null)
        {
            return PaperUi.Secondary(Host, "Sign in to TIDAL in Settings to search the music catalogue.");
        }

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        var field = PaperUi.Field(Host, "Find a track");
        field.Text = trackQuery;
        field.TextChanged += (_, _) => trackQuery = field.Text ?? string.Empty;
        row.AddView(field, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        row.AddView(PaperUi.QuietButton(Host, "Search", () => _ = SearchTracksAsync()));
        if (trackResults.Count > 0 || trackQuery.Length > 0)
        {
            row.AddView(PaperUi.QuietButton(Host, "Clear", ClearTrackSearch));
        }

        column.AddView(row);
        if (!string.IsNullOrEmpty(trackSearchStatus))
        {
            column.AddView(PaperUi.Secondary(Host, trackSearchStatus));
        }

        return column;
    }

    private async Task SearchTracksAsync()
    {
        var asked = trackQuery.Trim();
        if (asked.Length == 0)
        {
            return;
        }

        trackSearchStatus = "Looking…"; // §6: loading is a sentence
        trackResults = [];
        Invalidate();

        var token = await new TidalAccessTokenProvider(Host).RefreshAsync(CancellationToken.None);
        if (token.Status != TidalTokenPollStatus.Success || token.AccessToken is not { Length: > 0 } accessToken)
        {
            trackSearchStatus = "Couldn't reach TIDAL. Try again in a moment.";
            Invalidate();
            return;
        }

        var found = await new TidalCatalogClient(new HttpClient())
            .SearchAsync(accessToken, asked, MusicLibrary.TidalCountryCode, CancellationToken.None);
        trackResults = found;
        trackSearchStatus = found.Count == 0 ? $"No tracks found for “{asked}”." : null;
        Invalidate();
    }

    private void ClearTrackSearch()
    {
        trackQuery = string.Empty;
        trackSearchStatus = null;
        trackResults = [];
        Invalidate();
    }

    private View BuildTrackResults()
    {
        var rows = trackResults
            .Select(track => new PaperListRow(track.DisplayTitle, $"{track.DisplayArtist} — {track.DisplayAlbum}"))
            .ToList();

        // Tap plays — the same audition-without-filing rule the crate's own Play
        // follows (see the class remarks), so trying a search result can never cost
        // the queue already assembled. Long-press is the one place a result actually
        // joins the library, matching MusicArea's own long-press-for-actions
        // convention (§9 bans a kebab menu) rather than inventing a second gesture.
        return PaperUi.List(Host, rows,
            position => library.Audition(trackResults[position]),
            position => ShowTrackSearchActions(trackResults[position]));
    }

    private void ShowTrackSearchActions(Track track)
    {
        var saved = library.IsTrackSaved(track.Id);
        var action = saved
            ? new CardAction("Remove from library", () => RemoveSaved(track))
            : new CardAction("Save to library", () => Save(track), Lead: true);
        ActionCard.Show(Host, Overlay, track.DisplayTitle, track.DisplayArtist, action);
    }

    // Inline state change (§7): the library reloads and the crate/results redraw —
    // no toast, no confirmation. Undo-first for the one irreversible-feeling half of
    // this (losing the save), the same as Subscribe's own undo bar above.
    private void Save(Track track)
    {
        library.SaveTrack(track);
        DogEar.Show(Host, Overlay, $"Saved {track.DisplayTitle}.", onUndo: () => library.RemoveSavedTrack(track.Id));
    }

    private void RemoveSaved(Track track)
    {
        library.RemoveSavedTrack(track.Id);
        DogEar.Show(Host, Overlay, $"Removed {track.DisplayTitle}.", onUndo: () => library.SaveTrack(track));
    }

    private View BuildCrate()
    {
        var picks = ResolvePicks();
        if (picks.Count == 0)
        {
            return PaperUi.EmptyState(Host, EmptyCrateMessage());
        }

        // Rebuilt only when the run itself changes. Rebuilding it on every render
        // would put the crate back to the first record every time the pane redrew,
        // and lose the strikes made since.
        var key = string.Join("|", picks.Select(pick => pick.Id));
        if (crate is null || crateKey != key)
        {
            crate = new DiscoveryCrate(picks, picksStore.Vetoed);
            crateKey = key;
        }

        var current = crate.Current!;
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Heading(Host, "This week"));
        column.AddView(new CrateView(Host, current, Behind(), crate.CurrentIsVetoed),
            new LinearLayout.LayoutParams(
                PaperTheme.Dp(Host, CrateView.SleeveDp), PaperTheme.Dp(Host, CrateView.SleeveDp)));

        var title = PaperUi.Title(Host, current.Title);
        if (crate.CurrentIsVetoed)
        {
            // The same editor's mark the sleeve wears: struck in the pen's ink-blue,
            // not dimmed — a vetoed record is passed on, not unavailable.
            title.PaintFlags |= Android.Graphics.PaintFlags.StrikeThruText;
            title.SetTextColor(PaperTheme.InkBlue);
        }

        column.AddView(title);
        column.AddView(PaperUi.Secondary(Host, current.Subtitle));
        column.AddView(BuildNote(current));
        column.AddView(BuildStepper());
        column.AddView(BuildActions());
        return column;
    }

    /// <summary>
    /// One line about this record, in the owner's hand.
    ///
    /// One record at a time is what makes this affordable and what makes it read: a
    /// list of fourteen recommendations each carrying a line of calligraphy would be
    /// a wall of it. Hidden until there is a line — no profile, no key, or a call that
    /// failed leaves the crate exactly as it was, which is the same bargain every
    /// other voiced surface in the suite takes.
    /// </summary>
    private View BuildNote(CratePick pick)
    {
        var known = notes.TryGetValue(pick.Id, out var line);
        var view = PaperUi.Voice(Host, line ?? string.Empty);

        // An empty entry means the call is in flight or came back with nothing; either
        // way the seat stays closed rather than opening on a blank line.
        view.Visibility = string.IsNullOrEmpty(line) ? ViewStates.Gone : ViewStates.Visible;
        if (!known)
        {
            _ = ComposeNoteAsync(pick);
        }

        return view;
    }

    private async Task ComposeNoteAsync(CratePick pick)
    {
        // Claimed before the call, so flicking back and forth while it is in flight
        // cannot start the same one twice.
        notes[pick.Id] = string.Empty;

        var line = await voice.ComposeAsync(
            "a one-line note about a record in this week's crate, as the person who picked it out",
            $"{pick.Title} by {pick.Subtitle}", CancellationToken.None);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        notes[pick.Id] = line;
        if (crate?.Current?.Id == pick.Id)
        {
            Invalidate();
        }
    }

    // The run still to come, nearest first — the crate draws the first few as stepped
    // sleeve edges in their own cloth.
    private IReadOnlyList<CratePick> Behind() =>
        [.. crate!.Picks.Skip(crate.Index + 1).Take(CrateGeometry.MaxEdges)];

    private View BuildStepper()
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.QuietButton(Host, "‹", () => Step(forward: false)));
        row.AddView(PaperUi.Secondary(Host, $"{crate!.Index + 1} of {crate.Picks.Count}"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(Host, "›", () => Step(forward: true)));
        return row;
    }

    private View BuildActions()
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };

        // Play is an audition. It does not build a queue and does not disturb the one
        // you have — Discover is for deciding, not for filing.
        row.AddView(PaperUi.SecondaryButton(Host, "Play", Audition));
        row.AddView(crate!.CurrentIsVetoed
            ? PaperUi.QuietButton(Host, "Bring it back", () => Strike(veto: false))
            : PaperUi.QuietButton(Host, "Not for me", () => Strike(veto: true)));
        return row;
    }

    private void Step(bool forward)
    {
        if (forward)
        {
            crate!.Next();
        }
        else
        {
            crate!.Previous();
        }

        Invalidate();
    }

    private void Strike(bool veto)
    {
        if (veto)
        {
            crate!.Veto();
        }
        else
        {
            crate!.Restore();
        }

        // Written through immediately, though it is only read by the weekly fold: a
        // strike that lived in memory would be undone by the app being killed, which
        // is not something the owner did.
        picksStore.Vetoed = crate.Vetoed;
        Invalidate();
    }

    private void Audition()
    {
        var id = crate!.Current!.Id;
        var track = library.Catalog.Songs().FirstOrDefault(song => song.Id == id);
        if (track is not null)
        {
            library.Audition(track);
        }
    }

    private IReadOnlyList<CratePick> ResolvePicks()
    {
        // Records the owner does not have, so there is nothing in the library to
        // resolve them against — they are shown as the recommender named them, and
        // Save to library is what goes and finds one.
        //
        // Filtered against the library again on the way out as well as on the way in,
        // because a crate is a week old by the end of its run and the owner may have
        // bought one of its records in the meantime; a crate offering to add something
        // now on the shelf is the failure this whole path was built wrong around.
        return [.. Discoveries.Unowned(picksStore.LoadPicks(), library.Catalog.Songs())
            .Select(pick => new CratePick(Discoveries.Key(pick), pick.Title, pick.Artist))];
    }

    // Never a dead end: say which of the three reasons it is.
    private string EmptyCrateMessage()
    {
        if (!MusicSettingsStore.ListeningGeniusEnabled)
        {
            return "Listening Genius is off, so there's no weekly crate.";
        }

        return library.Catalog.Songs().Count == 0
            ? "Nothing to pull from yet — scan your library first."
            : "The next crate is made on Friday.";
    }

    private View BuildGenius()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.SecondaryButton(Host, GeniusToggleLabel(), ToggleListeningGenius));
        // The disclosure stays — what leaves the phone is the owner's to know — but four
        // sentences of it under the crate was most of the screen. Kept to what is
        // load-bearing: what it learns from, what leaves, and what off means.
        column.AddView(PaperUi.Secondary(Host,
            "Learns from what you play, never what you rate, and sends a short summary to Claude to " +
            "describe it. Records you pass on join that summary weekly. Off clears what it gathered."));
        return column;
    }

    private string GeniusToggleLabel() =>
        MusicSettingsStore.ListeningGeniusEnabled ? "Listening Genius: on" : "Listening Genius: off";

    private void ToggleListeningGenius()
    {
        var enabled = !MusicSettingsStore.ListeningGeniusEnabled;
        MusicSettingsStore.ListeningGeniusEnabled = enabled;
        library.Listening.LearningEnabled = enabled;
        if (!enabled)
        {
            library.Listening.Clear(); // off means forget what was gathered, not just stop
        }

        Invalidate();
    }
}
