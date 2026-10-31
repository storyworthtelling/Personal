using Constitution.Domain.Body;
using Constitution.Domain.Documents;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.DigestPages;

internal sealed class FakeSources : IDigestSources
{
    public List<string> Calls { get; } = [];

    public string? Unavailable { get; set; }

    public DigestRead<DigestWorkoutWeek> Workouts(string person, DateOnly weekStart)
    {
        Calls.Add("workouts");
        return Unavailable is not null ? new DigestRead<DigestWorkoutWeek>.Unavailable(Unavailable) : new DigestRead<DigestWorkoutWeek>.Held(DigestFixture.Week());
    }

    public DigestRead<IReadOnlyList<BodyReading>> Readings(string person)
    {
        Calls.Add("body");
        return new DigestRead<IReadOnlyList<BodyReading>>.Held(DigestFixture.Flat(BodySignal.Sleep, 7.0));
    }

    public DigestRead<DigestMealsWeek> Meals(DateOnly weekStart)
    {
        Calls.Add("meals");
        return new DigestRead<DigestMealsWeek>.Held(DigestFixture.Meals());
    }

    public DigestRead<DigestNextWeek> Next(string person, DateOnly nextWeekStart)
    {
        Calls.Add("next");
        return new DigestRead<DigestNextWeek>.Held(DigestFixture.Next());
    }
}

internal sealed class MemoryStamps : IDigestStamps
{
    public HashSet<(string Person, DateOnly Saturday)> Set { get; } = [];

    public bool Has(string person, DateOnly saturday) => Set.Contains((person, saturday));

    public void Mark(string person, DateOnly saturday) => Set.Add((person, saturday));
}
