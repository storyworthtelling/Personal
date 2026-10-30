using Constitution.Domain.Budgets;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.WorkoutPipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWeekDocuments.DocsData;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>Sam and Lee's meal documents with every seam faked. Lee's phone by default; <see cref="Person"/> changes whose tablet it is.</summary>
internal sealed class DocsRig
{
    public DocsRig()
    {
        Delivery = new DeliveryService(Tablet, Board, () => Now);
        Seams = new MealDocumentSeams(Delivery, Tablet, Probe, Expected, Delivered, Roster, Pantry, Budgets, Holidays, Log);
    }

    /// <summary>The Wednesday before the week: nothing of it has begun.</summary>
    public DateTimeOffset Now { get; set; } = Edt(10, 21, 12);

    public string Person { get; set; } = Lee;

    public MealWeek Week { get; set; } = DocsData.Week();

    public List<Recipe> Box { get; set; } = Cards();

    public FakeTablet Tablet { get; } = new();

    public Access Board { get; } = new();

    public DeliveryService Delivery { get; }

    public FakeInkProbe Probe { get; } = new();

    public MemoryExpected Expected { get; } = new();

    public MemoryDelivered Delivered { get; } = new();

    public FakeRoster Roster { get; } = new();

    public FakePantry Pantry { get; } = new();

    public MemoryBudgets Budgets { get; } = new();

    public FakeHolidays Holidays { get; } = new();

    public RecordingWeekLog Log { get; } = new();

    public MealDocumentSeams Seams { get; }

    public MealDocumentRequest Request() => new(Week, Box, Rules(), Now, [Person])
    {
        History = Box.Select(card => new CookedMeal(card.Id, Sunday.AddDays(-14))).ToList(),
    };

    public async Task<MealDocumentsResult.Done> RunAsync(MealDocumentRequest? request = null)
    {
        var result = await MealDocumentsStep.RunAsync(request ?? Request(), Seams, CancellationToken.None);
        return Assert.IsType<MealDocumentsResult.Done>(result);
    }

    public MealDocumentOutcome Sheet(MealDocumentsResult.Done done) => done.Documents.Single(item => item.Kind == Constitution.Domain.Delivery.DeliveryKind.Meals);

    public MealDocumentOutcome RecipeOn(MealDocumentsResult.Done done, int offset) =>
        done.Documents.Single(item => item.Kind == Constitution.Domain.Delivery.DeliveryKind.Recipe && item.Day == Sunday.AddDays(offset));

    public string SheetTitle => "Constitution — Meals — 2026-10-25";

    public static string RecipeTitle(int offset, string cook) => $"Constitution — Recipe — {Sunday.AddDays(offset):yyyy-MM-dd} — {cook}";
}

internal sealed class MemoryExpected : IExpectedDocuments
{
    public List<ExpectedDocument> Recorded { get; } = [];

    public Exception? RecordFails { get; set; }

    public IReadOnlyList<OutstandingDocument> Outstanding() => Recorded.Select(item => new OutstandingDocument(item, false)).ToList();

    public void Record(ExpectedDocument document)
    {
        if (RecordFails is not null)
        {
            throw RecordFails;
        }

        Recorded.RemoveAll(item => item.Name == document.Name);
        Recorded.Add(document);
        Records++;
    }

    public int Records { get; private set; }

    public void MarkSaved(string documentName) => throw new InvalidOperationException("The meal documents step never reads back.");

    public void Retire(string documentName) => throw new InvalidOperationException("The meal documents step never retires.");
}

internal sealed class MemoryDelivered : IDeliveredMealDocuments
{
    public Dictionary<string, DeliveredMealDocument> Held { get; } = [];

    public int Records { get; private set; }

    public DeliveredMealDocument? Find(string name) => Held.GetValueOrDefault(name);

    public void Record(DeliveredMealDocument delivered)
    {
        Records++;
        Held[delivered.Name] = delivered;
    }

    public void Remove(string name) => Held.Remove(name);
}

internal sealed class FakeRoster : IMealRosterSource
{
    public MealRosterRead Answer { get; set; } = new MealRosterRead.Ready(People());

    public MealRosterRead Read() => Answer;
}

internal sealed class FakePantry : IMealPantrySource
{
    public MealPantryRead Answer { get; set; } = new MealPantryRead.Ready(Stocked(), 0);

    public MealPantryRead Read() => Answer;
}

internal sealed class MemoryBudgets : IMonthlyBudgets
{
    public MonthlyBudgets Held { get; set; } = MonthlyBudgets.None;

    public MonthlyBudgets Load() => Held;

    public void Receive(BudgetPayload payload, DateTimeOffset receivedAt) => throw new InvalidOperationException("The meal documents step never writes a budget.");
}

internal sealed class FakeHolidays : IHolidayCalendar
{
    public HolidayEvents Answer { get; set; } = HolidayEvents.Unavailable;

    public HolidayEvents Between(DateOnly first, DateOnly last) => Answer;
}
