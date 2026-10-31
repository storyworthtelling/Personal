using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// Shows the most recent captured crash in Paper &amp; Ink: a Serif in-page
/// header, a plain-language line, and the saved report in Cascadia mono. The
/// report is selectable and can be copied or shared, so the fault can be sent
/// off-device — there is no ADB or logcat after cutover (CUTOVER.md §17). Lives
/// in the shared library so every head merges the same activity into its
/// manifest. Launched by <see cref="CrashReporter.ShowPendingCrash"/>.
/// </summary>
[Activity(
    Label = "Crash report",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class CrashViewerActivity : Activity
{
    private string detail = "No crash details were captured.";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);

        detail = Intent?.GetStringExtra(CrashReporter.ExtraCrashText) ?? detail;

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var scroll = new ScrollView(this);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X12),
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X6));

        layout.AddView(PaperUi.PageHeader(this, "Something went wrong"));

        layout.AddView(PaperUi.Body(this,
            "This app closed unexpectedly the last time it ran. The details below "
            + "were saved on the device so the fault can be diagnosed. Copy or share "
            + "them, or select the text directly, to send them on."),
            Spaced(0, DesignTokens.Space.X4));

        var detailCard = PaperUi.HairlineCard(this);
        var report = PaperUi.Mono(this, detail);
        report.SetTextIsSelectable(true); // the report is the point — let it be highlighted and copied
        detailCard.AddView(report);
        layout.AddView(detailCard, Spaced(0, 0));

        scroll.AddView(layout);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        var copy = PaperUi.SecondaryButton(this, "Copy", CopyToClipboard);
        var share = PaperUi.SecondaryButton(this, "Share", ShareReport);
        var dismiss = PaperUi.QuietButton(this, "Dismiss", Finish);
        root.AddView(PaperUi.BottomBar(this, copy, share, dismiss));

        SetContentView(root);
    }

    private void CopyToClipboard()
    {
        if (GetSystemService(ClipboardService) is ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("FocusPhone crash report", detail);
            Toast.MakeText(this, "Copied to clipboard.", ToastLength.Short)!.Show();
        }
    }

    private void ShareReport()
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, "FocusPhone crash report");
        intent.PutExtra(Intent.ExtraText, detail);
        StartActivity(Intent.CreateChooser(intent, "Share crash report"));
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topDp),
            BottomMargin = PaperTheme.Dp(this, bottomDp),
        };
}
