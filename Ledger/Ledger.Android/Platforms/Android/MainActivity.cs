using global::Android.App;
using global::Android.Content.PM;
using Microsoft.Maui;

namespace Ledger.Android.Platforms.Android;

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
        // Schedule the daily refresh backbone and kick an immediate cycle so balances are current
        // shortly after opening the app.
        RefreshWorker.Schedule(this);
        if (SettingsStore.IsConfigured)
            RefreshWorker.RunNow(this);
    }

    // Routes the Google account-picker result back to the voice sign-in flow (SetupPage → device-account
    // Drive read of the shared voice profile).
    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == Google.DeviceAccountSignIn.RequestCode)
            Google.DeviceAccountSignIn.HandleChooserResult(resultCode, data);
    }
}
