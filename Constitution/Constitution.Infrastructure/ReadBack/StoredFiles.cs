using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The shared habits of the read-back's files. A file that cannot be read is renamed to <c>.unreadable</c> beside
/// where it was, not deleted and not written over: what is in it may be the only copy of an answer, and the next
/// write would otherwise replace whatever the damage left. A second file set aside never replaces the first: the first
/// keeps <c>.unreadable</c> and the later ones are numbered, so a second failure cannot destroy what the first kept.
/// </summary>
internal static class StoredFiles
{
    private static readonly Regex NotAWordPart = new("[^A-Za-z0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Far more than a phone will ever see; reaching it means something is failing in a loop, and that is worth a loud stop.
    private const int MostCopies = 1000;

    public static void SetAside(string path)
    {
        for (var copy = 1; copy <= MostCopies; copy++)
        {
            var target = copy == 1 ? path + ".unreadable" : $"{path}.unreadable.{copy}";
            try
            {
                File.Move(path, target, overwrite: false);
                return;
            }
            catch (IOException) when (File.Exists(target))
            {
                // That name is taken by an earlier copy, which is kept; the next number is tried.
            }
        }

        throw new IOException($"{MostCopies} unreadable copies of '{Path.GetFileName(path)}' are already kept; this one was not set aside.");
    }

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
