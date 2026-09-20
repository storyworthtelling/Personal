using Folio.Domain.Reading;

namespace Folio.Tests.Reading;

/// <summary>
/// The sixteen passes, their order, and the estimate built from the measured figures.
///
/// <para>The bug worth reintroducing here is the one F49 actually caught: believing a group fits a
/// budget it does not. The estimate is arithmetic over measured per-pass times, so the moment
/// somebody adds a persona the number moves — which is the only way a capacity claim stays true.</para>
/// </summary>
public class ReadingPlanTests
{
    [Fact]
    public void SixteenPassesMakeARead()
    {
        Assert.Equal(16, ReadingPlan.Passes.Count);
        Assert.Equal(14, ReadingPlan.Passes.Count(pass => !pass.IsSynthesis));
        Assert.Equal(2, ReadingPlan.Passes.Count(pass => pass.IsSynthesis));
    }

    /// <summary>
    /// Sections before readers, so a read that is going to fail on a key or a quota fails on the
    /// cheap model rather than after seven passes of the expensive one.
    /// </summary>
    [Fact]
    public void TheCheapModelGoesFirst()
    {
        var tiers = ReadingPlan.Passes.Select(pass => pass.Tier).ToList();
        var lastStandard = tiers.LastIndexOf(ModelTier.Standard);
        var firstStrong = tiers.IndexOf(ModelTier.Strong);

        Assert.True(lastStandard < firstStrong,
            "A strong-tier pass runs before the last standard one, so a failing key costs more than it needs to.");
    }

    [Fact]
    public void TheSynthesisPassesComeLastBecauseTheyReadTheOthers()
    {
        Assert.All(ReadingPlan.Passes.Take(14), pass => Assert.False(pass.IsSynthesis));
        Assert.All(ReadingPlan.Passes.Skip(14), pass => Assert.True(pass.IsSynthesis));
    }

    /// <summary>
    /// The measurement that decided the transport, as arithmetic rather than as a remembered number:
    /// the group does not fit the worker's nine minutes, and does fit a foreground session.
    /// </summary>
    [Fact]
    public void TheReadDoesNotFitTheWorkerCycleItUsedToRunIn()
    {
        Assert.True(ReadingPlan.Estimate > TimeSpan.FromMinutes(9),
            $"The read is estimated at {ReadingPlan.Estimate.TotalMinutes:0.0} minutes, which would fit the "
            + "old nine-minute cycle — check the measured per-pass figures, because F49 says it does not.");
        Assert.True(ReadingPlan.Estimate < TimeSpan.FromMinutes(30),
            "The read outgrew even the foreground session's budget.");
    }

    [Fact]
    public void EveryPassNamesItselfAndItsModel()
    {
        Assert.All(ReadingPlan.Passes, pass =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pass.Id));
            Assert.False(string.IsNullOrWhiteSpace(pass.Name));
        });
        var ids = ReadingPlan.Passes.Select(pass => pass.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
