using AWebView = Android.Webkit.WebView;
using Microsoft.Extensions.Logging;
using NytRemarkable.Infrastructure.Nyt;

namespace NytRemarkable.Android;

public class WebViewPageFetcher(ILogger<WebViewPageFetcher> logger) : IArticlePageFetcher
{
    public async Task<string> FetchPageHtmlAsync(string url)
    {
        var cookie = CredentialStore.NytSessionCookie;
        if (!string.IsNullOrEmpty(cookie))
        {
            Android.Webkit.CookieManager.Instance!.SetAcceptCookie(true);
            Android.Webkit.CookieManager.Instance.SetCookie(".nytimes.com",
                $"NYT-S={cookie}; Domain=.nytimes.com; Path=/; Secure");
            Android.Webkit.CookieManager.Instance.Flush();
        }

        logger.LogInformation("Navigating to {Url}", url);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        AWebView? webView = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var context = global::Android.App.Application.Context;
            webView = new AWebView(context);
            webView.Settings.JavaScriptEnabled = true;
            webView.Settings.DomStorageEnabled = true;
            Android.Webkit.CookieManager.Instance!.SetAcceptThirdPartyCookies(webView, true);
            webView.SetWebViewClient(new ArticleWebViewClient(tcs, logger));
            webView.LoadUrl(url);
        });

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Page load timed out for {Url}", url);
            throw new HttpRequestException($"NYT article fetch timed out for {url}");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                webView?.Destroy();
                webView = null;
            });
        }
    }

    private sealed class ArticleWebViewClient(TaskCompletionSource<string> tcs, ILogger logger)
        : Android.Webkit.WebViewClient
    {
        public override void OnPageFinished(AWebView? view, string? url)
        {
            base.OnPageFinished(view, url);

            // If Cloudflare's challenge page is showing (title = 'nytimes.com'),
            // return null and wait — OnPageFinished will fire again after the redirect
            view?.EvaluateJavascript(
                "(function() { " +
                "  if (document.title === 'nytimes.com') return null; " +
                "  return document.documentElement.outerHTML; " +
                "})()",
                new HtmlExtractCallback(tcs, logger));
        }

        public override void OnReceivedError(AWebView? view, Android.Webkit.IWebResourceRequest? request,
            Android.Webkit.WebResourceError? error)
        {
            if (request?.IsForMainFrame == true)
            {
                logger.LogWarning("WebView error for {Url}: {Error}", request.Url, error?.Description);
                tcs.TrySetException(new HttpRequestException(
                    $"WebView navigation error: {error?.Description} for {request.Url}"));
            }
        }
    }

    private sealed class HtmlExtractCallback(TaskCompletionSource<string> tcs, ILogger logger)
        : Java.Lang.Object, Android.Webkit.IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            var raw = value?.ToString();
            if (raw == null || raw == "null")
            {
                logger.LogInformation("Cloudflare challenge in progress, waiting for redirect...");
                return;
            }

            // evaluateJavascript returns a JSON-encoded string — unwrap it
            string html;
            try
            {
                html = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? raw;
            }
            catch
            {
                html = raw;
            }

            logger.LogInformation("Page content extracted ({Length} chars)", html.Length);
            tcs.TrySetResult(html);
        }
    }
}
