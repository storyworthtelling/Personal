using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Web;
using FocusPhone.Ui;

namespace FocusPhone.Launcher;

/// <summary>
/// The only web surface on the device. Opens a single link (from a QR scan,
/// email, or text) and confines all navigation to the originating link's
/// registrable domain. No URL entry, no tabs, no downloads.
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var origin = Intent?.Data?.ToString() is { } raw && Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            ? parsed
            : null;
        if (origin is null)
        {
            Finish();
            return;
        }

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetBackgroundColor(PaperTheme.Paper);

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
        SetContentView(layout);

        ConfigureWebView(webView, origin);
        webView.LoadUrl(origin.ToString());
    }

    private void ConfigureWebView(WebView view, Uri origin)
    {
        var settings = view.Settings;
        settings.JavaScriptEnabled = true;
        settings.SafeBrowsingEnabled = true;
        settings.AllowFileAccess = false;
        settings.AllowContentAccess = false;
        settings.SetSupportMultipleWindows(false);
        settings.JavaScriptCanOpenWindowsAutomatically = false;
        settings.SetGeolocationEnabled(false);

        view.SetDownloadListener(new BlockedDownloadListener());
        view.SetWebViewClient(new SameDomainWebViewClient(this, LinkPolicy.CreateDefault(), origin));
    }

    public void ShowAddress(string url) => addressDisplay!.Text = url;

    public void ShowBlockedPage(string blockedUrl)
    {
        var originHost = Intent?.Data?.Host ?? "the original site";
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
            if (target is null || !Uri.TryCreate(target, UriKind.Absolute, out var targetUri)
                || !policy.IsNavigationAllowed(origin, targetUri))
            {
                activity.ShowBlockedPage(target ?? "(unparseable URL)");
                return true;
            }
            return false;
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

    private class BlockedDownloadListener : Java.Lang.Object, IDownloadListener
    {
        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition,
            string? mimetype, long contentLength)
        {
            // Downloads are not part of the restricted browsing surface.
        }
    }
}
