using Android.Content;
using Android.Webkit;
using Android.Widget;
using Codex.Domain.Web;

namespace Codex.Ui;

/// <summary>
/// The suite's <b>one</b> hardened web view. Flyleaf's restricted browser, Post's mail
/// reader and Pulp's trailer player are the same engine with different <see cref="WebPosture"/>s — a second WebView
/// configured separately would be a second place to get wrong, and the one that gets
/// forgotten is the one that gets exploited.
///
/// The hardening is identical in both postures and is not negotiable: no file access, no
/// content-provider access, no automatic windows, no geolocation, Safe Browsing on. What
/// the posture decides is whether script runs and whether a link may navigate in place.
///
/// <para><b>Reading mail is the strict one.</b> Script is off — an email never needs it,
/// and it is the entire attack surface of a document written by whoever chose to write to
/// the owner. No link navigates the reader either: every one is handed out to the
/// restricted browser, where the existing origin-containment rules meet it like any other
/// link, so a message can never quietly become a page.</para>
/// </summary>
public static class PaperWebView
{
    /// <summary>
    /// Applies the shared hardening plus a posture. <paramref name="onLinkOut"/> is called
    /// with any URL the posture refuses to follow in place — for mail, that is every link.
    /// </summary>
    /// <param name="playerEmbed">
    /// For the player only: the one embed its page places, which its client lets load into the
    /// subframe (and nothing else). Null for every other posture.
    /// </param>
    public static void Harden(WebView view, WebPosture posture, Action<string> onLinkOut, Uri? playerEmbed = null)
    {
        var settings = view.Settings;

        // Non-negotiable, in every posture.
        settings.AllowFileAccess = false;
        settings.AllowContentAccess = false;
        settings.SetSupportMultipleWindows(false);
        settings.JavaScriptCanOpenWindowsAutomatically = false;
        settings.SetGeolocationEnabled(false);
        settings.SafeBrowsingEnabled = posture.SafeBrowsing;

        settings.JavaScriptEnabled = posture.AllowsScript;
        settings.BlockNetworkImage = !posture.LoadsRemoteImages;

        // Never autoplay, in any posture (D77). True is also WebView's own default, so the two
        // postures that predate this line behave exactly as before.
        settings.MediaPlaybackRequiresUserGesture = !posture.PlaysMediaWithoutGesture;

        switch (posture.OnNavigation())
        {
            case NavigationVerdict.HandOut:
                view.SetWebViewClient(new LinksAlwaysLeaveClient(onLinkOut));
                break;
            case NavigationVerdict.Refuse:
                view.SetWebViewClient(new NothingNavigatesClient(playerEmbed));
                break;
            case NavigationVerdict.Follow:
                // The caller's own client applies its containment (Flyleaf's browser).
                break;
        }
    }

    /// <summary>
    /// Plays one YouTube trailer — the <see cref="WebPosture.Player"/> posture, and the only
    /// web player in the suite. The video id is validated before it can reach a URL
    /// (<see cref="PlayerEmbed.EmbedUrl"/> throws otherwise), the IFrame sits in a page loaded at
    /// the fixed https origin <see cref="PlayerEmbed.BaseUrlFor"/> names so the embed request
    /// carries a Referer (YouTube's error 153 otherwise), and no JavaScript interface is ever
    /// added. Fullscreen hands the engine's custom view to <paramref name="fullscreenHost"/>,
    /// which the caller lays over its whole window.
    /// </summary>
    /// <returns>The player's chrome, so the host's Back can leave fullscreen before it leaves the screen.</returns>
    public static PlayerChrome LoadPlayer(WebView view, FrameLayout fullscreenHost, string videoId, string applicationId)
    {
        var embed = PlayerEmbed.EmbedUrl(videoId, applicationId);
        var baseUrl = PlayerEmbed.BaseUrlFor(applicationId);
        if (!PlayerEmbed.IsEmbedUrl(embed))
        {
            throw new InvalidOperationException(
                $"The player was about to load {embed.Host}{embed.AbsolutePath}, which is not a YouTube embed of one video; nothing was loaded.");
        }

        Harden(view, WebPosture.Player, onLinkOut: _ => { }, playerEmbed: embed);
        // The IFrame player keeps its state in DOM storage; the player is the only posture that
        // turns it on, and only on its own view.
        view.Settings.DomStorageEnabled = true;
        var chrome = new PlayerChrome(fullscreenHost, WebPosture.Player.AllowsFullscreen);
        view.SetWebChromeClient(chrome);
        view.SetBackgroundColor(PaperTheme.Paper);

        // No "autoplay" in the iframe's allow list: the player waits for a tap inside it. No
        // picture-in-picture either: the trailer does not float out of Pulp.
        //
        // When the trailer ends, YouTube's end screen offers suggestions (same channel, with rel=0)
        // that play inside the iframe by script — no navigation, so the client cannot refuse them.
        // The page therefore re-cues the same video on the IFrame API's ENDED state: the thumbnail
        // returns and nothing else is there to tap. That is the whole script: no JavaScript
        // interface, nothing navigates. The video id is eleven characters of [A-Za-z0-9_-]
        // (PlayerEmbed.EmbedUrl threw otherwise), so it is safe inside a quoted JS string.
        var source = embed.AbsoluteUri.Replace("&", "&amp;", StringComparison.Ordinal);
        var page = $$"""
            <!doctype html><html><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="referrer" content="strict-origin-when-cross-origin">
            <style>
              html,body{background:{{Hex(PaperTheme.Paper)}};margin:0;padding:0}
              .frame{position:relative;width:100%;padding-top:56.25%}
              iframe{position:absolute;inset:0;width:100%;height:100%;border:0}
            </style></head><body><div class="frame">
            <iframe id="trailer" src="{{source}}" allow="fullscreen"
              allowfullscreen referrerpolicy="strict-origin-when-cross-origin"></iframe>
            </div>
            <script>
              function recue(e) {
                if (e.data === YT.PlayerState.ENDED) { e.target.cueVideoById('{{videoId}}'); }
              }
              function onYouTubeIframeAPIReady() {
                var events = { onStateChange: recue };
                new YT.Player('trailer', { events: events });
              }
            </script>
            <script src="https://www.youtube.com/iframe_api"></script>
            </body></html>
            """;

        view.LoadDataWithBaseURL(baseUrl, page, "text/html", "utf-8", null);
        return chrome;
    }

    /// <summary>
    /// Renders an email body. The HTML is wrapped rather than handed over raw, so the mail
    /// inherits the page's typography and width instead of whatever a marketing template
    /// decided, and is loaded with a <b>null base URL</b> — mail is not "at" an origin, and
    /// giving it one would let a relative reference resolve somewhere real.
    /// </summary>
    public static void LoadMail(WebView view, string html, Action<string> onLinkOut)
    {
        Harden(view, WebPosture.Reading, onLinkOut);
        view.SetBackgroundColor(PaperTheme.Paper);

        // Two dollar signs so the CSS braces are content rather than interpolation holes.
        var page = $$"""
            <!doctype html><html><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <style>
              html,body{background:{{Hex(PaperTheme.Paper)}};color:{{Hex(PaperTheme.Ink)}};
                margin:0;padding:16px;font-family:sans-serif;font-size:16px;line-height:1.5;
                word-break:break-word;overflow-wrap:anywhere}
              img{max-width:100%;height:auto}
              table{max-width:100%}
              a{color:{{Hex(PaperTheme.Accent)}}}
            </style></head><body>{{html}}</body></html>
            """;

        view.LoadDataWithBaseURL(null, page, "text/html", "utf-8", null);
    }

    private static string Hex(Android.Graphics.Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// Every navigation leaves. Used for mail, where the only correct answer to a link is
    /// to hand it out — including the ones a message tries to follow by itself.
    /// </summary>
    private sealed class LinksAlwaysLeaveClient(Action<string> onLinkOut) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            if (request?.Url?.ToString() is { } url)
            {
                onLinkOut(url);
            }
            return true; // never followed here, whoever asked
        }
    }

    /// <summary>
    /// Nothing navigates and nothing is handed out. Used for the player, where the YouTube logo
    /// and "watch on YouTube" would otherwise turn a trailer into the site. Windows cannot open
    /// either: multiple windows are off in every posture, so a <c>target=_blank</c> or
    /// <c>window.open</c> has nowhere to go.
    ///
    /// <para>Android's documentation says this callback "may be called for subframes" (and is not
    /// called for POST requests, or for loads the app started itself). So the one exception is the
    /// embed the page placed, loading into its subframe (<see cref="PlayerEmbed.AllowsFrameLoad"/>);
    /// refusing that would refuse the trailer itself. The main frame never moves.</para>
    /// </summary>
    private sealed class NothingNavigatesClient(Uri? embed) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            if (embed is null || request?.Url?.ToString() is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var requested))
            {
                return true;
            }

            return !PlayerEmbed.AllowsFrameLoad(requested, request.IsForMainFrame, embed);
        }
    }

    /// <summary>
    /// The player's chrome: the engine's fullscreen, where the video's custom view is laid over
    /// the caller's host while it lasts and handed back when the player, or the host's Back,
    /// leaves fullscreen. A posture without fullscreen refuses the custom view outright.
    /// </summary>
    public sealed class PlayerChrome : WebChromeClient
    {
        private readonly FrameLayout host;
        private readonly bool allowsFullscreen;
        private ICustomViewCallback? callback;

        internal PlayerChrome(FrameLayout host, bool allowsFullscreen)
        {
            this.host = host;
            this.allowsFullscreen = allowsFullscreen;
        }

        public bool IsFullscreen => callback is not null;

        public override void OnShowCustomView(Android.Views.View? view, ICustomViewCallback? customViewCallback)
        {
            if (view is null || !allowsFullscreen)
            {
                customViewCallback?.OnCustomViewHidden();
                return;
            }

            callback = customViewCallback;
            host.RemoveAllViews();
            host.AddView(view, new FrameLayout.LayoutParams(
                Android.Views.ViewGroup.LayoutParams.MatchParent, Android.Views.ViewGroup.LayoutParams.MatchParent));
            host.Visibility = Android.Views.ViewStates.Visible;
        }

        public override void OnHideCustomView()
        {
            host.RemoveAllViews();
            host.Visibility = Android.Views.ViewStates.Gone;
            callback?.OnCustomViewHidden();
            callback = null;
        }

        /// <summary>Leaves fullscreen if the player is in it; false when there was nothing to leave.</summary>
        public bool LeaveFullscreen()
        {
            if (!IsFullscreen)
            {
                return false;
            }

            OnHideCustomView();
            return true;
        }
    }
}
