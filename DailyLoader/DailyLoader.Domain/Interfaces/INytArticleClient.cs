using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface INytArticleClient
{
    Task<ArticleContent> GetArticle(string url);
}
