using Android.Content;
using Android.Provider;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Ui;

namespace Post;

/// <summary>
/// What happens once <see cref="CallStateReceiver"/> has seen a call go OFFHOOK and then
/// Idle: find the call that just ended, decide whether it looks like one worth a note,
/// resolve who it was with, and — if so — leave it in <see cref="PendingCallNoteStore"/>
/// for the shell to offer next time Post is opened.
/// </summary>
internal static class CallEndedNotePrompt
{
    private const string Tag = "CallNote"; // not copy: a log tag

    // The call-log provider's write for the call that just ended can lag the
    // OFFHOOK-then-IDLE broadcast by anywhere from nothing to a couple of seconds
    // depending on the device, so this waits and retries rather than reading once.
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.Zero, TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(2)];

    public static void Handle(Context context)
    {
        // Whether this is the call that just ended, and one someone was on, is JustEndedCall's (P14 B7): the row is judged
        // by when the call ended, not by when it began, which is what the call log's date is.
        var (verdict, number) = FindJustEndedCall(context);

        // --- The "non-spam" heuristic, and its honest limit ---
        // Android has no reliable, always-available "is this call spam" signal short of
        // building a Call Screening Service — a much larger integration than one prompt
        // warrants. What IS checked: a call nobody was on (JustEndedCall.Answered: missed,
        // declined, voicemail, blocked by the system, an outgoing call nobody picked up), and a
        // number already on the phone's own blocked-numbers list (the same list Post's own
        // "Block this number" writes to). An answered nuisance call that was never blocked
        // still prompts for a note; there is no honest way to tell that call apart from a
        // wanted one here.
        if (verdict == CallNoteVerdict.Offered && IsNumberBlocked(context, number!))
        {
            verdict = CallNoteVerdict.Blocked;
        }

        // Each call's end says what became of it in the weekly log, so an offer that never comes can be read there.
        if (verdict == CallNoteVerdict.Unreadable)
        {
            HeadLog.Warn(Tag, PostLogLines.CallNote(verdict));
        }
        else
        {
            HeadLog.Info(Tag, PostLogLines.CallNote(verdict));
        }
        if (verdict != CallNoteVerdict.Offered)
        {
            return;
        }

        var contactKey = ResolveContactKey(context, number!);
        var displayName = ContactNameResolver.DisplayName(context, contactKey);
        PendingCallNoteStore.Set(context, contactKey, number!, displayName);
    }

    // The newest row, read again while the log has not caught up (JustEndedCall.ReadAgain), with its verdict and number.
    private static (CallNoteVerdict Verdict, string? Number) FindJustEndedCall(Context context)
    {
        var verdict = CallNoteVerdict.NoCall;
        string? number = null;
        foreach (var delay in RetryDelays)
        {
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }

            try
            {
                (verdict, number) = ReadNewest(context);
            }
            catch (Java.Lang.SecurityException)
            {
                // READ_CALL_LOG refused: the note cannot know whose call it was. Said once per process, by name.
                HeadLog.Denied("READ_CALL_LOG");
                return (CallNoteVerdict.Unreadable, null);
            }
            if (!JustEndedCall.ReadAgain(verdict))
            {
                break;
            }
        }
        return (verdict, number);
    }

    private static (CallNoteVerdict Verdict, string? Number) ReadNewest(Context context)
    {
        using var cursor = context.ContentResolver!.Query(
            CallLog.Calls.ContentUri!,
            [CallLog.Calls.Number, CallLog.Calls.Date, CallLog.Calls.Duration, CallLog.Calls.Type],
            null, null, $"{CallLog.Calls.Date} DESC");
        if (cursor is null || !cursor.MoveToFirst())
        {
            return (CallNoteVerdict.NoCall, null);
        }

        var row = new CallLogRow(
            DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(1)),
            TimeSpan.FromSeconds(cursor.GetLong(2)),
            cursor.GetInt(3));
        var verdict = JustEndedCall.Judge(row, DateTimeOffset.UtcNow);
        var number = cursor.GetString(0) ?? string.Empty;
        if (verdict == CallNoteVerdict.Offered && number.Length == 0)
        {
            // A withheld number: the note would have no thread to join.
            return (CallNoteVerdict.Withheld, null);
        }
        return (verdict, number);
    }

    private static bool IsNumberBlocked(Context context, string number)
    {
        try
        {
            return BlockedNumberContract.IsBlocked(context, number);
        }
        catch (Exception e)
        {
            // Only the default SMS or dialer app may query this, and Google Messages holds the
            // texting role again, so the platform may refuse it — but a failed check should never
            // block the note prompt outright. Worst case here is offering it for a number that was
            // blocked. Logged by its shape: the platform's words can name the number.
            HeadLog.Quiet(Tag, "Could not check the blocked-numbers list", e);
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
            PostDiagnostics.LogWarning("CallNote", "Could not resolve the caller’s identity", e);
            return rawKey;
        }
    }
}
