using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes.Screens;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Household;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Everything <see cref="MealWeekRun"/> reaches outside itself, named once. The calendar, the record of written keys and the record of
/// issued numbers are the very ones the weekly workout run uses, so a key means the same event to both. None holds a token or a passphrase.
/// </summary>
/// <param name="Lease">Whether this install may plan the week (<see cref="HouseholdPlanningStanding.AskAsync"/>).</param>
/// <param name="Recipes">The recipe box over the household file.</param>
/// <param name="History">The confirmed cooked dinners.</param>
/// <param name="Taste">The taste profile and the roster it is about.</param>
/// <param name="Calendars">Google Calendar, read (<see cref="IHouseholdCalendarsSource"/> states the two rules it must keep).</param>
/// <param name="Planner">The meal author with the rules-only fallback.</param>
/// <param name="CalendarWriter">Google Calendar, written. The run puts <c>PastWriteGuard</c> in front of it.</param>
/// <param name="WrittenKeys">The record of keys written.</param>
/// <param name="KeyAssignments">The record of workout numbers issued, which placement keeps and reads.</param>
/// <param name="Records">The planned weeks, for Today and the Meals tab.</param>
/// <param name="Log">The Body trail's one-method seam.</param>
/// <param name="Documents">What the documents step reaches (the tablet, the stores, the roster, the pantry); null plans the week and prints nothing.</param>
/// <param name="Templates">What the rotating-templates step reaches (the library and the author); null leaves pages 2 and 3 off the sheet. Used only together with <paramref name="Documents"/>.</param>
public sealed record MealWeekSeams(
    Func<CancellationToken, Task<PlanningStanding>> Lease,
    IRecipeBoxSource Recipes,
    IMealHistoryStore History,
    IMealTasteSource Taste,
    IHouseholdCalendarsSource Calendars,
    MealWithFallback Planner,
    ICalendarWriter CalendarWriter,
    IWrittenKeyStore WrittenKeys,
    IKeyAssignmentStore KeyAssignments,
    IMealWeekRecords Records,
    IWorkoutWeekLog Log,
    MealDocumentSeams? Documents = null,
    MealTemplateSeams? Templates = null);
