using global::Android.App;
using global::Android.Content.PM;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Android.Platforms.Android;

[Activity(
    // Frozen: without an explicit Name the Java class name is generated from the namespace, so the
    // rename would move it and every home-screen shortcut would point at a class that no longer exists.
    Name = "com.storyworthtelling.storyboarder.MainActivity",
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
        PipelineWorker.Schedule(this);
        ResumeActiveWork();
    }

    // After a reinstall or the FocusPhone cutover's factory reset, the local API keys are gone but the
    // encrypted Drive vault still holds them. If the owner has set the vault passphrase, refill any missing
    // key from Drive so the pipeline can run without re-typing them. Best-effort: no passphrase, no network,
    // or no vault yet simply leaves the keys as they are.
    private void RestoreVaultSecrets() => Task.Run(() =>
    {
        try
        {
            var sync = IPlatformApplication.Current!.Services.GetRequiredService<VaultSync>();
            sync.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore: restore is a convenience; the Settings page always accepts the keys manually.
        }
    });

    // On launch (which, after a reboot, is the first time the app runs again) verify the outputs on disk
    // against the database and heal anything a reboot or interrupted write left dangling, then — if a
    // conversion is mid-flight, or healing regressed a finished book back into the pipeline — kick the
    // worker at once instead of waiting for the next 15-minute window. Best-effort: the periodic worker
    // still drains the pipeline if this fails.
    private void ResumeActiveWork() => Task.Run(() =>
    {
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            projects.VerifyAndHealOutputsAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (projects.HasActiveWorkAsync(CancellationToken.None).GetAwaiter().GetResult())
                PipelineWorker.RunNow(this);
        }
        catch
        {
            // Ignore: resume is an optimization; the scheduled periodic worker is the backbone.
        }
    });

    protected override void OnActivityResult(
        int requestCode, global::Android.App.Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (SafFilePicker.HandleResult(requestCode, resultCode, data))
            return;
        if (requestCode == Storyboarder.Android.Google.DeviceAccountSignIn.RequestCode)
            Storyboarder.Android.Google.DeviceAccountSignIn.HandleChooserResult(resultCode, data);
    }
}
