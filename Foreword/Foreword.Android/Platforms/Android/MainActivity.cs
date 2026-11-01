using Storyworthtelling.Maui.Diagnostics;
using Android.App;
using Android.Content.PM;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Identity;
using Microsoft.Maui;

namespace Foreword.Android.Platforms.Android;

// Without this, .NET for Android generates the Java name from the assembly and namespace, so
// the rename would move it. The manifest is regenerated each build and a launcher resolves
// MAIN/LAUNCHER at launch time, so this is far less dangerous than the worker above — but
// pinning it costs one attribute and makes the name a decision rather than a side effect of
// where the class happens to live.
[global::Android.Runtime.Register(ForewordIdentity.ApplicationId + ".MainActivity")]
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MauiTapWatch.Attach(this);
        RestoreVaultSecrets();
        DeliveryWorker.Schedule(this);
    }

    // After a reinstall or the FocusPhone cutover's factory reset, the local credentials are gone but the
    // encrypted Drive vault still holds them. If the owner has set the vault passphrase, refill any missing
    // credential from Drive so delivery can run without re-pasting them. Best-effort: no passphrase, no
    // network, or no vault yet simply leaves the credentials as they are.
    private void RestoreVaultSecrets() => Task.Run(() =>
    {
        try
        {
            var sync = IPlatformApplication.Current!.Services.GetRequiredService<VaultSync>();
            sync.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore: restore is a convenience; the Settings page always accepts the credentials manually.
        }
    });

    protected override void OnActivityResult(
        int requestCode, global::Android.App.Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == Foreword.Android.Google.DeviceAccountSignIn.RequestCode)
            Foreword.Android.Google.DeviceAccountSignIn.HandleChooserResult(resultCode, data);
    }

    // LOG-LOOP-PLAN.md §4.1: every touch is handed to the tap watch before it is dispatched, since the click
    // handler runs after ACTION_UP is delivered.
    public override bool DispatchTouchEvent(global::Android.Views.MotionEvent? e)
    {
        MauiTapWatch.Touch(this, e);
        return base.DispatchTouchEvent(e);
    }
}
