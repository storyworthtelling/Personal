using NytRemarkable.Domain.Models;

namespace NytRemarkable.Domain.Interfaces;

public interface IPdfComposer
{
    byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes);
}
