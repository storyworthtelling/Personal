namespace Foreword.Domain.Models;

public class ArticleContent
{
    public required string Title { get; init; }
    public required string Byline { get; init; }
    public required string Section { get; init; }
    public required IReadOnlyList<ContentBlock> Body { get; init; }

    /// <summary>
    /// The full text did not retrieve and this is the abstract standing in for it (bug B1).
    ///
    /// <para>It is on the page, in one plain line under the headline, because otherwise a
    /// two-sentence story looks like a short story rather than a failed download and the reader has
    /// no way to tell the difference. No decoration and no margin symbol — nothing to decode.</para>
    /// </summary>
    public bool AbstractOnly { get; init; }
}
