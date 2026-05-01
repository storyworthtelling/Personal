namespace NytRemarkable.Domain.Models;

public class CrosswordPuzzle
{
    public required DateOnly Date { get; init; }
    public required PuzzleType Type { get; init; }
    public required byte[] PdfBytes { get; init; }

    public string Title => Type == PuzzleType.Daily
        ? $"NYT Crossword {Date:yyyy-MM-dd}"
        : $"NYT Bonus Crossword {Date:MMMM yyyy}";
}
