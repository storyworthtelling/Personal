using Android.App;
using Android.Content;
using Android.Widget;
using Codex.Domain.Policy;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Ui;

namespace Post;

/// <summary>
/// Attachments, carried out. What to do with one is decided by the pure, tested
/// <see cref="AttachmentPlan"/>; this fetches the bytes and hands them somewhere.
///
/// Bytes are fetched <b>on demand</b> and written to the app's cache, never to the
/// timeline: Post is not a file manager, and an inbox that quietly accumulates every
/// picture anyone ever sent is the opposite of a to-do list. The cache is the system's to
/// reclaim.
/// </summary>
internal static class Attachments
{
    public static string Label(IReadOnlyList<AttachmentRef> attachments) => AttachmentPlan.Label(attachments);

    /// <summary>Offers what is attached, each row saying what opening it will do.</summary>
    public static void Offer(Activity host, FrameLayout overlay, TimelineItem item)
    {
        var actions = item.Attachments
            .Select(attachment => new CardAction(
                attachment.FileName ?? attachment.MimeType,
                () => Open(host, item, attachment),
                Detail: Describe(attachment),
                Destructive: AttachmentPlan.For(attachment) == AttachmentAction.Refuse))
            .ToArray();

        ActionCard.Show(host, overlay, "Attached", subtitle: null, actions);
    }

    private static string Describe(AttachmentRef attachment)
    {
        var size = attachment.SizeBytes > 0 ? $"{attachment.SizeBytes / 1024} KB · " : string.Empty;
        return AttachmentPlan.For(attachment) switch
        {
            AttachmentAction.OpenInPlate => $"{size}opens in Plate",
            AttachmentAction.ShowInline => $"{size}opens here",
            AttachmentAction.Refuse => $"{size}Post will not open this kind of file",
            _ => $"{size}opens in whatever can read it",
        };
    }

    private static void Open(Activity host, TimelineItem item, AttachmentRef attachment)
    {
        var opening = TapWatch.Action(PostKeyActions.OpenAttachment, TimeSpan.FromSeconds(60));
        var plan = AttachmentPlan.For(attachment);
        if (plan == AttachmentAction.Refuse)
        {
            // A refusal is the attachment working as designed, not a failure of the tap.
            opening.Succeeded();
            // Said plainly rather than silently doing nothing: a tap that appears to do
            // nothing reads as a broken app, not as a refusal.
            DogEar.SetNotice(host, Overlay(host), NoticeKeys.Attachment, new DogEar.Notice(
                "Post will not open that file.",
                $"{attachment.FileName} is the kind of file that installs or runs something. "
                + "Whoever sent it chose its name and its type, so Post does not open it at all.",
                PaperTheme.ProblemMark));
            FieldNotesStore.Record(host, notes => notes.WithAttachmentOpened(opened: false));
            return;
        }

        try
        {
            var uri = AttachmentCache.Fetch(host, item, attachment)
                ?? throw new InvalidOperationException("The attachment could not be fetched.");

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, attachment.MimeType);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            // A picture goes to Plate by name where Plate is installed; the suite has its
            // own photo app and routing a picture past it is the seam this phone removes.
            if (plan == AttachmentAction.OpenInPlate
                && host.PackageManager?.GetLaunchIntentForPackage(PlatePackage) is not null)
            {
                intent.SetPackage(PlatePackage);
            }

            host.StartActivity(intent);
            opening.Succeeded();
            FieldNotesStore.Record(host, notes => notes.WithAttachmentOpened(opened: true));
        }
        catch (Exception e)
        {
            opening.Failed($"{e.GetType().Name}: {e.Message}");
            Android.Util.Log.Warn("Post", $"Could not open an attachment: {e}");
            PostDiagnostics.LogWarning("Attachments", $"Could not open {attachment.FileName}", e);
            DogEar.SetNotice(host, Overlay(host), NoticeKeys.Attachment, new DogEar.Notice(
                "That attachment would not open.",
                "Nothing on the phone could read it, or it could not be fetched. Diagnostics has "
                + "the details.",
                PaperTheme.ProblemMark));
            FieldNotesStore.Record(host, notes => notes.WithAttachmentOpened(opened: false));
        }
    }

    private const string PlatePackage = PolicyContext.PlatePackage;

    // The activity's own content frame, so a notice folds the corner of the page the owner
    // is looking at rather than of some other one.
    private static FrameLayout Overlay(Activity host) =>
        host.FindViewById<FrameLayout>(Android.Resource.Id.Content)!;
}
