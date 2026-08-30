using Android.Content;
using Android.Views;
using Codex.Infrastructure.Music;
using Codex.Domain.Design;
using Codex.Domain.Music;
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
    private MusicView view = MusicView.Menu;
    private string query = string.Empty;

    public MusicArea(Activity host, MusicLibrary library) : base(host)
    {
        this.library = library;
        library.Changed += Invalidate;
    }

    public override BinderArea Tab => new("Music", Resource.Drawable.area_music, PaperTheme.ScoreMark, PaperTheme.ScoreFill);

    public override bool GoBack()
    {
        if (view == MusicView.Menu)
        {
            return false;
        }

        view = MusicView.Menu;
        query = string.Empty; // the filter belongs to the list you were in
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

        // The same setting Settings holds — both read and write VaultPassphraseStore,
        // so they cannot disagree. It is offered here too because this is the
        // after-a-reset path: entering it before signing in is what makes the library
        // come back on the first try instead of the next launch.
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
                return Browse(artists.Count,
                    [.. artists.Select(artist => (new PaperListRow(artist.Name, $"{artist.Tracks.Count} tracks"), artist.Name))],
                    name => library.PlayTracks(catalog.TracksByArtist(name), 0),
                    name => library.PlayShuffled(catalog.TracksByArtist(name)));

            case MusicView.Albums:
                var albums = catalog.Albums();
                return Browse(albums.Count,
                    [.. albums.Select(album => (new PaperListRow(album.Name, album.AlbumArtist, album.ArtRef), album))],
                    album => library.PlayTracks(catalog.TracksInAlbum(album.AlbumArtist, album.Name), 0),
                    album => library.PlayShuffled(catalog.TracksInAlbum(album.AlbumArtist, album.Name)),
                    asCovers: true);

            case MusicView.Songs:
                var songs = catalog.Songs();
                return Browse(songs.Count,
                    [.. songs.Select(track => (new PaperListRow(track.DisplayTitle, track.DisplayArtist, track.ArtRef), track))],
                    track => library.PlayTracks(songs, IndexIn(songs, track)),
                    library.PlaySmartMix);

            case MusicView.Genres:
                var genres = catalog.Genres();
                return Browse(genres.Count,
                    [.. genres.Select(genre => (new PaperListRow(genre), genre))],
                    genre => library.PlayTracks(catalog.TracksInGenre(genre), 0),
                    genre => library.PlayShuffled(catalog.TracksInGenre(genre)));

            case MusicView.Playlists:
                return PaperUi.EmptyState(Host, "No playlists yet.");

            default:
                var items = new[] { "Playlists", "Artists", "Albums", "Songs", "Genres" };
                return PaperUi.List(Host, [.. items.Select(item => new PaperListRow(item))], position =>
                {
                    view = Enum.Parse<MusicView>(items[position]);
                    query = string.Empty;
                    Invalidate();
                });
        }
    }

    /// <summary>
    /// One browse list, filtered. Every level of the library goes through here so the
    /// field appears by the same rule everywhere and the matching is the same
    /// matching — the diacritic-insensitive one, because nobody types the accent in
    /// Bartók to find a record they own.
    ///
    /// The filter is decided on the <b>unfiltered</b> count: a field that vanished
    /// once you had narrowed the list far enough would take the query with it.
    /// </summary>
    private View Browse<T>(
        int totalRows, IReadOnlyList<(PaperListRow Row, T Item)> entries, Action<T> onPlay, Action<T> onLongPress,
        bool asCovers = false)
    {
        if (totalRows == 0)
        {
            return Nothing();
        }

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var rows = new FrameLayout(Host);

        void Fill()
        {
            var matches = string.IsNullOrEmpty(query)
                ? entries
                : entries.Where(e => FilterMatch.MatchesAny(query, e.Row.Primary, e.Row.Secondary)).ToList();

            rows.RemoveAllViews();
            if (matches.Count == 0)
            {
                rows.AddView(PaperUi.EmptyState(Host, $"Nothing matches \u201c{query}\u201d."));
                return;
            }

            var listed = matches.Select(e => e.Row).ToList();
            void Play(int position) => onPlay(matches[position].Item);
            void LongPress(int position) => onLongPress(matches[position].Item);

            rows.AddView(asCovers
                ? new CoverGrid(Host, listed, Play, LongPress)
                : PaperUi.List(Host, listed, Play, LongPress));
        }

        if (FilterMatch.IsWarranted(totalRows))
        {
            var field = new FilterField(Host, "Filter", PaperTheme.ScoreMark, PaperTheme.ScoreFill);
            field.SetQuery(query);
            // Only the rows are rebuilt, never the pane: raising Changed on every
            // keystroke would replace the field itself and take the focus, the
            // keyboard and the cursor with it.
            field.QueryChanged += text =>
            {
                query = text;
                Fill();
            };
            column.AddView(field);
        }

        Fill();
        column.AddView(rows, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return column;
    }

    private View Nothing() => PaperUi.EmptyState(Host, "No music yet — scan your library.");

    // Songs play from their position in the whole list, not in the filtered view, so
    // starting a track under a filter still plays on into the rest of the library.
    private static int IndexIn(IReadOnlyList<Track> tracks, Track track)
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            if (ReferenceEquals(tracks[i], track))
            {
                return i;
            }
        }

        return 0;
    }

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
}
