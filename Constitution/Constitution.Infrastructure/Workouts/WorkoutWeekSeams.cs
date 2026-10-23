using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.ReadBack;
using Storyworthtelling.Remarkable;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Everything <see cref="WorkoutWeekRun"/> reaches outside itself, named once: the one place the head has to fill. Each is a
/// seam that already existed or one this run added; none holds a token or a passphrase (the tablet client and the delivery
/// service are already bound to this phone's pairing).
/// </summary>
/// <param name="Calendars">Google Calendar, read (see <see cref="IHouseholdCalendarsSource"/> for the two rules it must keep).</param>
/// <param name="Learning">The ledgers the pages have taught; unreadable ones stop the run.</param>
/// <param name="Planner">The plan author with the rules-only fallback (<see cref="NoPlanAuthor"/> until one exists), logging through <see cref="TrailPlanLog"/>.</param>
/// <param name="Conditions">The hourly forecast and air quality; a source that cannot read says so and changes nothing.</param>
/// <param name="HotelGyms">The places lookup for a work trip's hotel.</param>
/// <param name="Recovery">The morning's Body reads.</param>
/// <param name="Encouragement">The voiced seat; <see cref="SeatCacheEncouragement"/> over the shared cache.</param>
/// <param name="CalendarWriter">Google Calendar, written. The run puts <see cref="PastWriteGuard"/> in front of it.</param>
/// <param name="WrittenKeys">The record of keys written, which placement reads and grows.</param>
/// <param name="Delivery">The tablet service, already bound to this phone's pairing.</param>
/// <param name="Tablet">The same tablet client, for taking a replaced page off.</param>
/// <param name="Expected">The read-back's record of pages waiting for their ink.</param>
/// <param name="Delivered">What has gone to the tablet, for C-2.3.6.</param>
/// <param name="Log">The Body trail.</param>
public sealed record WorkoutWeekSeams(
    IHouseholdCalendarsSource Calendars,
    IWorkoutLearning Learning,
    PlanWithFallback Planner,
    IConditionsSource Conditions,
    IHotelGymLookup HotelGyms,
    IRecoveryReadSource Recovery,
    IWorkoutEncouragement Encouragement,
    ICalendarWriter CalendarWriter,
    IWrittenKeyStore WrittenKeys,
    DeliveryService Delivery,
    IRemarkableClient Tablet,
    IExpectedDocuments Expected,
    IDeliveredWorkouts Delivered,
    IWorkoutWeekLog Log);
