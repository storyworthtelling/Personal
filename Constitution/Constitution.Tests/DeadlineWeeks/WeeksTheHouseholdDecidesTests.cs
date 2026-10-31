using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Household;
using Constitution.Tests.Diagnostics;
using Constitution.Tests.SharedWeek;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.SharedWeek.SharedWeekData;

namespace Constitution.Tests.DeadlineWeeks;

/// <summary>D154: the phone that holds the lease decides a week the other phone planned, from the plan the household file carries; it never replaces a week it kept itself.</summary>
public class WeeksTheHouseholdDecidesTests
{
    private sealed class Own(MealWeekRecordRead read) : IMealWeekRecords
    {
        public MealWeekRecord? Saved { get; private set; }

        public MealWeekRecordRead Load() => read;

        public void Save(MealWeekRecord record) => Saved = record;
    }

    private sealed class Plans(WeekPlansRead read) : IWeekPlanSource
    {
        public WeekPlansRead Read() => read;
    }

    private static WeekPlansRead PublishedBy(string writer, string me) =>
        new WeekPlansRead.Held(new WeekPlanSet([new HeldWeekPlan(Plan(), writer)], []), me);

    [Fact]
    public void AWeekOnlyTheOtherPhonePlannedCanBeDecided()
    {
        var weeks = new WeeksTheHouseholdDecides(new Own(new MealWeekRecordRead.NothingYet()), new Plans(PublishedBy(OwnerPhone, PartnerPhone)));

        var held = Assert.IsType<MealWeekRecordRead.Held>(weeks.Load());

        Assert.Equal(Plan().WeekStart, Assert.Single(held.Record.Weeks).WeekStart);
    }

    [Fact]
    public void ThePartnersDeadlineIsDueFromThePublishedWeekAlone()
    {
        var weeks = new WeeksTheHouseholdDecides(new Own(new MealWeekRecordRead.NothingYet()), new Plans(PublishedBy(OwnerPhone, PartnerPhone)));

        var work = MealPassPlan.Decide(MealPassTrigger.Scheduled, Edt(10, 25, 9), Zone, new MealPassStampRead.NothingYet(), weeks.Load(), new MealDecisionRecordRead.NothingYet());

        Assert.True(work.Deadline);
    }

    [Fact]
    public void AWeekThisPhoneKeptItselfIsNeverReplaced()
    {
        var mine = new MealWeekRecordRead.Held(new MealWeekRecord([Stored(Edt(10, 20, 9))]));
        var weeks = new WeeksTheHouseholdDecides(new Own(mine), new Plans(PublishedBy(OwnerPhone, PartnerPhone)));

        Assert.Same(mine, weeks.Load());
    }

    [Fact]
    public void ThisPhonesOwnPublishedMirrorAddsNothing()
    {
        var weeks = new WeeksTheHouseholdDecides(new Own(new MealWeekRecordRead.NothingYet()), new Plans(PublishedBy(PartnerPhone, PartnerPhone)));

        Assert.IsType<MealWeekRecordRead.NothingYet>(weeks.Load());
    }

    [Fact]
    public void AnUnreadableLocalRecordStaysUnreadable()
    {
        var unreadable = new MealWeekRecordRead.Unreadable("damaged");
        var weeks = new WeeksTheHouseholdDecides(new Own(unreadable), new Plans(PublishedBy(OwnerPhone, PartnerPhone)));

        Assert.Same(unreadable, weeks.Load());
    }

    [Fact]
    public void AHouseholdFileThatWillNotOpenLeavesTheLocalWeeksAsTheyAre()
    {
        var weeks = new WeeksTheHouseholdDecides(new Own(new MealWeekRecordRead.NothingYet()), new Plans(new WeekPlansRead.Blocked("locked")));

        Assert.IsType<MealWeekRecordRead.NothingYet>(weeks.Load());
    }

    [Fact]
    public void SavingGoesToThisPhonesOwnRecord()
    {
        var own = new Own(new MealWeekRecordRead.NothingYet());
        var record = new MealWeekRecord([Stored(Edt(10, 20, 9))]);

        new WeeksTheHouseholdDecides(own, new Plans(PublishedBy(OwnerPhone, PartnerPhone))).Save(record);

        Assert.Same(record, own.Saved);
    }

    [Fact]
    public void TheDeadlineThePassAndTheTabsAllReadThroughIt()
    {
        var services = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => file.EndsWith("MealWeek/MealWeekServices.cs", StringComparison.Ordinal)));

        Assert.Equal(3, services.Split("new WeeksTheHouseholdDecides(").Length - 1);
    }
}
