namespace Constitution.Domain.Calendar;

/// <summary>
/// Everything the busy-time model needs to know that is not on a calendar: the zone the household
/// lives in, who the adults are, the work schedule Foreword keeps, and the three rules that make
/// time never-placed. All of it is passed in. No rule has a hidden default, so a household that has
/// not said when dinner is must say <see cref="FamilyDinner.Assumed"/> out loud.
/// </summary>
/// <param name="Zone">The household's time zone. Times of day are resolved in it, and days are its days.</param>
/// <param name="Adults">The adults whose time is planned. At least one; no two with the same name.</param>
/// <param name="Work">The work schedule read from the personal vault (see <see cref="WorkSchedule.Read"/>).</param>
/// <param name="Dinner">The family dinner window.</param>
/// <param name="Bedtime">The children's bedtime routine, or <see cref="BedtimeRoutine.None"/>.</param>
/// <param name="Lunch">The lunch gap freed on a day worked from home.</param>
public sealed record PlanningRules(
    TimeZoneInfo Zone,
    IReadOnlyList<Adult> Adults,
    WorkSchedule Work,
    FamilyDinner Dinner,
    BedtimeRoutine Bedtime,
    WorkFromHomeLunch Lunch)
{
    /// <summary>The adult of that name (matched without case), or null.</summary>
    public Adult? FindAdult(string name) =>
        Adults.FirstOrDefault(adult => string.Equals(adult.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
