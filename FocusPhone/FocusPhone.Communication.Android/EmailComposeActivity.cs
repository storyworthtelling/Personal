using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Ui;

namespace FocusPhone.Communication;

/// <summary>
/// In-app email composer: subject + Cc + body sent directly via Gmail, with file
/// attachments (a real Activity so the file picker's result can be routed back).
/// Reply threading rides in via the In-Reply-To extra. Launched by
/// EmailComposeDialog.Show.
/// </summary>
[Activity(
    Label = "Email",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class EmailComposeActivity : Activity
{
    public const string ExtraTo = "to";
    public const string ExtraName = "name";
    public const string ExtraSubject = "subject";
    public const string ExtraInReplyTo = "inReplyTo";

    private const int PickAttachment = 1;

    private readonly List<string> attachmentPaths = [];
    private string to = "";
    private string? inReplyTo;
    private EditText? subject;
    private EditText? cc;
    private EditText? body;
    private LinearLayout? attachmentList;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        to = Intent?.GetStringExtra(ExtraTo) ?? "";
        inReplyTo = Intent?.GetStringExtra(ExtraInReplyTo);
        var name = Intent?.GetStringExtra(ExtraName) ?? to;

        if (!new AccountStore(this).IsConfigured)
        {
            Toast.MakeText(this, "Set up Gmail in Settings first.", ToastLength.Long)!.Show();
            Finish();
            return;
        }

        SetContentView(BuildLayout(name));
        subject!.Text = Intent?.GetStringExtra(ExtraSubject) ?? "";
    }

    private View BuildLayout(string name)
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        root.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(PaperUi.PageHeader(this, $"Email {name}"));

        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        subject = PaperUi.Field(this, "Subject");
        cc = PaperUi.Field(this, "Cc (optional)");
        body = PaperUi.Field(this, "Message");
        body.SetSingleLine(false);
        body.SetMinLines(4);
        body.Gravity = GravityFlags.Top | GravityFlags.Start;
        content.AddView(subject, Row());
        content.AddView(cc, Row());
        content.AddView(body, Row());
        content.AddView(PaperUi.SecondaryButton(this, "Attach file", PickFile), Row());

        attachmentList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(attachmentList, Row());

        root.AddView(content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(PaperUi.BottomBar(this, PaperUi.PrimaryButton(this, "Send", Send)));
        return root;
    }

    private LinearLayout.LayoutParams Row()
    {
        var row = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        row.TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X3);
        return row;
    }

    private void PickFile()
    {
        var intent = new Intent(Intent.ActionGetContent);
        intent.SetType("*/*");
        intent.AddCategory(Intent.CategoryOpenable);
        StartActivityForResult(Intent.CreateChooser(intent, "Attach"), PickAttachment);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickAttachment || resultCode != Result.Ok || data?.Data is null)
        {
            return;
        }
        var path = CopyToCache(data.Data);
        if (path is null)
        {
            Toast.MakeText(this, "Couldn't attach that file.", ToastLength.Short)!.Show();
            return;
        }
        attachmentPaths.Add(path);
        var label = PaperUi.Secondary(this, $"📎 {System.IO.Path.GetFileName(path)}");
        attachmentList!.AddView(label);
    }

    // SendAsync attaches by file path, so copy the picked content into app cache.
    private string? CopyToCache(Android.Net.Uri uri)
    {
        try
        {
            var name = QueryDisplayName(uri) ?? $"attachment-{Guid.NewGuid():N}";
            var destination = System.IO.Path.Combine(CacheDir!.AbsolutePath, name);
            using var input = ContentResolver!.OpenInputStream(uri);
            if (input is null)
            {
                return null;
            }
            using var output = System.IO.File.Create(destination);
            input.CopyTo(output);
            return destination;
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Attachment copy failed: {e}");
            CommunicationDiagnostics.LogWarning("Email", "Attachment copy failed", e);
            return null;
        }
    }

    private string? QueryDisplayName(Android.Net.Uri uri)
    {
        // "_display_name" is the stable OpenableColumns name for a content Uri.
        using var cursor = ContentResolver!.Query(uri, ["_display_name"], null, null, null);
        return cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0) ? cursor.GetString(0) : null;
    }

    private void Send()
    {
        var account = new AccountStore(this);
        var subjectText = subject!.Text ?? "";
        var bodyText = body!.Text ?? "";
        var ccList = ParseAddresses(cc!.Text);
        var paths = attachmentPaths.ToList();

        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await new Infrastructure.Email.GmailClient(account.GmailAddress!, account.GmailAppPassword!)
                    .SendAsync(to, subjectText, bodyText, cc: ccList, attachmentPaths: paths, inReplyTo: inReplyTo);
                RunOnUiThread(() =>
                {
                    Toast.MakeText(this, "Sent.", ToastLength.Short)!.Show();
                    Finish();
                });
            }
            catch (Exception e)
            {
                RunOnUiThread(() => Toast.MakeText(this, $"Send failed: {e.Message}", ToastLength.Long)!.Show());
            }
        });
    }

    private static IReadOnlyList<string> ParseAddresses(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
