using Android.App;
using Android.Content.PM;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace DailyLoader.Android.Platforms.Android;

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
        if (requestCode == DailyLoader.Android.Google.DeviceAccountSignIn.RequestCode)
            DailyLoader.Android.Google.DeviceAccountSignIn.HandleChooserResult(resultCode, data);
    }
}
