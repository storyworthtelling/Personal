using Constitution.Domain.Documents;
using Constitution.Domain.Equipment;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The cover lists the session one row to an exercise, and each cell is a column of its own width. The domain has no font to
/// measure with, so this reads the real thing: every cell is drawn alone on a rendered page and its glyphs are measured
/// against the box the layout gave it, for every exercise name the planner can put on a cover.
/// </summary>
public class CoverFitTests
{
    private static readonly (string Dose, ExerciseLoad Load)[] Variants =
    [
        ("3 × 10", new ExerciseLoad(25, "dumbbell or kettlebell")),
        ("20 min", new ExerciseLoad(null, "resistance band")),
        ("5 min", new ExerciseLoad(null, "bodyweight")),
    ];

    private static IReadOnlyList<string> EveryExerciseName()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var equipment in new[] { EquipmentSet.Everything(), EquipmentSet.Of([]), EquipmentSet.Of([EquipmentCatalogue.MatId, EquipmentCatalogue.BandsId]), EquipmentSet.Of([EquipmentCatalogue.BenchId, EquipmentCatalogue.PullUpBarId, EquipmentCatalogue.TreadmillId]) })
        {
            foreach (var shift in Enumerable.Range(0, 6).Select(week => week * 7))
            {
                var plan = Plan(Calendars(), equipment, weekShift: shift);
                foreach (var exercise in plan.Plans.SelectMany(week => week.Sessions).SelectMany(session => session.Session.Exercises))
                {
                    names.Add(exercise.Name);
                }
            }
        }

        return names.Order(StringComparer.Ordinal).ToList();
    }

    private static InkPageLayout CoverOf(string name, string dose, ExerciseLoad load)
    {
        var exercise = new ExercisePageModel(name, dose, load, null, false, 120, null);
        return WorkoutDocument.Build(new WorkoutDocumentModel("Strength", WorkoutSamples.Tuesday, 40, "Needs a mat", [exercise]))[0];
    }

    /// <summary>The item rendered alone: how far left and right its real glyphs reach.</summary>
    private static (double Left, double Right) Reach(PrintedItem item)
    {
        var alone = InkPageLayout.Create("Probe", [], [item]);
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([alone]));
        var low = ConstitutionPage.Height - item.Bounds.Bottom - 2;
        var high = ConstitutionPage.Height - item.Bounds.Top + 2;
        var letters = document.GetPage(1).Letters
            .Where(letter => !letter.FontName.Contains("Serif", StringComparison.Ordinal) && letter.GlyphRectangle.Bottom >= low && letter.GlyphRectangle.Bottom <= high)
            .ToList();
        Assert.NotEmpty(letters);
        return (letters.Min(letter => letter.GlyphRectangle.Left), letters.Max(letter => letter.GlyphRectangle.Right));
    }

    private static IEnumerable<PrintedItem> ListCells(InkPageLayout cover)
    {
        var top = cover.Printed.Single(item => item.Name == "list-top").Bounds.Top;
        return cover.Printed.Where(item =>
            item.Role is PrintedRole.Strong or PrintedRole.Body or PrintedRole.Small
            && !string.IsNullOrWhiteSpace(item.Text) && item.Bounds.Bottom > top);
    }

    [Fact]
    public void EveryNameOnEveryCoverStaysInItsColumn()
    {
        var names = EveryExerciseName();
        Assert.True(names.Count >= 20, "Too few exercises were planned for this to cover the library.");
        Assert.Contains("Squat holding a weight at your chest", names);

        foreach (var name in names)
        {
            foreach (var (dose, load) in Variants)
            {
                var cover = CoverOf(name, dose, load);
                var cells = ListCells(cover).Where(item => item.Role == PrintedRole.Strong).ToList();
                Assert.NotEmpty(cells);
                foreach (var cell in cells)
                {
                    var reach = Reach(cell);
                    Assert.True(
                        reach.Right <= cell.Bounds.Right && reach.Left >= cell.Bounds.Left - 1,
                        $"'{cell.Text}' (cover of {name}, {dose}) runs to {reach.Right:0.0} in a column that ends at {cell.Bounds.Right:0.0}.");
                }
            }
        }
    }

    [Fact]
    public void EveryLoadAndDoseStaysInItsColumn()
    {
        foreach (var name in EveryExerciseName())
        {
            foreach (var (dose, load) in Variants)
            {
                foreach (var cell in ListCells(CoverOf(name, dose, load)).Where(item => item.Role != PrintedRole.Strong && item.Align == PrintedAlign.Left))
                {
                    var reach = Reach(cell);
                    Assert.True(
                        reach.Right <= cell.Bounds.Right,
                        $"'{cell.Text}' (cover of {name}) runs to {reach.Right:0.0} in a column that ends at {cell.Bounds.Right:0.0}.");
                }
            }
        }
    }

    [Fact]
    public void ARowStaysBetweenItsRules()
    {
        foreach (var name in EveryExerciseName())
        {
            foreach (var (dose, load) in Variants)
            {
                var cover = CoverOf(name, dose, load);
                var above = cover.Printed.Single(item => item.Name == "list-top").Bounds.Top;
                var below = cover.Printed.Single(item => item.Name == "list-row-1").Bounds.Top;
                foreach (var cell in ListCells(cover).Where(item => item.Bounds.Top < below))
                {
                    Assert.True(cell.Bounds.Top >= above && cell.Bounds.Bottom <= below, $"'{cell.Text}' (cover of {name}) is not inside its row.");
                }
            }
        }
    }
}
