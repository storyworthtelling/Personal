namespace DailyLoader.Domain.Models;

public abstract class ContentBlock { }

public class TextBlock : ContentBlock
{
    public required string Text { get; init; }
}

public class ImageBlock : ContentBlock
{
    public required byte[] ImageBytes { get; init; }
    public string? Caption { get; init; }
}
