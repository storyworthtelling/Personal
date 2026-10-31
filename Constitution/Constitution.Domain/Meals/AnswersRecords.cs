using Constitution.Domain.Documents;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Storyworthtelling.Household;

namespace Constitution.Domain.Meals;

/// <summary>What became of a write.</summary>
public enum MealAnswerOutcome
{
    /// <summary>The answer is in the file now.</summary>
    Recorded,

    /// <summary>The file already held this answer; nothing was written, and the first reading's time stands.</summary>
    AlreadyRecorded,

    /// <summary>The reading came after the deadline and the file already held this adult's on-time answer; it was set aside, for a notice.</summary>
    TooLate,

    /// <summary>The file's record for this answer cannot be read by this build, so it was left exactly as it is.</summary>
    Unreadable,
}

/// <summary>The document after a write, and what the write came to.</summary>
public sealed record MealAnswerWrite(HouseholdDocument Document, MealAnswerOutcome Outcome);

/// <summary>The meal answers a household document holds, and the answer records it holds that could not be read.</summary>
public sealed record MealAnswerSet(IReadOnlyList<MealAnswer> Answers, IReadOnlyList<UnreadableRecord> Unreadable);

/// <summary>
/// Writes and reads the meal answers in the household file (C-2.4.5). Each install writes only the answers of the adult whose
/// tablet it reads, whether or not it holds the planning lease: the partner's install does not plan, and it still reads its own
/// tablet's ink and contributes its answers, so the planner's install can read both adults'. Pure — time and the clock come
/// from the caller.
///
/// <para><b>Idempotent.</b> The same answer written again changes nothing and keeps the first reading's time, so a tablet's
/// ink read again on the next morning is not a later answer. <b>An on-time answer is never replaced by a late one</b>: a
/// reading after the deadline does not rewrite what this adult had already said before it.</para>
/// </summary>
public static class MealAnswerRecords
{
    public static MealAnswerWrite Write(HouseholdDocument document, HybridLogicalClock clock, string install, MealAnswer answer, DateTimeOffset deadline, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(install);
        ArgumentNullException.ThrowIfNull(answer);

        if (document.Live(MealAnswerMaps.Answer.KeyOf(answer)) is { } existing)
        {
            MealAnswer current;
            try
            {
                current = MealAnswerMaps.Answer.Read(new RecordReader(existing));
            }
            catch (HouseholdRecordException)
            {
                // A record this build cannot read may be a newer writer's; writing fields over it could clobber what it means.
                return new MealAnswerWrite(document, MealAnswerOutcome.Unreadable);
            }

            if (current.Verdict == answer.Verdict && string.Equals(current.Reason, answer.Reason, StringComparison.Ordinal))
            {
                return new MealAnswerWrite(document, MealAnswerOutcome.AlreadyRecorded);
            }

            if (current.ReadAt < deadline && answer.ReadAt >= deadline)
            {
                return new MealAnswerWrite(document, MealAnswerOutcome.TooLate);
            }
        }

        var stamp = clock.Witness(document.MaxStamp, now.ToUnixTimeMilliseconds());
        return new MealAnswerWrite(HouseholdRecords.Put(document, MealAnswerMaps.Answer, answer, stamp, install), MealAnswerOutcome.Recorded);
    }

    /// <summary>
    /// Every live answer of <paramref name="week"/> (every week when null), in week, line, person order. An answer that cannot be
    /// read is listed, never dropped and never fatal to the rest; other records of the <c>week-plan</c> kind are left alone.
    /// </summary>
    public static MealAnswerSet Read(HouseholdDocument document, DateOnly? week = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var answers = new List<MealAnswer>();
        var unreadable = new List<UnreadableRecord>();
        foreach (var record in document.LiveRecords.Where(record => MealAnswerKeys.IsAnswer(record.Key)))
        {
            try
            {
                var answer = MealAnswerMaps.Answer.Read(new RecordReader(record));
                if (week is null || answer.Week == week)
                {
                    answers.Add(answer);
                }
            }
            catch (HouseholdRecordException exception)
            {
                unreadable.Add(new UnreadableRecord(record.Key, exception.Message));
            }
        }

        var ordered = answers
            .OrderBy(answer => answer.Week)
            .ThenBy(answer => answer.ItemId, StringComparer.Ordinal)
            .ThenBy(answer => answer.Person.Value, StringComparer.Ordinal)
            .ToList();
        return new MealAnswerSet(ordered, unreadable);
    }

    /// <summary>The lines the sheet asks about, in page order: the YES box of every meal rule the book wrote for it.</summary>
    public static IReadOnlyList<string> ExpectedItems(MealSheetModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ExpectedDocument.ForMealSheet(model, "household").Pages
            .SelectMany(page => page.Rules)
            .OfType<MealItemRule>()
            .Select(rule => rule.YesBoxId)
            .ToList();
    }
}
