using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface IPdfComposer
{
    byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes);
}
