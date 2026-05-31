namespace DailyLoader.Infrastructure.Nyt;

public interface IArticlePageFetcher
{
    Task<string> FetchPageHtmlAsync(string url);
}
