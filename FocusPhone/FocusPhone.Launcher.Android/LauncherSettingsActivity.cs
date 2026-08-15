using Android.App;
using Android.App.Admin;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Unlock;
using FocusPhone.Ui;
using FocusPhone.Ui.Voice;

namespace FocusPhone.Launcher;

/// <summary>
/// The everyday, non-admin settings panel — reached by long-pressing the home
/// clock. It carries the quick device controls that shouldn't need the 72-hour
/// admin gate (radios, restart, install owner-app updates, the secrets-vault
/// passphrase), and an Admin row that leads into the gated UnlockActivity for
/// the escape-hatch controls.
///
/// Built imperatively with PaperUi and rendered through a single Render() so the
/// power countdown and the force-close undo row can rebuild in place. Every
/// device action is best-effort: on a phone where this app isn't the device
/// owner (or a radio can't be toggled), the action fails into an inline status
/// sentence rather than throwing.
///
/// Two controls from the approved mockup hit hard Android limits for a
/// non-privileged device-owner app and are degraded per the approved
/// "spike-first, drop/hide if unreliable" decision: a Wi-Fi hotspot has no
/// public direct-toggle API, and there is no power-off API (only reboot), so the
/// panel ships Wi-Fi + Bluetooth + Restart and omits the other two pending an
/// on-device capability spike.
/// </summary>
[Activity(
    Label = "Settings",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class LauncherSettingsActivity : Activity
{
    private readonly Handler handler = new(Looper.MainLooper!);

    private LinearLayout? content;
    private LinearLayout? actionBarHost;
    private EditText? passphraseField;
    private InlineCountdown? restart;
    private string? inlineStatus;
    private string? undoPackage;
    private bool refreshScheduled;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        restart = new InlineCountdown(new SystemClockAdapter(),
            TimeSpan.FromSeconds(DesignTokens.Motion.CutoverCountdownSeconds));

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var scroll = new ScrollView(this);
        content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        content.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X12), edge, edge);
        scroll.AddView(content);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        actionBarHost = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.AddView(actionBarHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        SetContentView(root);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Render();
    }

    protected override void OnPause()
    {
        base.OnPause();
        handler.RemoveCallbacksAndMessages(null);
        refreshScheduled = false;
        // An abandoned power countdown is cancelled, never resumed hidden (DESIGN.md §7).
        restart!.Cancel();
    }

    private void Render()
    {
        // The power countdown fires only from a live, visible countdown (DESIGN.md §7).
        if (restart!.HasExpired)
        {
            restart.Cancel();
            try
            {
                PolicyManager.Reboot(this);
                inlineStatus = "Restarting…";
            }
            catch (Exception e)
            {
                inlineStatus = $"Couldn't restart: {e.Message}";
            }
        }

        content!.RemoveAllViews();
        actionBarHost!.RemoveAllViews();

        content.AddView(PaperUi.PageHeader(this, "Settings"), MatchWrap());

        RenderConnectivity();
        RenderApps();
        RenderUpdates();
        RenderPower();
        RenderVault();
        RenderAdvanced();

        if (undoPackage is not null)
        {
            RenderUndoRow(undoPackage);
        }
        else if (inlineStatus is not null)
        {
            var status = PaperUi.Secondary(this, inlineStatus);
            var statusParams = MatchWrap();
            statusParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X4);
            content.AddView(status, statusParams);
        }

        var save = PaperUi.PrimaryButton(this, "Save", SaveVaultPassphrase);
        actionBarHost.AddView(PaperUi.BottomBar(this, save));

        if (restart!.IsRunning)
        {
            ScheduleRefresh();
        }
    }

    // ---- Connectivity: Wi-Fi + Bluetooth toggles ------------------------------

    private void RenderConnectivity()
    {
        AddGroupLabel("Connectivity");

        var wifi = (WifiManager?)GetSystemService(WifiService);
        AddToggleRow("Wi-Fi", wifi?.IsWifiEnabled ?? false, on =>
        {
            // SetWifiEnabled is deprecated for ordinary apps on Android 10+, but the device owner is
            // exempt from that block — this is the reliable device-owner path.
#pragma warning disable CA1422
            var ok = wifi?.SetWifiEnabled(on) ?? false;
#pragma warning restore CA1422
            if (!ok)
            {
                inlineStatus = "Couldn't change Wi-Fi. This needs the device-owner lockdown.";
                Render();
            }
        });

        var bluetooth = ((BluetoothManager?)GetSystemService(BluetoothService))?.Adapter;
        AddToggleRow("Bluetooth", bluetooth?.IsEnabled ?? false, on =>
        {
            // Enable/Disable is deprecated on recent Android and returns false when the platform declines;
            // surface that rather than pretend it worked (best-effort, pending an on-device spike).
#pragma warning disable CA1422
            var ok = bluetooth is not null && (on ? bluetooth.Enable() : bluetooth.Disable());
#pragma warning restore CA1422
            if (!ok)
            {
                inlineStatus = "Couldn't change Bluetooth on this device.";
                Render();
            }
        });
    }

    // ---- Apps: force-close ----------------------------------------------------

    private void RenderApps()
    {
        AddGroupLabel("Apps");
        AddContentButton(PaperUi.SecondaryButton(this, "Force-close an app", ShowForceClosePicker));
    }

    private void ShowForceClosePicker()
    {
        var launcherIntent = new Intent(Intent.ActionMain);
        launcherIntent.AddCategory(Intent.CategoryLauncher);
        var apps = PackageManager!
            .QueryIntentActivities(launcherIntent, (PackageInfoFlags)0)
            .Where(resolveInfo => resolveInfo.ActivityInfo!.PackageName != PackageName)
            .GroupBy(resolveInfo => resolveInfo.ActivityInfo!.PackageName!)
            .Select(group => group.First())
            .OrderBy(resolveInfo => resolveInfo.LoadLabel(PackageManager).ToString())
            .ToList();

        if (apps.Count == 0)
        {
            inlineStatus = "No apps to close.";
            Render();
            return;
        }

        var labels = apps.Select(resolveInfo => resolveInfo.LoadLabel(PackageManager!).ToString()).ToArray();
        new AlertDialog.Builder(this)
            .SetTitle("Force-close which app?")!
            .SetItems(labels, (_, e) => ForceClose(apps[e.Which].ActivityInfo!.PackageName!, labels[e.Which]))!
            .Show();
    }

    private void ForceClose(string package, string label)
    {
        try
        {
            var activityManager = (ActivityManager?)GetSystemService(ActivityService);
            activityManager?.KillBackgroundProcesses(package);
        }
        catch (Exception e)
        {
            inlineStatus = $"Couldn't close {label}: {e.Message}";
            Render();
            return;
        }

        // Undo-first (DESIGN.md §7): the app is closed immediately, then an undo row offers to relaunch it.
        inlineStatus = null;
        undoPackage = package;
        Render();
        // The undo row dismisses itself after the standard window (DESIGN.md §7) if nothing taps it.
        handler.RemoveCallbacksAndMessages(null);
        handler.PostDelayed(() =>
        {
            if (undoPackage == package)
            {
                undoPackage = null;
                Render();
            }
        }, DesignTokens.Motion.UndoWindowMs);
    }

    private void RenderUndoRow(string package)
    {
        var label = LabelFor(package);
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.Secondary(this, $"Closed {label}."),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(this, "Undo", () =>
        {
            var relaunch = PackageManager!.GetLaunchIntentForPackage(package);
            if (relaunch is not null)
            {
                StartActivity(relaunch);
            }
            undoPackage = null;
            Render();
        }));
        var rowParams = MatchWrap();
        rowParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X4);
        content!.AddView(row, rowParams);
    }

    private string LabelFor(string package)
    {
        try
        {
            return PackageManager!.GetApplicationLabel(
                PackageManager.GetApplicationInfo(package, (PackageInfoFlags)0)).ToString();
        }
        catch (PackageManager.NameNotFoundException)
        {
            return package;
        }
    }

    // ---- Updates: install owner-app updates now (out of the admin gate) --------

    private void RenderUpdates()
    {
        AddGroupLabel("Updates");
        AddContentButton(PaperUi.SecondaryButton(this, "Install from GitHub now", InstallFromGitHubNow));
    }

    // On-demand run of the owner-app update channel: pull the latest release now and install every
    // com.storyworthtelling app that's new or newer, instead of waiting for the 6-hour worker. force
    // re-checks every asset so it also doubles as a manual retry.
    private async void InstallFromGitHubNow()
    {
        inlineStatus = "Checking GitHub… new or updated apps install and appear in Apps.";
        undoPackage = null;
        Render();
        try
        {
            await UpdateService.CheckAndInstallAsync(this, force: true);
            inlineStatus = "GitHub check complete.";
        }
        catch (Exception e)
        {
            inlineStatus = $"GitHub install failed: {e.Message}";
        }
        Render();
    }

    // ---- Power: Restart (device-owner reboot) ---------------------------------

    private void RenderPower()
    {
        AddGroupLabel("Power");

        if (restart!.IsRunning)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            var seconds = (int)Math.Ceiling(restart.Remaining.TotalSeconds);
            var timer = PaperUi.Countdown(this);
            timer.Text = $"0:{seconds:D2}";
            row.AddView(timer, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
            row.AddView(PaperUi.SecondaryButton(this, "Cancel", () =>
            {
                restart.Cancel();
                Render();
            }));
            content!.AddView(row, MatchWrap());
            AddBody("Restarting when the countdown ends.");
        }
        else
        {
            AddContentButton(PaperUi.DestructiveButton(this, "Restart", () =>
            {
                restart!.Start();
                Render();
            }));
        }
    }

    // ---- Secrets vault passphrase ---------------------------------------------

    private void RenderVault()
    {
        AddGroupLabel("Secrets vault");
        // Shown in the clear (not a password field): it is the one secret NOT kept in the vault, so being
        // able to read and copy it is exactly what lets the owner set the same passphrase in every app.
        passphraseField = PaperUi.MonoField(this, "Choose a passphrase");
        passphraseField.Text = VaultPassphraseStore.Passphrase;
        content!.AddView(passphraseField, Spaced(0, DesignTokens.Space.X1));
        content.AddView(PaperUi.Secondary(this,
            "Encrypts the shared Drive backup of this phone's keys so a reset can restore them. " +
            "Shown in the clear so you can copy it into your other apps."), Spaced(0, DesignTokens.Space.X4));
    }

    private void SaveVaultPassphrase()
    {
        // The passphrase is the one secret NOT stored in the vault (it's the key), so saving just persists
        // it locally — there's no Launcher section to publish from this panel (the GitHub update token, the
        // Launcher's vault secret, is set in the admin token dialog).
        VaultPassphraseStore.Passphrase = passphraseField?.Text ?? string.Empty;
        Finish();
    }

    // ---- Advanced: the gated admin escape hatch -------------------------------

    private void RenderAdvanced()
    {
        AddGroupLabel("Advanced");
        AddContentButton(PaperUi.SecondaryButton(this, "Admin (device-owner controls)…",
            () => StartActivity(new Intent(this, typeof(UnlockActivity)))));
    }

    // ---- Shared building blocks ----------------------------------------------

    private void AddToggleRow(string label, bool isOn, Action<bool> onChange)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.Body(this, label),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        // Index 0 = On, 1 = Off; start on the segment matching the current radio state.
        row.AddView(PaperUi.SegmentedToggle(this, "On", "Off", index => onChange(index == 0), isOn ? 0 : 1));
        content!.AddView(row, Spaced(0, DesignTokens.Space.X2));
    }

    private void AddGroupLabel(string text)
    {
        var label = PaperUi.Secondary(this, text);
        label.TextSize = DesignTokens.TypeSp.Label;
        label.Typeface = PaperTheme.SansSemibold(this);
        label.SetTextColor(PaperTheme.InkMuted);
        var labelParams = MatchWrap();
        labelParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X6);
        labelParams.BottomMargin = PaperTheme.Dp(this, DesignTokens.Space.X2);
        content!.AddView(label, labelParams);
    }

    private void AddBody(string text)
    {
        var view = PaperUi.Secondary(this, text);
        view.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X2), 0, 0);
        content!.AddView(view, MatchWrap());
    }

    private void AddContentButton(Button button)
    {
        var layoutParams = MatchWrap();
        layoutParams.BottomMargin = PaperTheme.Dp(this, DesignTokens.Space.X2);
        content!.AddView(button, layoutParams);
    }

    private void ScheduleRefresh()
    {
        if (refreshScheduled)
        {
            return;
        }
        refreshScheduled = true;
        handler.PostDelayed(() =>
        {
            refreshScheduled = false;
            Render();
        }, 1000);
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topDp),
            BottomMargin = PaperTheme.Dp(this, bottomDp),
        };

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
}
