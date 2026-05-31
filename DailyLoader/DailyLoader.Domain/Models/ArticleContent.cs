namespace DailyLoader.Domain.Models;

public class ArticleContent
{
    public required string Title { get; init; }
    public required string Byline { get; init; }
    public required string Section { get; init; }
    public required IReadOnlyList<ContentBlock> Body { get; init; }
}
