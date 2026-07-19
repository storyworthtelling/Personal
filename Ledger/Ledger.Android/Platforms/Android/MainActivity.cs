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
}
