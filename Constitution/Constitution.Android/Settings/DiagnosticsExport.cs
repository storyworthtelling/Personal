using Android.Content;
using Codex.Ui.Voice;
using Constitution.Body;
using Constitution.Domain.Credentials;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Notices;
using Constitution.ReadBack;
using Constitution.Services;
using CredentialHealthReport = Storyworthtelling.Voice.Health.CredentialHealthReport;

namespace Constitution.Settings;

/// <summary>
/// Builds a real <see cref="DiagnosticsSnapshot"/> from what this install can know, renders it, and
/// hands the text to the share sheet. The last forty lines of the Body and vault trails ride along, each
/// through <see cref="TrailLines.Filter"/> (review S8), so a device test can bring back what the platform and
/// the bed answered. What it cannot know yet is stated as a shape, not guessed:
/// every part but Body's sync reports <see cref="DiagnosticsOutcome.NeverRan"/> until its worker records outcomes,
/// the role is <see cref="InstallRole.Unknown"/> until the household file says, and the counts that
/// need the plan store are zero. Credentials come from the health report, by state only.
/// </summary>
public static class DiagnosticsExport
{
    public static DiagnosticsSnapshot Build(Context context)
    {
        var version = context.PackageManager?.GetPackageInfo(context.PackageName!, 0)?.VersionName
            ?? throw new InvalidOperationException("The package manager gave no version name.");
        var connections = new ConstitutionConnections(context);
        var report = ConstitutionHealthReport.Read();
        var saves = new ConstitutionCredentialSaves(context);

        var credentials = new[]
        {
            Credential(ConnectionIds.SleepNumber, CredentialIds.SleepNumber,
                connections.SleepNumberLogin.Length > 0 && connections.SleepNumberPassword.Length > 0, report, saves),
            Credential(ConnectionIds.ReMarkable, CredentialIds.Remarkable, connections.RemarkableDeviceToken.Length > 0, report, saves),
            Credential(ConnectionIds.Claude, CredentialIds.Claude, connections.ClaudeApiKey.Length > 0, report, saves),
            Credential(ConnectionIds.Gemini, CredentialIds.Gemini, connections.GeminiApiKey.Length > 0, report, saves),
            new CredentialStatus(
                ConnectionId.Parse(ConnectionIds.Google),
                VoiceGoogleAccount.IsSignedIn ? CredentialState.Working : CredentialState.Missing),
        };

        var waiting = ConnectionNoticeBoard.With(board => NoticeCount.Tally(board.Waiting()));
        var outcomes = Enum.GetValues<DiagnosticsComponent>()
            .Select(component => new ComponentOutcome(component, OutcomeOf(context, component)));

        return new DiagnosticsSnapshot(
            AppVersion.Parse(version),
            InstallRole.Unknown,
            new DiagnosticsCounts(0, 0, 0, waiting),
            outcomes,
            [],
            credentials,
            // The newest lines of each trail, each through the filter that keeps words and counts and withholds the rest.
            [
                new TrailSection(TrailKind.Body, TrailLines.Last(new BodyLog(context).ReadAll())),
                new TrailSection(TrailKind.Vault, TrailLines.Last(new ConstitutionVaultLog(context).ReadAll())),
            ]);
    }

    // Body's sync and the morning read-back record how their last run ended; the other components (morning delivery, household sync, calendar write) have no outcome recorded for them yet.
    private static DiagnosticsOutcome OutcomeOf(Context context, DiagnosticsComponent component) => component switch
    {
        DiagnosticsComponent.BodySync => BodyServices.Create(context).Status.LastOutcome(),
        DiagnosticsComponent.ReadBack => ReadBackServices.StatusOf(context).LastOutcome(),
        _ => DiagnosticsOutcome.NeverRan,
    };

    /// <summary>The rendered text, ready to share.</summary>
    public static string Text(Context context) => DiagnosticsRenderer.Render(Build(context));

    /// <summary>Opens the share sheet with the rendered text as plain text.</summary>
    public static void Share(Context context)
    {
        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraText, Text(context));
        var chooser = Intent.CreateChooser(send, "Export diagnostics")!;
        chooser.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(chooser);
    }

    private static CredentialStatus Credential(string connectionId, string credentialId, bool isSet, CredentialHealthReport report, ConstitutionCredentialSaves saves)
    {
        var state = ConstitutionHealthReport.HealthOf(report, credentialId, saves.SavedAt(credentialId)) == ConnectionHealth.Rejected
            ? CredentialState.Rejected
            : isSet ? CredentialState.Working : CredentialState.Missing;
        return new CredentialStatus(ConnectionId.Parse(connectionId), state);
    }
}
