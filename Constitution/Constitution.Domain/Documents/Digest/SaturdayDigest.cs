namespace Constitution.Domain.Documents;

/// <summary>
/// The Saturday digest (C-2.6.1 to C-2.6.4, book p.10): five plain pages per adult. Workouts, Body (two pages), Meals
/// (shared), Next week with notes to the planner. No voiced line and no calligraphy anywhere: it reads as a record, not a
/// pep talk, and is gone three days after delivery (<c>ReadBackPolicy.DigestDays</c>).
/// </summary>
public static class SaturdayDigest
{
    public const int PageCount = 5;

    public static IReadOnlyList<InkPageLayout> Build(SaturdayDigestModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Person))
        {
            throw new ArgumentException("A digest belongs to someone.", nameof(model));
        }

        var series = DigestBody.Series(model.Readings, model.Saturday);
        var patterns = DigestPatterns.Find(series);
        var pages = new List<InkPageLayout>
        {
            DigestWorkouts.Page(DigestWorkouts.Summarise(model.Workouts), model.Saturday, 1, PageCount),
        };
        pages.AddRange(DigestBody.Pages(series, patterns, model.Saturday, 2, PageCount));
        pages.Add(DigestMeals.MealsPage(model.Meals, model.Saturday, 4, PageCount));
        pages.Add(DigestMeals.NextWeekPage(model.Next, model.Saturday, 5, PageCount));
        return pages;
    }
}
