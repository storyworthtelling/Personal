using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Voice.Tests.Health;

/// <summary>
/// When Google turns a phone's sign-in away, every app says so (audit 11 §4, owner's decision: a Settings row and a
/// dog-ear notice). The lockdown hides Google's own "sign in again" notification, so the vault, backups and log
/// uploads used to stop with no word anywhere.
/// </summary>
public class GoogleAccountNoticeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARefusedSignInRaisesOneNoticeThatSaysWhatIsWaitingAndWhereToGo()
    {
        var report = CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.GoogleAccount, "consent needed", Now);

        var notice = GoogleAccountNotice.For(report);

        Assert.NotNull(notice);
        Assert.Equal("Sign in to Google again", notice!.Headline);
        Assert.Contains("Settings", notice.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("consent needed", notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkingOrUntriedSignInRaisesNothing()
    {
        Assert.Null(GoogleAccountNotice.For(CredentialHealthReport.Empty));
        Assert.Null(GoogleAccountNotice.For(CredentialHealthReport.Empty.MarkOk(SharedCredentialIds.GoogleAccount, Now)));
    }

    [Fact]
    public void TheSettingsRowShowsOnlyWhileTheSignInIsRefused()
    {
        var refused = CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.GoogleAccount, "x", Now);

        Assert.True(GoogleAccountNotice.ShowsSignInAgain(refused));
        Assert.False(GoogleAccountNotice.ShowsSignInAgain(refused.MarkOk(SharedCredentialIds.GoogleAccount, Now)));
        Assert.Equal("google-account", SharedCredentialIds.GoogleAccount);
    }
}

public class GoogleAccountReporterTests
{
    private sealed class Sink : ICredentialHealthSink
    {
        public List<string> Lines { get; } = [];
        public void MarkOk(string credentialId) => Lines.Add($"ok {credentialId}");
        public void MarkFailing(string credentialId, string message) => Lines.Add($"failing {credentialId}");
    }

    [Fact]
    public void ATokenMarksTheSignInOkAndARefusalMarksItFailing()
    {
        var sink = new Sink();
        var report = GoogleAccountNotice.Reporter(sink);

        report(null);
        report("Google refused the sign-in.");

        Assert.Equal(["ok google-account", "failing google-account"], sink.Lines);
    }
}
