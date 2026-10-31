using Constitution.Domain.Diagnostics;

namespace Constitution.Domain.Calendar;

/// <summary>
/// The rules the weekly run plans by (<see cref="PlanningRules"/>) made from what the household gave, with every setting it did not give
/// replaced by its stated stand-in (<c>FamilyDinner.Assumed</c>, <c>BedtimeRoutine.Assumed</c>, <c>WorkFromHomeLunch.Assumed</c>, pickup duty for
/// everyone) and named in <see cref="Assumed"/>. An assumption nobody is told about is a setting nobody can correct, so the list goes to
/// the Diagnostics export (<see cref="AssumedSetting"/>) and the run never invents a value silently.
///
/// <para>A setting that was given but cannot be used (a dinner that runs past midnight, a bedtime too early for a routine before it, a zone the
/// phone does not know) is treated as not given. The lunch gap has no setting at all yet, so it is always the stand-in and always named.</para>
/// </summary>
public sealed record SetupRules(PlanningRules Rules, IReadOnlyList<AssumedSetting> Assumed)
{
    private static readonly TimeSpan UsualDinner = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    /// <param name="setup">What the household gave.</param>
    /// <param name="workScheduleJson">Foreword's work schedule as the personal vault holds it, or null when there is none.</param>
    /// <param name="phoneZone">The phone's own zone, used when the household has not given one.</param>
    public static SetupRules From(PlanningSetup setup, string? workScheduleJson, TimeZoneInfo phoneZone)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(phoneZone);

        var assumed = new HashSet<AssumedSetting>();

        var zone = ZoneOf(setup.ZoneId);
        if (zone is null)
        {
            zone = phoneZone;
            assumed.Add(AssumedSetting.TimeZone);
        }

        var adults = setup.Adults
            .Select(adult => new Adult(adult.Name, adult.OnPickup ?? true, adult.Addresses, adult.OtherNames))
            .ToList();
        if (setup.Adults.Any(adult => adult.OnPickup is null))
        {
            assumed.Add(AssumedSetting.PickupDuty);
        }

        var work = WorkSchedule.Read(workScheduleJson);
        if (!work.IsKnown)
        {
            assumed.Add(AssumedSetting.WorkHours);
        }

        var dinner = DinnerOf(setup);
        if (dinner is null)
        {
            dinner = FamilyDinner.Assumed;
            assumed.Add(AssumedSetting.DinnerTime);
        }

        BedtimeRoutine bedtime;
        if (setup.NoChildren)
        {
            bedtime = BedtimeRoutine.None;
        }
        else if (setup.Bedtime is { } given && given.ToTimeSpan() >= BedtimeRoutine.UsualLength)
        {
            bedtime = BedtimeRoutine.At(given);
        }
        else
        {
            bedtime = BedtimeRoutine.Assumed;
            assumed.Add(AssumedSetting.BedtimeRoutine);
        }

        // No setting holds the lunch gap yet, so it is always the stand-in.
        assumed.Add(AssumedSetting.LunchGap);

        var rules = new PlanningRules(zone, adults, work, dinner, bedtime, WorkFromHomeLunch.Assumed);
        return new SetupRules(rules, Enum.GetValues<AssumedSetting>().Where(assumed.Contains).ToList());
    }

    private static FamilyDinner? DinnerOf(PlanningSetup setup)
    {
        if (setup.DinnerStart is not { } start)
        {
            return null;
        }

        var length = setup.DinnerLength ?? UsualDinner;
        return length > TimeSpan.Zero && start.ToTimeSpan() + length <= Day ? new FamilyDinner(start, length) : null;
    }

    private static TimeZoneInfo? ZoneOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            // The id names no zone this phone has: the setting is as good as not given, and Diagnostics says the phone's zone was used.
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
