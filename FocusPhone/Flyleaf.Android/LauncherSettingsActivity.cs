using Android.App;
using Android.App.Admin;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Apps;
using Codex.Domain.Design;
using Codex.Domain.Time;
using Codex.Domain.Unlock;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Flyleaf;

/// <summary>
/// The everyday, non-admin settings panel — reached by long-pressing the home
/// clock. It carries the quick device controls that shouldn't need the 6-hour
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
/// Beyond the On/Off radio toggles, Connectivity offers scoped deep-links into the
/// system panels for the actions with no first-party device-owner API — join a Wi-Fi
/// network, pair a Bluetooth device, and name a hotspot — each opened as a specific
/// Activity (which the lockdown permits) with an inline-status fallback when a panel
/// isn't resolvable. Fully-editable hotspot SSID/password is system-privileged, so that
/// one may show a read-only SSID under the lockdown — confirm on-device. There is still
/// no power-off API (only reboot). This also adds the Shelves (#11) group: create /
/// rename / reorder / remove the tray category shelves, a new shelf getting an AI-drawn
/// bookend glyph (shared key) with a pencil-monogram fallback.
/// </summary>
[Activity(
    Label = "Settings",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class LauncherSettingsActivity : Activity
{
    private readonly Handler handler = new(Looper.MainLooper!);

    // Used only to fetch a Google access token for the AI shelf-glyph (best-effort; degrades to the
    // pencil monogram when there's no sign-in or key).
    private readonly VoiceGoogleAccount voiceAccount = new();
    private EditText? newShelfField;

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
        RenderShelves();
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

        // Joining a new Wi-Fi network, pairing a Bluetooth device, and naming a hotspot all need the
        // system panels (no first-party device-owner API), so open the scoped settings screen for each.
        // The lockdown blocks the general Settings/shade, but the launcher may start a specific panel.
        AddContentButton(PaperUi.SecondaryButton(this, "Choose a Wi-Fi network…",
            () => OpenSystemPanel(Android.Provider.Settings.ActionWifiSettings, "Wi-Fi settings")));
        AddContentButton(PaperUi.SecondaryButton(this, "Pair a Bluetooth device…",
            () => OpenSystemPanel(Android.Provider.Settings.ActionBluetoothSettings, "Bluetooth settings")));
        // Fully-editable hotspot SSID/password is system-privileged; the tether panel is the reachable
        // path (confirm on-device — it may show a read-only SSID under the lockdown).
        AddContentButton(PaperUi.SecondaryButton(this, "Set up a hotspot (name + password)…",
            () => OpenSystemPanel("android.settings.TETHER_SETTINGS", "hotspot settings",
                fallback: Android.Provider.Settings.ActionWirelessSettings)));
        // Airplane mode has no toggle API; the scoped panel is the reachable path (the shade that
        // would normally toggle it is disabled under the lockdown).
        AddContentButton(PaperUi.SecondaryButton(this, "Airplane mode…",
            () => OpenSystemPanel(Android.Provider.Settings.ActionAirplaneModeSettings, "airplane-mode settings")));
    }

    // Opens a specific system settings panel by action; on a device where it isn't resolvable, tries an
    // optional fallback action, then surfaces an inline status rather than throwing.
    private void OpenSystemPanel(string action, string label, string? fallback = null)
    {
        try
        {
            StartActivity(new Intent(action).AddFlags(ActivityFlags.NewTask));
        }
        catch (Exception)
        {
            if (fallback is not null)
            {
                try { StartActivity(new Intent(fallback).AddFlags(ActivityFlags.NewTask)); return; }
                catch (Exception) { /* fall through to the status */ }
            }
            inlineStatus = $"Couldn't open {label} on this device.";
            Render();
        }
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

        // Show whether the 6-hour channel is actually running and what it last did,
        // so a silent failure (rejected token, signature-mismatched install) is visible.
        var statusStore = new UpdateStatusStore(this);
        var lastRun = statusStore.LastRunUtc;
        if (lastRun is { } runUtc && statusStore.LastSummary is { } summary)
        {
            var when = RelativeTime.Format(runUtc.ToLocalTime(), DateTime.Now);
            AddBody($"Last checked {when}: {summary}");
        }
        else
        {
            AddBody("The channel checks every 6 hours. It hasn't run yet on this phone.");
        }
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
            var result = await UpdateService.CheckAndInstallAsync(this, force: true);
            inlineStatus = result.Summarize();
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

    // ---- Shelves (#11): create / rename / reorder / remove the tray categories -

    private void RenderShelves()
    {
        AddGroupLabel("Shelves");
        AddBody("The category shelves your apps sort onto. New shelves get an engraved glyph — " +
            "AI-drawn from the name when a key's set, a pencil monogram otherwise.");

        var catalog = new CategoryStore(this).Load();
        var ids = catalog.Categories.Select(c => c.Id).ToList();
        foreach (var category in catalog.Categories)
        {
            content!.AddView(BuildShelfRow(category, ids), Spaced(0, DesignTokens.Space.X1));
        }

        newShelfField = new EditText(this) { Hint = "New shelf name" };
        newShelfField.SetTextColor(PaperTheme.Ink);
        newShelfField.SetHintTextColor(PaperTheme.InkMuted);
        newShelfField.Typeface = PaperTheme.SerifRegular(this);
        content!.AddView(newShelfField, Spaced(DesignTokens.Space.X2, DesignTokens.Space.X1));
        AddContentButton(PaperUi.SecondaryButton(this, "Add a shelf", () => AddShelf(newShelfField.Text)));
    }

    private View BuildShelfRow(Category category, List<string> orderedIds)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.QuietButton(this, category.Name, () => RenameShelf(category)),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        var index = orderedIds.IndexOf(category.Id);
        if (index > 0)
        {
            row.AddView(PaperUi.QuietButton(this, "↑", () => MoveShelf(category.Id, -1)));
        }
        if (index < orderedIds.Count - 1)
        {
            row.AddView(PaperUi.QuietButton(this, "↓", () => MoveShelf(category.Id, +1)));
        }
        row.AddView(PaperUi.QuietButton(this, "Remove", () => RemoveShelf(category.Id)));
        return row;
    }

    private void AddShelf(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            inlineStatus = "Name the shelf first.";
            Render();
            return;
        }
        var store = new CategoryStore(this);
        var catalog = store.Load().WithAdded(trimmed, ShelfGlyphMaker.Monogram(trimmed));
        store.Save(catalog);
        inlineStatus = null;
        Render();
        // The shelf shows immediately with its monogram; quietly upgrade to the AI glyph if one lands.
        UpgradeShelfGlyphAsync(catalog.Categories[^1].Id, trimmed);
    }

    private async void UpgradeShelfGlyphAsync(string id, string name)
    {
        var glyph = await new ShelfGlyphMaker(voiceAccount.GetAccessToken).AiGlyphAsync(name, CancellationToken.None);
        if (glyph is null || IsFinishing)
        {
            return;
        }
        var store = new CategoryStore(this);
        var catalog = store.Load();
        if (!catalog.Contains(id))
        {
            return; // the shelf was removed before the glyph arrived
        }
        store.Save(catalog.WithGlyph(id, glyph));
        Render();
    }

    private void RenameShelf(Category category)
    {
        var field = new EditText(this) { Text = category.Name };
        new AlertDialog.Builder(this)
            .SetTitle($"Rename {category.Name}")!
            .SetView(field)!
            .SetPositiveButton("Rename", (_, _) =>
            {
                var name = field.Text?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    var store = new CategoryStore(this);
                    store.Save(store.Load().WithRenamed(category.Id, name));
                    Render();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void RemoveShelf(string id)
    {
        var store = new CategoryStore(this);
        store.Save(store.Load().WithRemoved(id));
        // Apps on the removed shelf fall loose in the tray automatically.
        Render();
    }

    private void MoveShelf(string id, int delta)
    {
        var store = new CategoryStore(this);
        var catalog = store.Load();
        var ids = catalog.Categories.Select(c => c.Id).ToList();
        var index = ids.IndexOf(id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ids.Count)
        {
            return;
        }
        (ids[index], ids[target]) = (ids[target], ids[index]);
        store.Save(catalog.Reordered(ids));
        Render();
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
