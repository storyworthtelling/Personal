using System.Text;
namespace Storyworthtelling.Household;

/// <summary>
/// Where a household document is kept. <see cref="LocalHouseholdStore"/> is the phone's own copy;
/// Release D's Drive store (the shared, encrypted file, finding §2.7) implements the same seam, so the
/// screens never know which one they read.
/// </summary>
public interface IHouseholdStore
{
    /// <summary>The stored household; empty when nothing has been stored yet. A damaged file throws.</summary>
    Task<HouseholdDocument> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(HouseholdDocument document, CancellationToken cancellationToken);
}

/// <summary>
/// The household as one file in the app's data directory, written atomically: the new text goes to a
/// temporary file which then replaces the real one, so an interrupted write leaves the previous
/// household whole. Saves inside one process are serialised. A damaged file is refused (it throws
/// <see cref="HouseholdFormatException"/>) rather than read as an empty household that the next save
/// would write over it for good.
/// </summary>
public sealed class LocalHouseholdStore : IHouseholdStore
{
    private readonly string directory;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <param name="directory">The app's data directory.</param>
    /// <param name="fileName">The file's name there. It is the address of the household on this install, so it is the app's to freeze.</param>
    public LocalHouseholdStore(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        this.directory = directory;
        FileName = fileName;
        path = Path.Combine(directory, fileName);
    }

    public string FileName { get; }

    public async Task<HouseholdDocument> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return HouseholdDocument.Empty;
            }

            var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            return HouseholdDocumentCodec.Deserialize(text);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(HouseholdDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = HouseholdDocumentCodec.Serialize(document);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await AtomicFile.WriteAsync(directory, path, text, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>Temp file, flushed, then moved over the real one — the rename is atomic on one file system.</summary>
internal static class AtomicFile
{
    public static async Task WriteAsync(string directory, string path, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
