using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// Puts the kept week in the household file for the other phone (D126): one <c>week-plan</c> record, written into this phone's copy through the service's own edit, which stamps it after
/// everything the copy holds and lets the next sync carry it to Drive. The only code that writes the kind, and called only by the weekly run after the lease said this phone plans.
///
/// <para><b>Never over a file that cannot be opened (G14).</b> The service refuses an edit while the file will not open or the phone's copy is damaged, and this asks first, so a refusal is a
/// typed <see cref="WeekPlanPublication.NotPublished"/> with nothing written. This class has no Drive store and never creates the file; a phone without one yet keeps the record in its
/// own copy, which the owner's first sync then carries. A week the file already holds exactly is <see cref="WeekPlanPublication.Unchanged"/> and costs no write and no stamp.</para>
///
/// <para>The answer is a value, never a throw for the file's sake. A copy that cannot be written keeps the exception whole on the answer for a log.</para>
/// </summary>
public sealed class HouseholdWeekPlanPublisher(HouseholdFileService service) : IWeekPlanPublisher
{
    public const string FileSentence = "The household file cannot be opened, so the week was not shared with the other phone.";
    public const string CopySentence = "This phone's copy of the household is damaged, so the week was not shared with the other phone.";
    public const string StoreSentence = "This phone's copy of the household could not be written, so the week was not shared with the other phone.";

    public async Task<WeekPlanPublication> PublishAsync(SharedWeekPlan plan, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (service.BlockedSentence is not null)
        {
            return new WeekPlanPublication.NotPublished(WeekPlanNotPublishedBecause.FileUnreadable, FileSentence);
        }

        try
        {
            var document = await service.ReadAsync(cancellation);
            if (document.Live(WeekPlanMaps.WeekPlan.KeyOf(plan)) is { } held
                && WeekPlanMaps.WeekPlan.FieldsOf(plan).All(field => held.Fields.TryGetValue(field.Key, out var value) && value == field.Value))
            {
                return new WeekPlanPublication.Unchanged();
            }

            await service.EditAsync(
                (current, stamp, writer) => HouseholdRecords.Put(current, WeekPlanMaps.WeekPlan, plan, stamp, writer),
                cancellation);
            return new WeekPlanPublication.Published();
        }
        catch (HouseholdUnreadableException exception)
        {
            return new WeekPlanPublication.NotPublished(WeekPlanNotPublishedBecause.FileUnreadable, FileSentence, exception);
        }
        catch (HouseholdFormatException exception)
        {
            return new WeekPlanPublication.NotPublished(WeekPlanNotPublishedBecause.CopyDamaged, CopySentence, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new WeekPlanPublication.NotPublished(WeekPlanNotPublishedBecause.NotStored, StoreSentence, exception);
        }
    }
}
