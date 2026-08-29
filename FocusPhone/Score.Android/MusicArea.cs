using Android.Content;
using Android.Views;
using Codex.Infrastructure.Music;
using Codex.Domain.Audio;
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
    private PlaylistEditor? editor;
    private string? generating;
    private readonly Selection selection = new();
    private readonly DownloadStore downloads;
    private bool naming;

    public MusicArea(Activity host, MusicLibrary library) : base(host)
    {
        this.library = library;
        downloads = new DownloadStore(host);
        library.Changed += Invalidate;
    }

    public override BinderArea Tab => new("Music", Resource.Drawable.area_music, PaperTheme.ScoreMark);

    public override bool GoBack()
    {
        // Picking is a mode, not a level: back leaves the mode and keeps the list.
        if (selection.IsActive)
        {
            EndSelection();
            return true;
        }

        // The editor is a level below the list it was opened from, so back closes it
        // and leaves you where you were rather than at the top menu.
        if (editor is not null)
        {
            editor = null;
            Invalidate();
            return true;
        }

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
        head.AddView(PaperUi.QuietButton(Host, "EQ", () => Host.StartActivity(new Intent(Host, typeof(EqualizerActivity)))));
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
                    album => ShowAlbumActions(album.Name, catalog.TracksInAlbum(album.AlbumArtist, album.Name)),
                    asCovers: true);

            case MusicView.Songs:
                var songs = catalog.Songs();
                return Browse(songs.Count,
                    [.. songs.Select(track => (new PaperListRow(track.DisplayTitle, track.DisplayArtist, track.ArtRef), track))],
                    track => library.PlayTracks(songs, IndexIn(songs, track)),
                    ShowTrackActions,
                    idOf: track => track.Id);

            case MusicView.Genres:
                var genres = catalog.Genres();
                return Browse(genres.Count,
                    [.. genres.Select(genre => (new PaperListRow(genre), genre))],
                    genre => library.PlayTracks(catalog.TracksInGenre(genre), 0),
                    genre => library.PlayShuffled(catalog.TracksInGenre(genre)));

            case MusicView.Playlists:
                return BuildPlaylists();

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
    /// The playlists level, and the editor that sits below it.
    ///
    /// The list carries its own primary action rather than hiding "new" behind a
    /// long-press, because on an empty list there is nothing to long-press — the
    /// empty state has to be the way in or the feature is invisible.
    /// </summary>
    private View BuildPlaylists()
    {
        if (editor is not null)
        {
            return editor.Build();
        }

        var songs = library.Catalog.Songs();
        var playlists = library.Playlists();

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.SecondaryButton(Host, "New playlist from rules", () => Edit(null)));

        // The other way to make one. It lives here rather than in Discover because a
        // generated playlist is a thing you keep, and Discover is about music you do
        // not own yet — and because generating from Discover would have had to touch
        // the queue to show you anything, which Discover is not allowed to do.
        var prompt = PaperUi.Field(Host, "Or describe one: a mood, a genre, an idea.");
        column.AddView(prompt);
        column.AddView(PaperUi.SecondaryButton(Host, "Generate", () => _ = GenerateAsync(prompt.Text)));
        if (!string.IsNullOrEmpty(generating))
        {
            column.AddView(PaperUi.Secondary(Host, generating));
        }

        var body = playlists.Count == 0
            ? PaperUi.EmptyState(Host, "No playlists yet.")
            : Browse(playlists.Count,
                [.. playlists.Select(list => (new PaperListRow(list.Name, list.Describe(songs)), list))],
                list => library.PlayTracks(list.Tracks(songs), 0),
                ShowPlaylistActions);

        column.AddView(body, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return column;
    }

    // Loading is a status sentence, never a spinner (DESIGN.md §6), and the result is
    // saved rather than played: the list is where you go to hear it, the same as every
    // other playlist.
    private async Task GenerateAsync(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return;
        }

        if (library.Listening.Taste is null)
        {
            generating = "Generating needs the shared Claude key — set it in Settings.";
            Invalidate();
            return;
        }

        generating = "Making a playlist\u2026";
        Invalidate();

        var made = await library.Listening.Taste.MakePlaylistAsync(
            library.Catalog.Songs(), new PlaylistRequest(seed.Trim()), CancellationToken.None);
        if (made.IsEmpty)
        {
            generating = "Couldn't make a playlist from that. Try another idea.";
            Invalidate();
            return;
        }

        var saved = Playlist.Generated(
            Guid.NewGuid().ToString("n"), made.Title, [.. made.Tracks.Select(track => track.Id)]);
        library.SavePlaylists([.. library.Playlists(), saved]);
        generating = null;
        Invalidate();
    }

    // Long-press an album. Shuffle was the whole of it before, which meant the one
    // §9 affordance the app has was spent on a shortcut.
    private void ShowAlbumActions(string name, IReadOnlyList<Track> tracks)
    {
        ActionCard.Show(Host, Overlay, name, $"{tracks.Count} tracks",
            new CardAction("Shuffle", () => library.PlayShuffled(tracks), Lead: true),
            new CardAction("Download", () => Download(name, tracks)));
    }

    private void ShowTrackActions(Track track)
    {
        ActionCard.Show(Host, Overlay, track.DisplayTitle, track.DisplayArtist,
            new CardAction("Start a mix from here", () => library.PlaySmartMix(track), Lead: true),
            new CardAction("Download", () => Download(track.DisplayTitle, [track])));
    }

    // Anything at any level: one track or a whole album, against the same allowance.
    //
    // A hidden-track split segment's MediaId is its shared source file, not its own
    // per-queue Id — DownloadsActivity.MediaUri reconstructs the same drive:// uri from
    // MediaId later to add/remove the download, so the two have to name the same file
    // or that reconstruction targets a file that was never actually downloaded.
    private void Download(string what, IReadOnlyList<Track> tracks) =>
        DownloadRequests.Ask(Host, Overlay, downloads, what,
            [.. tracks.Select(track => (
                new DownloadedItem(
                    track.ResolvedSourceId, track.DisplayTitle, DownloadKind.Music, track.Duration, DateTimeOffset.UtcNow),
                $"drive://{track.ResolvedSourceId}"))]);

    // §7's action card, not a kebab and not a confirm: the entries do the thing, and
    // delete is made safe by the undo bar rather than by asking first.
    private void ShowPlaylistActions(Playlist playlist)
    {
        var songs = library.Catalog.Songs();
        var actions = new List<CardAction>
        {
            new("Shuffle", () => library.PlayShuffled(playlist.Tracks(songs)), Lead: true),
        };

        // Only a rule playlist has rules to edit; a hand-made one is a list of tracks
        // and its editor is the list itself.
        if (playlist.Kind == PlaylistKind.Rules)
        {
            actions.Add(new CardAction("Edit rules", () => Edit(playlist)));
        }

        actions.Add(new CardAction("Delete", () => Delete(playlist), Destructive: true));
        ActionCard.Show(Host, Overlay, playlist.Name, playlist.Describe(songs), [.. actions]);
    }

    private void Edit(Playlist? playlist)
    {
        editor = new PlaylistEditor(Host, library.Catalog.Songs(), playlist, Saved, Closed);
        Invalidate();

        void Saved(Playlist saved)
        {
            var kept = library.Playlists().Where(list => list.Id != saved.Id).ToList();
            kept.Add(saved);
            library.SavePlaylists(kept);
            Closed();
        }

        void Closed()
        {
            editor = null;
            Invalidate();
        }
    }

    private void Delete(Playlist playlist)
    {
        var before = library.Playlists();
        library.SavePlaylists([.. before.Where(list => list.Id != playlist.Id)]);
        Invalidate();
        UndoBar.Show(Host, Overlay, $"Deleted {playlist.Name}.", onUndo: () =>
        {
            library.SavePlaylists(before);
            Invalidate();
        });
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
        bool asCovers = false, Func<T, string>? idOf = null)
    {
        if (totalRows == 0)
        {
            return Nothing();
        }

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var rows = new FrameLayout(Host);
        var picking = idOf is not null && selection.IsActive;

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

            if (picking)
            {
                // A tap picks; nothing plays. The whole list is one control while the
                // mode is on, which is why the mini-bar gives up its place to the
                // action bar rather than sitting under a list that no longer plays.
                var picked = matches
                    .Select(e => e.Row with { Picked = selection.Contains(idOf!(e.Item)) })
                    .ToList();
                rows.AddView(PaperUi.List(Host, picked, position =>
                {
                    selection.Toggle(idOf!(matches[position].Item));
                    Invalidate();
                }, picking: true));
                return;
            }

            var listed = matches.Select(e => e.Row).ToList();
            void Play(int position) => onPlay(matches[position].Item);
            void LongPress(int position) => onLongPress(matches[position].Item);

            rows.AddView(asCovers
                ? new CoverGrid(Host, listed, Play, LongPress)
                : PaperUi.List(Host, listed, Play, LongPress));
        }

        if (idOf is not null)
        {
            column.AddView(BuildPickerControls(entries.Select(e => idOf(e.Item)).ToList()));
        }

        if (FilterMatch.IsWarranted(totalRows))
        {
            var field = new FilterField(Host, "Filter", PaperTheme.ScoreMark);
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

    /// <summary>
    /// The way into selection mode, and what the mode offers above the list: the
    /// running count, an All that flips to None once everything showing is picked,
    /// and the way to keep the picks as a playlist.
    ///
    /// It sits above the rows rather than in the action bar because the bar is
    /// already carrying the three things you do with a selection, and a five-button
    /// bar is a menu.
    /// </summary>
    private View BuildPickerControls(IReadOnlyList<string> visible)
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        if (!selection.IsActive)
        {
            row.AddView(PaperUi.QuietButton(Host, "Select", () =>
            {
                selection.Begin();
                Invalidate();
            }));
            return row;
        }

        if (naming)
        {
            // Naming happens in place — no dialog, and no losing the picks to it.
            var field = PaperUi.Field(Host, "Playlist name");
            row.AddView(field, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
            row.AddView(PaperUi.QuietButton(Host, "Save", () => SaveAsPlaylist(field.Text)));
            return row;
        }

        row.AddView(PaperUi.Secondary(Host, $"{selection.Count} picked"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(Host, selection.AllSelected(visible) ? "None" : "All", () =>
        {
            selection.ToggleAll(visible);
            Invalidate();
        }));

        if (selection.Count > 0)
        {
            row.AddView(PaperUi.QuietButton(Host, "Save as playlist", () =>
            {
                naming = true;
                Invalidate();
            }));
        }

        return row;
    }

    /// <summary>
    /// The action bar the picks act through. It replaces the now-playing bar rather
    /// than stacking under it: while you are picking, the list is not playing
    /// anything, so a bar about what is playing is the wrong thing in the way.
    /// </summary>
    public override View? BuildBottomBar() => selection.IsActive
        ? PaperUi.BottomBar(Host,
            PaperUi.QuietButton(Host, "Cancel", EndSelection),
            PaperUi.SecondaryButton(Host, "Play next", () => Queue(next: true)),
            PaperUi.PrimaryButton(Host, $"Add {selection.Count}", () => Queue(next: false)))
        : null;

    public override bool ShowsNowPlayingBar => !selection.IsActive;

    // The root only. A drill-down opens straight into content — otherwise every level
    // of the library gains the same line of calligraphy.
    public override bool ShowsVoice => view == MusicView.Menu && editor is null && !selection.IsActive;

    private void Queue(bool next)
    {
        var tracks = Picked();
        if (tracks.Count == 0)
        {
            return;
        }

        if (next)
        {
            library.PlayNext(tracks);
        }
        else
        {
            library.Enqueue(tracks);
        }

        EndSelection();
    }

    // Manual playlists are the one kind the owner builds by hand, so the pick order
    // is the playlist order — Selection already keeps it.
    private void SaveAsPlaylist(string? name)
    {
        var tracks = Picked();
        if (tracks.Count == 0)
        {
            EndSelection();
            return;
        }

        var title = string.IsNullOrWhiteSpace(name) ? "New playlist" : name.Trim();
        library.SavePlaylists([.. library.Playlists(), Playlist.Manual(
            Guid.NewGuid().ToString("n"), title, [.. tracks.Select(track => track.Id)])]);
        EndSelection();
    }

    private IReadOnlyList<Track> Picked()
    {
        var byId = library.Catalog.Songs().ToDictionary(track => track.Id);
        return [.. selection.Items.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    private void EndSelection()
    {
        selection.Cancel();
        naming = false;
        Invalidate();
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
