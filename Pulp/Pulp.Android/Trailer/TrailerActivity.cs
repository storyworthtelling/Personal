using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Web;
using Codex.Ui;
using Pulp.Domain;
using Pulp.Domain.Player;
using Pulp.Domain.Shell;
using Pulp.Shell;

namespace Pulp.Trailer;

/// <summary>
/// The one thing Pulp plays: a YouTube video in the embed-only player — a trailer (D34), opened from the quiet
/// "Trailer" button by a title's poster (mockup 02, D218), or a game's highlights (D71, D313), opened from a
/// game's quiet "Highlights" line. The two differ only in their words (<see cref="PlayerCopy"/>). Everything else
/// Pulp shows is handed off to the service's own app; this is the single exception, and <c>PlaysNothingTests</c>
/// (G6) holds that this class is the only place in Pulp that reaches the player. The file keeps its name so the
/// guard and the manifest entry do not move.
///
/// <para>The player is the shared web view in its <see cref="WebPosture.Player"/> posture: script on,
/// nothing navigates, and nothing plays until the owner taps play inside the embed — a trailer never
/// autoplays (D77). The video id arrives in the intent, which makes it input, so it is checked before
/// anything is built from it. The page wears the Paper frame: the title as the Serif header over the
/// identity's rule, the player under it, and a status sentence saying what to do. Back leaves
/// fullscreen first if the player is in it, then closes the screen.</para>
/// </summary>
[Activity(
    Label = "Pulp",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper",
    // Rotating into landscape for a fullscreen trailer must not restart the screen (and the video).
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout)]
public class TrailerActivity : Activity
{
    public const string ExtraVideoId = "videoId";
    public const string ExtraTitle = "title";
    public const string ExtraKind = "kind";

    private WebView? web;
    private PaperWebView.PlayerChrome? chrome;

    /// <summary>
    /// The intent that opens a trailer. Refuses an id that is not a YouTube video id here, at the
    /// caller, as well as on arrival — a bad id is a bug in whoever supplied it, and saying so at the
    /// call site names the caller.
    /// </summary>
    public static Intent For(Context context, string videoId, string title) =>
        For(context, videoId, title, PlayerKind.Trailer);

    /// <summary>The intent that opens a game's highlights; the header is the generic label the caller built, never a video's title.</summary>
    public static Intent ForHighlights(Context context, string videoId, string label) =>
        For(context, videoId, label, PlayerKind.Highlights);

    private static Intent For(Context context, string videoId, string title, PlayerKind kind)
    {
        if (!PlayerEmbed.IsVideoId(videoId))
        {
            throw new ArgumentException($"\"{videoId}\" is not a YouTube video id; no video can be opened for it.", nameof(videoId));
        }

        var intent = new Intent(context, typeof(TrailerActivity));
        intent.PutExtra(ExtraVideoId, videoId);
        intent.PutExtra(ExtraTitle, title);
        intent.PutExtra(ExtraKind, kind.ToString());
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        var videoId = Intent?.GetStringExtra(ExtraVideoId);
        if (!PlayerEmbed.IsVideoId(videoId))
        {
            Android.Util.Log.Warn("Pulp", $"TrailerActivity opened without a valid YouTube video id (\"{videoId}\"); closing.");
            Finish();
            return;
        }

        var title = Intent?.GetStringExtra(ExtraTitle);
        var kind = PlayerCopy.Parse(Intent?.GetStringExtra(ExtraKind));
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        page.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        page.AddView(PaperUi.ScreenHeader(
            this,
            string.IsNullOrWhiteSpace(title) ? PlayerCopy.FallbackHeader(kind) : title,
            AreaPalette.For(AreaHue.Identity).Rule));

        // Laid over the whole window, empty and gone until the player asks for fullscreen.
        var fullscreenHost = new FrameLayout(this) { Visibility = ViewStates.Gone };
        fullscreenHost.SetBackgroundColor(PaperTheme.Ink);

        web = new WebView(this);
        chrome = PaperWebView.LoadPlayer(web, fullscreenHost, videoId!, PulpIdentity.ApplicationId);
        page.AddView(web, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // A status sentence, not a spinner (DESIGN.md §6): the embed shows its own poster frame
        // and waits for the owner.
        var status = PaperUi.Secondary(this, PlayerCopy.Status(kind));
        status.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X3), 0, PaperTheme.Dp(this, DesignTokens.Space.X6));
        page.AddView(status);

        var window = new FrameLayout(this);
        window.AddView(PaperUi.WrapForTopInset(this, page), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        window.AddView(fullscreenHost, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SetContentView(window);
    }

    protected override void OnPause()
    {
        // Leaving the screen stops the sound; a trailer does not play on behind another app.
        web?.OnPause();
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        web?.OnResume();
    }

    protected override void OnDestroy()
    {
        web?.Destroy();
        web = null;
        base.OnDestroy();
    }

    public override void OnBackPressed()
    {
        if (chrome?.LeaveFullscreen() == true)
        {
            return;
        }

        base.OnBackPressed();
    }
}
