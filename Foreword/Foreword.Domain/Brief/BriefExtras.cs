namespace Foreword.Domain.Brief;

/// <summary>
/// One xkcd strip. <paramref name="AltText"/> is the hover text — the second joke, and the reason a
/// printed strip has to carry it: on paper there is nothing to hover.
/// </summary>
public sealed record ComicStrip(
    int Number, string Title, string AltText, byte[] Image);

/// <summary>
/// One thing that happened on this date. The year is separate from the text because the page sets
/// it apart, and because "1969" read mid-sentence is a number rather than a date.
/// </summary>
public sealed record HistoryNote(int Year, string Text);
