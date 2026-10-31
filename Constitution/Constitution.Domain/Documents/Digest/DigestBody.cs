using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;

namespace Constitution.Domain.Documents;

/// <summary>
/// Pages 2 and 3 of the Saturday digest (C-2.6.2, D39): every Body signal, every week, as one small neutral line of weekly
/// averages over the owner's own last twelve weeks, the latest week as a dot with its value and the change in words, and a
/// gap where nothing was read. A week averages only when at least three of its days were read: one reading is not a week.
/// The change in words is the latest week against the average of the owner's own earlier weeks, and only when four of
/// those exist; nobody else's numbers are an input.
/// </summary>
public static class DigestBody
{
    public const int WeekCount = 12;

    /// <summary>Days a week must have been read on to be averaged.</summary>
    public const int EnoughDays = 3;

    /// <summary>Earlier weeks needed before the digest says how the latest week compares.</summary>
    public const int EnoughEarlierWeeks = 4;

    private const int FirstPageSignals = 5;

    public static IReadOnlyList<DigestSeries> Series(IEnumerable<BodyReading> readings, DateOnly saturday)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var settled = BodySignals.Settled(readings, saturday).Where(reading => reading.Day <= saturday).ToList();
        var firstDay = saturday.AddDays(-(7 * WeekCount) + 1);
        var result = new List<DigestSeries>();
        foreach (var signal in BodySignals.All)
        {
            var own = settled.Where(reading => reading.Signal == signal && reading.Day >= firstDay).ToList();
            var weeks = new List<double?>();
            for (var index = 0; index < WeekCount; index++)
            {
                var start = firstDay.AddDays(7 * index);
                var days = own.Where(reading => reading.Day >= start && reading.Day <= start.AddDays(6)).ToList();
                weeks.Add(days.Count >= EnoughDays ? days.Average(reading => reading.Value) : null);
            }

            var latest = weeks[WeekCount - 1];
            var earlier = weeks.Take(WeekCount - 1).Where(week => week is not null).Select(week => week!.Value).ToList();
            string? change = null;
            if (latest is { } value && earlier.Count >= EnoughEarlierWeeks)
            {
                change = new SignalChange(signal, value, earlier.Average()).Words;
            }

            result.Add(new DigestSeries(signal, weeks, latest, latest is { } now ? SignalWording.ValueText(signal, now) : null, change));
        }

        return result;
    }

    public static IReadOnlyList<InkPageLayout> Pages(
        IReadOnlyList<DigestSeries> series, IReadOnlyList<DigestPattern> patterns, DateOnly saturday, int firstPage, int pages)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(patterns);
        var date = saturday.ToDateTime(TimeOnly.MinValue);
        var first = new PageDraft("Body");
        first.Kicker($"SATURDAY DIGEST · {firstPage} OF {pages}");
        var second = new PageDraft("Body");
        second.Kicker($"SATURDAY DIGEST · {firstPage + 1} OF {pages}");

        for (var index = 0; index < series.Count; index++)
        {
            var draft = index < FirstPageSignals ? first : second;
            var slot = index < FirstPageSignals ? index : index - FirstPageSignals;
            Block(draft, series[index], 100f + slot * 82f);
        }

        var cursor = 100f + (series.Count - FirstPageSignals) * 82f + 22f;
        Patterns(second, patterns, cursor);

        first.NotesColumn();
        second.NotesColumn();
        first.Footer("Digest", date, firstPage, pages);
        second.Footer("Digest", date, firstPage + 1, pages);
        return [first.ToLayout(), second.ToLayout()];
    }

    private static void Block(PageDraft draft, DigestSeries series, float top)
    {
        var id = BodySignals.Id(series.Signal);
        draft.Line(PrintedRole.Strong, PageDraft.Left, top + 14f, 150f, BodySignals.Name(series.Signal), name: $"signal-{id}");
        draft.Add(new PrintedItem($"chart-{id}", PrintedRole.Chart, new InkRect(PageDraft.Left, top + 22f, 150f, 44f), null, null, Points: series.Weeks));
        draft.Line(PrintedRole.Mono, PageDraft.Left + 158f, top + 40f, 112f, series.LatestText ?? "no readings", name: $"latest-{id}");
        if (series.ChangeWords is { } words)
        {
            draft.Line(PrintedRole.Small, PageDraft.Left + 158f, top + 58f, 112f, words, name: $"change-{id}");
        }
    }

    private static void Patterns(PageDraft draft, IReadOnlyList<DigestPattern> patterns, float top)
    {
        var baseline = top;
        draft.Line(PrintedRole.Label, PageDraft.Left, baseline, PageDraft.ColumnWidth, DigestPatterns.Heading, name: "patterns-heading");
        baseline += 20f;
        if (patterns.Count == 0)
        {
            draft.Line(PrintedRole.Small, PageDraft.Left, baseline, PageDraft.ColumnWidth, "Nothing stood out this time.", name: "patterns-none");
            return;
        }

        var count = 0;
        foreach (var pattern in patterns)
        {
            foreach (var line in PageText.Wrap(pattern.Sentence, PageText.SmallColumnChars))
            {
                draft.Line(PrintedRole.Small, PageDraft.Left, baseline, PageDraft.ColumnWidth, line, name: $"pattern-{++count}");
                baseline += 14f;
            }
        }
    }
}
