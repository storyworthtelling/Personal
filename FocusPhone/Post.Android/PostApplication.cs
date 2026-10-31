using Android.App;
using Android.Runtime;
using Codex.Ui;

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
        // The rolling log first, so the crash reporter can append every crash to it as well as
        // keeping the latest for the one-shot popup — then the reporter, before the SQLite init
        // below, so that and anything after is captured.
        PostDiagnostics.Install(this);
        CrashReporter.Install(this, "Post", PostDiagnostics.Log);

        // The log loop (LOG-LOOP-PLAN.md, Phase 1): the build this process is, how the last ones
        // ended, a watch on every tap, and the weekly upload of what was logged.
        PostDiagnostics.LogLaunch(this);
        ExitTrail.Record(this, (problem, line) =>
        {
            if (problem)
            {
                PostDiagnostics.LogWarning("Exit", line);
            }
            else
            {
                PostDiagnostics.LogInfo("Exit", line);
            }
        });
        TapWatch.Install(PostDiagnostics.LogTap);
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
