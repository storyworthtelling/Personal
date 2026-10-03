namespace Storyworthtelling.Maui.State;

/// <summary>One notice as the Waiting page draws it.</summary>
public sealed record WaitingRow(
    string Headline, string Detail, string BarToken, string? Date, string? ActionLabel, Action? OnAction);

/// <summary>
/// What the Waiting page draws, decided with no view in it (DESIGN.md §13, mockup 17): the rows in the
/// order given (the state already ordered them), each with its role bar, its date and its one optional
/// action — or, when nothing is waiting, the sentence the page says instead.
///
/// <para>Pulled out of the view so that "the page empties live" can be tested as behaviour: build a
/// plan, withdraw the last notice, build it again.</para>
/// </summary>
public sealed record WaitingPlan(IReadOnlyList<WaitingRow> Rows, string? EmptySentence)
{
    /// <summary>§13's other half of "one fold, no count": a plain sentence, with no number in it.</summary>
    public const string NothingWaiting = "Nothing waiting.";

    public static WaitingPlan From(IReadOnlyList<DogEarNotice> notices, DateTimeOffset now)
    {
        if (notices.Count == 0)
        {
            return new WaitingPlan([], NothingWaiting);
        }

        var rows = notices
            .Select(notice => new WaitingRow(
                notice.Headline,
                notice.Detail,
                DogEarPalette.TokenFor(notice.Hue),
                NoticeDates.Label(notice.At, now),
                string.IsNullOrWhiteSpace(notice.ActionLabel) ? null : notice.ActionLabel,
                notice.OnAction))
            .ToList();

        return new WaitingPlan(rows, null);
    }
}
