using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;

namespace Score;

/// <summary>
/// Manage → Diagnostics: shows the rolling diagnostic log (non-fatal playback and
/// queue failures plus captured crashes) in Paper &amp; Ink and lets the owner copy,
/// share, or clear it. The share/export bundle carries a device snapshot, the full
/// log, and the last crash so a fault can be diagnosed off-device — there is no
/// logcat after cutover (CUTOVER.md §17). Reached from the Subscriptions screen.
/// </summary>
[Activity(
    Label = "Diagnostics",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class AudioDiagnosticsActivity : Activity
{
    private FrameLayout? undoHost;
    private LinearLayout? contentColumn;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        undoHost = new FrameLayout(this);
        var scroll = new ScrollView(this);
        contentColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        contentColumn.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X12),
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X6));
        BindContent();
        scroll.AddView(contentColumn);
        undoHost.AddView(scroll, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        root.AddView(undoHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        var copy = PaperUi.SecondaryButton(this, "Copy", CopyToClipboard);
        var share = PaperUi.SecondaryButton(this, "Share", ShareReport);
        var clear = PaperUi.QuietButton(this, "Clear", ClearLog);
        root.AddView(PaperUi.BottomBar(this, copy, share, clear));

        SetContentView(root);
    }

    private void BindContent()
    {
        contentColumn!.RemoveAllViews();
        var logText = AudioDiagnostics.ReadAll().TrimEnd();
        var hasEntries = logText.Length > 0;

        contentColumn.AddView(PaperUi.PageHeader(this, "Diagnostics"));
        contentColumn.AddView(PaperUi.Body(this, hasEntries
            ? "Recent problems captured on this device, oldest first. Use Share to send this to whoever is "
              + "helping fix the app."
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

    private void CopyToClipboard()
    {
        if (GetSystemService(Context.ClipboardService) is ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText(
                "FocusPhone diagnostics", AudioDiagnostics.BuildExport(this));
            Toast.MakeText(this, "Copied to clipboard.", ToastLength.Short)!.Show();
        }
    }

    private void ShareReport()
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, "FocusPhone Audio diagnostics");
        intent.PutExtra(Intent.ExtraText, AudioDiagnostics.BuildExport(this));
        StartActivity(Intent.CreateChooser(intent, "Share diagnostics"));
    }

    private void ClearLog()
    {
        if (!AudioDiagnostics.HasEntries)
        {
            return;
        }
        // Destructive acts are undo-first, never a confirm dialog (DESIGN.md §7):
        // clear now, refresh to the empty state, and offer a window to reverse it.
        var snapshot = AudioDiagnostics.ReadAll();
        AudioDiagnostics.Clear();
        BindContent();
        DogEar.Show(this, undoHost!, "Cleared diagnostics.", onUndo: () =>
        {
            AudioDiagnostics.Restore(snapshot);
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
