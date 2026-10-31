using Constitution.Domain.Placement;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The numbers issued to the week's sessions (<see cref="KeyAssignments"/>), in one small file in the app's private storage, read
/// and written whole on every call and replaced atomically. It holds names and nothing else: a person, a week, a kind, a weekday and
/// a number.
///
/// <para><b>A file that cannot be read is reported and left exactly where it is</b>, never set aside and never carried on from as
/// empty. Read as empty, every session would be handed a fresh number and the events already on the calendar would be held under
/// keys that name other days; the weekly run instead stops placing until a person has looked, and says so
/// (<c>PlacementRun.BlockedBecause</c>). Nothing is lost by waiting: no event is written and no page is taken off.</para>
/// </summary>
public sealed class FileKeyAssignments : IKeyAssignmentStore
{
    private static readonly object FileLock = new();

    private readonly string path;

    public FileKeyAssignments(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public KeyAssignmentsRead Load()
    {
        lock (FileLock)
        {
            return File.Exists(path) ? KeyAssignmentsCodec.Decode(File.ReadAllText(path)) : KeyAssignmentsRead.Of(KeyAssignments.Empty);
        }
    }

    public void Save(KeyAssignments assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        lock (FileLock)
        {
            AtomicFile.WriteAllText(path, KeyAssignmentsCodec.Encode(assignments));
        }
    }
}
