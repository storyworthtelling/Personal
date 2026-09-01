namespace DailyLoader.Domain.Models;

public class TopStory
{
    public required string Title { get; init; }
    public required string Byline { get; init; }
    public required string Section { get; init; }
    public required string Url { get; init; }
    public string Abstract { get; init; } = string.Empty;
}
