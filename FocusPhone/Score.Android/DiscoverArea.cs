using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// Discovery: a prompted playlist from the owner's own library, a quiet weekly
/// "New for you" shelf, and the Listening Genius control with its honest data note.
///
/// Playlists generate only when asked (DESIGN.md: no surprise work); loading is a
/// sentence, and the shelf is dismissable — never a toast, a push, or a badge.
///
/// It is already its own area rather than a view inside Music, because the approved
/// P6 screens promote it to a binder tab of its own — and because it covers podcasts
/// too, which it cannot do from inside the music side.
/// </summary>
public sealed class DiscoverArea : ScoreArea
{
    private readonly MusicLibrary library;
    private readonly FridayPicksStore picksStore;
    private string? status;

    public DiscoverArea(Activity host, MusicLibrary library) : base(host)
    {
        this.library = library;
        picksStore = new FridayPicksStore(host);
        library.Changed += Invalidate;
    }

    public override View BuildView()
    {
        var layout = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        var picks = ResolveFridayPicks();
        if (picks is not null && !picksStore.Dismissed)
        {
            layout.AddView(PaperUi.Heading(Host, "New for you"));
            layout.AddView(PaperUi.Secondary(Host, picks.Title));
            layout.AddView(PaperUi.SecondaryButton(Host, "Play these", () => library.PlayTracks(picks.Tracks, 0)));
            layout.AddView(PaperUi.QuietButton(Host, "Dismiss", () =>
            {
                picksStore.Dismissed = true;
                Invalidate();
            }));
        }

        layout.AddView(PaperUi.Body(Host, "Make a playlist from your library."));
        var input = PaperUi.Field(Host, "What do you want to hear? A mood, a genre, an idea.");
        layout.AddView(input);
        layout.AddView(PaperUi.PrimaryButton(Host, "Make a playlist", () => _ = MakePlaylistAsync(input.Text)));

        if (!string.IsNullOrEmpty(status))
        {
            layout.AddView(PaperUi.Secondary(Host, status));
        }

        layout.AddView(PaperUi.SecondaryButton(Host, GeniusToggleLabel(), ToggleListeningGenius));
        layout.AddView(PaperUi.Secondary(Host,
            "Listening Genius learns your taste on this phone from what you play — nothing you rate — and " +
            "sends a short summary to Claude with your key to describe it. Turn it off any time; that also " +
            "clears what's been gathered."));

        var scroll = new ScrollView(Host);
        scroll.AddView(layout);
        return scroll;
    }

    private DiscoveryPlaylist? ResolveFridayPicks()
    {
        var selection = picksStore.LoadSelection();
        if (selection.TrackIds.Count == 0)
        {
            return null;
        }

        var playlist = PlaylistPlan.Resolve(selection.Title, selection.TrackIds, library.Catalog.Songs(), 25);
        return playlist.IsEmpty ? null : playlist;
    }

    private async Task MakePlaylistAsync(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed) || library.Listening.Taste is null)
        {
            return;
        }

        status = "Building a playlist…";
        Invalidate();

        var playlist = await library.Listening.Taste.MakePlaylistAsync(
            library.Catalog.Songs(), new PlaylistRequest(seed.Trim()), CancellationToken.None);
        if (playlist.IsEmpty)
        {
            status = "Couldn't build a playlist from that. Try another idea.";
            Invalidate();
            return;
        }

        status = null;
        library.PlayTracks(playlist.Tracks, 0);
        Invalidate();
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
