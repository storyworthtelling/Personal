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

    /// <summary>
    /// A credential has just been replaced (a new login saved, the tablet paired again): the notice its
    /// old failure raised is about a credential that is gone, so it goes. Returns whether one was waiting.
    /// </summary>
    public static bool Clear(NoticeBoard board, string credentialId)
    {
        ArgumentNullException.ThrowIfNull(board);
        return KindFor(credentialId) is not null && board.Resolve(CauseKey(credentialId));
    }

    /// <summary>
    /// What a stored health says about the credential held now. A judgement made before the credential
    /// was last replaced says nothing about the replacement, so it reads as <see cref="ConnectionHealth.Unknown"/>
    /// ("not yet judged") until a live call with the new one is recorded. A judgement made after the
    /// replacement stands.
    /// </summary>
    /// <param name="stored">What the health report holds.</param>
    /// <param name="checkedAt">When the report says it judged, or null when it does not say.</param>
    /// <param name="savedAt">When the credential was last saved here, or null when it never has been by hand.</param>
    public static ConnectionHealth Judge(ConnectionHealth stored, DateTimeOffset? checkedAt, DateTimeOffset? savedAt)
    {
        if (savedAt is null || stored == ConnectionHealth.Unknown)
        {
            return stored;
        }

        return checkedAt is { } judged && judged > savedAt.Value ? stored : ConnectionHealth.Unknown;
    }
}
