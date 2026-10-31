using Constitution.Domain.Body;

namespace Constitution.Domain.Documents;

/// <summary>What one live store answered: the value, or a typed reason it could not (an unreadable file is not an empty week).</summary>
public abstract record DigestRead<T>
{
    private DigestRead()
    {
    }

    public sealed record Held(T Value) : DigestRead<T>;

    public sealed record Unavailable(string Reason) : DigestRead<T>;
}

/// <summary>The four live stores the Saturday digest is built from, behind one seam so the assembly is tested with fakes.</summary>
public interface IDigestSources
{
    /// <summary>The person's sessions in the week starting <paramref name="weekStart"/>, how each ended, and what moved.</summary>
    DigestRead<DigestWorkoutWeek> Workouts(string person, DateOnly weekStart);

    /// <summary>The person's Body readings, from this phone's own store. They go to that person's own tablet and nowhere else.</summary>
    DigestRead<IReadOnlyList<BodyReading>> Readings(string person);

    /// <summary>The household's meals for the week starting <paramref name="weekStart"/>: the same for both adults.</summary>
    DigestRead<DigestMealsWeek> Meals(DateOnly weekStart);

    /// <summary>Next week as it stands for the person.</summary>
    DigestRead<DigestNextWeek> Next(string person, DateOnly nextWeekStart);
}

public abstract record DigestAssembly
{
    private DigestAssembly()
    {
    }

    public sealed record Ready(SaturdayDigestModel Model) : DigestAssembly;

    /// <summary>A store could not answer; nothing is built, and the next pass tries again.</summary>
    public sealed record NotReady(string Reason) : DigestAssembly;
}

public static class DigestAssembler
{
    public static DigestAssembly Assemble(string person, DateOnly saturday, IDigestSources sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        ArgumentNullException.ThrowIfNull(sources);

        // The planning week runs Sunday to Saturday, so the digest's week starts six days before its Saturday.
        var weekStart = saturday.AddDays(-6);
        var nextStart = saturday.AddDays(1);

        var read = sources.Workouts(person, weekStart);
        if (read is DigestRead<DigestWorkoutWeek>.Unavailable noWorkouts)
        {
            return new DigestAssembly.NotReady(noWorkouts.Reason);
        }

        var workouts = ((DigestRead<DigestWorkoutWeek>.Held)read).Value;
        return Rest(person, saturday, sources, workouts, weekStart, nextStart);
    }

    private static DigestAssembly Rest(string person, DateOnly saturday, IDigestSources sources, DigestWorkoutWeek workouts, DateOnly weekStart, DateOnly nextStart)
    {
        var body = sources.Readings(person);
        if (body is DigestRead<IReadOnlyList<BodyReading>>.Unavailable noBody)
        {
            return new DigestAssembly.NotReady(noBody.Reason);
        }

        var meals = sources.Meals(weekStart);
        if (meals is DigestRead<DigestMealsWeek>.Unavailable noMeals)
        {
            return new DigestAssembly.NotReady(noMeals.Reason);
        }

        var next = sources.Next(person, nextStart);
        if (next is DigestRead<DigestNextWeek>.Unavailable noNext)
        {
            return new DigestAssembly.NotReady(noNext.Reason);
        }

        return new DigestAssembly.Ready(new SaturdayDigestModel(
            person.Trim(),
            saturday,
            workouts,
            ((DigestRead<IReadOnlyList<BodyReading>>.Held)body).Value,
            ((DigestRead<DigestMealsWeek>.Held)meals).Value,
            ((DigestRead<DigestNextWeek>.Held)next).Value));
    }
}
