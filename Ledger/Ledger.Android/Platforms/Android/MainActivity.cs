using global::Android.App;
using global::Android.Content.PM;
using Microsoft.Extensions.DependencyInjection;
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
        // After a reinstall or the FocusPhone cutover's factory reset, restore the Plaid credentials and
        // every connected bank from the encrypted Drive vault before scheduling work, so the refresh has
        // credentials to run with. Then schedule the daily refresh backbone and kick an immediate cycle.
        RestoreThenScheduleRefresh();
    }

    private void RestoreThenScheduleRefresh() => Task.Run(() =>
    {
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var vaultSync = scope.ServiceProvider.GetRequiredService<VaultSync>();
            vaultSync.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore: restore is a convenience; the Settings page always accepts the credentials manually.
        }

        RefreshWorker.Schedule(this);
        if (SettingsStore.IsConfigured)
            RefreshWorker.RunNow(this);
    });

    // Routes the Google account-picker result back to the voice sign-in flow (SetupPage → device-account
    // Drive read of the shared voice profile).
    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == Google.DeviceAccountSignIn.RequestCode)
            Google.DeviceAccountSignIn.HandleChooserResult(resultCode, data);
    }
}
