using Constitution.Domain.Meals.Pipeline;

namespace Constitution.Tests.MealWorker;

public class MealPassTrailTests
{
    [Fact]
    public void EachLineNamesItsPartAndItsProblem()
    {
        Assert.Equal("meal week busy, try again later", MealPassTrail.LogPhrase(MealPassPart.Plan, MealPassProblem.Busy, string.Empty));
        Assert.Equal("meal deadline busy, try again later", MealPassTrail.LogPhrase(MealPassPart.Deadline, MealPassProblem.Busy, string.Empty));
        Assert.Equal("meal week failed: IOException", MealPassTrail.LogPhrase(MealPassPart.Plan, MealPassProblem.Failed, "IOException"));
        Assert.Equal("meal deadline failed: IOException", MealPassTrail.LogPhrase(MealPassPart.Deadline, MealPassProblem.Failed, "IOException"));
        Assert.Equal("meal pass stamp not saved: IOException", MealPassTrail.LogPhrase(MealPassPart.Plan, MealPassProblem.StampNotSaved, "IOException"));
    }

    [Fact]
    public void EveryPartAndProblemHasAPhrase()
    {
        foreach (var part in Enum.GetValues<MealPassPart>())
        {
            foreach (var problem in Enum.GetValues<MealPassProblem>())
            {
                Assert.False(string.IsNullOrWhiteSpace(MealPassTrail.LogPhrase(part, problem, "IOException")), $"{part} {problem}");
            }
        }
    }

    [Fact]
    public void ABusyLineCarriesNoTypeName()
    {
        Assert.DoesNotContain("IOException", MealPassTrail.LogPhrase(MealPassPart.Plan, MealPassProblem.Busy, "IOException"), StringComparison.Ordinal);
    }

    [Fact]
    public void ANotConfiguredDeadlineHasItsOwnWords()
    {
        Assert.Equal("meal deadline stopped, not configured", MealDeadlineTrail.LogPhrase(new MealDeadlineOutcome.Stopped(MealDeadlineStoppedBecause.NotConfigured, "No setup.")));
    }
}
