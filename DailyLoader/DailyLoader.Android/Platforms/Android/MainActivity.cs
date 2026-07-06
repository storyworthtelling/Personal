using Android.App;
using Android.Content.PM;
using AndroidX.Work;
using Microsoft.Maui;

namespace DailyLoader.Android.Platforms.Android;

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
        DeliveryWorker.Schedule(this);
    }

    protected override void OnActivityResult(
        int requestCode, global::Android.App.Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == DailyLoader.Android.Google.DeviceAccountSignIn.RequestCode)
            DailyLoader.Android.Google.DeviceAccountSignIn.HandleChooserResult(resultCode, data);
    }
}
