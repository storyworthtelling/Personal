using Folio.Domain.Analysis;
using Folio.Domain.Model;
using Folio.Domain.Reading;
using Folio.Tests.Support;

namespace Folio.Tests.Pipeline;

/// <summary>
/// The seventeen reads actually run, in the order the plan sets, and each is recorded once.
///
/// <para>The bug this reintroduces is a step that never runs. It is the quietest failure in the
/// pipeline: the report still renders, from whatever the omnibus reduce produced, and every test
/// passes — the reader pages are simply missing and nothing says so. It was nearly shipped that way
/// here, because a green end-to-end suite was consistent with the step being unreachable.</para>
/// </summary>
public class PassPipelineTests
{
    [Fact]
    public void ThePlanCoversEveryPersonaExactlyOnce()
    {
        var passIds = ReadingPlan.Passes.Select(pass => pass.Id).ToList();

        foreach (var persona in PersonaCatalogue.All)
        {
            Assert.Single(passIds, id => id == persona.Id);
        }
        Assert.Equal(PersonaCatalogue.All.Count + ReadingPlan.Synthesis.Count, passIds.Count);
    }

    /// <summary>
    /// The ordering the cache depends on: every standard-tier pass before every strong-tier one, so
    /// the manuscript is written once per model rather than once per alternation (F50).
    /// </summary>
    [Fact]
    public void EveryPassOfOneModelRunsBeforeTheOther()
    {
        var switches = ReadingPlan.Passes
            .Select(pass => pass.Tier)
            .Zip(ReadingPlan.Passes.Skip(1).Select(pass => pass.Tier))
            .Count(pair => pair.First != pair.Second);

        Assert.True(switches <= 1,
            $"The plan changes model {switches} times; each change past the first pays another cache write.");
    }

    /// <summary>
    /// A pass that ran and found nothing is recorded, not left null — otherwise the pipeline reads it
    /// again on every cycle, for ever, billing each time.
    /// </summary>
    [Fact]
    public void ASilentSectionIsStillARecordedRead()
    {
        var silent = SectionRead.Silent("pacing");

        Assert.False(silent.HasContent);
        Assert.Equal("pacing", silent.PersonaId);
    }

    [Fact]
    public void ASectionWithSomethingToSayHasContent()
    {
        var read = new SectionRead("pacing", "The middle slackens.", [new ReportFinding("Ch. 12", "Four chapters do one chapter's work.")]);

        Assert.True(read.HasContent);
    }

    /// <summary>
    /// The readers replace the omnibus reduce's reviews rather than joining them — one voice read the
    /// whole book, and a page carrying both would be two instruments under one name.
    /// </summary>
    [Fact]
    public void TheReadersReplaceTheConsolidatedReviews()
    {
        var report = SampleAnalysis.Of() with
        {
            Reviews = [new Review("old", "stale", "from the omnibus reduce", ["a"], ["b"], "c")],
        };
        var readers = new[]
        {
            new Review("agent", "Yes, with the middle tightened.", "A read in character.", ["pitch"], ["middle"], "the middle tightened"),
        };

        var folded = report.WithReads(readers, [], [], Verdict.Empty);

        Assert.Equal("agent", Assert.Single(folded.Reviews).PersonaId);
    }

    /// <summary>
    /// …but a read that has not run yet leaves what is already there standing, rather than blanking a
    /// report because a pass is still pending.
    /// </summary>
    [Fact]
    public void AReadThatHasNotRunLeavesTheReportAlone()
    {
        var report = SampleAnalysis.Of() with
        {
            Reviews = [new Review("old", "stale", "from the omnibus reduce", ["a"], ["b"], "c")],
            Verdict = new Verdict(["a strength"], ["a problem"], "more middle"),
        };

        var folded = report.WithReads([], [], [], Verdict.Empty);

        Assert.Single(folded.Reviews);
        Assert.Equal("more middle", folded.Verdict.MostNeeds);
    }

    [Fact]
    public void ASynthesisedVerdictReplacesTheConsolidatedOne()
    {
        var report = SampleAnalysis.Of() with { Verdict = new Verdict([], [], "the old verdict") };

        var folded = report.WithReads([], [], [], new Verdict(["spine"], ["middle"], "the middle compressed"));

        Assert.Equal("the middle compressed", folded.Verdict.MostNeeds);
    }
}
