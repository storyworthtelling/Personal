using System.Security.Cryptography;
using System.Text;

namespace Index.Domain.Model;

/// <summary>
/// When the recorded history still has to be imported.
///
/// <para>The history is "already here" when ANY snapshot is dated on or before the history's last recorded
/// week. The weekly rows the app writes itself are dated after that week by construction, so they can never
/// close the gate — which is the point: the page writes this week's snapshot straight after trying the
/// import, and when the import's read failed (no file yet, no passphrase, no network) a gate that "any
/// snapshot closes" would have been shut for good with nothing imported.</para>
/// </summary>
public static class SnowballHistoryGate
{
    public static bool IsAlreadyPresent(DateOnly? earliestSnapshotWeek, DateOnly lastRecordedWeek) =>
        earliestSnapshotWeek is { } earliest && earliest <= lastRecordedWeek;
}

/// <summary>
/// An in-memory pause between attempts to read the history from Drive, so a page that is opened over and
/// over — or a network that is flapping — asks at most once per pause. Keyed, because the answer to a
/// wrong passphrase changes the moment the passphrase does: a new key is never held back by the old one's
/// attempt. Holds only a hash of the key, never the key (the key is the vault passphrase). Nothing here
/// is persisted; a restart starts fresh.
/// </summary>
public sealed class SnowballHistoryBackoff
{
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMinutes(5);

    private readonly TimeSpan pause;
    private readonly object gate = new();
    private string? lastKeyHash;
    private DateTimeOffset? lastAttempt;

    public SnowballHistoryBackoff(TimeSpan? pause = null)
    {
        this.pause = pause ?? DefaultPause;
    }

    public bool MayAttempt(DateTimeOffset now, string? key)
    {
        lock (gate)
        {
            return lastAttempt is null || lastKeyHash != Hash(key) || now - lastAttempt.Value >= pause;
        }
    }

    public void RecordAttempt(DateTimeOffset now, string? key)
    {
        lock (gate)
        {
            lastKeyHash = Hash(key);
            lastAttempt = now;
        }
    }

    private static string Hash(string? key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key ?? string.Empty)));
}
