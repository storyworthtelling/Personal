using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The meal pass's stamp (<see cref="MealPassStampCodec"/>) in one small file in the app's private storage, read and replaced whole (atomically). A file that is
/// there and cannot be read is <see cref="MealPassStampRead.Unreadable"/>, never an empty stamp; unlike the weeks and the decisions, <see cref="Save"/> then simply
/// replaces it, because the stamp holds no fact the household needs, only when the pass last looked, and a lost one costs one extra idempotent run.
/// </summary>
public sealed class FileMealPassStamps : IMealPassStamps
{
    public const string UnopenableReason = "The meal pass stamp could not be opened.";

    private static readonly object FileLock = new();

    private readonly string path;

    public FileMealPassStamps(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public MealPassStampRead Load()
    {
        lock (FileLock)
        {
            if (Directory.Exists(path))
            {
                return new MealPassStampRead.Unreadable(UnopenableReason);
            }

            if (!File.Exists(path))
            {
                return new MealPassStampRead.NothingYet();
            }

            try
            {
                return MealPassStampCodec.Decode(File.ReadAllText(path));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // The sentence is the reason; the exception can quote a path and the type is the caller's to log.
                return new MealPassStampRead.Unreadable(UnopenableReason);
            }
        }
    }

    public void Save(MealPassStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        lock (FileLock)
        {
            AtomicFile.WriteAllText(path, MealPassStampCodec.Encode(stamp));
        }
    }
}
