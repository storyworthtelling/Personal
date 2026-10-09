using Codex.Domain.Credentials;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Apps;
using Codex.Domain.Design;
using Codex.Domain.Unlock;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace Flyleaf;

/// <summary>
/// The escape hatch. Requesting admin access starts a 6-hour countdown that
/// is visible and cancellable; once it elapses, a 60-minute admin session can
/// be opened. Undo-first everywhere (DESIGN.md §7): even the irreversible
/// finalize-cutover runs as a visible 10-second inline countdown with Cancel —
/// it executes only from a live foreground countdown, and leaving the screen
/// cancels it.
/// </summary>
[Activity(
    Label = "Admin",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class UnlockActivity : Activity
{
    private const int InstallApkRequestCode = 4210;

    private readonly Handler handler = new(Looper.MainLooper!);
    private UnlockStore? store;
    private UnlockStateMachine? machine;
    private InlineCountdown? cutover;
    private LinearLayout? content;
    private LinearLayout? actionBarHost;
    private string? inlineStatus;
    private bool refreshScheduled;
    private UnlockState? lastRenderedState;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        store = new UnlockStore(this);
        var clock = new SystemClockAdapter();
        machine = store.LoadMachine(clock);
        cutover = new InlineCountdown(clock, TimeSpan.FromSeconds(DesignTokens.Motion.CutoverCountdownSeconds));

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

        // Pads itself down by the status-bar/display-cutout inset under enforced edge-to-edge
        // (API 35+), so the page isn't drawn partly behind the battery/clock icons.
        SetContentView(PaperUi.WrapForTopInset(this, root));
    }

    protected override void OnResume()
    {
        base.OnResume();
        Render();
        ScheduleRefresh();
    }

    protected override void OnPause()
    {
        base.OnPause();
        handler.RemoveCallbacksAndMessages(null);
        refreshScheduled = false;
        // An abandoned cutover countdown is cancelled, never resumed hidden.
        cutover!.Cancel();
        store!.Save(machine!);
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
            // Redraw only when something on screen is moving, or the state changed. Rebuilding the admin
            // page every second would dismiss an open delay dropdown; state changes still redraw here.
            if (NeedsTickRedraw())
            {
                Render();
            }
            ScheduleRefresh();
        }, 1000);
    }

    private bool NeedsTickRedraw()
    {
        var state = machine!.CurrentState;
        return state != lastRenderedState
            || state == UnlockState.Pending
            || cutover!.IsRunning
            || cutover.HasExpired;
    }

    private void Render()
    {
        if (cutover!.HasExpired)
        {
            cutover.Cancel();
            PolicyManager.LockDownDebugging(this);
            inlineStatus = "Debugging is now disabled for good.";
        }

        var currentState = machine!.CurrentState;
        // The 60-minute admin session also expires passively (UnlockStateMachine.Touch, recomputed
        // from wall-clock time on every observation) while this screen happens to be sitting open —
        // catch that transition here too, not just the explicit Re-lock button, so Chrome is
        // re-suspended the moment the session actually ends rather than at some later unrelated
        // policy-reapply trigger.
        if (lastRenderedState == UnlockState.AdminActive && currentState != UnlockState.AdminActive)
        {
            PolicyManager.ApplyCurrentPolicy(this);
        }
        lastRenderedState = currentState;

        content!.RemoveAllViews();
        actionBarHost!.RemoveAllViews();
        switch (currentState)
        {
            case UnlockState.Locked:
                AddHeader("Locked");
                AddBody($"Admin access requires a {machine.UnlockDelay.TotalHours:0}-hour delay. The countdown is visible and can be cancelled at any time.");
                PinActions(PaperUi.PrimaryButton(this, "Request admin unlock", () => Transition(machine.RequestUnlock)));
                break;

            case UnlockState.Pending:
                AddHeader("Unlock pending");
                AddBody("Admin mode opens in");
                var remaining = machine.RemainingDelay ?? TimeSpan.Zero;
                var countdown = PaperUi.Countdown(this);
                countdown.Text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                countdown.Gravity = GravityFlags.CenterHorizontal;
                content!.AddView(countdown, MatchWrap());
                PinActions(PaperUi.SecondaryButton(this, "Cancel request", () => Transition(machine.CancelRequest)));
                break;

            case UnlockState.Ready:
                AddHeader("Ready");
                AddBody("The delay has elapsed. Admin mode lasts 60 minutes and re-locks automatically.");
                // Entering admin mode is what un-suspends the login browser (Chrome) for OAuth
                // Custom Tabs — reapply policy immediately rather than waiting for the next
                // unrelated trigger (a reboot, an app install) to pick the change up.
                PinActions(PaperUi.PrimaryButton(this, "Enter admin mode", () => TransitionAndReapplyPolicy(machine.EnterAdminMode)));
                break;

            case UnlockState.AdminActive:
                RenderAdminSession();
                break;
        }
    }

    private void RenderAdminSession()
    {
        AddHeader("Admin mode");
        AddBody("Session ends automatically after 60 minutes.");
        var delayParams = MatchWrap();
        delayParams.BottomMargin = PaperTheme.Dp(this, DesignTokens.Space.X3);
        content!.AddView(DelayRow(), delayParams);
        AddContentButton(PaperUi.SecondaryButton(this, "Unblock apps…", ShowBlockedApps));
        AddContentButton(PaperUi.SecondaryButton(this, "Review policy log…", ShowPolicyLog));
        AddContentButton(PaperUi.SecondaryButton(this, "Set update token…", ShowUpdateTokenPrompt));
        // "Install from GitHub now" moved to the non-admin Settings panel (LauncherSettingsActivity) —
        // installing the owner's own app updates shouldn't require the 6-hour admin gate.
        AddContentButton(PaperUi.SecondaryButton(this, "Install APK from Downloads…", PickApkToInstall));

        // The finalize-cutover control appears only while there's still work to do. Its one job is to
        // disable debugging permanently, so once that's done the button retires for a done-state line
        // (the flag the enforcement itself reads is the source of truth).
        var lockedDown = new BlocklistStore(this).DebuggingLockedDown;
        switch (CutoverStatus.Decide(lockedDown, cutover!.IsRunning))
        {
            case CutoverPrompt.Countdown:
                // The live cutover countdown: Cascadia seconds + Cancel beside it.
                var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                row.SetGravity(GravityFlags.CenterVertical);
                var seconds = (int)Math.Ceiling(cutover.Remaining.TotalSeconds);
                var timer = PaperUi.Countdown(this);
                timer.Text = $"0:{seconds:D2}";
                row.AddView(timer, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
                row.AddView(PaperUi.SecondaryButton(this, "Cancel", () =>
                {
                    cutover.Cancel();
                    Render();
                }));
                var rowParams = MatchWrap();
                rowParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X4);
                content!.AddView(row, rowParams);
                AddBody("Disabling debugging when the countdown ends. ADB access becomes impossible; only a recovery-mode factory reset undoes the lockdown.");
                break;

            case CutoverPrompt.Finalize:
                AddContentButton(PaperUi.DestructiveButton(this, "Finalize cutover: disable debugging permanently", () =>
                {
                    cutover.Start();
                    Render();
                }));
                break;

            case CutoverPrompt.Complete:
                // Fully cut over: nothing left to finalize, so no button — just a quiet done-state line.
                AddBody("Cutover complete — debugging is permanently disabled.");
                break;
        }

        if (inlineStatus is not null)
        {
            var status = PaperUi.Secondary(this, inlineStatus);
            var statusParams = MatchWrap();
            statusParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X4);
            content!.AddView(status, statusParams);
        }

        // Re-locking is what re-suspends Chrome; reapply immediately rather than leaving it
        // runnable until some unrelated trigger reapplies policy.
        PinActions(PaperUi.SecondaryButton(this, "Re-lock now", () => TransitionAndReapplyPolicy(machine!.Relock)));
    }

    // The delay is chosen on the admin page, not while Locked: a request in flight (Pending or Ready)
    // refuses a change (UnlockStateMachine.SetDelay), and admin mode is the only state that can see it.
    // The spinner is rebuilt on each admin render, with its selection set before its listener can fire,
    // so the spurious first selection a Spinner reports on layout cannot write a value the owner never picked.
    private LinearLayout DelayRow()
    {
        var current = Array.IndexOf(UnlockStateMachine.DelayChoices, (int)machine!.UnlockDelay.TotalHours);
        var spinner = new Spinner(this);
        spinner.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
            UnlockStateMachine.DelayChoices.Select(DelayLabel).ToArray());
        if (current >= 0)
        {
            spinner.SetSelection(current);
        }
        spinner.ItemSelected += (_, e) => OnDelayPicked(e.Position);

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(PaperUi.Secondary(this, "Admin unlock delay"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(spinner, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        return row;
    }

    private static string DelayLabel(int hours) =>
        hours % 24 == 0
            ? $"{hours / 24} day{(hours == 24 ? string.Empty : "s")}"
            : $"{hours} hour{(hours == 1 ? string.Empty : "s")}";

    private void OnDelayPicked(int position)
    {
        var hours = UnlockStateMachine.DelayChoices[position];
        if (TimeSpan.FromHours(hours) == machine!.UnlockDelay)
        {
            return;
        }
        try
        {
            machine.SetDelay(TimeSpan.FromHours(hours));
        }
        catch (InvalidOperationException e)
        {
            inlineStatus = e.Message;
            Render();
            return;
        }
        store!.Save(machine);
        inlineStatus = $"Admin unlock delay set to {DelayLabel(hours)}. It applies to the next request.";
        Render();
    }

    private void ShowBlockedApps()
    {
        var blocklistStore = new BlocklistStore(this);
        var data = blocklistStore.Load();
        var blocked = data.BlockedPackages.OrderBy(p => p).ToArray();
        if (blocked.Length == 0)
        {
            inlineStatus = "No blocked apps.";
            Render();
            return;
        }

        new AlertDialog.Builder(this)
            .SetTitle("Unblock which app?")!
            .SetItems(blocked, (_, e) =>
            {
                var categorizer = new AppCategorizer(data);
                categorizer.Unblock(blocked[e.Which]);
                blocklistStore.Save(categorizer.Export());
                new PolicyAuditStore(this).Record(
                    Codex.Domain.Policy.PolicyEventKind.AppUnblocked, $"Unblocked {blocked[e.Which]}");
                PolicyManager.ApplyCurrentPolicy(this);
                inlineStatus = $"Unblocked {blocked[e.Which]}.";
                Render();
            })!
            .Show();
    }

    // The reviewable/exportable policy audit log: what the device-owner policy did and any failures,
    // so a silent block (like Gemini being quarantined) is diagnosable instead of a mystery. The
    // rolling record + newest-first format are the unit-tested PolicyAuditLog; this only shows it and
    // hands the plain text to the system share sheet for export.
    private void ShowPolicyLog()
    {
        var text = new PolicyAuditStore(this).Load().Format();
        new AlertDialog.Builder(this)
            .SetTitle("Policy log")!
            .SetMessage(text)!
            .SetPositiveButton("Export", (_, _) => ExportPolicyLog(text))!
            .SetNegativeButton("Close", (_, _) => { })!
            .Show();
    }

    private void ExportPolicyLog(string text)
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, "Flyleaf policy log");
        intent.PutExtra(Intent.ExtraText, text);
        try
        {
            StartActivity(Intent.CreateChooser(intent, "Export policy log"));
        }
        catch (Android.Content.ActivityNotFoundException)
        {
            inlineStatus = "No app is available to export the log.";
            Render();
        }
    }

    // Manual install path for the owner's own APKs (the sibling repo apps and the
    // FocusPhone heads) dropped into Downloads — the replacement for the adb
    // sideload once debugging is locked down. NoInstallUnknownSources stays on:
    // this is a first-party, admin-gated, device-owner silent install, not the
    // open system installer. A Storage Access Framework picker hands back a
    // readable content URI, so no storage permission is needed under scoped
    // storage.
    private void PickApkToInstall()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable!);
        intent.SetType("application/vnd.android.package-archive");
        // Some document providers report a saved .apk as a generic octet-stream;
        // accept both so the file the restricted browser downloaded is selectable.
        intent.PutExtra(Intent.ExtraMimeTypes, new[]
        {
            "application/vnd.android.package-archive",
            "application/octet-stream",
        });
        try
        {
            StartActivityForResult(intent, InstallApkRequestCode);
        }
        catch (Android.Content.ActivityNotFoundException)
        {
            inlineStatus = "No file picker is available to choose an APK.";
            Render();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == InstallApkRequestCode && resultCode == Result.Ok && data?.Data is { } uri)
        {
            InstallApkFromUri(uri);
        }
    }

    private async void InstallApkFromUri(Android.Net.Uri uri)
    {
        // Success is a quiet state change (DESIGN.md §5): the app simply appears
        // in the tray. Failures surface inline.
        inlineStatus = "Installing… the app appears in Apps when it finishes.";
        Render();
        try
        {
            using var input = ContentResolver!.OpenInputStream(uri);
            if (input is null)
            {
                inlineStatus = "Could not read the selected file.";
                Render();
                return;
            }
            await ApkInstaller.InstallAsync(this, input);
        }
        catch (Exception e)
        {
            inlineStatus = $"Install failed: {e.Message}";
            Render();
        }
    }

    private void ShowUpdateTokenPrompt()
    {
        var input = PaperUi.MonoField(this, "GitHub token with repo read access");
        input.Text = new UpdateSettingsStore(this).GitHubToken;

        // Show the token field only when it's missing or GitHub just rejected it; a working token collapses to
        // a status line with Replace — it's backed up to the vault and restored automatically. Both views are
        // built and toggled by visibility so Replace needn't rebuild the dialog, and Save reads input.Text
        // whether or not the field is visible.
        var tokenState = CredentialFieldState.Decide(
            !string.IsNullOrEmpty(input.Text),
            new CredentialHealthStore().Load().Get(CredentialIds.GitHubUpdateToken));
        var tokenStatusRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        tokenStatusRow.SetGravity(GravityFlags.CenterVertical);
        tokenStatusRow.AddView(PaperUi.Secondary(this, "Update token: " + tokenState.StatusText),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        tokenStatusRow.AddView(PaperUi.SecondaryButton(this, "Replace", () =>
        {
            input.Text = string.Empty;
            input.Visibility = ViewStates.Visible;
            tokenStatusRow.Visibility = ViewStates.Gone;
        }));
        input.Visibility = tokenState.ShowEntry ? ViewStates.Visible : ViewStates.Gone;
        tokenStatusRow.Visibility = tokenState.ShowEntry ? ViewStates.Gone : ViewStates.Visible;

        // The secrets-vault passphrase lives with the other admin secret: it encrypts the shared Drive file
        // that backs up the update token (and every app's keys) so a factory reset can restore them.
        // Shown in the clear (not a password field) so it can be read back and copied — it's the one secret
        // that isn't kept in the vault, so being able to see it is what lets the owner set the same one everywhere.
        var passphrase = PaperUi.MonoField(this, "Secrets vault passphrase");
        passphrase.Text = VaultPassphraseStore.Passphrase;

        var fields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        fields.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X4),
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), 0);
        fields.AddView(tokenStatusRow);
        fields.AddView(input);
        fields.AddView(passphrase);

        new AlertDialog.Builder(this)
            .SetTitle("Update channel token")!
            .SetView(fields)!
            .SetPositiveButton("Save", (_, _) =>
            {
                new UpdateSettingsStore(this).GitHubToken = input.Text;
                VaultPassphraseStore.Passphrase = passphrase.Text ?? string.Empty;
                PublishTokenToVault(input.Text);
                inlineStatus = "Update token set.";
                Render();
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // Backs the GitHub update token up to the owner's encrypted secrets vault (needs Google signed in for the
    // greeting/vault and a passphrase set). Best-effort and off the UI thread.
    private async void PublishTokenToVault(string? token)
    {
        try
        {
            var access = new SecretsVaultAccess(new VoiceGoogleAccount().GetAccessToken, "Flyleaf");
            await access.Service.PublishAsync(
                v => v with { GitHubUpdateToken = token ?? string.Empty },
                System.Threading.CancellationToken.None);
        }
        catch
        {
            // Best-effort: the vault picks the token up on the next successful save.
        }
    }

    private void Transition(Action action)
    {
        action();
        store!.Save(machine!);
        inlineStatus = null;
        Render();
    }

    // For a transition that flips AdminModeActive (entering or leaving admin mode): the login
    // browser's suspended/runnable state is decided from that flag on every policy pass, so the
    // change has to be applied here rather than waiting for some unrelated trigger to reapply it.
    private void TransitionAndReapplyPolicy(Action action)
    {
        Transition(action);
        PolicyManager.ApplyCurrentPolicy(this);
    }

    private void AddHeader(string text) => content!.AddView(PaperUi.PageHeader(this, text));

    private void AddBody(string text)
    {
        var view = PaperUi.Secondary(this, text);
        view.SetPadding(0, 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X6));
        content!.AddView(view);
    }

    private void AddContentButton(Button button)
    {
        var layoutParams = MatchWrap();
        layoutParams.BottomMargin = PaperTheme.Dp(this, DesignTokens.Space.X3);
        content!.AddView(button, layoutParams);
    }

    private void PinActions(params View[] actions) =>
        actionBarHost!.AddView(PaperUi.BottomBar(this, actions));

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
}
