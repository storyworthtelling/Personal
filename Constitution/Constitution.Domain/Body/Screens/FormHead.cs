namespace Constitution.Domain.Body.Screens;

/// <summary>
/// A chart's printed form head (book page 16): the field names of a doctor's chart in small mono,
/// "SIGNAL SLEEP, UNITS HOURS, PERIOD MONTH". The period is left off a chart that is only asking
/// permission, because it has not got one yet. Wording is the book's, in capitals, as a form is printed.
/// </summary>
public sealed record FormHead(string Signal, string Units, string? Period)
{
    /// <summary>The field name and the value beside it, in the order they are printed across the head.</summary>
    public IReadOnlyList<(string Field, string Value)> Fields =>
        Period is null
            ? [("SIGNAL", Signal), ("UNITS", Units)]
            : [("SIGNAL", Signal), ("UNITS", Units), ("PERIOD", Period)];

    /// <summary>The head for one signal over one window; null for a chart that has no window yet.</summary>
    public static FormHead For(BodySignal signal, BodyWindow? window) => new(
        BodySignals.Name(signal).ToUpperInvariant(),
        BodySignals.Units(signal).ToUpperInvariant(),
        window is null ? null : PeriodWord(window.Value));

    private static string PeriodWord(BodyWindow window) => window switch
    {
        BodyWindow.Month => "MONTH",
        BodyWindow.Quarter => "QUARTER",
        BodyWindow.Year => "YEAR",
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "That is not one of Body's windows."),
    };
}
