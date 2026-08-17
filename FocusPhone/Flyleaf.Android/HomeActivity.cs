using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Time;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Flyleaf;

/// <summary>
/// The home screen: a clock header over six textless icon tiles on a
/// paper-like background. A tile tints to the accent color while its app has
/// an active notification. Swipe up opens the allowlisted app tray.
/// </summary>
[Activity(
    Label = "Flyleaf",
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

    // Owner's-voice greeting under the date, and a one-time sign-in so it can read the shared profile.
    private readonly Codex.Ui.Voice.VoiceGoogleAccount voiceAccount = new();
    private Codex.Ui.Voice.VoiceGreeting? greeting;
    private TextView? greetingView;
    private Button? voiceSignInButton;
    private readonly Dictionary<Tile, ImageView> tileViews = [];
    private TimeTickReceiver? timeTickReceiver;
    private NotificationsChangedReceiver? notificationsReceiver;
    private GestureDetector? gestureDetector;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        greeting = new Codex.Ui.Voice.VoiceGreeting(voiceAccount.GetAccessToken, "Flyleaf");
        RestoreUpdateTokenFromVault();
        SetContentView(BuildLayout());
        gestureDetector = new GestureDetector(this, new SwipeUpListener(OpenAppTray));
        PolicyManager.ApplyCurrentPolicy(this);
        CrashReporter.ShowPendingCrash(this);
    }

    // After a reinstall or the cutover's factory reset, refill the GitHub update token from the encrypted
    // Drive vault (needs the vault passphrase set + Google signed in), so the self-update channel keeps
    // working without re-entering it in admin mode. Runs only when the token is missing locally.
    private void RestoreUpdateTokenFromVault() => Task.Run(async () =>
    {
        try
        {
            var settings = new UpdateSettingsStore(this);
            if (!string.IsNullOrEmpty(settings.GitHubToken))
                return; // already have a local token — never overwrite it

            var access = new SecretsVaultAccess(voiceAccount.GetAccessToken, "Flyleaf");
            var result = await access.Service.LoadAsync(System.Threading.CancellationToken.None);
            if (result.Status == SecretsVaultStatus.Loaded)
            {
                // Drive-by-ID (#13): pick up the ids the apps persisted for the shared files.
                Codex.Ui.Voice.SharedDriveFileIds.SeedFrom(result.Vault.DriveFileIds);
                if (!string.IsNullOrEmpty(result.Vault.GitHubUpdateToken))
                    settings.GitHubToken = result.Vault.GitHubUpdateToken;
            }
        }
        catch
        {
            // Ignore: restore is a convenience; admin mode always re-enters the token manually.
        }
    });

    protected override void OnResume()
    {
        base.OnResume();
        UpdateClock();
        UpdateSetDefaultLauncherButton();
        UpdateVoiceGreeting();
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

    // Shows the sign-in button when there's no Google account yet, then composes the greeting once there
    // is. Both stay quiet (hidden) when there's no shared profile/key or on any error.
    private void UpdateVoiceGreeting()
    {
        if (voiceSignInButton is not null)
            voiceSignInButton.Visibility = Codex.Ui.Voice.VoiceGoogleAccount.IsSignedIn ? ViewStates.Gone : ViewStates.Visible;
        if (Codex.Ui.Voice.VoiceGoogleAccount.IsSignedIn)
            ShowGreetingAsync();
    }

    private async void ShowGreetingAsync()
    {
        if (greeting is null || greetingView is null)
            return;
        var message = await greeting.ComposeAsync("welcome", "the owner is looking at their home screen", CancellationToken.None);
        if (string.IsNullOrWhiteSpace(message))
            return;
        greetingView.Text = message;
        greetingView.Visibility = ViewStates.Visible;
    }

    private async Task VoiceSignInAsync()
    {
        try
        {
            await voiceAccount.SignIn(this);
        }
        catch
        {
            // Cancelled or failed — leave the button up to try again.
        }
        UpdateVoiceGreeting();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == Codex.Ui.Voice.VoiceGoogleAccount.RequestCode)
            Codex.Ui.Voice.VoiceGoogleAccount.HandleChooserResult(resultCode, data);
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Space.X6), PaperTheme.Dp(this, DesignTokens.Space.X16),
            PaperTheme.Dp(this, DesignTokens.Space.X6), PaperTheme.Dp(this, DesignTokens.Space.X8));

        // The drawn analog face (DESIGN.md §11): no digital time on the home
        // screen. Long-pressing the clock opens the everyday Settings panel — its
        // hub dot is the only mark for that gesture; Admin (the 72-hour gate) now
        // lives behind a row inside Settings, so the home screen stays blank.
        clockView = new PaperClockView(this);
        clockView.LongClick += (_, _) => StartActivity(new Intent(this, typeof(LauncherSettingsActivity)));
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

        // A greeting in the owner's voice under the date, hidden until composed (needs the shared profile
        // + a signed-in Google account); and a one-time sign-in button that shows only until then.
        greetingView = PaperUi.Secondary(this, string.Empty);
        greetingView.Gravity = GravityFlags.CenterHorizontal;
        greetingView.Visibility = ViewStates.Gone;
        var greetingParams = MatchWrap();
        greetingParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X2);
        root.AddView(greetingView, greetingParams);

        voiceSignInButton = PaperUi.SecondaryButton(this, "Sign in for a voiced hello", () => _ = VoiceSignInAsync());
        voiceSignInButton.Visibility = ViewStates.Gone;
        var voiceButtonParams = MatchWrap();
        voiceButtonParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X2);
        root.AddView(voiceSignInButton, voiceButtonParams);

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
