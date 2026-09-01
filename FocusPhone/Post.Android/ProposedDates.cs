using Android.Content;
using Codex.Domain.Post;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// Where a detected date waits between the sync that found it and the screen that offers
/// it. Detection runs in the background worker, which has nothing to offer anything on, so
/// the correspondence folds its corner the next time the owner opens it.
///
/// That is the design's answer twice over: a found date raises a <b>dog-ear on the
/// thread</b> — never a buzz, since only a person earns one, and never a chip on the row —
/// and it is <b>proposed rather than written</b>, so nothing reaches the calendar until the
/// owner says so.
///
/// Keyed by contact, so one waiting date per correspondence: a fold means something waits,
/// not how much.
/// </summary>
internal static class ProposedDates
{
    private const string PreferencesName = "proposedDates";

    public static void Offer(Context context, TimelineItem item, ProposedEvent proposal, bool? clashed) =>
        Prefs(context).Edit()!
            .PutString(item.ContactKey, ProposedDateCodec.Encode(new ProposedDate(proposal, clashed)))!
            .Apply();

    /// <summary>What is waiting for one correspondence, or null. A day that has passed is gone.</summary>
    public static ProposedDate? For(Context context, string contactKey) =>
        ProposedDateCodec.Decode(
            Prefs(context).GetString(contactKey, null), DateOnly.FromDateTime(DateTime.Now));

    public static void Clear(Context context, string contactKey) =>
        Prefs(context).Edit()!.Remove(contactKey)!.Apply();

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
