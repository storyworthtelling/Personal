using System.Globalization;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Documents;

/// <summary>A day's square on the digest: filled for a done day, open for a skip, nothing on a rest day.</summary>
public enum DigestMark
{
    None,
    Filled,
    Open,
}

public sealed record DigestDay(DateOnly Day, DigestMark Mark, DigestSession? Session);

public sealed record DigestWorkoutsSummary(
    int Done, int Planned, int DoneMinutes, int PlannedMinutes, IReadOnlyList<DigestDay> Days, string Headline, IReadOnlyList<string> Moved);

/// <summary>
/// Page 1 of the Saturday digest (C-2.6.1, book p.10): done against planned as a count and minutes, the week day by
/// day, and what moved in sentences. A count and minutes only: no streak, no percentage, no comparison with anyone.
/// A planned session that was not read (nothing came back for it) shows an open square and is not counted done; the
/// digest does not call it a skip, it just does not claim it.
/// </summary>
public static class DigestWorkouts
{
    public static DigestWorkoutsSummary Summarise(DigestWorkoutWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        var days = new List<DigestDay>();
        for (var offset = 0; offset < 7; offset++)
        {
            var day = week.WeekStart.AddDays(offset);
            var session = week.Sessions.FirstOrDefault(item => item.Day == day);
            var mark = session is null ? DigestMark.None : session.Outcome == SessionOutcome.Done ? DigestMark.Filled : DigestMark.Open;
            days.Add(new DigestDay(day, mark, session));
        }

        var done = week.Sessions.Where(item => item.Outcome == SessionOutcome.Done).ToList();
        var doneMinutes = done.Sum(item => item.PlannedMinutes);
        var plannedMinutes = week.Sessions.Sum(item => item.PlannedMinutes);
        var headline = string.Create(
            CultureInfo.InvariantCulture,
            $"{done.Count} of {week.Sessions.Count} workouts done · {doneMinutes} of {plannedMinutes} minutes");
        return new DigestWorkoutsSummary(done.Count, week.Sessions.Count, doneMinutes, plannedMinutes, days, headline, week.Moved);
    }

    public static InkPageLayout Page(DigestWorkoutsSummary summary, DateOnly saturday, int number, int pages)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var draft = new PageDraft("Workouts");
        draft.Kicker($"SATURDAY DIGEST · {number} OF {pages}");
        draft.Line(PrintedRole.Strong, PageDraft.Left, 96f, PageDraft.ColumnWidth, summary.Headline, name: "headline");

        var baseline = 140f;
        var filled = 0;
        var open = 0;
        foreach (var day in summary.Days.Where(item => item.Mark != DigestMark.None))
        {
            var session = day.Session!;
            var name = day.Mark == DigestMark.Filled ? $"mark-filled-{++filled}" : $"mark-open-{++open}";
            var role = day.Mark == DigestMark.Filled ? PrintedRole.MarkFilled : PrintedRole.MarkOpen;
            draft.Add(new PrintedItem(name, role, new InkRect(PageDraft.Left, baseline - 11f, 12f, 12f), null, null));
            draft.Line(PrintedRole.Label, PageDraft.Left + 20f, baseline, 40f, day.Day.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant());
            var text = $"{session.Name} · {session.PlannedMinutes} min" + (string.IsNullOrWhiteSpace(session.RatingSummary) ? "" : $" · {session.RatingSummary}");
            draft.Line(PrintedRole.Body, PageDraft.Left + 66f, baseline, PageDraft.ColumnWidth - 66f, text);
            baseline += 30f;
        }

        if (summary.Moved.Count > 0)
        {
            baseline += 14f;
            draft.Line(PrintedRole.Label, PageDraft.Left, baseline, PageDraft.ColumnWidth, "WHAT MOVED");
            baseline += 20f;
            var moved = 0;
            foreach (var sentence in summary.Moved)
            {
                foreach (var line in PageText.Wrap(sentence, PageText.BodyColumnChars))
                {
                    draft.Line(PrintedRole.Body, PageDraft.Left, baseline, PageDraft.ColumnWidth, line, name: $"moved-{++moved}");
                    baseline += 16f;
                }
            }
        }

        PageDraft.RequireRoom(baseline, PageDraft.NotesBottom - 20f, "The digest's workouts page");
        draft.NotesColumn();
        draft.Footer("Digest", saturday.ToDateTime(TimeOnly.MinValue), number, pages);
        return draft.ToLayout();
    }
}
