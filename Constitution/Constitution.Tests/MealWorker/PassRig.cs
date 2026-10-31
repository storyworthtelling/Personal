using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.MealWorker;

/// <summary>The pass's seams as fakes that keep a diary of what was asked of them, in order.</summary>
internal sealed class PassRig
{
    public List<string> Diary { get; } = [];

    public List<string> Lines { get; } = [];

    public List<MealPassStamp> Saved { get; } = [];

    public MealWeekRecordRead Weeks { get; set; } = new MealWeekRecordRead.NothingYet();

    public MealDecisionRecordRead Decisions { get; set; } = new MealDecisionRecordRead.NothingYet();

    public MealPassStampRead StampRead { get; set; } = new MealPassStampRead.NothingYet();

    public Exception? SaveFails { get; set; }

    public Func<CancellationToken, Task<GateResult<MealWeekOutcome>>> Plan { get; set; } =
        _ => Task.FromResult<GateResult<MealWeekOutcome>>(new GateResult<MealWeekOutcome>.Ran(new MealWeekOutcome.Planned(MealPassPlanTests.Report())));

    public Func<CancellationToken, Task<GateResult<MealDeadlineOutcome>>> Decide { get; set; } =
        _ => Task.FromResult<GateResult<MealDeadlineOutcome>>(
            new GateResult<MealDeadlineOutcome>.Ran(new MealDeadlineOutcome.NothingToDecide(MealDeadlineNothingBecause.AlreadyDecided, "Done.")));

    /// <summary>The adult whose tablet this phone has; null means none, so no recipe is ever due.</summary>
    public string? Person { get; set; }

    /// <summary>What the tablet record holds, by delivered name. Each look is written to the diary.</summary>
    public Dictionary<string, DeliveredMealDocument> DeliveredRecords { get; } = [];

    /// <summary>When set, looking a page up in the record of deliveries throws it.</summary>
    public Exception? DeliveredFails { get; set; }

    public Func<CancellationToken, Task<GateResult<MealRecipesOutcome>>> Recipes { get; set; } =
        _ => Task.FromResult<GateResult<MealRecipesOutcome>>(new GateResult<MealRecipesOutcome>.Ran(new MealRecipesOutcome.Nothing("None.")));

    public MealPassSeams Seams => new(
        new Records(this),
        new Decided(this),
        new Stamps(this),
        async token =>
        {
            Diary.Add("plan");
            return await Plan(token);
        },
        async token =>
        {
            Diary.Add("decide");
            return await Decide(token);
        },
        new Log(this),
        new Delivered(this),
        Person,
        async token =>
        {
            Diary.Add("recipes");
            return await Recipes(token);
        });

    public Task<MealPassResult> RunAsync(DateTimeOffset now, MealPassTrigger trigger = MealPassTrigger.Scheduled, CancellationToken cancellation = default) =>
        MealWeekPass.RunAsync(trigger, now, Zone, Seams, cancellation);

    /// <summary>Monday 7:05 with the Sunday week's deadline long past and undecided: both parts are due.</summary>
    public PassRig BothDue()
    {
        Weeks = Kept(Sunday);
        return this;
    }

    private sealed class Records(PassRig rig) : IMealWeekRecords
    {
        public MealWeekRecordRead Load()
        {
            rig.Diary.Add("read weeks");
            return rig.Weeks;
        }

        public void Save(MealWeekRecord record) => throw new InvalidOperationException("The pass never saves the weeks.");
    }

    private sealed class Decided(PassRig rig) : IMealDecisionRecords
    {
        public MealDecisionRecordRead Load()
        {
            rig.Diary.Add("read decisions");
            return rig.Decisions;
        }

        public void Save(MealDecisionRecord record) => throw new InvalidOperationException("The pass never saves the decisions.");
    }

    private sealed class Stamps(PassRig rig) : IMealPassStamps
    {
        public MealPassStampRead Load()
        {
            rig.Diary.Add("read stamp");
            return rig.StampRead;
        }

        public void Save(MealPassStamp stamp)
        {
            if (rig.SaveFails is { } failure)
            {
                throw failure;
            }

            rig.Diary.Add("save stamp");
            rig.Saved.Add(stamp);
            rig.StampRead = new MealPassStampRead.Held(stamp);
        }
    }

    private sealed class Delivered(PassRig rig) : IDeliveredMealDocuments
    {
        public DeliveredMealDocument? Find(string name)
        {
            rig.Diary.Add("read delivered");
            if (rig.DeliveredFails is { } failure)
            {
                throw failure;
            }

            return rig.DeliveredRecords.GetValueOrDefault(name);
        }

        public void Record(DeliveredMealDocument delivered) => throw new InvalidOperationException("The pass never records a delivery.");

        public void Remove(string name) => throw new InvalidOperationException("The pass never removes a delivery.");
    }

    private sealed class Log(PassRig rig) : IWorkoutWeekLog
    {
        public void Outcome(string phrase) => rig.Lines.Add(phrase);
    }
}
