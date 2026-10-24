using System.Text;

namespace Pulp.Infrastructure.Household;

/// <summary>
/// Temp file, flushed, then moved over the real one — the rename is atomic on one file system. The
/// household's own store writes this way inside <c>Storyworthtelling.Household</c>; this is the
/// synchronous form <see cref="LocalInstallStore"/> uses, which the shared library does not carry.
/// </summary>
internal static class AtomicFile
{
    public static void Write(string directory, string path, string text)
    {
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
