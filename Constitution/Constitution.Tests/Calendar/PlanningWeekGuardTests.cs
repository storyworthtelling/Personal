using System.Text.RegularExpressions;

namespace Constitution.Tests.Calendar;

/// <summary>
/// Which day opens the household's week is decided in one file, <c>PlanningWeek</c>. Nothing else may ask whether a date is a
/// Monday, count days since a remembered Monday, or divide a day number by seven: each of those is a second copy of the
/// answer, and two copies is how a week ends up starting on different days in the planner and the lease.
/// </summary>
public class PlanningWeekGuardTests
{
    // The dinners, groceries, documents, recipes, Today and the words they speak still count from Monday. They are the second
    // half of the move to Sunday weeks; the entry for a folder goes when that folder is converted.
    private static readonly string[] NotYet =
    [
        "Constitution.Domain/Meals/",
        "Constitution.Domain/Groceries/",
        "Constitution.Domain/Documents/",
        "Constitution.Domain/Recipes/",
        "Constitution.Domain/Today/",
        "Constitution.Domain/Voice/",
        "Constitution.Domain/ReadBack/",
        "Constitution.Domain/Placement/MealPlacement.cs",
        "Constitution.Android/MealsTab/",
    ];

    private static readonly Regex[] Shapes =
    [
        new(@"DayOfWeek\.Monday", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        new(@"\bMondayOf\w*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        new(@"DayNumber\s*/\s*\d", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        new(@"DayNumber\s*%\s*\d", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        new(@"\(int\)\s*\w+(\.\w+)*\s*\+\s*6\)\s*%\s*7", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
    ];

    [Fact]
    public void NoSourceDecidesTheFirstDayItself()
    {
        var found = new List<string>();
        foreach (var file in ConstitutionSource.Files(".cs"))
        {
            var path = ConstitutionSource.Relative(file);
            var inShippedCode = path.StartsWith("Constitution.Domain/", StringComparison.Ordinal)
                || path.StartsWith("Constitution.Infrastructure/", StringComparison.Ordinal)
                || path.StartsWith("Constitution.Android/", StringComparison.Ordinal);
            if (!inShippedCode
                || path.EndsWith("/PlanningWeek.cs", StringComparison.Ordinal)
                || path.EndsWith("/KeyAssignments.cs", StringComparison.Ordinal)
                || NotYet.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var number = 0; number < lines.Length; number++)
            {
                if (Shapes.Any(shape => shape.IsMatch(lines[number])))
                {
                    found.Add($"{path}:{number + 1}");
                }
            }
        }

        Assert.True(found.Count == 0, "These decide a week for themselves; ask PlanningWeek:\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheGuardCanSeeAMonday()
    {
        Assert.Contains(Shapes, shape => shape.IsMatch("if (day.DayOfWeek != DayOfWeek.Monday)"));
        Assert.Contains(Shapes, shape => shape.IsMatch("var weekIndex = start.DayNumber / 7;"));
        Assert.Contains(Shapes, shape => shape.IsMatch("var since = ((int)day.DayOfWeek + 6) % 7;"));
        Assert.DoesNotContain(Shapes, shape => shape.IsMatch("var week = PlanningWeek.IndexOf(start);"));
    }

    [Fact]
    public void ThePlanningWeekFileExists()
    {
        Assert.Contains(ConstitutionSource.Files(".cs"), file => ConstitutionSource.Relative(file) == "Constitution.Domain/Calendar/PlanningWeek.cs");
    }
}
