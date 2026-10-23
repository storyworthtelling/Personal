using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.Documents;

/// <summary>
/// The owner's hand is for the owner's voice. When the voice is missing the seat falls back to a plain sentence, and that
/// sentence prints in the plain body face: a sentence in script that the owner never wrote would be a forgery of the hand.
/// </summary>
public class VoiceHandTests
{
    private const string Line = "Five more pounds today. You were ready for it last week.";

    private static ExercisePageModel Page(bool voiced) =>
        new("Goblet squat", "3 × 10", new ExerciseLoad(25, "kettlebell"), 60, false, 132, Line, null, voiced);

    private static List<UglyToad.PdfPig.Content.Letter> LettersOf(ExercisePageModel page)
    {
        var model = WorkoutSamples.Workout(exercises: [page]);
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([WorkoutDocument.Build(model)[1]]));
        return document.GetPage(1).Letters.ToList();
    }

    [Fact]
    public void AVoicedLinePrintsInTheVoiceHand()
    {
        var letters = LettersOf(Page(voiced: true));

        Assert.Contains(letters, letter => letter.FontName.Contains("Pinyon", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainFallbackNeverUsesTheVoiceHand()
    {
        var letters = LettersOf(Page(voiced: false));

        Assert.DoesNotContain(letters, letter => letter.FontName.Contains("Pinyon", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainFallbackPrintsInTheBodyFace()
    {
        var letters = LettersOf(Page(voiced: false));

        var body = string.Concat(letters.Where(letter => letter.FontName.Contains("SourceSans3-Regular", StringComparison.Ordinal)).Select(letter => letter.Value));
        Assert.Contains("ready", body, StringComparison.Ordinal);
        Assert.Contains("last", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainFallbackFitsTheColumn()
    {
        var page = WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [Page(voiced: false)]))[1];

        Assert.Empty(page.Violations());
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var notesEdge = page.Map.ById(WorkoutIds.Notes).Bounds.Left;
        Assert.DoesNotContain(document.GetPage(1).Letters, letter => letter.GlyphRectangle.Left < notesEdge && letter.GlyphRectangle.Right > notesEdge);
    }

    [Fact]
    public void AVoicedPageIsUnchangedByTheFlag()
    {
        var withFlag = ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [Page(voiced: true)])));
        var without = ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [WorkoutSamples.Squat(figure: false, voice: Line)])));

        Assert.Equal(without, withFlag);
    }
}
