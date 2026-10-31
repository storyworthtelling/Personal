using Constitution.Domain.Documents;

namespace Constitution.Tests.Documents;

/// <summary>
/// Two small invented pages the document tests draw: a workout page (dose, number box, rating lanes,
/// large Done tick, notes margin) and a meal page (YES ticks with the shaded reason field opposite,
/// and a column of standard ticks). They are fixtures, not the real documents: later releases build
/// those from the same factories. Every figure and name here is made up.
/// </summary>
internal static class DocumentSamples
{
    public static InkPageLayout ExercisePage()
    {
        var targets = new List<InkTarget>();
        var printed = new List<PrintedItem>
        {
            Text("dose", PrintedRole.Dose, 36, 76, 110, 62, "12", null),
            Text("dose-unit", PrintedRole.Label, 154, 112, 60, 16, "reps", null),
            Text("change", PrintedRole.Body, 36, 142, 250, 18, "Up 2 reps on last week", null),
        };

        var box = InkTargets.NumberBox("load", 36, 176, "weight lifted", "pounds");
        targets.Add(box);
        printed.Add(Text("load-unit", PrintedRole.Label, InkTargets.UnitLabelBounds(box, 60), "lb", box.Id));

        printed.Add(Text("feel-label", PrintedRole.Label, 36, 232, 200, 16, "How did it feel?", null));
        var lanes = InkTargets.CircleLanes("feel", 36, 256, ["easy", "right", "hard"]);
        targets.AddRange(lanes.Lanes);
        printed.AddRange(lanes.PrintedItems());

        var done = InkTargets.LargeTick("done", 36, 510, "this exercise is finished");
        targets.Add(done);
        printed.Add(Text("done-label", PrintedRole.Label, 72, 516, 60, 16, "Done", done.Id));

        var notes = InkTargets.NotesMargin("notes", 106, 520);
        targets.Add(notes);
        printed.Add(Text("notes-label", PrintedRole.Label, 318, 82, 91, 16, "Notes", notes.Id));
        printed.AddRange(InkTargets.NotesRules(notes));

        return InkPageLayout.Create("Push-ups", targets, printed);
    }

    public static InkPageLayout MealPage()
    {
        var targets = new List<InkTarget>();
        var printed = new List<PrintedItem>();
        var dishes = new[] { "Lentil soup", "Baked ziti", "Veggie chili" };

        for (var row = 0; row < dishes.Length; row++)
        {
            var top = 96f + row * 56f;
            var yes = InkTargets.TickRows($"yes{row + 1}", 36, top + 11, ["serve this dinner"])[0];
            targets.Add(yes);
            printed.Add(Text($"yes{row + 1}-label", PrintedRole.Label, 62, top + 14, 30, 16, "YES", yes.Id));
            printed.Add(Text($"dish{row + 1}", PrintedRole.Body, 98, top + 12, 92, 18, dishes[row], null));

            var reason = InkTargets.NoBecause($"no{row + 1}", yes, 409, 119);
            targets.Add(reason);
            printed.Add(Text($"no{row + 1}-label", PrintedRole.Label, 198, top + 14, 84, 16, "No, because", reason.Id));
        }

        var groceries = InkTargets.TickRows("buy", 36, 300, ["lentils", "pasta", "tomatoes", "onions"]);
        targets.AddRange(groceries);
        var items = new[] { "Lentils", "Pasta", "Tomatoes", "Onions" };
        for (var i = 0; i < groceries.Count; i++)
        {
            printed.Add(Text($"buy{i + 1}-label", PrintedRole.Body, 62, groceries[i].Bounds.Top, 140, 18, items[i], groceries[i].Id));
        }

        return InkPageLayout.Create("Dinners", targets, printed);
    }

    public static IReadOnlyList<InkPageLayout> Pages() => [ExercisePage(), MealPage()];

    public static PrintedItem Text(
        string name, PrintedRole role, float left, float top, float width, float height, string text, string? owner) =>
        Text(name, role, new InkRect(left, top, width, height), text, owner);

    public static PrintedItem Text(string name, PrintedRole role, InkRect bounds, string text, string? owner) =>
        new(name, role, bounds, text, owner);
}
