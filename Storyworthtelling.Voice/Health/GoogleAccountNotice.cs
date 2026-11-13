namespace Storyworthtelling.Voice.Health;

/// <summary>A refused Google sign-in, in words: the headline the dog-ear wears and the line behind it.</summary>
public sealed record SignInNotice(string Headline, string Detail);

/// <summary>
/// What every app says when Google turns its sign-in away (audit 11 §4; owner's decision: a Settings row and a dog-ear
/// notice). The locked phone's disabled status bar hides Google's own "sign in again" notification, so the vault,
/// the backups and the log uploads used to stop with no word anywhere. The token code marks
/// <see cref="SharedCredentialIds.GoogleAccount"/> failing when Google refuses and Ok when a token comes back; this
/// reads that mark. The failure's own message is never shown.
/// </summary>
public static class GoogleAccountNotice
{
    /// <summary>The dog-ear key every app sets this notice under, so it is one notice however many parts raise it.</summary>
    public const string Key = SharedCredentialIds.GoogleAccount;

    public static SignInNotice? For(CredentialHealthReport report) =>
        ShowsSignInAgain(report)
            ? new SignInNotice(
                "Sign in to Google again",
                "Google turned this phone's sign-in away, so Drive is waiting: the vault, backups and log uploads. Open Settings and choose Sign in again.")
            : null;

    /// <summary>Whether Settings shows the "Sign in again" row: only while the last word from Google was a refusal.</summary>
    public static bool ShowsSignInAgain(CredentialHealthReport report) =>
        report.Get(SharedCredentialIds.GoogleAccount).Status == CredentialStatus.Failing;

    /// <summary>
    /// The token source's health callback for an app's store: null marks <see cref="SharedCredentialIds.GoogleAccount"/>
    /// Ok, a reason marks it failing. Handed to <c>DeviceAccountTokens</c>.
    /// </summary>
    public static Action<string?> Reporter(ICredentialHealthSink sink) => reason =>
    {
        if (reason is null)
        {
            sink.MarkOk(SharedCredentialIds.GoogleAccount);
        }
        else
        {
            sink.MarkFailing(SharedCredentialIds.GoogleAccount, reason);
        }
    };
}
