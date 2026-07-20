using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Ui;

namespace FocusPhone.Communication;

/// <summary>
/// Gmail account setup: address + app password. Reached by long-pressing the
/// thread list; on a locked-down phone this is where admin mode rotates the
/// app password.
/// </summary>
[Activity(
    Label = "Settings",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class SettingsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        var account = new AccountStore(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(this);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X12),
            PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp), PaperTheme.Dp(this, DesignTokens.Space.X6));

        // The owner's medallion marks the settings screen (DESIGN.md §13).
        var headerRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        headerRow.SetGravity(GravityFlags.CenterVertical);
        var medallionId = Resources!.GetIdentifier("owner_medallion", "drawable", PackageName);
        if (medallionId != 0)
        {
            var medallion = new ImageView(this);
            medallion.SetImageResource(medallionId);
            var size = PaperTheme.Dp(this, DesignTokens.Shape.TouchTargetDp);
            var medallionParams = new LinearLayout.LayoutParams(size, size)
            {
                MarginEnd = PaperTheme.Dp(this, DesignTokens.Space.X3),
            };
            headerRow.AddView(medallion, medallionParams);
        }
        var heading = PaperUi.PageHeader(this, "Settings");
        heading.SetPadding(0, 0, 0, 0);
        headerRow.AddView(heading);
        layout.AddView(headerRow, Spaced(0, DesignTokens.Space.X6));

        var address = PaperUi.Field(this, "name@gmail.com");
        address.Text = account.GmailAddress;
        AddFieldGroup(layout, "Gmail address", address, null);

        var password = PaperUi.MonoField(this, "");
        password.Text = account.GmailAppPassword;
        password.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        password.Typeface = PaperTheme.Mono(this); // setting InputType resets the typeface
        AddFieldGroup(layout, "App password", password,
            "Create one at myaccount.google.com/apppasswords.");

        var enableMessages = PaperUi.SecondaryButton(this, "Enable message sync (notification access)", () =>
            StartActivity(new Intent(Android.Provider.Settings.ActionNotificationListenerSettings)));
        layout.AddView(enableMessages, Spaced(DesignTokens.Space.X4, 0));

        var note = PaperUi.Secondary(this,
            "Message sync reads incoming texts from Google Messages notifications and " +
            "voicemails (with transcription) from Google Phone notifications. Call and text " +
            "history require the call-log and SMS permissions, which take effect after the " +
            "device is locked down (or a whitelisted install).");
        layout.AddView(note, Spaced(DesignTokens.Space.X6, 0));

        // On a locked-down phone there is no logcat, so surface captured crashes
        // and background failures here where the owner can review and share them.
        var diagnostics = PaperUi.QuietButton(this, "Diagnostics", () =>
            StartActivity(new Intent(this, typeof(DiagnosticsActivity))));
        layout.AddView(diagnostics, Spaced(DesignTokens.Space.X4, 0));

        scroll.AddView(layout);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // Success is the state change: saving closes the screen (DESIGN.md §7).
        var save = PaperUi.PrimaryButton(this, "Save", () =>
        {
            account.GmailAddress = address.Text;
            account.GmailAppPassword = password.Text;
            if (account.IsConfigured)
            {
                // Sync now so conversations appear promptly instead of waiting
                // for the next periodic run.
                EmailSyncWorker.RunOnce(this);
            }
            Finish();
        });
        root.AddView(PaperUi.BottomBar(this, save));

        SetContentView(root);
    }

    private void AddFieldGroup(LinearLayout layout, string label, EditText field, string? helper)
    {
        var labelView = PaperUi.Secondary(this, label);
        labelView.TextSize = DesignTokens.TypeSp.Label;
        labelView.Typeface = PaperTheme.SansSemibold(this);
        labelView.SetTextColor(PaperTheme.Ink);
        layout.AddView(labelView, Spaced(0, DesignTokens.Space.X1));
        layout.AddView(field, Spaced(0, helper is null ? DesignTokens.Space.X4 : DesignTokens.Space.X1));
        if (helper is not null)
        {
            layout.AddView(PaperUi.Secondary(this, helper), Spaced(0, DesignTokens.Space.X4));
        }
    }

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topDp),
            BottomMargin = PaperTheme.Dp(this, bottomDp),
        };
}
