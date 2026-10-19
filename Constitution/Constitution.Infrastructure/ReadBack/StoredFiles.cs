using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The shared habits of the read-back's files. A file that cannot be read is renamed to <c>.unreadable</c> beside
/// where it was, not deleted and not written over: what is in it may be the only copy of an answer, and the next
/// write would otherwise replace whatever the damage left.
/// </summary>
internal static class StoredFiles
{
    private static readonly Regex NotAWordPart = new("[^A-Za-z0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static void SetAside(string path) => File.Move(path, path + ".unreadable", overwrite: true);

    /// <summary>A file name for a document title: readable at a glance, and unique to the exact title by its hash.</summary>
    public static string NameFor(string title)
    {
        var readable = NotAWordPart.Replace(title, "-").Trim('-');
        if (readable.Length > 48)
        {
            readable = readable[..48].TrimEnd('-');
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(title)))[..12].ToLowerInvariant();
        return readable.Length == 0 ? hash : readable + "-" + hash;
    }
}
