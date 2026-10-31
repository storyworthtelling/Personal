using Constitution.Domain.Placement;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The keys the weekly run has written to the calendar (<see cref="WrittenKeys"/>), in one small file in the app's private
/// storage, read and written whole and replaced atomically. A file that cannot be read is reported and left exactly where it
/// is: read as empty, every session would look unwritten and be written again.
/// </summary>
public sealed class FileWrittenKeys : IWrittenKeyStore
{
    private static readonly object FileLock = new();

    private readonly string path;

    public FileWrittenKeys(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public WrittenKeysRead Load()
    {
        lock (FileLock)
        {
            return File.Exists(path) ? WrittenKeysCodec.Decode(File.ReadAllText(path)) : WrittenKeysRead.Of(WrittenKeys.Empty);
        }
    }

    public void Save(WrittenKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        lock (FileLock)
        {
            AtomicFile.WriteAllText(path, WrittenKeysCodec.Encode(keys));
        }
    }
}
