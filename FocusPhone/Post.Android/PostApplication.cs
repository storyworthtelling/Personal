using Android.App;
using Android.Runtime;
using Codex.Domain.Post;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Post;

[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round")]
public class PostApplication : Application
{
    public PostApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        // The log loop first (LOG-LOOP-PLAN.md): the rolling log sealing into the outbox, the
        // crash reporter appending every crash to it — before the SQLite init below, so that and
        // anything after is captured — the tap watch, the launch line and every exit since the last
        // launch. Post keeps its own upload worker, which shipped first under its own frozen name.
        HeadDiagnostics.Install(this, new HeadSetup(
            PostDiagnostics.Label, "post", PostKeyActions.SendLogsNow,
            MailAccess.Account().GetAccessToken, () => VoiceGoogleAccount.IsSignedIn,
            ExtraLogs: [(PostDiagnostics.TextsFileName, PostDiagnostics.TextsMaxBytes)],
            ScheduleUpload: false));
        LogUploadWorker.Schedule(this);
        // Explicitly initialize the SQLitePCL provider before any database is
        // opened. The bundle's automatic module-initializer can be removed by
        // the Release trimmer, which would make the first SqliteConnection.Open()
        // throw and crash the app the moment it launches.
        SQLitePCL.Batteries_V2.Init();

        // Bring the Messages-for-Web bridge online if a Google account is already
        // paired, so incoming texts stream into the cache. No-op (stub) when the
        // gmbridge binding isn't built in.
        // A picture or group text whose download failed is asked for again while the
        // carrier still holds it. Off the main thread: it reads the provider.
        var app = this;
        Task.Run(() => MmsStubSweep.Run(app));

        try
        {
            GmBridgeClient.Shared(this).ConnectIfPaired();
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Bridge startup failed: {e}");
            PostDiagnostics.LogWarning("Bridge", "Bridge startup failed", e);
        }
    }
}
