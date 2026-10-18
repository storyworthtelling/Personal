using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain;
using Constitution.Domain.Health;

namespace Constitution.Health;

/// <summary>
/// The page Health Connect opens when the owner asks why Constitution wants a permission (C-7.1.1):
/// a Serif header, what is read, and that Constitution only reads. Plain paper, no binder tabs, and it
/// opens nothing else: no links, no web view, no other activity. Back and Done both just close it.
///
/// <para><b>Why it must exist.</b> Health Connect will not show its permission dialog for an app that
/// declares no rationale activity, and says nothing about why; the request simply opens nothing (TF
/// §1.3, rule 1). It is the same kind of silent trap as Post's four ROLE_SMS components.</para>
///
/// <para><b>The platform contract, as assumed (the device spike was skipped, D97 — nothing here has
/// met a phone):</b>
/// <list type="bullet">
/// <item><c>androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE</c> is the intent the Health Connect
/// APK sends on Android 13 and lower, and the platform docs still show the filter beside the alias, so
/// it is declared here as an <c>[IntentFilter]</c>. The activity is <b>exported</b>, since another app
/// (Health Connect) has to start it; it shows a static page, so there is nothing for a stray caller to
/// reach. Constitution's floor is Android 14, so this filter is belt-and-braces.</item>
/// <item>On Android 14 and higher Health Connect is part of the framework and finds the page through
/// <c>android.intent.action.VIEW_PERMISSION_USAGE</c> with the
/// <c>android.intent.category.HEALTH_PERMISSIONS</c> category, on an <c>activity-alias</c> that
/// targets this activity and is protected by <c>android.permission.START_VIEW_PERMISSION_USAGE</c>
/// (held by the system only, so no ordinary app can start the alias). No .NET attribute expresses an
/// alias, so it is in <c>Properties/AndroidManifest.xml</c>; the merged manifest carries both.</item>
/// <item>No <c>&lt;queries&gt;</c> entry for the Health Connect package. That element is needed for
/// package visibility on Android 13 and lower, where Health Connect is a separate APK to look up. On
/// 14 and higher it is a system module, the permission request goes through the platform's own
/// permission controller and not through a package lookup, and the floor here is 34. Declaring the
/// old package would also tell the package manager more than the app needs.</item>
/// <item>The theme is the suite's Paper theme, as for every activity here.</item>
/// </list></para>
///
/// <para>The activity's Java name is built from the frozen id (<see cref="ConstitutionIdentity"/>) and
/// guarded by <c>ConstitutionInvariantsTests</c>; the alias targets that exact name.</para>
/// </summary>
[Activity(
    Name = ConstitutionIdentity.HealthRationaleActivityName,
    Label = "Health Connect",
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter(["androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE"])]
public class HealthRationaleActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        SetContentView(BuildPage());
    }

    private View BuildPage()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        page.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        page.AddView(PaperUi.ScreenHeader(this, HealthRationale.Title, PaperTheme.Ink));

        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var sentence = PaperUi.Body(this, HealthRationale.Sentence);
        sentence.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X4), 0, PaperTheme.Dp(this, DesignTokens.Space.X4));
        body.AddView(sentence);
        body.AddView(PaperUi.BodyMuted(this, HealthRationale.BackgroundSentence));

        var scroll = new ScrollView(this);
        scroll.AddView(body);
        page.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        page.AddView(PaperUi.BottomBar(this, PaperUi.PrimaryButton(this, "Done", Finish)));
        return PaperUi.WrapForTopInset(this, page);
    }
}
