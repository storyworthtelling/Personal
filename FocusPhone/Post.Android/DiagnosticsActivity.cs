using Codex.Domain.Post;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;

namespace Post;

/// <summary>
/// Settings → Diagnostics: shows the rolling diagnostic log (non-fatal failures
/// plus captured crashes) in Paper &amp; Ink and lets the owner copy, share, or
/// clear it. The share/export bundle carries a device snapshot, the full log, and
/// the last crash so a fault can be diagnosed off-device — there is no logcat
/// after cutover (CUTOVER.md §17). Reached from the Settings screen.
/// </summary>
[Activity(
    Label = "Diagnostics",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class DiagnosticsActivity : Activity
{
    private FrameLayout? undoHost;
    private LinearLayout? contentColumn;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        undoHost = new FrameLayout(this);
        var scroll = new ScrollView(this);
        contentColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        BindContent();
        scroll.AddView(contentColumn);
        undoHost.AddView(scroll, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        root.AddView(BinderFrame.Wrap(
            this, "Diagnostics", PostShellAreas.All[PostShellAreas.Settings].Hue, undoHost,
            PostShellAreas.All, PostShellAreas.Settings, SwitchArea),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // Send logs now is the screen's one primary act (§7): the weekly upload, this minute.
        var send = PaperUi.PrimaryButton(this, "Send logs now", SendLogsNow);
        var share = PaperUi.SecondaryButton(this, "Share", ShareReport);
        var copy = PaperUi.QuietButton(this, "Copy", CopyToClipboard);
        var clear = PaperUi.QuietButton(this, "Clear", ClearLog);
        root.AddView(PaperUi.BottomBar(this, send, share, copy, clear));

        SetContentView(root);
    }

    // Names the tab PostActivity should land on once this screen finishes (see
    // PostActivity.PendingArea); ReorderToFront brings its existing instance forward rather
    // than creating a second one, or starts it fresh if it isn't running at all.
    private void SwitchArea(int index)
    {
        PostActivity.PendingArea = index;
        var intent = new Intent(this, typeof(PostActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    private void BindContent()
    {
        contentColumn!.RemoveAllViews();
        var logText = PostDiagnostics.ReadAll().TrimEnd();
        var hasEntries = logText.Length > 0;

        // The loading sentence, then the outcome as a state change (§5): no spinner, no toast.
        if (sending)
        {
            contentColumn.AddView(PaperUi.Secondary(this, "Sending the logs to Drive…"), Spaced(0, DesignTokens.Space.X4));
        }
        else if (PostLogUpload.Status(this) is { } status)
        {
            contentColumn.AddView(PaperUi.Secondary(this, status), Spaced(0, DesignTokens.Space.X4));
        }

        contentColumn.AddView(PaperUi.Body(this, hasEntries
            ? "Problems captured since the logs last went to Drive, oldest first. They go weekly, on Saturday "
              + "evening, and leave the phone once Drive has a confirmed copy. Use Share to send this to whoever is "
              + "helping fix the app; the shared copy also carries the texts trail — when each text arrived, "
              + "by which route, never what it said."
            : "No problems have been captured. Anything that goes wrong — including quietly, in the background "
              + "— will be recorded here."),
            Spaced(0, DesignTokens.Space.X4));

        if (hasEntries)
        {
            var card = PaperUi.HairlineCard(this);
            card.AddView(PaperUi.Mono(this, logText));
            contentColumn.AddView(card, Spaced(0, 0));
        }
    }

    private bool sending;

    // The weekly upload, now: the same run the worker makes, outside its window. Declared as a key action,
    // so a send that fails or never reports is itself in the next upload.
    private void SendLogsNow()
    {
        if (sending)
        {
            return;
        }
        sending = true;
        BindContent();
        var action = TapWatch.Action(PostKeyActions.SendLogsNow, TimeSpan.FromMinutes(5));
        var app = ApplicationContext!;
        Task.Run(async () =>
        {
            var result = await PostLogUpload.RunAsync(app, CancellationToken.None);
            if (result.Succeeded)
            {
                action.Succeeded();
            }
            else
            {
                action.Failed(result.Refusal is not null ? "waiting for the vault passphrase" : result.Error ?? "did not complete");
            }
            RunOnUiThread(() =>
            {
                sending = false;
                BindContent();
                if (!result.Succeeded)
                {
                    DogEar.ShowNotices(this, undoHost!,
                    [
                        new DogEar.Notice("The logs did not reach Drive.",
                            result.Refusal is not null
                                ? "Set the vault passphrase in Settings; the logs wait on the phone until then."
                                : "They are still on the phone and will go with the next weekly upload.",
                            PaperTheme.ProblemMark),
                    ]);
                }
            });
        });
    }

    private void CopyToClipboard()
    {
        if (GetSystemService(Context.ClipboardService) is ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText(
                "FocusPhone diagnostics", PostDiagnostics.BuildExport(this));
            Toast.MakeText(this, "Copied to clipboard.", ToastLength.Short)!.Show();
        }
    }

    private void ShareReport()
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, "Post diagnostics");
        intent.PutExtra(Intent.ExtraText, PostDiagnostics.BuildExport(this));
        StartActivity(Intent.CreateChooser(intent, "Share diagnostics"));
    }

    private void ClearLog()
    {
        if (!PostDiagnostics.HasEntries)
        {
            return;
        }
        // Destructive acts are undo-first, never a confirm dialog (DESIGN.md §7):
        // clear now, refresh to the empty state, and offer a window to reverse it.
        var snapshot = PostDiagnostics.ReadAll();
        PostDiagnostics.Clear();
        BindContent();
        DogEar.Show(this, undoHost!, "Cleared diagnostics.", onUndo: () =>
        {
            PostDiagnostics.Restore(snapshot);
            BindContent();
        });
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topDp),
            BottomMargin = PaperTheme.Dp(this, bottomDp),
        };
}
