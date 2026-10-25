using Constitution.Domain.Meals;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The estimates, in one small file on this install (C-6.6.2). The head puts it under the app's no-backup folder, so neither the
/// platform's backup nor the household's files ever hold it, and nothing here can reach Drive. A file that cannot be read says so
/// (<see cref="AteStoreRead.Unreadable"/>) and shows nothing; the next record keeps the unreadable copy aside rather than writing
/// over it, so a damaged file is never silently lost.
/// </summary>
public sealed class FileAteEstimates : IAteEstimates
{
    private static readonly object FileLock = new();

    private readonly string path;

    public FileAteEstimates(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public AteStoreRead Read()
    {
        lock (FileLock)
        {
            return Load();
        }
    }

    public AteEstimate? Record(AteEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        lock (FileLock)
        {
            var current = LoadForWriting();
            var previous = current.FirstOrDefault(item => item.Day == estimate.Day && item.Meal == estimate.Meal);
            var next = current.Where(item => item.Day != estimate.Day || item.Meal != estimate.Meal).Append(estimate);
            Save(AteCodec.Trim(next, estimate.Day));
            return previous;
        }
    }

    public void Withdraw(AteEstimate recorded, AteEstimate? previous)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        lock (FileLock)
        {
            var next = LoadForWriting().Where(item => item.Day != recorded.Day || item.Meal != recorded.Meal);
            Save(previous is null ? next.ToList() : next.Append(previous).ToList());
        }
    }

    private AteStoreRead Load()
    {
        if (!File.Exists(path))
        {
            return new AteStoreRead([], false);
        }

        return AteCodec.Read(File.ReadAllText(path));
    }

    // A file that cannot be read is moved aside first, so what is written next never destroys the only copy of it.
    private IReadOnlyList<AteEstimate> LoadForWriting()
    {
        var read = Load();
        if (read.Unreadable)
        {
            File.Move(path, path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + ".unreadable", overwrite: true);
        }

        return read.Items;
    }

    private void Save(IEnumerable<AteEstimate> items) => AtomicFile.WriteAllText(path, AteCodec.Write(items.OrderBy(item => item.Day).ThenBy(item => item.Meal)));
}
