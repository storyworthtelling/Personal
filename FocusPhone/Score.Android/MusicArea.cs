using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Audio;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui;
using Codex.Ui.Voice;
using Score.Google;

namespace Score;

/// <summary>
/// The music area: sign-in, the folder picker, and browsing the scanned catalog.
///
/// Three states before there is anything to browse — no Google account, no chosen
/// folder, no scanned files — and each is a different screen rather than an error on
/// the same one, because each has a different next step.
/// </summary>
public sealed class MusicArea : ScoreArea
{
    private readonly MusicLibrary library;
    private readonly DiscoverArea discover;
    private MusicView view = MusicView.Menu;

    public MusicArea(Activity host, MusicLibrary library, DiscoverArea discover) : base(host)
    {
        this.library = library;
        this.discover = discover;
        library.Changed += Invalidate;
    }

    public override bool GoBack()
    {
        if (view == MusicView.Menu)
        {
            return false;
        }

        view = MusicView.Menu;
        Invalidate();
        return true;
    }

    public override View BuildView()
    {
        if (!library.IsSignedIn)
        {
            return BuildDriveSetup();
        }

        return library.HasFolder ? BuildBrowse() : BuildFolderPicker();
    }

    private View BuildDriveSetup()
    {
        var layout = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        layout.AddView(PaperUi.Body(Host, "Stream your own music from Google Drive."));
        layout.AddView(PaperUi.Secondary(Host,
            "First register this app as an Android OAuth client (Drive API enabled), then sign in:\n\n" +
            $"Package: {AppSigningInfo.PackageName}\nSHA-1: {AppSigningInfo.Sha1Fingerprint()}"));

        // The secrets-vault passphrase (optional): set the same one as your other apps to back up and restore
        // your keys from Drive, encrypted. Persisted when you sign in.
        // Shown in the clear (not a password field) so it can be read back and copied — it's the one secret
        // that isn't kept in the vault, so being able to see it is what lets the owner set the same one everywhere.
        var passphrase = PaperUi.MonoField(Host, "Secrets vault passphrase (optional)");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        layout.AddView(passphrase);

        layout.AddView(PaperUi.PrimaryButton(Host, "Sign in with Google", () =>
        {
            VaultPassphraseStore.Passphrase = passphrase.Text ?? string.Empty;
            _ = library.SignInAsync();
        }));
        return WrapScroll(layout);
    }

    private View BuildFolderPicker()
    {
        if (library.Folders is null)
        {
            _ = library.LoadFoldersAsync();
            return PaperUi.EmptyState(Host, "Finding your Drive folders…");
        }

        var folders = library.Folders;
        var container = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        container.AddView(PaperUi.Body(Host, "Pick the folder your music lives in."));
        container.AddView(PaperUi.SecondaryButton(Host, "Use all of My Drive", () => library.ChooseFolder("root")));

        var rows = folders.Select(folder => new PaperListRow(folder.Name)).ToList();
        container.AddView(
            PaperUi.List(Host, rows, position => library.ChooseFolder(folders[position].Id)),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return container;
    }

    private View BuildBrowse()
    {
        var container = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        var head = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.CenterVertical);
        head.AddView(PaperUi.Heading(Host, view.ToString()),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        head.AddView(PaperUi.QuietButton(Host, "Shuffle", () => library.PlayShuffled(library.Catalog.Songs())));
        head.AddView(PaperUi.QuietButton(Host, "Sound", () => Host.StartActivity(new Intent(Host, typeof(EqualizerActivity)))));
        head.AddView(PaperUi.QuietButton(Host, "Rescan", () => _ = library.ScanAsync()));
        container.AddView(head);

        if (!string.IsNullOrEmpty(library.Status))
        {
            container.AddView(PaperUi.Secondary(Host, library.Status));
        }

        // An empty catalog (and no scan running) is the cold-start dead-end: show the
        // "how to add music" guide instead of a bare "no music" line under every browse view.
        var catalogEmpty = library.Catalog.Songs().Count == 0;
        var body = catalogEmpty && string.IsNullOrEmpty(library.Status) ? BuildEmptyGuide() : BuildBody();
        container.AddView(body, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return container;
    }

    // The cold-start guide: how to get music into the Drive folder. Tone stays quiet-neutral
    // (DESIGN.md §11) and the supported extensions come straight from AudioFileTypes so the
    // guide can't drift from what the scanner actually accepts.
    private View BuildEmptyGuide()
    {
        var layout = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        layout.AddView(PaperUi.Body(Host, "No music in this folder yet. Here's how to add some."));
        layout.AddView(PaperUi.Secondary(Host,
            "Put audio files in your Drive music folder — rip your CDs to tagged FLAC or MP3, copy the " +
            "files iTunes already made, or buy real downloads (Bandcamp, Qobuz, Amazon Music). Streaming " +
            "apps like YouTube Music can't be saved here."));
        layout.AddView(PaperUi.Secondary(Host,
            "What organizes the library is each file's embedded tags — title, artist, album artist, album, " +
            "track number, genre. Folder names and structure don't matter; the tags do."));
        layout.AddView(PaperUi.Secondary(Host,
            "Playable file types: " + string.Join(", ", AudioFileTypes.SupportedExtensions) + "."));
        layout.AddView(PaperUi.PrimaryButton(Host, "Scan again", () => _ = library.ScanAsync()));
        return WrapScroll(layout);
    }

    private View BuildBody()
    {
        var catalog = library.Catalog;
        switch (view)
        {
            case MusicView.Artists:
                var artists = catalog.Artists();
                return artists.Count == 0
                    ? Nothing()
                    : PaperUi.List(Host,
                        [.. artists.Select(artist => new PaperListRow(artist.Name, $"{artist.Tracks.Count} tracks"))],
                        position => library.PlayTracks(catalog.TracksByArtist(artists[position].Name), 0),
                        position => library.PlayShuffled(catalog.TracksByArtist(artists[position].Name)));

            case MusicView.Albums:
                var albums = catalog.Albums();
                return albums.Count == 0
                    ? Nothing()
                    : PaperUi.List(Host,
                        [.. albums.Select(album => new PaperListRow(album.Name, album.AlbumArtist))],
                        position => library.PlayTracks(catalog.TracksInAlbum(albums[position].AlbumArtist, albums[position].Name), 0),
                        position => library.PlayShuffled(catalog.TracksInAlbum(albums[position].AlbumArtist, albums[position].Name)));

            case MusicView.Songs:
                var songs = catalog.Songs();
                return songs.Count == 0
                    ? Nothing()
                    : PaperUi.List(Host,
                        [.. songs.Select(track => new PaperListRow(track.DisplayTitle, track.DisplayArtist))],
                        position => library.PlayTracks(songs, position),
                        position => library.PlaySmartMix(songs[position]));

            case MusicView.Genres:
                var genres = catalog.Genres();
                return genres.Count == 0
                    ? Nothing()
                    : PaperUi.List(Host, [.. genres.Select(genre => new PaperListRow(genre))],
                        position => library.PlayTracks(catalog.TracksInGenre(genres[position]), 0),
                        position => library.PlayShuffled(catalog.TracksInGenre(genres[position])));

            case MusicView.Playlists:
                return PaperUi.EmptyState(Host, "No playlists yet.");

            case MusicView.Discovery:
                return discover.BuildView();

            default:
                var items = new[] { "Discovery", "Playlists", "Artists", "Albums", "Songs", "Genres" };
                return PaperUi.List(Host, [.. items.Select(item => new PaperListRow(item))], position =>
                {
                    view = Enum.Parse<MusicView>(items[position]);
                    Invalidate();
                });
        }
    }

    private View Nothing() => PaperUi.EmptyState(Host, "No music yet — scan your library.");

    private View WrapScroll(View inner)
    {
        var scroll = new ScrollView(Host);
        scroll.AddView(inner);
        return scroll;
    }
}

/// <summary>The music side's browse levels — the iPod top menu and its lists.</summary>
internal enum MusicView
{
    Menu,
    Playlists,
    Artists,
    Albums,
    Songs,
    Genres,
    Discovery,
}
