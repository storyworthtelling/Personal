using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace FocusPhone.Launcher;

/// <summary>
/// The swipe-up tray: every launchable, non-suspended app. Long-press blocks
/// an app — the block is permanent (survives reinstall) and takes effect via
/// an immediate policy re-apply.
/// </summary>
[Activity(
    Label = "Apps",
    Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class AppTrayActivity : ListActivity
{
    private List<ResolveInfo> apps = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ListView!.SetBackgroundColor(Palette.Paper);
        LoadApps();

        ListView.ItemClick += (_, e) =>
        {
            var info = apps[e.Position].ActivityInfo!;
            var intent = PackageManager!.GetLaunchIntentForPackage(info.PackageName!);
            if (intent is not null)
            {
                StartActivity(intent);
            }
        };

        ListView.ItemLongClick += (_, e) =>
        {
            ConfirmBlock(apps[e.Position]);
            e.Handled = true;
        };
    }

    private void LoadApps()
    {
        var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);

        apps = PackageManager!
            .QueryIntentActivities(intent, (PackageInfoFlags)0)
            .Where(resolveInfo => resolveInfo.ActivityInfo!.PackageName != PackageName)
            .OrderBy(resolveInfo => resolveInfo.LoadLabel(PackageManager).ToString())
            .ToList();

        ListAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1,
            apps.Select(resolveInfo => resolveInfo.LoadLabel(PackageManager).ToString()!).ToList());
    }

    private void ConfirmBlock(ResolveInfo app)
    {
        var packageName = app.ActivityInfo!.PackageName!;
        var label = app.LoadLabel(PackageManager!).ToString();

        new AlertDialog.Builder(this)
            .SetTitle($"Block {label}?")!
            .SetMessage($"{label} will be unusable and stay blocked even if reinstalled. Unblocking requires admin mode (72-hour delay).")!
            .SetPositiveButton("Block", (_, _) =>
            {
                var store = new BlocklistStore(this);
                var categorizer = new Domain.Apps.AppCategorizer(store.Load());
                categorizer.Block(packageName);
                store.Save(categorizer.Export());
                PolicyManager.ApplyCurrentPolicy(this);
                LoadApps();
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }
}
