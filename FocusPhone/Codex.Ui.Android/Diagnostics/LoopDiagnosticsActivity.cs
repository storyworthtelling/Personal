using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// The shared Diagnostics screen (LOG-LOOP-PLAN.md §3.3): the installed head's log since the last confirmed
/// upload, the upload status sentence, and Send logs now as its primary act — declared as the head's own key
/// action, so a send that fails or never reports is itself in the next upload. Share and Copy carry the export;
/// Clear is undo-first (DESIGN.md §7). Heads with no Diagnostics screen of their own open this one.
/// </summary>
[Activity(Label = "Diagnostics", Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class LoopDiagnosticsActivity : Activity
{
    private FrameLayout? undoHost;
    private LinearLayout? column;
    private bool sending;

    public static void Open(Context context) =>
        context.StartActivity(new Intent(context, typeof(LoopDiagnosticsActivity)));

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this, "Diagnostics");
        PaperTransitions.Apply(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(PaperTheme.Paper);
        root.AddView(PaperUi.ScreenHeader(this, "Diagnostics", PaperTheme.ResolvedMark));

        undoHost = new FrameLayout(this);
        var scroll = new ScrollView(this);
        column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var pad = PaperTheme.Dp(this, DesignTokens.Space.X4);
        column.SetPadding(pad, 0, pad, pad);
        scroll.AddView(column);
        undoHost.AddView(scroll, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        root.AddView(undoHost, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.PrimaryButton(this, "Send logs now", SendLogsNow),
            PaperUi.SecondaryButton(this, "Share", Share),
            PaperUi.QuietButton(this, "Copy", Copy),
            PaperUi.QuietButton(this, "Clear", ClearLog)));
        SetContentView(root);
        Bind();
    }

    private void Bind()
    {
        column!.RemoveAllViews();
        var head = HeadDiagnostics.Current;
        var text = head?.ReadAll().TrimEnd() ?? string.Empty;
        if (sending)
        {
            column.AddView(PaperUi.Secondary(this, "Sending the logs to Drive…"), Spaced(DesignTokens.Space.X4));
        }
        else if (head?.UploadStatus is { } status)
        {
            column.AddView(PaperUi.Secondary(this, status), Spaced(DesignTokens.Space.X4));
        }
        column.AddView(PaperUi.Body(this, text.Length > 0
            ? "Problems captured since the logs last went to Drive, oldest first. They go weekly, on Saturday evening, "
              + "and leave the phone once Drive has a confirmed copy."
            : "Nothing has been captured since the logs last went to Drive."), Spaced(DesignTokens.Space.X4));
        if (text.Length > 0)
        {
            var card = PaperUi.HairlineCard(this);
            card.AddView(PaperUi.Mono(this, text));
            column.AddView(card, Spaced(0));
        }
    }

    private void SendLogsNow()
    {
        var head = HeadDiagnostics.Current;
        if (head is null || sending)
        {
            return;
        }
        sending = true;
        Bind();
        var action = TapWatch.Action(head.Setup.SendLogsNowAction, TimeSpan.FromMinutes(5));
        Task.Run(async () =>
        {
            var result = await head.UploadAsync(CancellationToken.None);
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
                Bind();
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

    private void Share()
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraText, HeadDiagnostics.Current?.BuildExport() ?? string.Empty);
        StartActivity(Intent.CreateChooser(intent, "Share diagnostics"));
    }

    private void Copy()
    {
        if (GetSystemService(ClipboardService) is ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("Diagnostics", HeadDiagnostics.Current?.BuildExport() ?? string.Empty);
        }
    }

    private void ClearLog()
    {
        var head = HeadDiagnostics.Current;
        if (head is null || !head.HasEntries)
        {
            return;
        }
        var snapshot = head.ReadAll();
        head.Clear();
        Bind();
        DogEar.Show(this, undoHost!, "Cleared diagnostics.", onUndo: () =>
        {
            head.Restore(snapshot);
            Bind();
        });
    }

    private LinearLayout.LayoutParams Spaced(int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = PaperTheme.Dp(this, bottomDp) };
}
