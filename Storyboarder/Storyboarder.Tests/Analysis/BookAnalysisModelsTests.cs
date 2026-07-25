using System.Text.Json;
using Storyboarder.Domain.Analysis;
using Xunit;

namespace Storyboarder.Tests.Analysis;

public class BookAnalysisModelsTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Finding_ReadsTheCitedObjectForm()
    {
        var finding = JsonSerializer.Deserialize<Finding>(
            "{\"chapter\":\"Chapter 7\",\"detail\":\"The key is never found.\"}", Options)!;

        Assert.Equal("Chapter 7", finding.Chapter);
        Assert.Equal("The key is never found.", finding.Detail);
    }

    [Fact]
    public void Finding_ReadsABareString_SoOlderReportsStillLoad()
    {
        // Every report consolidated before chapter citations existed stored its findings as plain
        // strings. Re-rendering one of those must not throw — it simply has nothing to cite.
        var finding = JsonSerializer.Deserialize<Finding>("\"The key is never found.\"", Options)!;

        Assert.Equal(string.Empty, finding.Chapter);
        Assert.Equal("The key is never found.", finding.Detail);
    }

    [Fact]
    public void Finding_IgnoresUnknownAndNonStringProperties()
    {
        var finding = JsonSerializer.Deserialize<Finding>(
            "{\"chapter\":\"Chapter 2\",\"severity\":3,\"tags\":[\"a\"],\"detail\":\"Eye colour shifts.\"}", Options)!;

        Assert.Equal("Chapter 2", finding.Chapter);
        Assert.Equal("Eye colour shifts.", finding.Detail);
    }

    [Fact]
    public void Finding_RoundTrips()
    {
        var original = new Finding("Chapters 7–8", "The middle sags.");

        var restored = JsonSerializer.Deserialize<Finding>(JsonSerializer.Serialize(original, Options), Options);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Finding_RejectsAShapeItCannotRead()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Finding>("[1,2]", Options));
    }

    [Fact]
    public void WithCraft_FoldsTheSecondReduceCallIntoTheReport()
    {
        var craft = new CraftReport(
            [new LineEdit("Chapter 1", "He felt the cold.", "The cold came.", "Filtered.")],
            [new ProseNote("Chapter 1", "Filter words", "he felt", "Cut it.")],
            [new DialogueNote("Chapter 1", "Alice", "On the nose.", "Trust the subtext.")],
            [new SceneBeat("Chapter 1", "Alice waits.", "Stalls.", "Does no work.")]);

        var merged = BookAnalysisResult.Empty with { Logline = "A girl quests." };
        merged = merged.WithCraft(craft);

        Assert.Equal("A girl quests.", merged.Logline);
        Assert.Equal(craft.LineEdits, merged.LineEdits);
        Assert.Equal(craft.ProseNotes, merged.ProseNotes);
        Assert.Equal(craft.DialogueNotes, merged.DialogueNotes);
        Assert.Equal(craft.SceneBreakdown, merged.SceneBreakdown);
    }
}
