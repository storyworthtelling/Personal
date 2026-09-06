using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface INytArticleClient
{
    Task<ArticleContent> GetArticle(string url);
}
