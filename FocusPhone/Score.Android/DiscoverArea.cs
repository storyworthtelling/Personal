using Android.Views;
using Codex.Domain.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// Discovery: a quiet weekly "New for you" shelf and the Listening Genius control
/// with its honest data note.
///
/// Generating a playlist from a prompt used to live here and now lives under
/// Playlists, where the result is kept instead of played: Discover is about music the
/// owner does not own yet, and it is not allowed to touch the queues.
///
/// The shelf appears only when the weekly run has something (DESIGN.md: no surprise
/// work) and is dismissable — never a toast, a push, or a badge.
///
/// It is already its own area rather than a view inside Music, because the approved
/// P6 screens promote it to a binder tab of its own — and because it covers podcasts
/// too, which it cannot do from inside the music side.
/// </summary>
public sealed class DiscoverArea : ScoreArea
{
    private readonly MusicLibrary library;
    private readonly FridayPicksStore picksStore;

    public DiscoverArea(Activity host, MusicLibrary library) : base(host)
    {
        this.library = library;
        picksStore = new FridayPicksStore(host);
        library.Changed += Invalidate;
    }

    public override BinderArea Tab => new("Discover", Resource.Drawable.area_discover, PaperTheme.ScoreMark);

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
