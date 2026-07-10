using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;

namespace FocusPhone.Ui;

/// <summary>
/// Shows the most recent captured crash in Paper &amp; Ink: a Serif in-page
/// header, a plain-language line, and the saved report in Cascadia mono. Lives
/// in the shared library so every head merges the same activity into its
/// manifest. Launched by <see cref="CrashReporter.ShowPendingCrash"/>.
/// </summary>
[Activity(
    Label = "Crash report",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class CrashViewerActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        var detail = Intent?.GetStringExtra(CrashReporter.ExtraCrashText)
            ?? "No crash details were captured.";

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var scroll = new ScrollView(this);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X12),
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X6));

        layout.AddView(PaperUi.PageHeader(this, "Something went wrong"));

        layout.AddView(PaperUi.Body(this,
            "This app closed unexpectedly the last time it ran. The details below "
            + "were saved on the device so the fault can be diagnosed."),
            Spaced(0, DesignTokens.Space.X4));

        var detailCard = PaperUi.HairlineCard(this);
        detailCard.AddView(PaperUi.Mono(this, detail));
        layout.AddView(detailCard, Spaced(0, 0));

        scroll.AddView(layout);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        var dismiss = PaperUi.SecondaryButton(this, "Dismiss", Finish);
        root.AddView(PaperUi.BottomBar(this, dismiss));

        SetContentView(root);
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topDp),
            BottomMargin = PaperTheme.Dp(this, bottomDp),
        };
}
