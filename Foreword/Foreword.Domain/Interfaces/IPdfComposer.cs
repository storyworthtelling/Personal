using Foreword.Domain.Brief;
using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface IPdfComposer
{
    byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes);

    /// <summary>
    /// The whole brief: the foreword page, the stories, the comic, then the puzzle.
    /// </summary>
    byte[] Compose(
        IReadOnlyList<ArticleContent> articles,
        byte[] crosswordPdfBytes,
        DateOnly date,
        string voice,
        IReadOnlyList<ComicStrip>? comics = null,
        HistoryNote? history = null);
}
