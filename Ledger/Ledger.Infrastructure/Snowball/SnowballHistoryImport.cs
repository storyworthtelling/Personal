using Index.Domain.Household;
using Index.Domain.Model;

namespace Index.Infrastructure.Snowball;

/// <summary>
/// Everything <see cref="Services.SnowballService.ImportHistoryIfNeededAsync"/> needs to bring the owner's
/// recorded history in from their Drive: where it comes from, the vault passphrase that opens it, which
/// install this is (her install never reads it), the device's memory that it has been imported, and the
/// pause between attempts. One instance for the app, because the pause is shared state.
///
/// <para>Deliberately holds an <see cref="ISnowballHistorySource"/> and not a Drive file: the file is
/// built for its own name where the source is, never resolved from the container.</para>
/// </summary>
public sealed class SnowballHistoryImport(
    ISnowballHistorySource source, Func<string?> passphrase, Func<InstallRole> role,
    ISnowballHistoryImportMemory memory, SnowballHistoryBackoff? backoff = null, TimeSpan? readTimeout = null)
{
    /// <summary>The page awaits the import, so a Drive call that never answers must not hold it open.</summary>
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(30);

    public ISnowballHistorySource Source { get; } = source;

    public Func<string?> Passphrase { get; } = passphrase;

    public Func<InstallRole> Role { get; } = role;

    public ISnowballHistoryImportMemory Memory { get; } = memory;

    public SnowballHistoryBackoff Backoff { get; } = backoff ?? new SnowballHistoryBackoff();

    public TimeSpan ReadTimeout { get; } = readTimeout ?? DefaultReadTimeout;
}
