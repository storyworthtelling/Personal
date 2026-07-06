using global::Android.App;
using global::Android.Content.PM;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Android.Platforms.Android;

[Activity(
    Theme = "@style/PaperSplashTheme",
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
        PipelineWorker.Schedule(this);
        ResumeActiveWork();
    }

    // On first launch after an app update, kick the worker immediately if a conversion is mid-flight,
    // so a days- or weeks-long job resumes at once instead of waiting for the next 15-minute window.
    // Best-effort: the periodic worker still drains the pipeline if this fails.
    private void ResumeActiveWork() => Task.Run(() =>
    {
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
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
