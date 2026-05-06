using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace NytRemarkable.Infrastructure.Nyt;

public class PlaywrightPageFetcher : IArticlePageFetcher, IAsyncDisposable
{
    private readonly NytOptions options;
    private readonly ILogger<PlaywrightPageFetcher> logger;
    private readonly SemaphoreSlim initLock = new(1, 1);
    private IPlaywright? playwright;
    private IBrowser? browser;

    public PlaywrightPageFetcher(IOptions<NytOptions> options, ILogger<PlaywrightPageFetcher> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<string> FetchPageHtmlAsync(string url)
    {
        var b = await GetBrowser();

        await using var context = await b.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
        });

        await context.AddCookiesAsync([new Cookie
        {
            Name = "NYT-S",
            Value = options.SessionCookie,
            Domain = ".nytimes.com",
            Path = "/",
            Secure = true
        }]);

        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', {get: () => undefined});
            Object.defineProperty(window, 'chrome', {
                get: () => ({ runtime: {}, loadTimes: function(){}, csi: function(){}, app: {} })
            });
        ");

        logger.LogInformation("Navigating to {Url}", url);

        IResponse? response;
        try
        {
            response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Playwright navigation threw for {Url}", url);
            throw;
        }

        var status = response?.Status ?? 0;
        logger.LogInformation("Navigation result: HTTP {Status} for {Url}", status, url);

        if (response == null || !response.Ok)
        {
            // Cloudflare serves a JS challenge as a 403 — wait up to 30s for it to complete
            // and redirect to the actual article before giving up
            logger.LogInformation("Non-OK response for {Url}, waiting for Cloudflare challenge to resolve", url);
            try
            {
                await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 30000 });
                logger.LogInformation("Challenge resolved, article loaded for {Url}", url);
            }
            catch (TimeoutException)
            {
                try
                {
                    var content = await page.ContentAsync();
                    logger.LogWarning("Challenge did not resolve (first 500 chars): {Snippet}",
                        content.Length > 500 ? content[..500] : content);
                }
                catch { /* best-effort */ }

                throw new HttpRequestException($"NYT article fetch failed: {status} for {url}");
            }
        }

        return await page.ContentAsync();
    }

    private async Task<IBrowser> GetBrowser()
    {
        if (browser != null) return browser;

        await initLock.WaitAsync();
        try
        {
            if (browser != null) return browser;
            logger.LogInformation("Launching Chrome");
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                Channel = "chrome",
                Args = ["--disable-blink-features=AutomationControlled"]
            });
            logger.LogInformation("Chrome launched");
        }
        finally
        {
            initLock.Release();
        }

        return browser;
    }

    public async ValueTask DisposeAsync()
    {
        if (browser != null)
        {
            await browser.DisposeAsync();
            browser = null;
        }
        playwright?.Dispose();
        playwright = null;
        initLock.Dispose();
    }
}
