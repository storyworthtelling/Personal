using Constitution.Domain.Documents;

namespace Constitution.Tests.DigestPages;

/// <summary>Job 1 — the digest is assembled from the four live stores behind one seam, and a store that cannot answer stops it, typed.</summary>
public class DigestAssemblerTests
{
    [Fact]
    public void AssemblesFromEveryStore()
    {
        var sources = new FakeSources();
        var ready = Assert.IsType<DigestAssembly.Ready>(DigestAssembler.Assemble("Sam", DigestFixture.Saturday, sources));
        Assert.Equal("Sam", ready.Model.Person);
        Assert.Equal(DigestFixture.Saturday, ready.Model.Saturday);
        Assert.Equal(["workouts", "body", "meals", "next"], sources.Calls);
        Assert.Equal(5, SaturdayDigest.Build(ready.Model).Count);
    }

    [Fact]
    public void AskedForTheWeekThatEndedAndTheOneAfter()
    {
        var weeks = new List<DateOnly>();
        var sources = new WeekSpy(weeks);
        DigestAssembler.Assemble("Sam", DigestFixture.Saturday, sources);
        Assert.Equal(DigestFixture.WeekStart, weeks[0]);
        Assert.Equal(DigestFixture.Saturday.AddDays(1), weeks[^1]);
    }

    [Fact]
    public void AnUnavailableStoreStopsItWithItsReason()
    {
        var sources = new FakeSources { Unavailable = "The skip ledger could not be read." };
        var stopped = Assert.IsType<DigestAssembly.NotReady>(DigestAssembler.Assemble("Sam", DigestFixture.Saturday, sources));
        Assert.Equal("The skip ledger could not be read.", stopped.Reason);
    }

    [Fact]
    public void ABlankPersonIsRefused()
    {
        Assert.Throws<ArgumentException>(() => DigestAssembler.Assemble(" ", DigestFixture.Saturday, new FakeSources()));
    }

    private sealed class WeekSpy(List<DateOnly> weeks) : IDigestSources
    {
        private readonly FakeSources inner = new();

        public DigestRead<DigestWorkoutWeek> Workouts(string person, DateOnly weekStart)
        {
            weeks.Add(weekStart);
            return inner.Workouts(person, weekStart);
        }

        public DigestRead<IReadOnlyList<Constitution.Domain.Body.BodyReading>> Readings(string person) => inner.Readings(person);

        public DigestRead<DigestMealsWeek> Meals(DateOnly weekStart) => inner.Meals(weekStart);

        public DigestRead<DigestNextWeek> Next(string person, DateOnly nextWeekStart)
        {
            weeks.Add(nextWeekStart);
            return inner.Next(person, nextWeekStart);
        }
    }
}
