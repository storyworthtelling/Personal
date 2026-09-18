using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Web;
using Codex.Ui;

namespace Flyleaf;

/// <summary>
/// The only web surface on the device. Opens a single link (from a QR scan,
/// email, or text) and confines all navigation to the originating link's
/// registrable domain. No URL entry, no tabs. Downloads are the one thing
/// allowed off-domain: a saved file does not open another site, and release/
/// artifact links routinely redirect to a signed storage host on an unrelated
/// domain — so a link the same-domain gate blocks is handed to the system
/// download manager when it points at a file rather than a page.
/// </summary>
[Activity(
    Label = "Web",
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["http", "https"])]
public class RestrictedBrowserActivity : Activity
{
    private WebView? webView;
    private TextView? addressDisplay;
    private string originHost = "the original site";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        var origin = Intent?.Data?.ToString() is { } raw && Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            ? parsed
            : null;
        if (origin is null)
        {
            Finish();
            return;
        }
        originHost = origin.Host;

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };

        addressDisplay = PaperUi.Mono(this, "");
        addressDisplay.SetTextColor(PaperTheme.InkMuted);
        addressDisplay.SetSingleLine(true);
        addressDisplay.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        addressDisplay.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Space.X4), PaperTheme.Dp(this, DesignTokens.Space.X3),
            PaperTheme.Dp(this, DesignTokens.Space.X4), PaperTheme.Dp(this, DesignTokens.Space.X3));
        layout.AddView(addressDisplay, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var rule = new View(this);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        layout.AddView(rule, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(this, DesignTokens.Shape.HairlineDp)));

        webView = new WebView(this);
        layout.AddView(webView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        // Pads itself down by the status-bar/display-cutout inset under enforced edge-to-edge
        // (API 35+), so the address bar isn't drawn partly behind the battery/clock icons.
        SetContentView(PaperUi.WrapForTopInset(this, layout));

        var policy = LinkPolicy.CreateDefault();
        ConfigureWebView(webView, origin, policy);
        // Gate the ARRIVAL url too, not just later navigations: ShouldOverrideUrlLoading fires only on
        // subsequent loads, so a link that lands straight on a blocked social/adult domain (e.g.
        // facebook.com) would otherwise load. Run it through the same gate first.
        if (policy.IsNavigationAllowed(origin, origin))
        {
            webView.LoadUrl(origin.ToString());
        }
        else
        {
            ShowBlockedPage(origin.ToString());
        }
    }

    private void ConfigureWebView(WebView view, Uri origin, LinkPolicy policy)
    {
        // The hardening itself lives in Codex.Ui's PaperWebView, shared with Post's mail
        // reader. Two WebViews configured separately is two places to get wrong, and the
        // one that gets forgotten is the one that gets exploited. What differs here is only
        // the posture: this is a browser, so script runs and a link may navigate in place —
        // which is why this keeps its own client rather than taking the leave-always one.
        PaperWebView.Harden(view, WebPosture.Browsing, onLinkOut: _ => { });

        view.SetDownloadListener(new DownloadStartListener(this));
        view.SetWebViewClient(new SameDomainWebViewClient(this, policy, origin));
    }

    public void ShowAddress(string url) => addressDisplay!.Text = url;

    private void ShowStatus(string message) => addressDisplay!.Text = message;

    /// <summary>
    /// Hands a URL to the system download manager, saving it to the public
    /// Downloads folder. Called for links the browser recognises as files —
    /// including cross-domain ones the same-domain navigation gate rejects.
    /// </summary>
    public void StartDownload(string url, string? userAgent, string? contentDisposition, string? mimeType)
    {
        var target = Android.Net.Uri.Parse(url);
        if (target is null)
        {
            return;
        }

        var fileName = URLUtil.GuessFileName(url, contentDisposition, mimeType);
        var request = new DownloadManager.Request(target);
        request.SetTitle(fileName);
        request.SetDescription(originHost);
        request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
        request.SetDestinationInExternalPublicDir(Android.OS.Environment.DirectoryDownloads, fileName);
        if (!string.IsNullOrEmpty(mimeType))
        {
            request.SetMimeType(mimeType);
        }

        // The signed URL self-authenticates, but carry the session cookies and
        // user agent so same-origin downloads behind a login work too.
        var cookies = CookieManager.Instance?.GetCookie(url);
        if (!string.IsNullOrEmpty(cookies))
        {
            request.AddRequestHeader("Cookie", cookies);
        }
        if (!string.IsNullOrEmpty(userAgent))
        {
            request.AddRequestHeader("User-Agent", userAgent);
        }

        ((DownloadManager)GetSystemService(DownloadService)!).Enqueue(request);
        ShowStatus($"Saving {fileName} to Downloads…");
    }

    public void ShowBlockedPage(string blockedUrl)
    {
        // Styled entirely from DesignTokens; fonts come from the shared assets
        // via the android_asset base URL, with system fallbacks.
        webView!.LoadDataWithBaseURL("file:///android_asset/", $$"""
            <html><head><style>
            @font-face { font-family: 'Source Serif 4'; src: url('{{DesignTokens.FontAssets.SerifSemibold}}'); }
            @font-face { font-family: 'Source Sans 3'; src: url('{{DesignTokens.FontAssets.SansRegular}}'); }
            @font-face { font-family: 'Cascadia Code'; src: url('{{DesignTokens.FontAssets.Mono}}'); }
            body { background: {{DesignTokens.Paper}}; color: {{DesignTokens.Ink}};
                   font-family: 'Source Sans 3', sans-serif; padding: 2em 1.5em; }
            h2 { font-family: 'Source Serif 4', serif; font-weight: 600; }
            .card { background: {{DesignTokens.PaperDeep}}; border-left: 3px solid {{DesignTokens.Accent}};
                    border-radius: 6px; padding: 1em 1.25em; }
            .url { word-break: break-all; color: {{DesignTokens.InkMuted}};
                   font-family: 'Cascadia Code', monospace; font-size: 0.85em; }
            </style></head><body>
            <h2>Blocked</h2>
            <div class="card">This page stays on <b>{{originHost}}</b>. The link below leads elsewhere and was not opened.</div>
            <p class="url">{{WebUtility.HtmlEncode(blockedUrl)}}</p>
            </body></html>
            """, "text/html", "utf-8", null);
    }

    public override void OnBackPressed()
    {
        if (webView?.CanGoBack() == true)
        {
            webView.GoBack();
        }
        else
        {
            base.OnBackPressed();
        }
    }

    private static class WebUtility
    {
        public static string HtmlEncode(string s) => System.Net.WebUtility.HtmlEncode(s);
    }

    private class SameDomainWebViewClient : WebViewClient
    {
        private readonly RestrictedBrowserActivity activity;
        private readonly LinkPolicy policy;
        private readonly Uri origin;

        public SameDomainWebViewClient(RestrictedBrowserActivity activity, LinkPolicy policy, Uri origin)
        {
            this.activity = activity;
            this.policy = policy;
            this.origin = origin;
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            var target = request?.Url?.ToString();
            if (target is not null && Uri.TryCreate(target, UriKind.Absolute, out var targetUri)
                && policy.IsNavigationAllowed(origin, targetUri))
            {
                return false;
            }

            // A same-domain page loads above; anything else is off-limits — unless
            // it is a file to save, which crosses domains without opening a site.
            if (target is not null && Uri.TryCreate(target, UriKind.Absolute, out var downloadUri)
                && DownloadHeuristics.IsDownloadUrl(downloadUri))
            {
                activity.StartDownload(target, null, DownloadHeuristics.ReadContentDisposition(downloadUri), null);
            }
            else
            {
                activity.ShowBlockedPage(target ?? "(unparseable URL)");
            }
            return true;
        }

        public override void OnPageFinished(WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            if (url is not null)
            {
                activity.ShowAddress(url);
            }
        }
    }

    private class DownloadStartListener : Java.Lang.Object, IDownloadListener
    {
        private readonly RestrictedBrowserActivity activity;

        public DownloadStartListener(RestrictedBrowserActivity activity)
        {
            this.activity = activity;
        }

        // Fires when the WebView itself resolves a load to a file (an allowed
        // same-domain link, or a redirect it followed internally). Cross-domain
        // links the navigation gate intercepts are downloaded from there instead.
        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition,
            string? mimetype, long contentLength)
        {
            if (url is not null)
            {
                activity.StartDownload(url, userAgent, contentDisposition, mimetype);
            }
        }
    }
}
