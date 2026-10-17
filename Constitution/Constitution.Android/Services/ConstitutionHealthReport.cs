using Codex.Ui.Voice;
using Constitution.Domain.Credentials;
using Constitution.Domain.Notices;
using Storyworthtelling.Voice.Health;

namespace Constitution.Services;

/// <summary>
/// Reads the credential-health report the HTTP choke points write
/// (<see cref="CredentialHealthStore"/>, the shared native-head store every FocusPhone head uses,
/// in this app's own private preferences) and states it to a <see cref="NoticeBoard"/> through
/// <see cref="ConnectionNotices"/>, so a notice goes when its cause does. Not wired to a screen yet;
/// the Waiting page is later.
/// </summary>
public static class ConstitutionHealthReport
{
    /// <summary>The credentials Constitution tracks, in the order they are stated.</summary>
    private static readonly string[] TrackedIds =
        [CredentialIds.SleepNumber, CredentialIds.Remarkable, CredentialIds.Claude, CredentialIds.Gemini];

    /// <summary>The current report.</summary>
    public static CredentialHealthReport Read() => new CredentialHealthStore().Load();

    /// <summary>One credential's health in Domain's words.</summary>
    public static ConnectionHealth HealthOf(CredentialHealthReport report, string credentialId) =>
        report.Get(credentialId).Status switch
        {
            CredentialStatus.Failing => ConnectionHealth.Rejected,
            CredentialStatus.Ok => ConnectionHealth.Working,
            _ => ConnectionHealth.Unknown,
        };

    /// <summary>States every tracked credential to the board; returns whether the board changed.</summary>
    public static bool Apply(NoticeBoard board, CredentialHealthReport report, DateTimeOffset now)
    {
        var changed = false;
        foreach (var id in TrackedIds)
        {
            changed |= ConnectionNotices.Apply(board, id, HealthOf(report, id), now);
        }

        return changed;
    }
}
