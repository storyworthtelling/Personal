using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Time;
using FocusPhone.Ui;

namespace FocusPhone.Launcher;

/// <summary>
/// The home screen: a clock header over six textless icon tiles on a
/// paper-like background. A tile tints to the accent color while its app has
/// an active notification. Swipe up opens the allowlisted app tray.
/// </summary>
[Activity(
    Label = "FocusPhone",
    MainLauncher = true,
    Exported = true,
    LaunchMode = Android.Content.PM.LaunchMode.SingleTask,
    StateNotNeeded = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter(
    [Intent.ActionMain],
    Categories = [Intent.CategoryHome, Intent.CategoryDefault])]
public class HomeActivity : Activity
{
    private PaperClockView? clockView;
    private TextView? dateView;
    private Button? setDefaultLauncherButton;
    private readonly Dictionary<Tile, ImageView> tileViews = [];
    private TimeTickReceiver? timeTickReceiver;
    private NotificationsChangedReceiver? notificationsReceiver;
    private GestureDetector? gestureDetector;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        SetContentView(BuildLayout());
        gestureDetector = new GestureDetector(this, new SwipeUpListener(OpenAppTray));
        PolicyManager.ApplyCurrentPolicy(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        UpdateClock();
        UpdateSetDefaultLauncherButton();
        timeTickReceiver = new TimeTickReceiver(UpdateClock);
        RegisterReceiver(timeTickReceiver, new IntentFilter(Intent.ActionTimeTick));
        notificationsReceiver = new NotificationsChangedReceiver(UpdateTileTints);
        RegisterReceiver(notificationsReceiver,
            new IntentFilter(TileNotificationListener.ActionNotificationsChanged),
            ReceiverFlags.NotExported);
    }

    protected override void OnPause()
    {
        base.OnPause();
        UnregisterReceiver(timeTickReceiver);
        UnregisterReceiver(notificationsReceiver);
    }

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e is not null)
        {
            gestureDetector?.OnTouchEvent(e);
        }
        return base.DispatchTouchEvent(e);
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Space.X6), PaperTheme.Dp(this, DesignTokens.Space.X16),
            PaperTheme.Dp(this, DesignTokens.Space.X6), PaperTheme.Dp(this, DesignTokens.Space.X8));

        // The drawn analog face (DESIGN.md §11): no digital time on the home
        // screen. Long-pressing the clock opens admin — its hub dot is the
        // only mark for that gesture.
        clockView = new PaperClockView(this);
        clockView.LongClick += (_, _) => StartActivity(new Intent(this, typeof(UnlockActivity)));
        var clockSize = PaperTheme.Dp(this, 176);
        root.AddView(clockView, new LinearLayout.LayoutParams(clockSize, clockSize)
        {
            Gravity = GravityFlags.CenterHorizontal,
        });

        dateView = PaperUi.Heading(this, "");
        dateView.Typeface = PaperTheme.SerifRegular(this);
        dateView.Gravity = GravityFlags.CenterHorizontal;
        var dateParams = MatchWrap();
        dateParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X4);
        root.AddView(dateView, dateParams);

        // Shown only until FocusPhone is the default home app (before device-owner
        // pinning). ROLE_HOME cannot be force-granted programmatically, so this
        // opens the system Home-app picker.
        setDefaultLauncherButton = PaperUi.SecondaryButton(this, "Set as default launcher", () =>
            StartActivity(new Intent(Android.Provider.Settings.ActionHomeSettings)));
        setDefaultLauncherButton.Visibility = ViewStates.Gone;
        var buttonParams = MatchWrap();
        buttonParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X4);
        root.AddView(setDefaultLauncherButton, buttonParams);

        var grid = new GridLayout(this) { ColumnCount = 2, RowCount = 3 };
        var gridParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0)
        {
            Weight = 1,
            TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X8),
        };
        root.AddView(grid, gridParams);

        foreach (var tile in Tile.All)
        {
            grid.AddView(BuildTileView(tile));
        }

        // Icon-only affordance for the swipe-up tray (DESIGN.md §7).
        var hint = PaperUi.HintChevron(this, OpenAppTray);
        var hintSize = PaperTheme.Dp(this, DesignTokens.Shape.TouchTargetDp);
        root.AddView(hint, new LinearLayout.LayoutParams(hintSize, hintSize)
        {
            Gravity = GravityFlags.CenterHorizontal,
        });
        return root;
    }

    private View BuildTileView(Tile tile)
    {
        var icon = new ImageView(this) { ContentDescription = tile.Name };
        var resourceId = Resources!.GetIdentifier(tile.IconResourceName, "drawable", PackageName);
        icon.SetImageResource(resourceId);
        icon.SetColorFilter(PaperTheme.Ink);
        var pad = PaperTheme.Dp(this, DesignTokens.Space.X6);
        icon.SetPadding(pad, pad, pad, pad);

        icon.Click += (_, _) =>
        {
            var intent = tile.BuildLaunchIntent(this);
            if (intent is not null)
            {
                StartActivity(intent);
            }
            else
            {
                Toast.MakeText(this, $"{tile.Name} isn't installed", ToastLength.Short)!.Show();
            }
        };

        var layoutParams = new GridLayout.LayoutParams(
            GridLayout.InvokeSpec(GridLayout.Undefined, GridLayout.Fill, 1f),
            GridLayout.InvokeSpec(GridLayout.Undefined, GridLayout.Fill, 1f))
        {
            Width = 0,
            Height = 0,
        };
        icon.LayoutParameters = layoutParams;
        tileViews[tile] = icon;
        return icon;
    }

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private void UpdateSetDefaultLauncherButton()
    {
        if (setDefaultLauncherButton is null)
        {
            return;
        }
        var roleManager = GetSystemService(Context.RoleService) as Android.App.Roles.RoleManager;
        var isDefaultLauncher = roleManager?.IsRoleHeld(Android.App.Roles.RoleManager.RoleHome) ?? false;
        setDefaultLauncherButton.Visibility = isDefaultLauncher ? ViewStates.Gone : ViewStates.Visible;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        clockView!.SetTime(now.TimeOfDay);
        dateView!.Text = TimeFormats.DateLong(now);
    }

    private void UpdateTileTints(IReadOnlySet<string> packagesWithNotifications)
    {
        foreach (var (tile, view) in tileViews)
        {
            var lit = tile.NotificationPackages.Any(packagesWithNotifications.Contains);
            view.SetColorFilter(lit ? PaperTheme.Accent : PaperTheme.Ink);
        }
    }

    private void OpenAppTray() => StartActivity(new Intent(this, typeof(AppTrayActivity)));

    private class TimeTickReceiver : BroadcastReceiver
    {
        private readonly Action onTick;

        public TimeTickReceiver(Action onTick)
        {
            this.onTick = onTick;
        }

        public override void OnReceive(Context? context, Intent? intent) => onTick();
    }

    private class NotificationsChangedReceiver : BroadcastReceiver
    {
        private readonly Action<IReadOnlySet<string>> onChanged;

        public NotificationsChangedReceiver(Action<IReadOnlySet<string>> onChanged)
        {
            this.onChanged = onChanged;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            var packages = intent?.GetStringArrayExtra(TileNotificationListener.ExtraPackages) ?? [];
            onChanged(packages.ToHashSet());
        }
    }

    private class SwipeUpListener : GestureDetector.SimpleOnGestureListener
    {
        private const float MinimumDistance = 200f;
        private const float MinimumVelocity = 800f;

        private readonly Action onSwipeUp;

        public SwipeUpListener(Action onSwipeUp)
        {
            this.onSwipeUp = onSwipeUp;
        }

        public override bool OnFling(MotionEvent? e1, MotionEvent e2, float velocityX, float velocityY)
        {
            if (e1 is not null && e1.GetY() - e2.GetY() > MinimumDistance && -velocityY > MinimumVelocity)
            {
                onSwipeUp();
                return true;
            }
            return false;
        }
    }
}
