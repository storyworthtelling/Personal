using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// The week plans other phones left in the household file (D126), as this phone's copy holds them: the library's own merge has already decided which record of a week stands (the newer
/// stamp, ties by writer), and only that one is read. <b>Reads only.</b> A file that will not open, or a copy that is damaged, is <see cref="WeekPlansRead.Blocked"/> with the file's own
/// sentence and nothing is read as if it were the household. A record that cannot be read is set aside with a typed reason (<see cref="WeekPlanMaps.Read"/>) and counted in the
/// trail the way an unreadable card is, as a number and nothing more.
/// </summary>
public sealed class HouseholdWeekPlans(HouseholdFileService service) : IWeekPlanSource
{
    public WeekPlansRead Read()
    {
        if (service.BlockedSentence is { } blocked)
        {
            return new WeekPlansRead.Blocked(blocked);
        }

        HouseholdDocument document;
        try
        {
            document = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdFormatException)
        {
            return new WeekPlansRead.Blocked(HouseholdSentences.LocalDamaged);
        }

        var set = WeekPlanMaps.Read(document);
        service.NoteUnreadable(set.Skipped.Count);
        return new WeekPlansRead.Held(set, service.InstallId);
    }
}
