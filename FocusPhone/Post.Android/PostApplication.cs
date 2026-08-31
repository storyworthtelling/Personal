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
        // Install first so the SQLite init below (and anything after) is captured.
        CrashReporter.Install(this, "Post");
        // Layer the rolling, reviewable/shareable diagnostics log on top of the
        // one-shot crash popup (Settings → Diagnostics).
        PostDiagnostics.Install(this);
        // Explicitly initialize the SQLitePCL provider before any database is
        // opened. The bundle's automatic module-initializer can be removed by
        // the Release trimmer, which would make the first SqliteConnection.Open()
        // throw and crash the app the moment it launches.
        SQLitePCL.Batteries_V2.Init();

        // Bring the Messages-for-Web bridge online if a Google account is already
        // paired, so incoming texts stream into the cache. No-op (stub) when the
        // gmbridge binding isn't built in.
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
