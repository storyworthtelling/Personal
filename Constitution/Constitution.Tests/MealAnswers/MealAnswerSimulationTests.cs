using Constitution.Domain.Meals;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealAnswers;

/// <summary>Two installs, answers arriving in any order: the same final set, and nothing late reverses an approval.</summary>
public class MealAnswerSimulationTests
{
    public static TheoryData<int> Seeds()
    {
        var seeds = new TheoryData<int>();
        for (var seed = 1; seed <= MealAnswerSimulation.SeedCount; seed++)
        {
            seeds.Add(seed);
        }

        return seeds;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void AnyArrivalOrderGivesTheSameVerdict(int seed)
    {
        var simulation = MealAnswerSimulation.Run(seed);
        var now = After(11 * 60);
        var expected = Summary(MealDeadline.Decide(MealAnswerRecords.Read(simulation.Truth).Answers, Deadline, now));

        for (var arrival = 0; arrival < 4; arrival++)
        {
            var planner = simulation.Arrive(arrival);
            Assert.True(planner.ContentEquals(simulation.Truth), $"seed {seed}, arrival {arrival}: the files did not converge");
            Assert.Equal(expected, Summary(MealDeadline.Decide(MealAnswerRecords.Read(planner).Answers, Deadline, now)));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ALateAnswerNeverReversesAnApprovedItem(int seed)
    {
        var simulation = MealAnswerSimulation.Run(seed);
        var all = MealAnswerRecords.Read(simulation.Truth).Answers;
        var onTime = all.Where(answer => answer.ReadAt < Deadline).ToList();

        var atTheDeadline = MealDeadline.Decide(onTime, Deadline, Deadline);
        var afterTheLateOnes = MealDeadline.Decide(all, Deadline, After(11 * 60));

        foreach (var approved in atTheDeadline.Items.Where(item => item.Outcome == MealItemOutcome.Approved))
        {
            var later = afterTheLateOnes.Item(approved.ItemId)!;
            Assert.True(later.Outcome == MealItemOutcome.Approved, $"seed {seed}: {approved.ItemId} was approved at the deadline and is {later.Outcome} after late answers");
            Assert.Equal(approved.Basis, later.Basis);
        }

        foreach (var denied in atTheDeadline.Items.Where(item => item.Outcome == MealItemOutcome.Denied))
        {
            Assert.Equal(denied.Denials, afterTheLateOnes.Item(denied.ItemId)!.Denials);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void AWriterNeverLetsALateReadingReplaceAnOnTimeOne(int seed)
    {
        var simulation = MealAnswerSimulation.Run(seed);
        var kept = MealAnswerRecords.Read(simulation.Truth).Answers;

        foreach (var answer in kept.Where(answer => answer.ReadAt >= Deadline))
        {
            var hadOnTime = simulation.Readings.Any(reading =>
                reading.Person == answer.Person && reading.ItemId == answer.ItemId && reading.ReadAt < Deadline);
            Assert.False(hadOnTime, $"seed {seed}: {answer.Person} {answer.ItemId} kept a late reading over an on-time one");
        }
    }

    [Fact]
    public void TheSeedsReachEveryKindOfVerdict()
    {
        var outcomes = new HashSet<MealItemOutcome>();
        var late = 0;
        for (var seed = 1; seed <= MealAnswerSimulation.SeedCount; seed++)
        {
            var simulation = MealAnswerSimulation.Run(seed);
            var answers = MealAnswerRecords.Read(simulation.Truth).Answers;
            late += answers.Count(answer => answer.ReadAt >= Deadline);
            foreach (var item in MealDeadline.Decide(answers, Deadline, After(11 * 60)).Items)
            {
                outcomes.Add(item.Outcome);
            }
        }

        Assert.Contains(MealItemOutcome.Approved, outcomes);
        Assert.Contains(MealItemOutcome.Denied, outcomes);
        Assert.True(late > MealAnswerSimulation.SeedCount, $"only {late} late answers were kept across the seeds");
    }

    private static string Summary(MealDecision decision) =>
        string.Join(
            ";",
            decision.Items.Select(item =>
                $"{item.ItemId}={item.Outcome}/{item.Basis}/{string.Join(",", item.Denials.Select(denial => denial.Person + ":" + denial.Reason))}/{item.Ignored.Count}"));
}
