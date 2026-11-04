using Android.App;
using Android.App.Admin;
using Android.App.Roles;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Telephony;
using Android.Views;
using Android.Widget;

namespace WatchSpike;

/// <summary>
/// One scrolling screen of plain readouts and four buttons. Deliberately undesigned: this is an
/// instrument for two questions, not a watch app, and it is deleted once it has answered them.
/// </summary>
[Activity(Name = "com.storyworthtelling.watchspike.MainActivity", Label = "Watch spike", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private const string PlayStore = "com.android.vending";
    private const int RoleRequest = 41;
    private TextView? readout;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.SetPadding(36, 48, 36, 96);
        readout = new TextView(this) { TextSize = 13 };
        column.AddView(readout);
        column.AddView(Button("Check again", Refresh));
        column.AddView(Button("Ask for the SMS role", AskForSmsRole));
        column.AddView(Button("Reply to the last sender", ReplyToLastSender));
        column.AddView(Button("Hide Play Store", () => HidePlayStore(true)));
        column.AddView(Button("Show Play Store", () => HidePlayStore(false)));
        column.AddView(Button("Reinstall this app silently", ReinstallSilently));
        var scroll = new ScrollView(this);
        scroll.AddView(column);
        SetContentView(scroll);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Refresh();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == RoleRequest)
        {
            SpikeLog.Note(this, resultCode == Result.Ok ? "The SMS role was granted." : "The SMS role was refused or not offered.");
            Refresh();
        }
    }

    private Button Button(string text, Action onTap)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => onTap();
        return button;
    }

    private void Refresh()
    {
        var lines = new List<string> { "P14 watch spike", string.Empty, "TEXTS (A60)" };
        var pm = PackageManager!;
        lines.Add($"Telephony hardware: {Yes(pm.HasSystemFeature("android.hardware.telephony"))}");
        lines.Add($"Telephony messaging: {Yes(pm.HasSystemFeature("android.hardware.telephony.messaging"))}");
        var telephony = GetSystemService(TelephonyService) as TelephonyManager;
        lines.Add($"SMS capable: {Yes(telephony?.IsSmsCapable ?? false)}");
        lines.Add($"Voice capable: {Yes(telephony?.IsVoiceCapable ?? false)}");
        lines.Add($"Default SMS app: {Telephony.Sms.GetDefaultSmsPackage(this) ?? "none"}");
        var roles = GetSystemService(Java.Lang.Class.FromType(typeof(RoleManager))) as RoleManager;
        lines.Add($"SMS role exists here: {Yes(roles?.IsRoleAvailable(RoleManager.RoleSms) ?? false)}");
        lines.Add($"This app holds it: {Yes(roles?.IsRoleHeld(RoleManager.RoleSms) ?? false)}");
        lines.Add($"Texts received by this app: {SpikeLog.Texts(this)}");
        lines.Add(SpikeLog.LastText(this) ?? "No text received yet.");
        lines.Add(string.Empty);
        lines.Add("DEVICE OWNER (A63)");
        var dpm = GetSystemService(DevicePolicyService) as DevicePolicyManager;
        var admin = new ComponentName(this, Java.Lang.Class.FromType(typeof(SpikeAdmin)));
        lines.Add($"Admin active: {Yes(dpm?.IsAdminActive(admin) ?? false)}");
        lines.Add($"Device owner: {Yes(dpm?.IsDeviceOwnerApp(PackageName!) ?? false)}");
        lines.Add($"Play Store hidden: {Yes(IsHidden(dpm, admin))}");
        lines.Add(string.Empty);
        lines.Add($"Last event: {SpikeLog.LastNote(this) ?? "none"}");
        lines.Add($"Watch: {Build.Model}, Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt})");
        readout!.Text = string.Join("\n", lines);
    }

    private static string Yes(bool value) => value ? "yes" : "no";

    private bool IsHidden(DevicePolicyManager? dpm, ComponentName admin)
    {
        if (dpm is null || !dpm.IsDeviceOwnerApp(PackageName!))
        {
            return false;
        }

        try
        {
            return dpm.IsApplicationHidden(admin, PlayStore);
        }
        catch (Java.Lang.SecurityException e)
        {
            SpikeLog.Note(this, $"Could not read Play Store's hidden state: {e.GetType().Name}.");
            return false;
        }
    }

    private void AskForSmsRole()
    {
        var roles = GetSystemService(Java.Lang.Class.FromType(typeof(RoleManager))) as RoleManager;
        if (roles is null || !roles.IsRoleAvailable(RoleManager.RoleSms))
        {
            SpikeLog.Note(this, "This watch has no SMS role to offer.");
            Refresh();
            return;
        }

        StartActivityForResult(roles.CreateRequestRoleIntent(RoleManager.RoleSms), RoleRequest);
    }

    private void ReplyToLastSender()
    {
        var to = SpikeLog.LastSender(this);
        if (string.IsNullOrEmpty(to))
        {
            SpikeLog.Note(this, "No text has arrived yet, so there is nobody to reply to.");
            Refresh();
            return;
        }

        try
        {
            var sms = GetSystemService(Java.Lang.Class.FromType(typeof(SmsManager))) as SmsManager;
            sms!.SendTextMessage(to, null, "Test reply from the watch spike.", null, null);
            SpikeLog.Note(this, "A test reply was handed to the watch's SMS service.");
        }
        catch (Exception e)
        {
            SpikeLog.Note(this, $"The test reply was refused: {e.GetType().Name}.");
        }
        Refresh();
    }

    private void HidePlayStore(bool hide)
    {
        var dpm = GetSystemService(DevicePolicyService) as DevicePolicyManager;
        var admin = new ComponentName(this, Java.Lang.Class.FromType(typeof(SpikeAdmin)));
        if (dpm is null || !dpm.IsDeviceOwnerApp(PackageName!))
        {
            SpikeLog.Note(this, "Not device owner, so Play Store cannot be hidden.");
            Refresh();
            return;
        }

        try
        {
            var changed = dpm.SetApplicationHidden(admin, PlayStore, hide);
            SpikeLog.Note(this, $"Play Store {(hide ? "hide" : "show")}: {(changed ? "done" : "no change")}.");
        }
        catch (Java.Lang.SecurityException e)
        {
            SpikeLog.Note(this, $"Play Store {(hide ? "hide" : "show")} was refused: {e.GetType().Name}.");
        }
        Refresh();
    }

    private void ReinstallSilently()
    {
        try
        {
            var installer = PackageManager!.PackageInstaller;
            var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            var sessionId = installer.CreateSession(parameters);
            using var session = installer.OpenSession(sessionId);
            using (var input = File.OpenRead(ApplicationInfo!.SourceDir!))
            using (var output = session.OpenWrite("spike.apk", 0, input.Length))
            {
                input.CopyTo(output);
                session.Fsync(output);
            }
            var result = new Intent(this, typeof(InstallResultReceiver));
            var pending = PendingIntent.GetBroadcast(this, sessionId, result, PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent);
            session.Commit(pending!.IntentSender);
            SpikeLog.Note(this, "Reinstall started; the result appears here shortly.");
        }
        catch (Exception e)
        {
            SpikeLog.Note(this, $"Reinstall could not start: {e.GetType().Name}.");
        }
        Refresh();
    }
}
