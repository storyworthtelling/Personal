using NytRemarkable.Domain.Models;

namespace NytRemarkable.Domain.Interfaces;

public interface INytArticleClient
{
    Task<ArticleContent> GetArticle(string url);
}
