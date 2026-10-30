using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The decided weeks (<see cref="MealDecisionRecordCodec"/>) in one small file in the app's private storage, read and replaced whole (atomically). A file that
/// is there and cannot be read is <see cref="MealDecisionRecordRead.Unreadable"/>, never an empty record, and <see cref="Save"/> sets it aside
/// (<c>UnreadableCopies</c>, which never replaces an earlier copy) before it writes a new one.
/// </summary>
public sealed class FileMealDecisionRecords : IMealDecisionRecords
{
    public const string UnopenableReason = "The saved meal decisions could not be opened.";

    private static readonly object FileLock = new();

    private readonly string path;

    public FileMealDecisionRecords(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public MealDecisionRecordRead Load()
    {
        lock (FileLock)
        {
            return Read();
        }
    }

    public void Save(MealDecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (FileLock)
        {
            if (Read() is MealDecisionRecordRead.Unreadable)
            {
                UnreadableCopies.SetAside(path);
            }

            AtomicFile.WriteAllText(path, MealDecisionRecordCodec.Encode(record));
        }
    }

    private MealDecisionRecordRead Read()
    {
        if (Directory.Exists(path))
        {
            return new MealDecisionRecordRead.Unreadable(UnopenableReason);
        }

        if (!File.Exists(path))
        {
            return new MealDecisionRecordRead.NothingYet();
        }

        try
        {
            return MealDecisionRecordCodec.Decode(File.ReadAllText(path));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The sentence is the reason; the exception can quote a path and the type is the caller's to log.
            return new MealDecisionRecordRead.Unreadable(UnopenableReason);
        }
    }
}
