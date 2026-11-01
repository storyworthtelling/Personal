using System.Globalization;
using Codex.Domain.Plate;

namespace Codex.Domain.Diagnostics;

/// <summary>
/// Plate's lines for the log loop (LOG-LOOP-PLAN.md §4.5): the DCIM insert at send time, each scan's delivery,
/// the recognition's closeness and decision — a number only, never the picture or the name — and the errands
/// other apps send. Fixed wording, because the reader's digest groups on it.
/// </summary>
public static class PlateLogLines
{
    public static string Sent(int sent, int putBack) =>
        putBack == 0
            ? string.Create(CultureInfo.InvariantCulture, $"SEND {sent} to DCIM/Camera")
            : string.Create(CultureInfo.InvariantCulture, $"SEND {sent} to DCIM/Camera · {putBack} put back in the Darkroom");

    public static string Delivered(ScanDestination destination, bool delivered, string? reason) =>
        delivered ? $"SCAN {destination}: handed over" : $"SCAN {destination}: not delivered — {reason ?? "no reason given"}";

    public static string Recognised(int faces, FaceMatch nearest)
    {
        if (faces == 0)
        {
            return "RECOGNISE no faces";
        }
        var count = faces == 1 ? "1 face" : string.Create(CultureInfo.InvariantCulture, $"{faces} faces");
        return nearest.IsSomeone
            ? string.Create(CultureInfo.InvariantCulture, $"RECOGNISE {count} · nearest {nearest.Closeness:0.00} of {FaceBook.SameFace:0.00} · suggested someone")
            : string.Create(CultureInfo.InvariantCulture, $"RECOGNISE {count} · nobody within {FaceBook.SameFace:0.00}");
    }

    public static string Errand(string action, bool returned) =>
        returned ? $"ERRAND {action}: returned a result" : $"ERRAND {action}: cancelled";
}
