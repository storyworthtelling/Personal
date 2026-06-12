using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

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
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
[IntentFilter(
    [Intent.ActionMain],
    Categories = [Intent.CategoryHome, Intent.CategoryDefault])]
public class HomeActivity : Activity
{
    private TextView? timeView;
    private TextView? dateView;
    private readonly Dictionary<Tile, ImageView> tileViews = [];
    private TimeTickReceiver? timeTickReceiver;
    private NotificationsChangedReceiver? notificationsReceiver;
    private GestureDetector? gestureDetector;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(BuildLayout());
        gestureDetector = new GestureDetector(this, new SwipeUpListener(OpenAppTray));
        PolicyManager.ApplyCurrentPolicy(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        UpdateClock();
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
        root.SetBackgroundColor(Palette.Paper);
        root.SetPadding(48, 160, 48, 96);

        timeView = new TextView(this) { TextSize = 64 };
        timeView.SetTextColor(Palette.Ink);
        timeView.Gravity = GravityFlags.CenterHorizontal;
        timeView.LongClick += (_, _) => StartActivity(new Intent(this, typeof(UnlockActivity)));
        root.AddView(timeView, MatchWrap());

        dateView = new TextView(this) { TextSize = 18 };
        dateView.SetTextColor(Palette.InkMuted);
        dateView.Gravity = GravityFlags.CenterHorizontal;
        root.AddView(dateView, MatchWrap());

        var grid = new GridLayout(this) { ColumnCount = 2, RowCount = 3 };
        var gridParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1, TopMargin = 96 };
        root.AddView(grid, gridParams);

        foreach (var tile in Tile.All)
        {
            grid.AddView(BuildTileView(tile));
        }
        return root;
    }

    private View BuildTileView(Tile tile)
    {
        var icon = new ImageView(this) { ContentDescription = tile.Name };
        var resourceId = Resources!.GetIdentifier(tile.IconResourceName, "drawable", PackageName);
        icon.SetImageResource(resourceId);
        icon.SetColorFilter(Palette.Ink);
        icon.SetPadding(64, 64, 64, 64);

        icon.Click += (_, _) =>
        {
            var intent = tile.BuildLaunchIntent(this);
            if (intent is not null)
            {
                StartActivity(intent);
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

    private void UpdateClock()
    {
        var now = DateTime.Now;
        timeView!.Text = now.ToString("HH:mm");
        dateView!.Text = now.ToString("dddd, MMMM d");
    }

    private void UpdateTileTints(IReadOnlySet<string> packagesWithNotifications)
    {
        foreach (var (tile, view) in tileViews)
        {
            var lit = tile.NotificationPackages.Any(packagesWithNotifications.Contains);
            view.SetColorFilter(lit ? Palette.Accent : Palette.Ink);
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
