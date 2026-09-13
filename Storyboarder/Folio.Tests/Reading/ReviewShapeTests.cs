using Folio.Domain.Analysis;
using Folio.Domain.Reading;

namespace Folio.Tests.Reading;

/// <summary>
/// G23 — every persona read prints assessment, strengths, weaknesses and condition, and none of the
/// four is empty.
///
/// <para>The bug this reintroduces is flattening a review to a summary line, which the first drawing
/// of the readers' page did: seven three-line summaries on one sheet. That threw away what the
/// <i>shipped</i> report already produces — <c>Review</c> has carried assessment, strengths,
/// weaknesses and a recommendation all along, and the renderer has always printed all of them. A
/// redesign that quietly delivers less than what is already working is the worst kind, because every
/// test passes and the page looks cleaner.</para>
/// </summary>
public class ReviewShapeTests
{
    private static Review Whole() => new(
        "agent", "Yes, with the middle tightened.",
        "A sustained assessment in character.",
        ["The marsh and the inquest are the pitch."],
        ["The middle is where I would lose an editor."],
        "The middle tightened before I sent it out.");

    /// <summary>The record still carries all six parts — the four printed plus who and the verdict.</summary>
    [Fact]
    public void TheRecordHasNotBeenFlattened()
    {
        var members = typeof(Review).GetProperties().Select(property => property.Name).ToArray();

        Assert.Contains(nameof(Review.PersonaId), members);
        Assert.Contains(nameof(Review.Verdict), members);
        Assert.Contains(nameof(Review.Assessment), members);
        Assert.Contains(nameof(Review.Strengths), members);
        Assert.Contains(nameof(Review.Weaknesses), members);
        Assert.Contains(nameof(Review.Condition), members);
    }

    [Fact]
    public void AReadWithNoStrengthsIsAFailedPassNotAHarshOne()
    {
        var reason = ReaderRead.Incomplete(Whole() with { Strengths = [] });

        Assert.NotNull(reason);
        Assert.Contains("failed pass", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReadWithNoWeaknessesIsAFailedPassNotAKindOne()
    {
        var reason = ReaderRead.Incomplete(Whole() with { Weaknesses = [] });

        Assert.NotNull(reason);
        Assert.Contains("failed pass", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReadWithNoAssessmentIsAFailedPass()
    {
        Assert.NotNull(ReaderRead.Incomplete(Whole() with { Assessment = string.Empty }));
    }

    /// <summary>
    /// The rename is the point of this one: a field called Recommendation invites a restatement of
    /// the review, where a Condition is what would have to change for the verdict to move.
    /// </summary>
    [Fact]
    public void TheConditionIsNotCalledARecommendation()
    {
        var members = typeof(Review).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Recommendation", members);
        Assert.DoesNotContain("Perspective", members);
    }
}
