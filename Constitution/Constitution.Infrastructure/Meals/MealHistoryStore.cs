using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The cooked history (<see cref="MealHistoryCodec"/>) in one small file in the app's private storage, read and written whole and replaced
/// atomically. A file that cannot be read is reported and left exactly where it is, never carried on from as empty: the planner would plan
/// on a memory it lost. No file yet is a first install, which is an empty history that can be read.
/// </summary>
public sealed class MealHistoryStore : IMealHistoryStore
{
    public const string UnopenableReason = "The saved dinner history could not be opened.";

    private static readonly object FileLock = new();

    private readonly string path;

    public MealHistoryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public MealHistoryRead Load()
    {
        lock (FileLock)
        {
            return Read();
        }
    }

    public void Append(IEnumerable<CookedMeal> meals, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(meals);
        lock (FileLock)
        {
            var held = Read();
            if (held.UnreadableBecause is { } reason)
            {
                throw new InvalidOperationException(reason);
            }

            var cutoff = today.AddDays(-MealHistoryRetention.Days);
            var merged = held.Meals.Concat(meals)
                .Where(meal => meal.Day >= cutoff)
                .DistinctBy(meal => (meal.Recipe, meal.Day))
                .OrderBy(meal => meal.Day)
                .ThenBy(meal => meal.Recipe.Value, StringComparer.Ordinal)
                .ToList();
            if (File.Exists(path) && merged.SequenceEqual(held.Meals))
            {
                return;
            }

            AtomicFile.WriteAllText(path, MealHistoryCodec.Write(merged));
        }
    }

    private MealHistoryRead Read()
    {
        if (Directory.Exists(path))
        {
            // Something else sits where the file should be: it is there, and it cannot be read, which is not "nothing saved".
            return new MealHistoryRead([], UnopenableReason);
        }

        if (!File.Exists(path))
        {
            return new MealHistoryRead([], null);
        }

        try
        {
            return MealHistoryCodec.Read(File.ReadAllText(path));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The sentence is the reason; the exception can quote a path and the type is the caller's to log.
            return new MealHistoryRead([], UnopenableReason);
        }
    }
}
