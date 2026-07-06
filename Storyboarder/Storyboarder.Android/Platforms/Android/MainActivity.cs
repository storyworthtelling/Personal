using global::Android.App;
using global::Android.Content.PM;
using Microsoft.Maui;

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
    }

    protected override void OnActivityResult(
        int requestCode, global::Android.App.Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == Storyboarder.Android.Google.DeviceAccountSignIn.RequestCode)
            Storyboarder.Android.Google.DeviceAccountSignIn.HandleChooserResult(resultCode, data);
    }
}
