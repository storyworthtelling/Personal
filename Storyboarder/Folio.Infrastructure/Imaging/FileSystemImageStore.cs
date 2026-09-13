using Microsoft.Extensions.Options;
using Folio.Domain.Imaging;
using Folio.Infrastructure.Configuration;

namespace Folio.Infrastructure.Imaging;

// Stores generated images and PDFs under the configured output root. Paths are deterministic so a
// retry overwrites cleanly and a stored path stays valid across restarts.
public sealed class FileSystemImageStore : IImageStore
{
    private readonly string root;

    public FileSystemImageStore(IOptions<StorageOptions> options)
    {
        root = Path.GetFullPath(options.Value.OutputRoot);
    }

    public string Save(string relativePath, byte[] content)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Could not determine a directory for '{relativePath}'.", nameof(relativePath));
        Directory.CreateDirectory(directory);

        // Write to a uniquely-named temp file in the same directory, then atomically rename it over the
        // target. A background worker force-stopped mid-write (WorkManager's ~10-minute hard cap while
        // saving a large volume PDF) then leaves only a stray .tmp, never a truncated target — so the
        // next open of the volume can't hit "StartXRef table could not be found" and wedge the novel.
        // The unique name also makes two writers to the same path resolve to one clean rename rather than
        // interleaving bytes into a single file. Rename is atomic within a filesystem; temp and target
        // share a directory, so it never degrades to a copy.
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, content);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        return fullPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of the temp file; the original write failure is what matters and is
            // rethrown by the caller.
        }
    }

    public byte[] Read(string absolutePath) => File.ReadAllBytes(absolutePath);

    public bool Exists(string absolutePath) => File.Exists(absolutePath);

    public void Delete(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
        else if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
