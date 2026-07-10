using Android.App;
using Android.Runtime;
using FocusPhone.Ui;

namespace FocusPhone.Communication;

[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round")]
public class CommunicationApplication : Application
{
    public CommunicationApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        // Install first so the SQLite init below (and anything after) is captured.
        CrashReporter.Install(this, "Communication");
        // Explicitly initialize the SQLitePCL provider before any database is
        // opened. The bundle's automatic module-initializer can be removed by
        // the Release trimmer, which would make the first SqliteConnection.Open()
        // throw and crash the app the moment it launches.
        SQLitePCL.Batteries_V2.Init();
    }
}
