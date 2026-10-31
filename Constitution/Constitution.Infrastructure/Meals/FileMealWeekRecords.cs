using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The planned weeks (<see cref="MealWeekRecordCodec"/>) in one small file in the app's private storage, read and replaced whole
/// (atomically). A file that is there and cannot be read is <see cref="MealWeekRecordRead.Unreadable"/>, never an empty record, and
/// <see cref="Save"/> sets it aside (<c>UnreadableCopies</c>, which never replaces an earlier copy) before it writes a new one.
/// </summary>
public sealed class FileMealWeekRecords : IMealWeekRecords
{
    public const string UnopenableReason = "The saved meal weeks could not be opened.";

    private static readonly object FileLock = new();

    private readonly string path;

    public FileMealWeekRecords(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public MealWeekRecordRead Load()
    {
        lock (FileLock)
        {
            return Read();
        }
    }

    public void Save(MealWeekRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (FileLock)
        {
            if (Read() is MealWeekRecordRead.Unreadable)
            {
                UnreadableCopies.SetAside(path);
            }

            AtomicFile.WriteAllText(path, MealWeekRecordCodec.Encode(record));
        }
    }

    private MealWeekRecordRead Read()
    {
        if (Directory.Exists(path))
        {
            return new MealWeekRecordRead.Unreadable(UnopenableReason);
        }

        if (!File.Exists(path))
        {
            return new MealWeekRecordRead.NothingYet();
        }

        try
        {
            return MealWeekRecordCodec.Decode(File.ReadAllText(path));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The sentence is the reason; the exception can quote a path and the type is the caller's to log.
            return new MealWeekRecordRead.Unreadable(UnopenableReason);
        }
    }
}
