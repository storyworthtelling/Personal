using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Household;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The two adults' meal answers over the household file, read from the phone's own copy of it (the working copy, as for the recipe box; Drive is reached by
/// the service's pass). A file that will not open is <see cref="MealAnswersRead.Unreadable"/> with the file's own sentence, never an empty set of answers: an unread
/// "no" would be approved. An answer record this build cannot read is counted only when its key names this week, and left exactly as it is.
/// </summary>
public sealed class HouseholdMealAnswers(HouseholdFileService service) : IMealAnswerSource
{
    public MealAnswersRead Read(DateOnly week)
    {
        if (service.BlockedSentence is { } blocked)
        {
            return new MealAnswersRead.Unreadable(blocked);
        }

        HouseholdDocument document;
        try
        {
            document = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdFormatException)
        {
            return new MealAnswersRead.Unreadable(HouseholdSentences.LocalDamaged);
        }

        var set = MealAnswerRecords.Read(document, week);
        service.NoteUnreadable(set.Unreadable.Count);
        var named = RecordIds.Separator + RecordDates.Write(week) + RecordIds.Separator;
        var thisWeeks = set.Unreadable.Count(record => record.Key.Id.Contains(named, StringComparison.Ordinal));
        return new MealAnswersRead.Held(set.Answers, thisWeeks);
    }
}

/// <summary>
/// The taste notes the deadline teaches, as <c>taste-note</c> records in the household file (<see cref="TasteMaps.Note"/>: one record per person, kind and
/// subject, so changing your mind edits the one record). The same note saved again changes nothing and keeps its first stamp; a record this build cannot read is
/// left exactly as it is, never written over. A file that will not open refuses the whole write.
/// </summary>
public sealed class HouseholdTasteNotes(HouseholdFileService service) : IMealTasteNotes
{
    public MealNotesSave Save(IReadOnlyList<TasteNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Count == 0)
        {
            return new MealNotesSave.Saved(0);
        }

        if (service.BlockedSentence is { } blocked)
        {
            return new MealNotesSave.Refused(blocked);
        }

        var written = 0;
        try
        {
            service.EditAsync(
                (HouseholdDocument document, HybridLogicalClock clock, string writer) =>
                {
                    written = 0;
                    var next = document;
                    foreach (var note in notes)
                    {
                        if (next.Live(TasteMaps.Note.KeyOf(note)) is { } kept && !Differs(kept, note))
                        {
                            continue;
                        }

                        var stamp = clock.Witness(next.MaxStamp, service.Now.ToUnixTimeMilliseconds());
                        next = HouseholdRecords.Put(next, TasteMaps.Note, note, stamp, writer);
                        written++;
                    }

                    return next;
                },
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdUnreadableException exception)
        {
            return new MealNotesSave.Refused(exception.Message);
        }
        catch (HouseholdFormatException)
        {
            return new MealNotesSave.Refused(HouseholdSentences.LocalDamaged);
        }

        return new MealNotesSave.Saved(written);
    }

    // A record this build cannot read is never "different": it may be a newer writer's, and writing fields over it could clobber what it means.
    private static bool Differs(HouseholdRecord kept, TasteNote note)
    {
        TasteNote current;
        try
        {
            current = TasteMaps.Note.Read(new RecordReader(kept));
        }
        catch (HouseholdRecordException)
        {
            return false;
        }

        return current.Stance != note.Stance || current.Origin != note.Origin;
    }
}
