using Storyworthtelling.Time;

namespace Codex.Domain.Plate;

/// <summary>
/// What a Darkroom cell says to a screen reader (DESIGN.md §17, audit W9.3, report 10 §7): what it is, when it was
/// taken, and whether it is chosen. Plate holds the only copy, and a cull ends in a send or a delete, so "chosen" must
/// be heard as well as seen.
/// </summary>
public static class DarkroomWords
{
    public static string Cell(DarkroomItem item, bool chosen, DateTime now)
    {
        var what = item.Kind == DarkroomKind.Scan ? "Scan" : "Photo";
        var line = $"{what}, {RelativeTime.Format(item.CapturedAt, now)}";
        return chosen ? line + ", chosen" : line;
    }
}
