using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Apps;
using Codex.Domain.Design;
using Codex.Domain.Policy;
using Codex.Ui;

namespace Flyleaf;

/// <summary>
/// The swipe-up tray of launchable apps, filtered for distraction-free use:
/// our own FocusPhone apps (reached from the home tiles), blocked packages,
/// browsers, and games are excluded. Everything else is auto-sorted into
/// design-styled category folders; the owner's other repo apps (Storyboarder,
/// Ledger, …) and anything unplaced sit loose at the root. Apps keep their own
/// icons; folders draw a textless ink glyph and open a sub-grid. Long-press an
/// app for a Paper menu: file it into a folder (undo-first blocking lives here
/// too — the block happens at once and a 6-second undo bar offers the reversal).
/// </summary>
[Activity(
    Label = "Apps",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class AppTrayActivity : Activity
{
    private const int GridColumns = 4;

    private readonly Dictionary<string, ResolveInfo> resolveByPackage = [];
    private TrayModel model = new(RepoApps: [], Folders: [], LooseApps: []);
    private List<TrayCell> cells = [];
    private Category? openFolder;
    // Phase E: the nine built-in shelves seed the catalog. Phase D wires this to a persistent
    // CategoryStore so the owner's #11 create / rename / reorder edits survive.
    private readonly CategoryCatalog catalog = CategoryCatalog.Seed();

    private GridView? gridView;
    private TextView? headerView;
    private FrameLayout? undoHost;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        SetContentView(BuildLayout());
        LoadApps();

        gridView!.ItemClick += (_, e) =>
        {
            switch (cells[e.Position])
            {
                case AppCell app:
                    var intent = PackageManager!.GetLaunchIntentForPackage(app.App.ActivityInfo!.PackageName!);
                    if (intent is not null)
                    {
                        StartActivity(intent);
                    }
                    break;
                case FolderCell folder:
                    openFolder = folder.Category;
                    Render();
                    break;
            }
        };

        gridView.ItemLongClick += (_, e) =>
        {
            if (cells[e.Position] is AppCell app)
            {
                ShowAppMenu(app.App);
            }
            e.Handled = true;
        };
    }

    public override void OnBackPressed()
    {
        if (openFolder is not null)
        {
            openFolder = null;
            Render();
            return;
        }
        base.OnBackPressed();
    }

    private View BuildLayout()
    {
        undoHost = new FrameLayout(this);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        column.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        headerView = PaperUi.PageHeader(this, "Apps");
        column.AddView(headerView);

        gridView = new GridView(this)
        {
            NumColumns = GridColumns,
            StretchMode = StretchMode.StretchColumnWidth,
        };
        gridView.SetVerticalSpacing(PaperTheme.Dp(this, DesignTokens.Space.X4));
        gridView.SetHorizontalSpacing(PaperTheme.Dp(this, DesignTokens.Space.X4));
        gridView.SetBackgroundColor(Android.Graphics.Color.Transparent);
        gridView.CacheColorHint = Android.Graphics.Color.Transparent;
        // Transparent selector (keep the paper background flat under a tap).
        gridView.SetSelector(Android.Resource.Color.Transparent);
        column.AddView(gridView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        undoHost.AddView(column, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        return undoHost;
    }

    private void LoadApps()
    {
        var launcherIntent = new Intent(Intent.ActionMain);
        launcherIntent.AddCategory(Intent.CategoryLauncher);

        var blocked = new BlocklistStore(this).Load().BlockedPackages.ToHashSet();
        var browsers = BrowserPackages();

        var allowed = PackageManager!
            .QueryIntentActivities(launcherIntent, (PackageInfoFlags)0)
            .Where(resolveInfo => IsAllowedInTray(resolveInfo, blocked, browsers))
            .OrderBy(resolveInfo => resolveInfo.LoadLabel(PackageManager).ToString())
            .ToList();

        resolveByPackage.Clear();
        var trayApps = new List<TrayApp>();
        foreach (var resolveInfo in allowed)
        {
            var package = resolveInfo.ActivityInfo!.PackageName!;
            if (resolveByPackage.ContainsKey(package))
            {
                continue; // one launcher entry per package is enough
            }
            resolveByPackage[package] = resolveInfo;
            var category = (int)(resolveInfo.ActivityInfo!.ApplicationInfo?.Category ?? ApplicationCategories.Undefined);
            trayApps.Add(new TrayApp(package, category));
        }

        model = AppTrayLayout.Build(trayApps, new AppFolderStore(this).Load(), catalog);

        // A shelf we were viewing may have emptied out (its last app moved/blocked); fall back to root.
        if (openFolder is { } viewing && model.Folders.All(folder => folder.Category.Id != viewing.Id))
        {
            openFolder = null;
        }

        Render();
    }

    private void Render()
    {
        cells = BuildCells();
        headerView!.Text = openFolder?.Name ?? "Apps";
        gridView!.Adapter = new TrayAdapter(this, cells);
    }

    private List<TrayCell> BuildCells()
    {
        if (openFolder is { } viewing)
        {
            var folder = model.Folders.FirstOrDefault(f => f.Category.Id == viewing.Id);
            if (folder is null)
            {
                return [];
            }
            return folder.Apps.Select(AppCellFor).ToList();
        }

        var rootCells = new List<TrayCell>();
        rootCells.AddRange(model.RepoApps.Select(AppCellFor));
        rootCells.AddRange(model.Folders.Select(folder => (TrayCell)new FolderCell(folder.Category)));
        rootCells.AddRange(model.LooseApps.Select(AppCellFor));
        return rootCells;
    }

    private TrayCell AppCellFor(TrayApp app) => new AppCell(resolveByPackage[app.PackageName]);

    private bool IsAllowedInTray(ResolveInfo resolveInfo, HashSet<string> blocked, HashSet<string> browsers)
    {
        var package = resolveInfo.ActivityInfo!.PackageName!;
        if (package == PackageName || PolicyContext.FocusPhonePackages.Contains(package))
        {
            return false; // Our own FocusPhone apps are reached from the home tiles, not the tray.
        }
        if (blocked.Contains(package) || browsers.Contains(package))
        {
            return false;
        }
        return resolveInfo.ActivityInfo!.ApplicationInfo?.Category != ApplicationCategories.Game;
    }

    private HashSet<string> BrowserPackages()
    {
        var view = new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://example.com"));
        view.AddCategory(Intent.CategoryBrowsable);
        return PackageManager!
            .QueryIntentActivities(view, PackageInfoFlags.MatchAll)
            .Select(resolveInfo => resolveInfo.ActivityInfo!.PackageName!)
            .ToHashSet();
    }

    // Long-press menu: file the app into a folder, or block it. Repo apps stay at
    // the root by design, so they're offered only the block action.
    private void ShowAppMenu(ResolveInfo app)
    {
        var package = app.ActivityInfo!.PackageName!;
        var label = app.LoadLabel(PackageManager!).ToString();

        var actions = new List<(string Label, Action OnClick)>();
        if (!IsRepoApp(package))
        {
            actions.Add(("Move to folder…", () => ShowFolderPicker(package, label)));
        }
        actions.Add(("Block", () => BlockWithUndo(app)));
        ShowPaperMenu(label, actions);
    }

    private void ShowFolderPicker(string package, string label)
    {
        var store = new AppFolderStore(this);
        var actions = catalog.Categories
            .Select(category => (category.Name, (Action)(() =>
            {
                store.SetFolder(package, category.Id);
                LoadApps();
            })))
            .ToList();
        actions.Add(("None (loose)", () =>
        {
            store.ClearFolder(package);
            LoadApps();
        }));
        ShowPaperMenu($"Move {label} to…", actions);
    }

    private static bool IsRepoApp(string package) =>
        package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
        && !PolicyContext.FocusPhonePackages.Contains(package);

    // A Paper-styled action sheet (no default dialog chrome): a Serif title over
    // left-aligned quiet buttons on a rounded paper card.
    private void ShowPaperMenu(string title, IReadOnlyList<(string Label, Action OnClick)> actions)
    {
        var dialog = new Dialog(this);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var pad = PaperTheme.Dp(this, DesignTokens.Space.X6);
        column.SetPadding(pad, pad, pad, pad);

        column.AddView(PaperUi.Heading(this, title));

        foreach (var action in actions)
        {
            var button = PaperUi.QuietButton(this, action.Label, () =>
            {
                dialog.Dismiss();
                action.OnClick();
            });
            button.Gravity = GravityFlags.CenterVertical | GravityFlags.Start;
            column.AddView(button, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        dialog.SetContentView(column);
        dialog.Window?.SetBackgroundDrawable(
            PaperUi.Fill(this, PaperTheme.Paper, DesignTokens.Shape.RadiusCard));
        dialog.Show();
    }

    // Undo-first: block immediately, then offer the reversal for six seconds.
    private void BlockWithUndo(ResolveInfo app)
    {
        var packageName = app.ActivityInfo!.PackageName!;
        var label = app.LoadLabel(PackageManager!).ToString();

        SetBlocked(packageName, blocked: true);
        UndoBar.Show(this, undoHost!, $"Blocked {label}.", onUndo: () =>
            SetBlocked(packageName, blocked: false));
    }

    private void SetBlocked(string packageName, bool blocked)
    {
        var store = new BlocklistStore(this);
        var categorizer = new AppCategorizer(store.Load());
        if (blocked)
        {
            categorizer.Block(packageName);
        }
        else
        {
            categorizer.Unblock(packageName);
        }
        store.Save(categorizer.Export());
        PolicyManager.ApplyCurrentPolicy(this);
        LoadApps();
    }
}
