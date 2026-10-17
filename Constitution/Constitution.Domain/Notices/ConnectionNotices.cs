using Constitution.Domain.Credentials;

namespace Constitution.Domain.Notices;

/// <summary>What the last live call with a credential said about it, in Domain's own words.</summary>
public enum ConnectionHealth
{
    /// <summary>Never exercised, or restored and not yet used. Says nothing either way.</summary>
    Unknown,

    /// <summary>The most recent call with it succeeded.</summary>
    Working,

    /// <summary>The most recent call with it was rejected.</summary>
    Rejected,
}

/// <summary>
/// Turns a credential's health into the notice waiting behind the dog-ear, and takes the notice away
/// when the cause goes. Pure: the Android head reads the shared credential-health report and states
/// each credential here; nothing in this type knows how the report is stored.
///
/// <para>Only the credentials whose failure the owner can act on from the corner have a notice: the bed
/// login (<see cref="NoticeKind.BedOffline"/>) and the reMarkable token
/// (<see cref="NoticeKind.ReMarkableDelivery"/>). The Claude and Gemini keys, and Google, have none; a
/// rejected key shows on the Settings field instead, which already reveals itself for exactly that.</para>
/// </summary>
public static class ConnectionNotices
{
    /// <summary>The notice a rejected credential raises, or null when it has none.</summary>
    public static NoticeKind? KindFor(string credentialId) => credentialId switch
    {
        CredentialIds.SleepNumber => NoticeKind.BedOffline,
        CredentialIds.Remarkable => NoticeKind.ReMarkableDelivery,
        _ => null,
    };

    /// <summary>The key the board files a credential's notice under, so raising and resolving name the same cause.</summary>
    public static string CauseKey(string credentialId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        return "credential:" + credentialId;
    }

    /// <summary>
    /// States one credential's health to the board. Rejected raises its notice (idempotent), Working
    /// resolves it, Unknown changes nothing. Returns whether the board changed.
    /// </summary>
    public static bool Apply(NoticeBoard board, string credentialId, ConnectionHealth health, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        var kind = KindFor(credentialId);
        if (kind is null)
        {
            return false;
        }

        return health switch
        {
            ConnectionHealth.Rejected => board.Raise(kind.Value, CauseKey(credentialId), now),
            ConnectionHealth.Working => board.Resolve(CauseKey(credentialId)),
            _ => false,
        };
    }
}
