using Android.Content;
using Android.Provider;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// What happens once <see cref="CallStateReceiver"/> has seen a call go OFFHOOK and then
/// Idle: find the call that just ended, decide whether it looks like one worth a note,
/// resolve who it was with, and — if so — leave it in <see cref="PendingCallNoteStore"/>
/// for the shell to offer next time Post is opened.
/// </summary>
internal static class CallEndedNotePrompt
{
    // The call-log provider's write for the call that just ended can lag the
    // OFFHOOK-then-IDLE broadcast by anywhere from nothing to a couple of seconds
    // depending on the device, so this waits and retries rather than reading once.
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.Zero, TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(2)];

    // A call-log row only counts as "the call that just ended" if it is this fresh —
    // otherwise a slow write could hand back an earlier, unrelated call instead.
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromSeconds(15);

    // CallLog.Calls.TYPE constants (mirrors Codex.Domain.Timeline.CallRecordMapper, which
    // only names the ones it maps). BlockedType is a documented, stable platform constant
    // (added API 24) not bound as a named field on this Android binding's Calls class.
    private const int TypeMissed = 3;
    private const int TypeVoicemail = 4;
    private const int TypeRejected = 5;
    private const int TypeBlocked = 6;

    public static void Handle(Context context)
    {
        var call = FindJustEndedCall(context);
        if (call is null)
        {
            return; // Nothing fresh enough to be confident this is the call that just ended.
        }

        // --- The "non-spam" heuristic, and its honest limit ---
        // Android has no reliable, always-available "is this call spam" signal short of
        // building a Call Screening Service — a much larger integration than one prompt
        // warrants. What IS checked here: a call the OWNER explicitly declined or the
        // SYSTEM itself blocked (CallLog.Calls.TYPE), and a number already on the phone's
        // own blocked-numbers list (the same list Post's own "Block this number" writes
        // to). Neither of those can actually fire for a call that reached OFFHOOK in the
        // first place — a rejected or blocked call never connects, so it never reaches
        // this method at all — so in practice this guards edge cases (two calls arriving
        // close together, a number blocked mid-call) rather than doing real spam
        // detection. An answered nuisance call that was never blocked still prompts for a
        // note; there is no honest way to tell that call apart from a wanted one here.
        if (call.Value.Type is TypeRejected or TypeBlocked or TypeMissed or TypeVoicemail)
        {
            return;
        }
        if (IsNumberBlocked(context, call.Value.Number))
        {
            return;
        }

        var contactKey = ResolveContactKey(context, call.Value.Number);
        var displayName = ContactNameResolver.DisplayName(context, contactKey);
        PendingCallNoteStore.Set(context, contactKey, call.Value.Number, displayName);
    }

    private static (string Number, int Type)? FindJustEndedCall(Context context)
    {
        foreach (var delay in RetryDelays)
        {
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }

            using var cursor = context.ContentResolver!.Query(
                CallLog.Calls.ContentUri!,
                [CallLog.Calls.Number, CallLog.Calls.Date, CallLog.Calls.Type],
                null, null, $"{CallLog.Calls.Date} DESC");
            if (cursor is null || !cursor.MoveToFirst())
            {
                continue;
            }

            var number = cursor.GetString(0) ?? "";
            var whenUtc = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(1));
            var type = cursor.GetInt(2);
            if (number.Length > 0 && DateTimeOffset.UtcNow - whenUtc <= FreshnessWindow)
            {
                return (number, type);
            }
        }
        return null;
    }

    private static bool IsNumberBlocked(Context context, string number)
    {
        try
        {
            return BlockedNumberContract.IsBlocked(context, number);
        }
        catch (Exception e)
        {
            // Only the default SMS/dialer app may query this; Post holds the SMS role,
            // so it should work — but a failed check should never block the note prompt
            // outright. Worst case here is offering it for a number that was blocked.
            PostDiagnostics.LogWarning("CallNote", "Could not check the blocked-numbers list", e);
            return false;
        }
    }

    // Matches the same identity resolution the Mailbox merge uses (ContactAssociations +
    // the owner's manual links), so the note lands on the correspondence already showing
    // for this person rather than a second, unmerged one keyed by the bare number.
    private static string ResolveContactKey(Context context, string number)
    {
        var rawKey = ContactKeyNormalizer.NormalizePhone(number);
        try
        {
            using var database = TimelineDatabase.Open(context);
            var resolver = ContactAssociations.BuildResolver(context, database.IdentityLinks);
            return resolver.ResolveThreadKey(rawKey);
        }
        catch (Exception e)
        {
            // The identity resolver is a convenience over the raw key; never let it stop
            // the note from landing on at least the number's own (unmerged) thread.
            PostDiagnostics.LogWarning("CallNote", "Could not resolve the caller's identity", e);
            return rawKey;
        }
    }
}
