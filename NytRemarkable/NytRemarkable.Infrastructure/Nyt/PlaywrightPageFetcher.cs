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
        var response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });

        if (response == null || !response.Ok)
        {
            var status = response?.Status ?? 0;
            throw new HttpRequestException($"NYT article fetch failed: {status} for {url}");
        }

        logger.LogInformation("Playwright fetched {Url} (HTTP {Status})", url, response.Status);
        return await page.ContentAsync();
    }

    private async Task<IBrowser> GetBrowser()
    {
        if (browser != null) return browser;

        await initLock.WaitAsync();
        try
        {
            if (browser != null) return browser;
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-setuid-sandbox"]
            });
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
