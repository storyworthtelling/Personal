using global::Android.App;
using global::Android.Content.PM;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Index.Domain.Household;
using Index.Domain.Identity;

namespace Index.Android.Platforms.Android;

[Activity(
    // Frozen: without an explicit Name the Java class name is generated from the namespace, so a
    // future rename would move it and every home-screen shortcut would point at a class that no
    // longer exists.
    Name = IndexIdentity.ApplicationId + ".MainActivity",
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
        // Decide which install this is FIRST, then restore and schedule only what that install may do.
        // After a reinstall or the FocusPhone cutover's factory reset, the owner's phone restores the
        // Plaid credentials and every connected bank from the encrypted Drive vault before scheduling
        // work, so the refresh has credentials to run with. Her phone does none of it: it never talks
        // to Plaid, so copying the owner's credentials onto it, or scheduling a refresh that would then
        // use them, is exactly what the role exists to prevent.
        RestoreThenScheduleRefresh();
    }

    private void RestoreThenScheduleRefresh() => Task.Run(() =>
    {
        // Resolved once, from the household file the passphrase opens, and remembered in the local
        // settings (InstallRoleService). Unsettled (no passphrase yet, no file yet) answers Owner —
        // the behaviour every install had before the role existed.
        var role = InstallRole.Owner;
        try
        {
            var roles = IPlatformApplication.Current!.Services.GetRequiredService<InstallRoleService>();
            role = roles.ResolveAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            IPlatformApplication.Current!.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<MainActivity>()
                .LogWarning(ex, "Could not settle which install this is; launching as the owner's.");
        }

        var plan = LaunchPlan.For(role);

        if (plan.RestoreFromVault)
        {
            try
            {
                // Through the one gate a saved passphrase also restores through, so the two can never run
                // at the same moment; if one is already under way this launch simply does not start another.
                var services = IPlatformApplication.Current!.Services;
                using var scope = services.CreateScope();
                var vaultSync = scope.ServiceProvider.GetRequiredService<VaultSync>();
                services.GetRequiredService<RestoreGate>()
                    .TryStart(() => vaultSync.RestoreAsync(CancellationToken.None))?.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Restore is a convenience (Settings always accepts the credentials manually), but the
                // reason it did not complete is kept, not dropped.
                IPlatformApplication.Current!.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<MainActivity>()
                    .LogWarning(ex, "Restoring from the vault at launch did not complete.");
            }
        }

        if (plan.ScheduleRefresh)
            RefreshWorker.Schedule(this);
        else
            RefreshWorker.CancelAll(this);

        if (plan.RunRefreshNow && SettingsStore.IsConfigured)
            RefreshWorker.RunNow(this);

        // Release F (D52/D89): the monthly worksheet round-trip runs on its own, no button — see
        // WorksheetDeliveryWorker's own remarks for why this is a daily wakeup with an internal
        // due-today gate rather than a true monthly WorkManager period.
        if (plan.ScheduleWorksheet)
            WorksheetDeliveryWorker.Schedule(this);
        else
            WorksheetDeliveryWorker.CancelAll(this);
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
