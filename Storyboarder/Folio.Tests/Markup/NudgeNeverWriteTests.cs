using Folio.Domain.Markup;

namespace Folio.Tests.Markup;

/// <summary>
/// G19 — a mark has no member that could hold replacement prose.
///
/// <para>The bug this reintroduces is adding <c>SuggestedText</c>. It would not break anything; it
/// would compile, render, and read well. That is the danger. <b>Nudge never write</b> is the brief's
/// first principle, and the way a principle like that dies is not a decision to abandon it — it is
/// one field, filled in by the next prompt that could, after which the product diagnoses and drafts
/// and nobody remembers which it was meant to be.</para>
///
/// <para>So the guard is over the record's shape rather than over any prompt's wording: the mark
/// carries what is wrong and stops, and a new member is a decision somebody has to make on purpose.</para>
/// </summary>
public class NudgeNeverWriteTests
{
    [Fact]
    public void TheMarkCarriesExactlyWhatItNeedsAndNothingThatCouldHoldProse()
    {
        var members = typeof(Mark).GetProperties()
            .Select(property => property.Name)
            .Where(name => name is not ("IsPoint" or "Length" or "EqualityContract"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["EndOffset", "Kind", "Label", "Provenance", "Reason", "Severity", "StartOffset"],
            members);
    }

    /// <summary>
    /// Named for what it would actually be called. A member matching any of these is the failure —
    /// and the test says so in the words of the principle, so the next person reads why rather than
    /// which assertion tripped.
    /// </summary>
    [Fact]
    public void NoMemberIsNamedForWritingTheAuthorsSentences()
    {
        string[] forbidden =
        [
            "suggested", "replacement", "rewrite", "rewritten", "newtext", "prose",
            "draft", "instead", "better", "fix", "corrected", "revision",
        ];

        var offenders = typeof(Mark).GetProperties()
            .Select(property => property.Name)
            .Where(name => forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "A mark diagnoses and stops — nudge never write. These members could hold the author's "
            + "sentences instead of a note about them:\n" + string.Join("\n", offenders));
    }

    /// <summary>The same rule for the four marks: every one is a diagnosis, none is an edit.</summary>
    [Fact]
    public void TheFourMarksAreTheCopyeditorsOwn()
    {
        Assert.Equal(
            [MarkKind.Caret, MarkKind.Dele, MarkKind.Query, MarkKind.Check],
            Enum.GetValues<MarkKind>());
    }
}
