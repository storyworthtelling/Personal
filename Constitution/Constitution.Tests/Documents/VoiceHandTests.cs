using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.Documents;

/// <summary>
/// The owner's hand is for the owner's voice. A workout page's encouragement is printed only when it was voiced
/// (with no voice the seat hides, owner's ruling W10), so the line there is always in their hand.
/// </summary>
public class VoiceHandTests
{
    private const string Line = "Five more pounds today. You were ready for it last week.";

    private static ExercisePageModel Page() =>
        new("Goblet squat", "3 × 10", new ExerciseLoad(25, "kettlebell"), 60, false, 132, Line);

    private static List<UglyToad.PdfPig.Content.Letter> LettersOf(ExercisePageModel page)
    {
        var model = WorkoutSamples.Workout(exercises: [page]);
        // The page's own content: the frame's signature is in the same hand and is not the owner's voice.
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.RenderContent([WorkoutDocument.Build(model)[1]]));
        return document.GetPage(1).Letters.ToList();
    }

    [Fact]
    public void AVoicedLinePrintsInTheVoiceHand()
    {
        var letters = LettersOf(Page());

        Assert.Contains(letters, letter => letter.FontName.Contains("Italianno", StringComparison.Ordinal));
    }

    [Fact]
    public void APageWithNoLineHasNoVoiceHand()
    {
        var letters = LettersOf(Page() with { Encouragement = null });

        Assert.DoesNotContain(letters, letter => letter.FontName.Contains("Italianno", StringComparison.Ordinal));
    }
}
