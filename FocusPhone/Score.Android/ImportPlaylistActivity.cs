using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// Where a shared playlist lands: share a Spotify playlist link into Score and get a
/// Score playlist of the songs you already have.
///
/// The screen exists because the interesting part of an import is what it
/// <b>could not</b> find. A partial match is the normal case — a setlist names
/// fifteen songs and the library holds eleven — so the missing four are listed by
/// name rather than quietly dropped, and the count is stated before anything is
/// saved. Nothing is written until Save.
///
/// It is a screen and not a dialog (§7 bans those), and the sentence under the
/// header is the loading state (§6 bans spinners).
/// </summary>
// Exported because a share target is reached from another app by definition. What
// it accepts is one piece of text, and everything it does with that text is reading:
// a link that is not a playlist link is refused, and nothing is written to the
// library until the owner presses Save.
[Activity(Label = "Import playlist", Theme = "@style/Theme.FocusPhone.Paper", Exported = true)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
public class ImportPlaylistActivity : Activity
{
    private readonly ISharedPlaylistSource source = new SpotifyPlaylistClient(new HttpClient());

    private LinearLayout body = null!;
    private EditText name = null!;
    private string status = string.Empty;
    private PlaylistImportResult? result;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);
        SetContentView(BuildLayout());
        Render();
        _ = ImportAsync(Intent?.GetStringExtra(Intent.ExtraText));
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        name = PaperUi.Field(this, "Playlist name");
        name.Visibility = ViewStates.Gone;
        page.AddView(name);

        var scroll = new ScrollView(this);
        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(body);
        page.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(BinderFrame.Wrap(
            this, "Import playlist", ScoreShellAreas.All[ScoreShellAreas.Music].Hue, page,
            ScoreShellAreas.All, ScoreShellAreas.Music, SwitchArea),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.QuietButton(this, "Close", Finish),
            PaperUi.PrimaryButton(this, "Save playlist", Save)));
        return root;
    }

    // Tapping the tab this screen was opened from just returns to it; any other tab asks
    // ScoreActivity to land there once this screen finishes (see ScoreActivity.PendingArea).
    // Reachable only as an external share target (ActionSend) — if Score isn't already
    // running, ScoreActivity starts fresh at that area rather than nothing happening.
    private void SwitchArea(int index)
    {
        ScoreActivity.PendingArea = index;
        var intent = new Intent(this, typeof(ScoreActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    private async Task ImportAsync(string? shared)
    {
        var id = SharedPlaylistLink.Parse(shared);
        if (id is null)
        {
            Say("That share wasn't a Spotify playlist link.");
            return;
        }

        Say("Reading the playlist…");

        SharedPlaylist playlist;
        try
        {
            playlist = await source.FetchAsync(id, CancellationToken.None);
        }
        catch (HttpRequestException e)
        {
            // The page is read without an account and without anyone's blessing, so
            // it failing is an ordinary outcome and says so in a sentence.
            Say($"Couldn't reach Spotify: {e.Message}");
            return;
        }

        if (playlist.IsEmpty)
        {
            Say("Couldn't read that playlist. It may be private, or Spotify's page may have changed.");
            return;
        }

        using var database = MusicDatabase.Open(this);
        var catalog = database.Catalog.LoadAll();

        result = PlaylistImport.Resolve(playlist.Tracks, catalog);
        name.Text = playlist.Title;
        name.Visibility = ViewStates.Visible;
        Say($"{result.Matched.Count} of {playlist.Tracks.Count} are in your library.");
    }

    private void Say(string message)
    {
        status = message;
        RunOnUiThread(Render);
    }

    private void Render()
    {
        body.RemoveAllViews();
        body.AddView(PaperUi.Secondary(this, status));

        if (result is null)
        {
            return;
        }

        foreach (var track in result.Matched)
        {
            body.AddView(PaperUi.Body(this, $"{track.DisplayTitle} — {track.DisplayArtist}"));
        }

        if (result.Missing.Count == 0)
        {
            return;
        }

        // The point of the screen. Named, so the songs that are missing can be found
        // some other way rather than silently not being there on Sunday.
        body.AddView(PaperUi.Heading(this, "Not in your library"));
        foreach (var track in result.Missing)
        {
            body.AddView(PaperUi.Secondary(this, $"{track.Title} — {track.Artist}"));
        }
    }

    private void Save()
    {
        if (result is null || result.Matched.Count == 0)
        {
            Say("Nothing to save.");
            return;
        }

        var store = new PlaylistStore(this);
        var title = string.IsNullOrWhiteSpace(name.Text) ? "Imported playlist" : name.Text!.Trim();
        store.Save([.. store.Load(), Playlist.Manual(
            Guid.NewGuid().ToString("n"), title, [.. result.Matched.Select(track => track.Id)])]);
        Finish();
    }
}
