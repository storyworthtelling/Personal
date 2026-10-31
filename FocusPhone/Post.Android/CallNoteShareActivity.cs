using Android.App;
using Android.Content;
using Android.OS;
using Codex.Domain.Post;
using Codex.Ui;

namespace Post;

/// <summary>
/// How a note reaches Post from elsewhere. Google's Call Notes exports only through the
/// share sheet, so Post is a share target for plain text — a filter that is invisible when
/// missing and silent when it goes, which is why it is guarded.
///
/// A router rather than a screen: it reads the share, files the note against that person,
/// and finishes, so Back never returns to an empty shim. A share with no number in it
/// cannot be threaded, and guessing whose call it was would be worse than dropping it —
/// the correspondence screen always offers manual entry.
/// </summary>
[Activity(
    Name = "com.storyworthtelling.focusphone.communication.CallNoteShareActivity",
    Label = "Post",
    Exported = true,
    NoHistory = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter(
    [Intent.ActionSend],
    Categories = [Intent.CategoryDefault],
    DataMimeType = "text/plain")]
public class CallNoteShareActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);

        var shared = Intent?.GetStringExtra(Intent.ExtraText);
        var note = CallNote.FromShare(shared, DateTimeOffset.UtcNow);
        if (note is not null)
        {
            try
            {
                using var database = TimelineDatabase.Open(this);
                database.Cache.Upsert([note]);
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("Post", $"Could not keep a shared note: {e}");
                PostDiagnostics.LogWarning("CallNote", "Could not keep a shared note", e);
            }
        }

        Finish();
    }
}
