using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface IPdfComposer
{
    byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes);
}
