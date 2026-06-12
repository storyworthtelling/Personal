using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Apps;
using FocusPhone.Domain.Unlock;

namespace FocusPhone.Launcher;

/// <summary>
/// The escape hatch. Requesting admin access starts a 72-hour countdown that
/// is visible and cancellable; once it elapses, a 60-minute admin session can
/// be opened to unblock apps, re-pair integrations, or finalize the cutover.
/// </summary>
[Activity(
    Label = "Admin",
    Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class UnlockActivity : Activity
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private UnlockStore? store;
    private UnlockStateMachine? machine;
    private LinearLayout? content;
    private bool refreshScheduled;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        store = new UnlockStore(this);
        machine = store.LoadMachine(new SystemClockAdapter());

        var root = new ScrollView(this);
        root.SetBackgroundColor(Palette.Paper);
        content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(64, 128, 64, 64);
        root.AddView(content);
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
        content!.RemoveAllViews();
        switch (machine!.CurrentState)
        {
            case UnlockState.Locked:
                AddTitle("Locked");
                AddBody("Admin access requires a 72-hour delay. The countdown is visible and can be cancelled at any time.");
                AddButton("Request admin unlock", () => Transition(machine.RequestUnlock));
                break;

            case UnlockState.Pending:
                AddTitle("Unlock pending");
                var remaining = machine.RemainingDelay ?? TimeSpan.Zero;
                AddBody($"Admin mode opens in {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}");
                AddButton("Cancel request", () => Transition(machine.CancelRequest));
                break;

            case UnlockState.Ready:
                AddTitle("Ready");
                AddBody("The delay has elapsed. Admin mode lasts 60 minutes and re-locks automatically.");
                AddButton("Enter admin mode", () => Transition(machine.EnterAdminMode));
                break;

            case UnlockState.AdminActive:
                RenderAdminSession();
                break;
        }
    }

    private void RenderAdminSession()
    {
        AddTitle("Admin mode");
        AddBody("Session ends automatically after 60 minutes.");
        AddButton("Unblock apps…", ShowBlockedApps);
        AddButton("Re-lock now", () => Transition(machine!.Relock));
        AddButton("Finalize cutover: disable debugging permanently", () =>
            new AlertDialog.Builder(this)
                .SetTitle("Disable debugging?")!
                .SetMessage("This is the final cutover step. ADB access becomes impossible; only a recovery-mode factory reset can undo the lockdown.")!
                .SetPositiveButton("Disable forever", (_, _) => PolicyManager.LockDownDebugging(this))!
                .SetNegativeButton("Cancel", (_, _) => { })!
                .Show());
    }

    private void ShowBlockedApps()
    {
        var blocklistStore = new BlocklistStore(this);
        var data = blocklistStore.Load();
        var blocked = data.BlockedPackages.OrderBy(p => p).ToArray();
        if (blocked.Length == 0)
        {
            Toast.MakeText(this, "No blocked apps.", ToastLength.Short)!.Show();
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
            })!
            .Show();
    }

    private void Transition(Action action)
    {
        action();
        store!.Save(machine!);
        Render();
    }

    private void AddTitle(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 32 };
        view.SetTextColor(Palette.Ink);
        view.SetPadding(0, 0, 0, 32);
        content!.AddView(view);
    }

    private void AddBody(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 16 };
        view.SetTextColor(Palette.InkMuted);
        view.SetPadding(0, 0, 0, 48);
        content!.AddView(view);
    }

    private void AddButton(string label, Action onClick)
    {
        var button = new Button(this) { Text = label };
        button.Click += (_, _) => onClick();
        var layoutParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = 24,
        };
        content!.AddView(button, layoutParams);
    }
}
