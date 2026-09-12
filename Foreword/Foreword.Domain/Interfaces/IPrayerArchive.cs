using Foreword.Domain.Prayer;

namespace Foreword.Domain.Interfaces;

/// <summary>
/// The private archive of what is held, what rotates, and what was written.
///
/// <para>A seam so the morning's delivery can be tested without Drive, and so the one
/// implementation that talks to Google stays thin.</para>
/// </summary>
public interface IPrayerArchive
{
    Task<PrayerArchiveRead> LoadAsync(string passphrase, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the archive back. Implementations <b>refuse</b> rather than overwrite when the file
    /// already there cannot be read — it is the only copy of what the owner wrote.
    /// </summary>
    Task SaveAsync(PrayerArchive archive, string passphrase, CancellationToken cancellationToken);
}
