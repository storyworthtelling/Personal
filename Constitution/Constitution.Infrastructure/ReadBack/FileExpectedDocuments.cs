using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The delivered documents still to be read, in one file in the app's private storage, read and written whole on every
/// call (the worker and the app each build their own and share the file). Delivery records a document once the tablet
/// has taken it; the read-back retires it once its page is trashed.
///
/// <para>A file that cannot be read is set aside (<see cref="StoredFiles.SetAside"/>) and the store carries on empty.
/// That costs the reading of the pages it described, not the pages: they stay on the tablet, untrashed.</para>
/// </summary>
public sealed class FileExpectedDocuments : IExpectedDocuments
{
    private static readonly object FileLock = new();

    private readonly string path;

    public FileExpectedDocuments(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public IReadOnlyList<OutstandingDocument> Outstanding()
    {
        lock (FileLock)
        {
            return Load();
        }
    }

    public void Record(ExpectedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (FileLock)
        {
            var kept = Load().Where(item => item.Document.Name != document.Name).ToList();
            kept.Add(new OutstandingDocument(document, false));
            Save(kept);
        }
    }

    public void MarkSaved(string documentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        lock (FileLock)
        {
            var all = Load();
            if (all.Any(item => item.Document.Name == documentName))
            {
                Save(all.Select(item => item.Document.Name == documentName ? item with { Saved = true } : item));
            }
        }
    }

    public void Retire(string documentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        lock (FileLock)
        {
            var all = Load();
            if (all.Any(item => item.Document.Name == documentName))
            {
                Save(all.Where(item => item.Document.Name != documentName));
            }
        }
    }

    private IReadOnlyList<OutstandingDocument> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return ExpectedDocumentCodec.Decode(File.ReadAllText(path));
        }
        catch (FormatException)
        {
            StoredFiles.SetAside(path);
            return [];
        }
    }

    private void Save(IEnumerable<OutstandingDocument> documents) => AtomicFile.WriteAllText(path, ExpectedDocumentCodec.Encode(documents));
}
