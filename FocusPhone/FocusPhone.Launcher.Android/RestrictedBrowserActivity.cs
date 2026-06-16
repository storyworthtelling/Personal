using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using FocusPhone.Domain.Web;

namespace FocusPhone.Launcher;

/// <summary>
/// The only web surface on the device. Opens a single link (from a QR scan,
/// email, or text) and confines all navigation to the originating link's
/// registrable domain. No URL entry, no tabs, no downloads.
/// </summary>
[Activity(
    Label = "Web",
    Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
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
        layout.SetBackgroundColor(Palette.Paper);

        addressDisplay = new TextView(this);
        addressDisplay.SetTextColor(Palette.InkMuted);
        addressDisplay.SetSingleLine(true);
        addressDisplay.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        addressDisplay.SetPadding(32, 24, 32, 24);
        layout.AddView(addressDisplay, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

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
        webView!.LoadDataWithBaseURL(null, $"""
            <html><body style="background:#FAF8F2;color:#3A3A33;font-family:sans-serif;padding:2em">
            <h2>Blocked</h2>
            <p>This page stays on <b>{originHost}</b>. The link below leads elsewhere and was not opened:</p>
            <p style="word-break:break-all;color:#8A8578">{WebUtility.HtmlEncode(blockedUrl)}</p>
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
