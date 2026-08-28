using Android.Views;
using Codex.Domain.Audio;
using Codex.Domain.Music;
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
/// hands back the show's own feed. Catalogue search for music arrives with streaming.
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

    public DiscoverArea(Activity host, MusicLibrary library) : base(host)
    {
        this.library = library;
        picksStore = new FridayPicksStore(host);
        subscriptionStore = new SubscriptionStore(host);
        voice = new Codex.Ui.Voice.VoiceGreeting(
            new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score");
        library.Changed += Invalidate;
    }

    public override BinderArea Tab => new("Discover", Resource.Drawable.area_discover, PaperTheme.ScoreMark);

    public override bool GoBack()
    {
        // A search replaced the crate, so leaving it is coming back up a level.
        if (results.Count > 0 || query.Length > 0)
        {
            ClearSearch();
            return true;
        }

        return false;
    }

    public override View BuildView()
    {
        var layout = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        layout.AddView(BuildSearch());
        layout.AddView(results.Count > 0 ? BuildResults() : BuildCrate());
        layout.AddView(BuildGenius());

        var scroll = new ScrollView(Host);
        scroll.AddView(layout);
        return scroll;
    }

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

        UndoBar.Show(Host, Overlay, $"Following {show.Title}.", onUndo: () =>
        {
            var current = new SubscriptionList(subscriptionStore.Load());
            current.Remove(show.FeedUrl);
            subscriptionStore.Save(current.Export());
            Invalidate();
        });
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
        var selection = picksStore.LoadSelection();
        if (selection.TrackIds.Count == 0)
        {
            return [];
        }

        // Re-resolved against the live catalog every time, so a track that has left
        // the library leaves the crate with it rather than sitting there unplayable.
        var playlist = PlaylistPlan.Resolve(
            selection.Title, selection.TrackIds, library.Catalog.Songs(), FridayStack.Largest);
        return [.. playlist.Tracks.Select(track =>
            new CratePick(track.Id, track.DisplayTitle, track.DisplayArtist, track.ArtRef))];
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
        column.AddView(PaperUi.Secondary(Host,
            "Listening Genius learns your taste on this phone from what you play — nothing you rate — and " +
            "sends a short summary to Claude with your key to describe it. Records you pass on here are the " +
            "one exception: they're written to the summary once a week, with the crate they came from. Turn " +
            "it off any time; that also clears what's been gathered."));
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
