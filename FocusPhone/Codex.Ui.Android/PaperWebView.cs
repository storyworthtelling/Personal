using Android.Content;
using Android.Webkit;
using Codex.Domain.Web;

namespace Codex.Ui;

/// <summary>
/// The suite's <b>one</b> hardened web view. Flyleaf's restricted browser and Post's mail
/// reader are the same engine with different <see cref="WebPosture"/>s — a second WebView
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
    public static void Harden(WebView view, WebPosture posture, Action<string> onLinkOut)
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

        if (!posture.AllowsInPlaceNavigation)
        {
            view.SetWebViewClient(new LinksAlwaysLeaveClient(onLinkOut));
        }
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
}
