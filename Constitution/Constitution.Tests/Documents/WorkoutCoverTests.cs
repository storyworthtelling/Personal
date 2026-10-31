using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.Documents;

/// <summary>C-2.3.1: the cover lists the session and states the document's rules, and does not speak in the owner's voice.</summary>
public class WorkoutCoverTests
{
    private static InkPageLayout Cover(WorkoutDocumentModel? model = null) => WorkoutDocument.Build(model ?? WorkoutSamples.Workout())[0];

    private static string PageText(InkPageLayout cover)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([cover]));
        return TextOf(document.GetPage(1));
    }

    internal static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The page's text in reading order with spaces where the draws were apart (PdfPig's own <c>Text</c> runs separate draws together).</summary>
    internal static string TextOf(UglyToad.PdfPig.Content.Page page) =>
        Squash(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));

    [Fact]
    public void CarriesTheThreeRulesAndTheStoppedEarlyRule()
    {
        var text = PageText(Cover());

        Assert.Contains("Tick each page as you finish it, and circle how it felt.", text);
        Assert.Contains("A page left unticked counts as skipped.", text);
        Assert.Contains("Stopped early? Tick only the pages you finished; the plan eases from where you stopped.", text);
    }

    [Fact]
    public void HasNoVoicedLine()
    {
        var cover = Cover();

        Assert.DoesNotContain(cover.Printed, item => item.Role == PrintedRole.Voice);
        Assert.Empty(cover.Map.Targets);
    }

    [Fact]
    public void ListsEachExerciseWithDoseAndPage()
    {
        var text = PageText(Cover());

        Assert.Contains("Treadmill walk 5 min easy pace p. 2", text);
        Assert.Contains("Goblet squat 3 × 10 25 lb kettlebell p. 3", text);
        Assert.Contains("Stretch 5 min hips, chest, calves p. 8", text);
    }

    [Fact]
    public void ShowsTheTotalTimeAndTheCount()
    {
        var text = PageText(Cover());

        Assert.Contains("40", text);
        Assert.Contains("min", text);
        Assert.Contains("7 exercises · 1 page each", text);
        Assert.Contains("Kettlebell, band, bench, mat", text);
    }

    [Fact]
    public void NamesTheDayAndTheTwelveHourTime()
    {
        var text = PageText(Cover());

        Assert.Contains("WORKOUT · TUESDAY 5:30 PM", text);
        Assert.Contains("Constitution · Strength · Tue Oct 20", text);
        Assert.Contains("1 of 8", text);
    }

    [Fact]
    public void TheDoseRungPrintsTheMinutes()
    {
        var cover = Cover();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([cover]));
        var page = document.GetPage(1);
        var big = page.Letters.Where(l => l.Value is "4" or "0" && l.PointSize > 40).ToList();

        Assert.Equal(2, big.Count);
        Assert.All(big, l => Assert.Equal(Storyworthtelling.Paper.PaperTypeScale.Dose, (float)l.PointSize, 0.1f));
    }

    [Fact]
    public void TheListIsInRowOrderDownThePage()
    {
        var cover = Cover();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([cover]));
        var page = document.GetPage(1);
        var tops = new[] { "Treadmill", "Goblet", "Band", "Incline", "Glute", "Dead", "Stretch" }
            .Select(word => page.GetWords().First(w => w.Text == word).BoundingBox.Bottom)
            .ToList();

        Assert.Equal(tops.OrderByDescending(y => y), tops);
    }

    [Fact]
    public void NothingRunsOutsideTheMargins()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Cover()]));
        var page = document.GetPage(1);

        Assert.All(page.Letters, l =>
        {
            Assert.InRange(l.GlyphRectangle.Left, 35.0, 410.0);
            Assert.InRange(l.GlyphRectangle.Right, 35.0, 410.0);
        });
    }

    [Fact]
    public void TooManyExercisesForOnePageAreRefused()
    {
        var many = Enumerable.Range(0, 12).Select(i => WorkoutSamples.Row() with { Name = $"Move {i + 1}" }).ToList();

        var error = Assert.Throws<ArgumentException>(() => WorkoutDocument.Build(WorkoutSamples.Workout(exercises: many)));
        Assert.Contains("cover", error.Message);
    }

    [Fact]
    public void AnEmptyWorkoutIsRefused()
    {
        Assert.Throws<ArgumentException>(() => WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [])));
    }

    [Fact]
    public void TheCoverCarriesStillSoreWhenSoreToo()
    {
        var cover = WorkoutDocument.Build(WorkoutSamples.Workout(WorkoutSamples.Sore()))[0];

        Assert.NotNull(cover.Map.TryGet(WorkoutIds.StillSore));
        Assert.Empty(cover.Violations());
    }
}
