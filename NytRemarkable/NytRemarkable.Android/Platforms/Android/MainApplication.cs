using global::Android.App;
using global::Android.Runtime;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace NytRemarkable.Android.Platforms.Android;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var msg = e.ExceptionObject?.ToString() ?? "Unknown error";
            CredentialStore.LastDeliveryResult = $"CRASH: {msg}";
        };

        base.OnCreate();
    }
}
