using System.Security.Cryptography;

namespace Storyworthtelling.Diagnostics;

/// <summary>Which phone a batch came from (plan §3.2), from the role the app already derives.</summary>
public static class LogPhone
{
    public const string Owner = "owner";
    public const string Partner = "partner";

    /// <summary>Nobody is signed in yet, so whose phone this is cannot be read.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// Who is uploading: the app, a random per-install id, which phone, and which build. The install id is
/// random and nothing else — never a serial number, an account or an address.
/// </summary>
public sealed record LogInstall(string App, string InstallId, string Phone, string AppVersion, long VersionCode)
{
    /// <summary>Twelve random hex digits: enough to tell installs apart, nothing to identify a device by.</summary>
    public static string NewInstallId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
}

/// <summary>The header every batch carries (plan §3.2).</summary>
public sealed record LogBatchHeader(
    int Schema,
    string App,
    string InstallId,
    string Phone,
    string AppVersion,
    long VersionCode,
    int LogKeyEpoch,
    DateTimeOffset FirstUtc,
    DateTimeOffset LastUtc,
    IReadOnlyList<long> Segments);

/// <summary>One sealed segment's text, labelled with the log it came from.</summary>
public sealed record LogBatchPart(long Sequence, string Log, string Text);

/// <summary>
/// One Drive file's plaintext: a header and up to <see cref="MaxPlaintextBytes"/> of sealed segments.
/// <see cref="LogBatchCodec"/> turns it into the JSON → gzip → base64 string that VaultEnvelope seals.
/// </summary>
public sealed record LogBatch(LogBatchHeader Header, IReadOnlyList<LogBatchPart> Parts)
{
    public const int CurrentSchema = 1;

    /// <summary>256 KB of plaintext per file (plan §10, tuned from the logs later).</summary>
    public const int MaxPlaintextBytes = 256 * 1024;

    /// <summary>
    /// Builds a batch from sealed segments, oldest first. The batch is dated by its segments' seal times,
    /// since a log line's own stamp is the friendly 12-hour form without a year.
    /// </summary>
    public static LogBatch Build(LogInstall install, IReadOnlyList<SealedSegment> segments, Func<SealedSegment, string> read)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("A batch needs at least one sealed segment.", nameof(segments));
        }
        var ordered = segments.OrderBy(s => s.Sequence).ToList();
        return new LogBatch(
            new LogBatchHeader(
                CurrentSchema,
                install.App,
                install.InstallId,
                install.Phone,
                install.AppVersion,
                install.VersionCode,
                LogKey.Epoch,
                ordered.Min(s => s.SealedUtc).ToUniversalTime(),
                ordered.Max(s => s.SealedUtc).ToUniversalTime(),
                ordered.Select(s => s.Sequence).ToList()),
            ordered.Select(s => new LogBatchPart(s.Sequence, s.LogName, read(s))).ToList());
    }
}
