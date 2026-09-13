using Folio.Domain.Analysis;
using Folio.Domain.Reading;

namespace Folio.Tests.Reading;

/// <summary>
/// G22 — every persona read carries a non-empty condition.
///
/// <para>The bug this reintroduces is accepting a read with no "Would need". It is not a parse
/// error and never was: the mapping it replaces coalesced a missing field to <c>string.Empty</c>,
/// so the page rendered, the reader's verdict printed, and the only thing lost was the sentence
/// that makes the verdict actionable. A verdict with no condition attached is an opinion.</para>
/// </summary>
public class ReaderConditionTests
{
    private static Review Whole() => new(
        "developmental-editor",
        "Sound spine, one soft section.",
        "A sustained assessment of the book's structure, in character, at length.",
        ["The spine holds from the first chapter."],
        ["The middle four chapters do one chapter's work."],
        "The middle compressed to two chapters.");

    [Fact]
    public void AWholeReadPasses()
    {
        Assert.Null(ReaderRead.Incomplete(Whole()));
        Assert.True(ReaderRead.IsWhole(Whole()));
    }

    [Fact]
    public void AReadWithNoConditionIsAFailedPass()
    {
        var read = Whole() with { Condition = string.Empty };

        var reason = ReaderRead.Incomplete(read);
        Assert.NotNull(reason);
        Assert.Contains("condition", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void WhitespaceIsNotACondition(string condition)
    {
        Assert.NotNull(ReaderRead.Incomplete(Whole() with { Condition = condition }));
    }

    [Fact]
    public void AReadWithNoVerdictIsAFailedPass()
    {
        Assert.NotNull(ReaderRead.Incomplete(Whole() with { Verdict = string.Empty }));
    }

    [Fact]
    public void AReadAttributedToNobodyIsAFailedPass()
    {
        Assert.NotNull(ReaderRead.Incomplete(Whole() with { PersonaId = string.Empty }));
    }

    /// <summary>The reason names the reader, because seven passes run and one of them failed.</summary>
    [Fact]
    public void TheReasonSaysWhichReaderCameBackShort()
    {
        var reason = ReaderRead.Incomplete(Whole() with { PersonaId = "genre-fan", Condition = string.Empty });

        Assert.Contains("genre-fan", reason!, StringComparison.Ordinal);
    }
}
