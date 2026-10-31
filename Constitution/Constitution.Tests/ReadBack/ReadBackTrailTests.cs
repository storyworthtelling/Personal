using Constitution.Domain.Diagnostics;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// The read-back's lines in the Body trail. Every phrase must come through the export's own filter whole, because a
/// withheld line is a morning the owner's device test cannot read, and none may hold a title, a name or a mark.
/// </summary>
public class ReadBackTrailTests
{
    private const string Stamp = "[Nov 4, 7:15 am] INFO Body — ";

    public static TheoryData<DocumentReadBack> EveryOutcome => new()
    {
        new DocumentReadBack.Settled("Constitution — Workout — 2026-11-03", 2, 1),
        new DocumentReadBack.DigestTrashed("Constitution — Digest — 2026-11-03"),
        new DocumentReadBack.DigestKept("Constitution — Digest — 2026-11-03"),
        new DocumentReadBack.NoMarksInCloud("Constitution — Workout — 2026-11-03"),
        new DocumentReadBack.NotOnTablet("Constitution — Workout — 2026-11-03"),
        new DocumentReadBack.Unfinished("Constitution — Workout — 2026-11-03", "The read had nothing for page 2."),
        new DocumentReadBack.GaveUp("Constitution — Workout — 2026-11-03"),
        new DocumentReadBack.Failed("Constitution — Workout — 2026-11-03", ReadBackStage.Download, new HttpRequestException("down")),
        new DocumentReadBack.Failed("Constitution — Workout — 2026-11-03", ReadBackStage.Read, new TaskCanceledException()),
        new DocumentReadBack.Failed("Constitution — Workout — 2026-11-03", ReadBackStage.Resolve, new KeyNotFoundException("No target called 'done' is on this page.")),
        new DocumentReadBack.Failed("Constitution — Workout — 2026-11-03", ReadBackStage.Persist, new UnauthorizedAccessException()),
        new DocumentReadBack.Failed("Constitution — Workout — 2026-11-03", ReadBackStage.Ask, new InvalidOperationException()),
        new DocumentReadBack.Failed("Constitution — Workout — 2026-11-03", ReadBackStage.Trash, new IOException()),
    };

    [Theory]
    [MemberData(nameof(EveryOutcome))]
    public void EveryPhraseSurvivesTheExportFilterWhole(DocumentReadBack outcome)
    {
        var line = Stamp + ReadBackTrail.LogPhrase(outcome);

        Assert.Equal(line, TrailLines.Filter(line));
    }

    [Theory]
    [MemberData(nameof(EveryOutcome))]
    public void NoPhraseHoldsATitleATextOrAnExceptionMessage(DocumentReadBack outcome)
    {
        var phrase = ReadBackTrail.LogPhrase(outcome);

        Assert.DoesNotContain("Constitution", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("2026", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("No target", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("page 2", phrase, StringComparison.Ordinal);
        Assert.StartsWith("ink read back", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void ASettledPhraseCarriesOnlyCounts()
    {
        Assert.Equal("ink read back settled, 2 pages read, 1 questions asked", ReadBackTrail.LogPhrase(new DocumentReadBack.Settled("x", 2, 1)));
    }

    [Fact]
    public void AFailurePhraseNamesTheStepAndTheExceptionTypeOnly()
    {
        var phrase = ReadBackTrail.LogPhrase(new DocumentReadBack.Failed("x", ReadBackStage.Trash, new IOException("C:\\private\\path")));

        Assert.Equal("ink read back failed at trash: IOException, document kept", phrase);
    }

    [Fact]
    public void EveryStageNameIsInTheVocabulary()
    {
        foreach (var stage in Enum.GetNames<ReadBackStage>())
        {
            Assert.Contains(stage.ToLowerInvariant(), TrailLines.Vocabulary);
        }
    }

    [Fact]
    public void ThePhrasesCountDocumentsToo()
    {
        var line = Stamp + "ink read back worker ran, 2 documents";

        Assert.Equal(line, TrailLines.Filter(line));
    }
}
