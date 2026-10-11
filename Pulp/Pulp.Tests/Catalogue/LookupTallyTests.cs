using Pulp.Domain.Catalogue;

namespace Pulp.Tests.Catalogue;

public class LookupTallyTests
{
    [Fact]
    public void CountsEachSourceSeparatelyAndInTotal()
    {
        var tally = new LookupTally();

        tally.Counted(LookupSource.StreamingAvailability);
        tally.Counted(LookupSource.StreamingAvailability);
        tally.Counted(LookupSource.Omdb);

        Assert.Equal(2, tally.Count(LookupSource.StreamingAvailability));
        Assert.Equal(1, tally.Count(LookupSource.Omdb));
        Assert.Equal(0, tally.Count(LookupSource.YouTube));
        Assert.Equal(3, tally.Total);
    }

    [Fact]
    public void CountingFromManyRequestsAtOnceLosesNothing()
    {
        var tally = new LookupTally();

        Parallel.For(0, 2000, _ => tally.Counted(LookupSource.TvMaze));

        Assert.Equal(2000, tally.Count(LookupSource.TvMaze));
    }
}
