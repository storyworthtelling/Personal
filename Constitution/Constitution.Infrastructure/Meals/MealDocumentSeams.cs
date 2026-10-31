using Constitution.Domain.Budgets;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Storyworthtelling.Remarkable;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Everything the documents step of the weekly meal run reaches outside itself, named once. The tablet client, the delivery service, the ink probe
/// and the read-back's record of expected documents are the very ones the workout run uses, so a title means the same page to both. None holds a token or a passphrase.
/// </summary>
/// <param name="Delivery">The tablet service, already bound to this phone's pairing (D85).</param>
/// <param name="Tablet">The same tablet client, for looking a title up and taking a replaced page off.</param>
/// <param name="InkProbe">Looks at a page on the tablet for the owner's ink before it is replaced (S3).</param>
/// <param name="Expected">The read-back's record of pages waiting for their ink. Written only once the tablet has the page.</param>
/// <param name="Delivered">What has gone to the tablet, for sending only before the slot.</param>
/// <param name="Roster">Who eats.</param>
/// <param name="Pantry">What is on hand and which staples are out.</param>
/// <param name="Budgets">Index's grocery budget by month.</param>
/// <param name="Holidays">The family calendar's holidays (the default cannot be asked, so no ask-ahead page).</param>
/// <param name="Log">The Body trail's one-method seam.</param>
public sealed record MealDocumentSeams(
    DeliveryService Delivery,
    IRemarkableClient Tablet,
    IPageInkProbe InkProbe,
    IExpectedDocuments Expected,
    IDeliveredMealDocuments Delivered,
    IMealRosterSource Roster,
    IMealPantrySource Pantry,
    IMonthlyBudgets Budgets,
    IHolidayCalendar Holidays,
    IWorkoutWeekLog Log);
