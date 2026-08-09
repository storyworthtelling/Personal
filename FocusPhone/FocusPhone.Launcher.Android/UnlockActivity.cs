using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Apps;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Unlock;
using FocusPhone.Ui;
using FocusPhone.Ui.Voice;
using Storyworthtelling.Voice.Secrets;

namespace FocusPhone.Launcher;

/// <summary>
/// The escape hatch. Requesting admin access starts a 72-hour countdown that
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
    private readonly Handler handler = new(Looper.MainLooper!);
    private UnlockStore? store;
    private UnlockStateMachine? machine;
    private InlineCountdown? cutover;
    private LinearLayout? content;
    private LinearLayout? actionBarHost;
    private string? inlineStatus;
    private bool refreshScheduled;

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

        SetContentView(root);
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
            Render();
            ScheduleRefresh();
        }, 1000);
    }

    private void Render()
    {
        if (cutover!.HasExpired)
        {
            cutover.Cancel();
            PolicyManager.LockDownDebugging(this);
            inlineStatus = "Debugging is now disabled for good.";
        }

        content!.RemoveAllViews();
        actionBarHost!.RemoveAllViews();
        switch (machine!.CurrentState)
        {
            case UnlockState.Locked:
                AddHeader("Locked");
                AddBody("Admin access requires a 72-hour delay. The countdown is visible and can be cancelled at any time.");
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
                PinActions(PaperUi.PrimaryButton(this, "Enter admin mode", () => Transition(machine.EnterAdminMode)));
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
        AddContentButton(PaperUi.SecondaryButton(this, "Unblock apps…", ShowBlockedApps));
        AddContentButton(PaperUi.SecondaryButton(this, "Set update token…", ShowUpdateTokenPrompt));

        if (cutover!.IsRunning)
        {
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
        }
        else
        {
            AddContentButton(PaperUi.DestructiveButton(this, "Finalize cutover: disable debugging permanently", () =>
            {
                cutover.Start();
                Render();
            }));
        }

        if (inlineStatus is not null)
        {
            var status = PaperUi.Secondary(this, inlineStatus);
            var statusParams = MatchWrap();
            statusParams.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X4);
            content!.AddView(status, statusParams);
        }

        PinActions(PaperUi.SecondaryButton(this, "Re-lock now", () => Transition(machine!.Relock)));
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
                PolicyManager.ApplyCurrentPolicy(this);
                inlineStatus = $"Unblocked {blocked[e.Which]}.";
                Render();
            })!
            .Show();
    }

    private void ShowUpdateTokenPrompt()
    {
        var input = PaperUi.MonoField(this, "GitHub token with repo read access");
        input.Text = new UpdateSettingsStore(this).GitHubToken;

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
            var access = new SecretsVaultAccess(new VoiceGoogleAccount().GetAccessToken, "FocusPhone.Launcher");
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
