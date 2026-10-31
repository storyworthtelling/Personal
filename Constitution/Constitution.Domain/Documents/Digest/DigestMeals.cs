using Constitution.Domain.Time;
using System.Globalization;

namespace Constitution.Domain.Documents;

/// <summary>
/// Pages 4 and 5 of the Saturday digest (C-2.6.3): the week's meals in three facts, identical on both adults' copies,
/// and next week with four ruled lines for notes to the planner. Neither page carries anything private; the meals
/// page takes no person at all.
/// </summary>
public static class DigestMeals
{
    public const string NotesHeading = "Notes to the planner";

    public static InkPageLayout MealsPage(DigestMealsWeek meals, DateOnly saturday, int number, int pages)
    {
        ArgumentNullException.ThrowIfNull(meals);
        var draft = new PageDraft("Meals");
        draft.Kicker($"SATURDAY DIGEST · {number} OF {pages}");
        var baseline = 100f;
        if (!string.IsNullOrWhiteSpace(meals.LovedDish))
        {
            draft.Line(PrintedRole.Label, PageDraft.Left, baseline, PageDraft.FullWidth, "THE DISH THIS HOUSE LOVED");
            baseline += 22f;
            draft.Line(PrintedRole.Strong, PageDraft.Left, baseline, PageDraft.FullWidth, meals.LovedDish, name: "loved-dish");
            baseline += 36f;
        }

        if (!string.IsNullOrWhiteSpace(meals.StretchDish))
        {
            draft.Line(PrintedRole.Label, PageDraft.Left, baseline, PageDraft.FullWidth, "THE STRETCH");
            baseline += 22f;
            var verdict = string.IsNullOrWhiteSpace(meals.StretchVerdict) ? meals.StretchDish : $"{meals.StretchDish} — {meals.StretchVerdict}";
            draft.Line(PrintedRole.Body, PageDraft.Left, baseline, PageDraft.FullWidth, verdict, name: "stretch");
            baseline += 18f;
            if (!string.IsNullOrWhiteSpace(meals.StretchFollows))
            {
                draft.Line(PrintedRole.Body, PageDraft.Left, baseline, PageDraft.FullWidth, meals.StretchFollows, name: "stretch-follows");
                baseline += 18f;
            }

            baseline += 18f;
        }

        draft.Line(PrintedRole.Label, PageDraft.Left, baseline, PageDraft.FullWidth, "THE WEEK IN THREE FACTS");
        baseline += 22f;
        var count = 0;
        foreach (var fact in meals.Facts.Take(3))
        {
            draft.Line(PrintedRole.Body, PageDraft.Left, baseline, PageDraft.FullWidth, fact, name: $"fact-{++count}");
            baseline += 20f;
        }

        PageDraft.RequireRoom(baseline, PageDraft.NotesBottom - 20f, "The digest's meals page");
        draft.Footer("Digest", saturday.ToDateTime(TimeOnly.MinValue), number, pages);
        return draft.ToLayout();
    }

    public static InkPageLayout NextWeekPage(DigestNextWeek next, DateOnly saturday, int number, int pages)
    {
        ArgumentNullException.ThrowIfNull(next);
        var draft = new PageDraft("Next week");
        draft.Kicker($"SATURDAY DIGEST · {number} OF {pages}");
        var baseline = 100f;
        baseline = List(draft, "WORKOUTS", next.Workouts, baseline, "workout");
        baseline = List(draft, "DINNERS", next.Dinners, baseline + 10f, "dinner");

        baseline += 16f;
        draft.Line(PrintedRole.Strong, PageDraft.Left, baseline, PageDraft.FullWidth, NotesHeading, name: "planner-heading");
        var top = baseline + 12f;
        foreach (var id in DigestIds.PlannerNotes)
        {
            draft.Target(InkTargets.WriteLine(id, PageDraft.Left, top, PageDraft.FullWidth, "a note to the planner"));
            top += InkTargets.WriteLineHeight + InkTargets.MinimumClearance;
        }

        PageDraft.RequireRoom(top, 556f, "The digest's next-week page");
        draft.Footer("Digest", saturday.ToDateTime(TimeOnly.MinValue), number, pages);
        return draft.ToLayout();
    }

    private static float List(PageDraft draft, string label, IReadOnlyList<DigestPlannedItem> items, float baseline, string prefix)
    {
        draft.Line(PrintedRole.Label, PageDraft.Left, baseline, PageDraft.FullWidth, label);
        baseline += 20f;
        var count = 0;
        foreach (var item in items)
        {
            var day = item.Day.ToString("ddd MMM d", CultureInfo.InvariantCulture);
            draft.Line(PrintedRole.Body, PageDraft.Left, baseline, PageDraft.FullWidth, $"{day} · {item.Text}", name: $"{prefix}-{++count}");
            baseline += 18f;
        }

        return baseline;
    }
}
