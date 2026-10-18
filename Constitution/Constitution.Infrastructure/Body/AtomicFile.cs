using System.Text;

namespace Constitution.Infrastructure.Body;

/// <summary>
/// Writes a file so that a reader sees the old content or the new and never half of either: the text goes to a
/// sibling, is flushed to the disk, and then replaces the file in one step. A process killed mid-write (a worker
/// that Android reclaims) therefore cannot leave a Body file torn.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var staging = path + ".tmp";
        using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        File.Move(staging, path, overwrite: true);
    }
}
