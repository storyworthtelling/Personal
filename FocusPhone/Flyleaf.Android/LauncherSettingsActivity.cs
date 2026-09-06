using Android.App;
using Android.App.Admin;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Telephony;
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
/// Repainted to the Paper &amp; Ink kit (the approved P5 mockup): a Serif in-page
/// header with a <b>neutral Settings-gray</b> marker underline — Settings is the one
/// screen that looks the same in every app, so it wears the neutral mark rather than
/// Flyleaf's identity hue (DESIGN.md §9), exactly like Post and Score — Serif group
/// labels, and each group drawn as a hairline-ruled card of rows over the paper grain
/// (the theme's window background). A small mono build-number line sits at the foot so
/// an update is visible even when nothing else changed.
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
/// isn't resolvable. Mobile data is a direct On/Off toggle (replacing the old airplane
/// deep-link); like the other radios it is best-effort — on a device that doesn't grant
/// the call, it says so rather than pretend. This also carries the Shelves (#11) group:
/// create / rename / reorder / remove the tray category shelves, a new shelf getting an
/// AI-drawn bookend glyph (shared key) with a pencil-monogram fallback.
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
        var edge = Dp(DesignTokens.Shape.ScreenEdgeDp);
        content.SetPadding(edge, Dp(DesignTokens.Space.X12), edge, edge);
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

        // §7: every screen opens with the Serif title + a full-width marker underline.
        // Settings takes the neutral resolved mark (like Post/Score), not Flyleaf's amber.
        content.AddView(PaperUi.ScreenHeader(this, "Settings", PaperTheme.ResolvedMark), MatchWrap());

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
            statusParams.TopMargin = Dp(DesignTokens.Space.X4);
            content.AddView(status, statusParams);
        }

        // The build line is PaperUi's now, so every head shows the same one. It used to be a
        // private method here and nowhere else, which is why six of seven apps had none.
        var buildLine = PaperUi.BuildNumber(this, "Flyleaf");
        var buildParams = MatchWrap();
        buildParams.TopMargin = Dp(DesignTokens.Space.X8);
        content.AddView(buildLine, buildParams);

        var save = PaperUi.PrimaryButton(this, "Save", SaveVaultPassphrase);
        actionBarHost.AddView(PaperUi.BottomBar(this, save));

        if (restart!.IsRunning)
        {
            ScheduleRefresh();
        }
    }

    // ---- Connectivity: Wi-Fi / mobile data / Bluetooth + scoped deep-links -----

    private void RenderConnectivity()
    {
        var card = StartGroup("Connectivity");

        var wifi = (WifiManager?)GetSystemService(WifiService);
        AddToggleRow(card, "Wi-Fi", null, wifi?.IsWifiEnabled ?? false, on =>
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
        AddNavRow(card, "Choose a network…", null,
            () => OpenSystemPanel(Android.Provider.Settings.ActionWifiSettings, "Wi-Fi settings"));

        // Mobile data On/Off. There is no device-owner API for this, and toggling data needs
        // MODIFY_PHONE_STATE / carrier privileges the lockdown doesn't grant, so this is best-effort:
        // it attempts the call and, where the platform declines, says so instead of pretending.
        var telephony = (TelephonyManager?)GetSystemService(TelephonyService);
        var dataOn = false;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            // Java's isDataEnabled()/setDataEnabled() bind as one get/set property, DataEnabled.
#pragma warning disable CA1422, CS0618
            try { dataOn = telephony?.DataEnabled ?? false; } catch (Exception) { dataOn = false; }
#pragma warning restore CA1422, CS0618
        }
        var carrier = telephony?.NetworkOperatorName;
        AddToggleRow(card, "Mobile data", string.IsNullOrEmpty(carrier) ? null : carrier, dataOn, on =>
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.O || telephony is null)
                {
                    throw new InvalidOperationException("needs Android 8+");
                }
#pragma warning disable CA1422, CS0618
                telephony.DataEnabled = on;
#pragma warning restore CA1422, CS0618
            }
            catch (Exception)
            {
                inlineStatus = "Couldn't change mobile data on this device. Try Choose a network for the mobile-data panel.";
                Render();
            }
        });

        var bluetooth = ((BluetoothManager?)GetSystemService(BluetoothService))?.Adapter;
        AddToggleRow(card, "Bluetooth", null, bluetooth?.IsEnabled ?? false, on =>
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
        AddNavRow(card, "Pair a device…", null,
            () => OpenSystemPanel(Android.Provider.Settings.ActionBluetoothSettings, "Bluetooth settings"));

        // Fully-editable hotspot SSID/password is system-privileged; the tether panel is the reachable
        // path (confirm on-device — it may show a read-only SSID under the lockdown).
        AddNavRow(card, "Set up a hotspot (name + password)…", null,
            () => OpenSystemPanel("android.settings.TETHER_SETTINGS", "hotspot settings",
                fallback: Android.Provider.Settings.ActionWirelessSettings));
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
        var card = StartGroup("Apps");
        AddNavRow(card, "Force-close an app", "Stop one that's misbehaving", ShowForceClosePicker);
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
        rowParams.TopMargin = Dp(DesignTokens.Space.X4);
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
        var card = StartGroup("Updates");
        AddNavRow(card, "Install from GitHub now", null, InstallFromGitHubNow);

        // Show whether the 6-hour channel is actually running and what it last did,
        // so a silent failure (rejected token, signature-mismatched install) is visible.
        var statusStore = new UpdateStatusStore(this);
        var lastRun = statusStore.LastRunUtc;
        if (lastRun is { } runUtc && statusStore.LastSummary is { } summary)
        {
            var when = RelativeTime.Format(runUtc.ToLocalTime(), DateTime.Now);
            AddCardCaption($"Last checked {when}: {summary}", progress: true);
        }
        else
        {
            AddCardCaption("The channel checks every 6 hours. It hasn't run yet on this phone.");
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
        var card = StartGroup("Power");

        if (restart!.IsRunning)
        {
            var row = BeginRow(card);
            var seconds = (int)Math.Ceiling(restart.Remaining.TotalSeconds);
            var timer = PaperUi.Countdown(this);
            timer.Text = $"0:{seconds:D2}";
            row.AddView(timer, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
            row.AddView(PaperUi.QuietButton(this, "Cancel", () =>
            {
                restart.Cancel();
                Render();
            }));
            AddCardCaption("Restarting when the countdown ends.");
        }
        else
        {
            AddNavRow(card, "Restart", "Counts down in place — no dialog", () =>
            {
                restart!.Start();
                Render();
            });
        }
    }

    // ---- Secrets vault passphrase ---------------------------------------------

    private void RenderVault()
    {
        var card = StartGroup("Secrets vault");

        var block = new LinearLayout(this) { Orientation = Orientation.Vertical };
        block.SetPadding(Dp(DesignTokens.Space.X4), Dp(DesignTokens.Space.X3),
            Dp(DesignTokens.Space.X4), Dp(DesignTokens.Space.X4));

        block.AddView(PaperUi.Body(this, "Vault passphrase"), MatchWrap());

        // Shown in the clear (not a password field): it is the one secret NOT kept in the vault, so being
        // able to read and copy it is exactly what lets the owner set the same passphrase in every app.
        passphraseField = PaperUi.MonoField(this, "Choose a passphrase");
        passphraseField.Text = VaultPassphraseStore.Passphrase;
        var fieldParams = MatchWrap();
        fieldParams.TopMargin = Dp(DesignTokens.Space.X2);
        block.AddView(passphraseField, fieldParams);

        var helper = PaperUi.Secondary(this,
            "Encrypts the shared Drive backup of this phone's keys so a reset can restore them. " +
            "Shown in the clear so you can copy it into your other apps.");
        helper.TextSize = DesignTokens.TypeSp.Label;
        var helperParams = MatchWrap();
        helperParams.TopMargin = Dp(DesignTokens.Space.X2);
        block.AddView(helper, helperParams);

        card.AddView(block, MatchWrap());
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
        var card = StartGroup("Advanced");
        AddNavRow(card, "Admin (device-owner controls)…", null,
            () => StartActivity(new Intent(this, typeof(UnlockActivity))));
    }

    // ---- Shelves (#11): create / rename / reorder / remove the tray categories -

    private void RenderShelves()
    {
        var card = StartGroup("Shelves");

        var catalog = new CategoryStore(this).Load();
        var ids = catalog.Categories.Select(c => c.Id).ToList();
        foreach (var category in catalog.Categories)
        {
            AddShelfRow(card, category, ids);
        }
        AddShelfAddRow(card);

        AddCardCaption("The category shelves your apps sort onto. New shelves get an engraved glyph — " +
            "AI-drawn from the name when you're signed in with Google, a pencil monogram otherwise.");

        // The AI glyph reads the shared Claude key from the encrypted vault on Drive, which needs a Google
        // sign-in. Without it there was no path to sign in, so the glyph stayed a monogram (P5-27).
        if (VoiceGoogleAccount.IsSignedIn)
        {
            AddCardCaption($"Signed in with Google as {VoiceGoogleAccount.Email} — AI glyphs enabled.");
        }
        else
        {
            var signIn = PaperUi.SecondaryButton(this, "Sign in with Google (for AI shelf glyphs)", SignInWithGoogle);
            var lp = MatchWrap();
            lp.TopMargin = Dp(DesignTokens.Space.X2);
            content!.AddView(signIn, lp);
        }
    }

    private void AddShelfRow(LinearLayout card, Category category, List<string> orderedIds)
    {
        var row = BeginRow(card);

        // A small bronze chip with the shelf's pencil monogram — a settings-scale stand-in for the
        // engraved bookend the shelf carries in the tray.
        var glyph = PaperUi.Secondary(this, ShelfGlyphMaker.Monogram(category.Name));
        glyph.Gravity = GravityFlags.Center;
        glyph.SetTextColor(PaperTheme.Paper);
        glyph.Typeface = PaperTheme.SerifSemibold(this);
        glyph.Background = PaperUi.Fill(this, PaperTheme.Bronze, DesignTokens.Shape.HairlineDp * 3);
        var glyphParams = new LinearLayout.LayoutParams(Dp(28), Dp(32)) { MarginEnd = Dp(DesignTokens.Space.X3) };
        row.AddView(glyph, glyphParams);

        var name = PaperUi.Body(this, category.Name);
        name.Clickable = true;
        name.Click += (_, _) => RenameShelf(category);
        row.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

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
    }

    private void AddShelfAddRow(LinearLayout card)
    {
        var row = BeginRow(card);
        newShelfField = new EditText(this) { Hint = "New shelf name" };
        newShelfField.SetTextColor(PaperTheme.Ink);
        newShelfField.SetHintTextColor(PaperTheme.InkMuted);
        newShelfField.Typeface = PaperTheme.SerifRegular(this);
        row.AddView(newShelfField, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(this, "Add", () => AddShelf(newShelfField.Text)));
    }

    private async void SignInWithGoogle()
    {
        try
        {
            await voiceAccount.SignIn(this);
            Recreate(); // rebuild the screen so the status updates and new glyphs can upgrade
        }
        catch (Exception)
        {
            // Best-effort: a cancelled or failed sign-in leaves the monogram fallback in place.
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }
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

    // ---- Card building blocks -------------------------------------------------

    // Opens a group: a Serif group label, then an empty hairline-ruled card (paper-bright over the
    // paper grain) that the Render* methods fill with rows.
    private LinearLayout StartGroup(string label)
    {
        var labelView = PaperUi.Secondary(this, label);
        labelView.TextSize = DesignTokens.TypeSp.Body;
        labelView.Typeface = PaperTheme.SerifSemibold(this);
        labelView.SetTextColor(PaperTheme.Ink);
        var labelParams = MatchWrap();
        labelParams.TopMargin = Dp(DesignTokens.Space.X6);
        labelParams.BottomMargin = Dp(DesignTokens.Space.X2);
        content!.AddView(labelView, labelParams);

        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.Background = PaperUi.Outline(this, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        content.AddView(card, MatchWrap());
        return card;
    }

    // A padded horizontal row inside a card, with a hairline divider above it when it isn't the first.
    private LinearLayout BeginRow(LinearLayout card)
    {
        if (card.ChildCount > 0)
        {
            var divider = new View(this);
            divider.SetBackgroundColor(PaperTheme.Hairline);
            card.AddView(divider, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, Dp(DesignTokens.Shape.HairlineDp)));
        }
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(DesignTokens.Space.X4), Dp(DesignTokens.Space.X3),
            Dp(DesignTokens.Space.X4), Dp(DesignTokens.Space.X3));
        card.AddView(row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return row;
    }

    // A card row with a title (and optional description), returned so the caller can append a trailing
    // control. The text column takes the leftover width.
    private LinearLayout AddCardRow(LinearLayout card, string title, string? desc)
    {
        var row = BeginRow(card);
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Body(this, title), MatchWrap());
        if (!string.IsNullOrEmpty(desc))
        {
            var descView = PaperUi.Secondary(this, desc!);
            descView.TextSize = DesignTokens.TypeSp.Label;
            var descParams = MatchWrap();
            descParams.TopMargin = Dp(DesignTokens.Space.X1);
            column.AddView(descView, descParams);
        }
        row.AddView(column, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        return row;
    }

    // A tappable row that leads somewhere: a chevron on the right and the whole row acts as the tap.
    private void AddNavRow(LinearLayout card, string title, string? desc, Action onClick)
    {
        var row = AddCardRow(card, title, desc);
        var chevron = PaperUi.Secondary(this, "›");
        chevron.SetTextColor(PaperTheme.InkMuted);
        row.AddView(chevron, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        row.Clickable = true;
        row.Click += (_, _) => onClick();
    }

    // A row with an On/Off segmented control (the kit's toggle, §8 highlighter wash — not an iOS switch).
    private void AddToggleRow(LinearLayout card, string title, string? desc, bool isOn, Action<bool> onChange)
    {
        var row = AddCardRow(card, title, desc);
        var toggle = PaperUi.Segmented(this, ["On", "Off"], index => onChange(index == 0), isOn ? 0 : 1, PaperTheme.Accent);
        // The segmented cells are equal-weight, so the control needs a bounded width to lay out.
        row.AddView(toggle, new LinearLayout.LayoutParams(Dp(128), ViewGroup.LayoutParams.WrapContent));
    }

    // A quiet caption beneath a card — a status sentence (DESIGN.md §7), optionally in the
    // In-progress hue for "last checked …" style lines.
    private void AddCardCaption(string text, bool progress = false)
    {
        var view = PaperUi.Secondary(this, text);
        view.TextSize = DesignTokens.TypeSp.Label;
        if (progress)
        {
            view.SetTextColor(PaperTheme.ProgressText);
        }
        var lp = MatchWrap();
        lp.TopMargin = Dp(DesignTokens.Space.X2);
        content!.AddView(view, lp);
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

    private int Dp(int value) => PaperTheme.Dp(this, value);

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
}
