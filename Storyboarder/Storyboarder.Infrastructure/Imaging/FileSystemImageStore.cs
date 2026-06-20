using Microsoft.Extensions.Options;
using Storyboarder.Domain.Imaging;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.Imaging;

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
        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    public byte[] Read(string absolutePath) => File.ReadAllBytes(absolutePath);

    public bool Exists(string absolutePath) => File.Exists(absolutePath);
}
