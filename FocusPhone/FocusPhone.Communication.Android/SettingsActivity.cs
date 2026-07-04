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
        var account = new AccountStore(this);

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetBackgroundColor(PaperTheme.Paper);
        layout.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Space.X6), PaperTheme.Dp(this, DesignTokens.Space.X12),
            PaperTheme.Dp(this, DesignTokens.Space.X6), PaperTheme.Dp(this, DesignTokens.Space.X6));

        var heading = PaperUi.Title(this, "Gmail account");
        layout.AddView(heading, Spaced(0, DesignTokens.Space.X6));

        var address = PaperUi.Field(this, "name@gmail.com");
        address.Text = account.GmailAddress;
        AddFieldGroup(layout, "Gmail address", address, null);

        var password = PaperUi.MonoField(this, "");
        password.Text = account.GmailAppPassword;
        password.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        password.Typeface = PaperTheme.Mono(this); // setting InputType resets the typeface
        AddFieldGroup(layout, "App password", password,
            "Create one at myaccount.google.com/apppasswords.");

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
            Toast.MakeText(this, "Saved.", ToastLength.Short)!.Show();
            Finish();
        });
        layout.AddView(save, Spaced(DesignTokens.Space.X8, 0));

        var enableMessages = PaperUi.SecondaryButton(this, "Enable message sync (notification access)", () =>
            StartActivity(new Intent(Android.Provider.Settings.ActionNotificationListenerSettings)));
        layout.AddView(enableMessages, Spaced(DesignTokens.Space.X3, 0));

        var note = PaperUi.Secondary(this,
            "Message sync reads incoming texts from Google Messages notifications. " +
            "Call history requires the call-log permission, which takes effect after the " +
            "device is locked down (or a whitelisted install).");
        layout.AddView(note, Spaced(DesignTokens.Space.X6, 0));

        SetContentView(layout);
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
