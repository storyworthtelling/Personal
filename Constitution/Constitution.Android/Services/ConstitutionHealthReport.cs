using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain.Credentials;
using Constitution.Domain.Notices;
using Constitution.Settings;
using Storyworthtelling.Voice.Health;

namespace Constitution.Services;

/// <summary>
/// Reads the credential-health report the HTTP choke points write
/// (<see cref="CredentialHealthStore"/>, the shared native-head store every FocusPhone head uses,
/// in this app's own private preferences) and states it to a <see cref="NoticeBoard"/> through
/// <see cref="ConnectionNotices"/>, so a notice goes when its cause does. Settings and the Body tab read the report (<see cref="Read"/>, <see cref="HealthOf"/>) for their credential
/// lines; <see cref="Apply"/>, which states every tracked credential to the board at once, has no caller yet.
/// </summary>
public static class ConstitutionHealthReport
{
    /// <summary>The credentials Constitution tracks, in the order they are stated.</summary>
    private static readonly string[] TrackedIds =
        [CredentialIds.SleepNumber, CredentialIds.Remarkable, CredentialIds.Claude, CredentialIds.Gemini];

    /// <summary>The current report.</summary>
    public static CredentialHealthReport Read() => new CredentialHealthStore().Load();

    /// <summary>
    /// One credential's health in Domain's words, as it bears on the credential held now: a judgement
    /// recorded before <paramref name="savedAt"/> belongs to the one that was replaced and reads as
    /// unknown (<see cref="ConnectionNotices.Judge"/>).
    /// </summary>
    public static ConnectionHealth HealthOf(CredentialHealthReport report, string credentialId, DateTimeOffset? savedAt)
    {
        var health = report.Get(credentialId);
        var stored = health.Status switch
        {
            CredentialStatus.Failing => ConnectionHealth.Rejected,
            CredentialStatus.Ok => ConnectionHealth.Working,
            _ => ConnectionHealth.Unknown,
        };
        return ConnectionNotices.Judge(stored, health.CheckedAtUtc, savedAt);
    }

    /// <summary>
    /// A hand-entered credential was just replaced (a login saved, the tablet paired): its old failure no
    /// longer describes it. The moment is recorded, so a stored rejection older than it stops counting,
    /// and the notice that rejection raised comes off the board at once. The shared health store has no
    /// way to forget one credential, and the new one has not been judged yet, so "not yet judged" is
    /// recorded here rather than a success nobody saw.
    /// </summary>
    public static void CredentialReplaced(Context context, string credentialId)
    {
        new ConstitutionCredentialSaves(context).MarkSaved(credentialId, DateTimeOffset.UtcNow);
        ConnectionNoticeBoard.With(board => ConnectionNotices.Clear(board, credentialId));
    }

    /// <summary>States every tracked credential to the board; returns whether the board changed.</summary>
    public static bool Apply(NoticeBoard board, CredentialHealthReport report, Func<string, DateTimeOffset?> savedAt, DateTimeOffset now)
    {
        var changed = false;
        foreach (var id in TrackedIds)
        {
            changed |= ConnectionNotices.Apply(board, id, HealthOf(report, id, savedAt(id)), now);
        }

        return changed;
    }
}
