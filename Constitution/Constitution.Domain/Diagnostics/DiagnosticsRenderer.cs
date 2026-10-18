using System.Globalization;
using System.Text;

namespace Constitution.Domain.Diagnostics;

/// <summary>
/// Turns a <see cref="DiagnosticsSnapshot"/> into the plain text the owner shares. Pure and
/// deterministic: the snapshot in, the same text out, with the sentences all coming from
/// <see cref="DiagnosticsPhrases"/> and the numbers all from bounded counts. The renderer holds no
/// private information to leak because the snapshot holds none to give it. The trail lines are the one place free
/// text could ride in, which is why each is a <see cref="TrailLine"/>: made only through <see cref="TrailLines.Filter"/>.
/// </summary>
public static class DiagnosticsRenderer
{
    public static string Render(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var text = new StringBuilder();
        text.Append("Constitution diagnostics\n");
        text.Append("Version: ").Append(snapshot.Version.Value).Append('\n');
        text.Append("Install role: ").Append(DiagnosticsPhrases.Of(snapshot.Role)).Append('\n');

        var counts = snapshot.Counts;
        text.Append("\nCounts\n");
        text.Append("  Workouts planned: ").Append(Number(counts.WorkoutsPlanned)).Append('\n');
        text.Append("  Workouts done: ").Append(Number(counts.WorkoutsDone)).Append('\n');
        text.Append("  Recipes in the box: ").Append(Number(counts.RecipesInBox)).Append('\n');
        text.Append("  Notices waiting: ").Append(Number(counts.NoticesWaiting.Sum(entry => entry.Count))).Append('\n');
        foreach (var entry in counts.NoticesWaiting)
        {
            text.Append("    ").Append(entry.Kind).Append(": ").Append(Number(entry.Count)).Append('\n');
        }

        text.Append("\nLast runs\n");
        foreach (var entry in snapshot.Outcomes)
        {
            text.Append("  ").Append(DiagnosticsPhrases.Of(entry.Component)).Append(": ")
                .Append(DiagnosticsPhrases.Of(entry.Outcome)).Append('\n');
        }
        if (snapshot.Outcomes.Count == 0)
        {
            text.Append("  Nothing has run yet\n");
        }

        text.Append("\nDocuments\n");
        foreach (var entry in snapshot.Documents)
        {
            text.Append("  ").Append(entry.File.Value).Append(": ").Append(DiagnosticsPhrases.Of(entry.Target))
                .Append(", ").Append(DiagnosticsPhrases.Of(entry.Outcome)).Append('\n');
        }
        if (snapshot.Documents.Count == 0)
        {
            text.Append("  None delivered yet\n");
        }

        text.Append("\nConnections\n");
        foreach (var entry in snapshot.Credentials)
        {
            text.Append("  ").Append(entry.Connection.Value).Append(": ").Append(DiagnosticsPhrases.Of(entry.State)).Append('\n');
        }
        if (snapshot.Credentials.Count == 0)
        {
            text.Append("  None reported\n");
        }

        // The newest lines of each trail, each already through TrailLines.Filter. Body first, whatever order they came in.
        text.Append("\nTrails\n");
        foreach (var section in snapshot.Trails.OrderBy(section => section.Kind))
        {
            text.Append("  ").Append(DiagnosticsPhrases.Of(section.Kind)).Append('\n');
            foreach (var line in section.Lines)
            {
                text.Append("    ").Append(line.Value).Append('\n');
            }
            if (section.Lines.Count == 0)
            {
                text.Append("    Nothing recorded yet\n");
            }
        }
        if (snapshot.Trails.Count == 0)
        {
            text.Append("  Nothing recorded yet\n");
        }

        return text.ToString();
    }

    private static string Number(int count) => count.ToString(CultureInfo.InvariantCulture);
}
