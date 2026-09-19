using System.Text.RegularExpressions;
using Folio.Domain.Analysis;
using Folio.Domain.Readiness;

namespace Folio.Domain.Report;

/// <summary>A stretch of the chart, as indices into the curve.</summary>
public readonly record struct ChapterRange(int From, int To);

/// <summary>
/// Turns the chapter citation a finding arrived with ("Ch. 12-15") into a place on the pacing chart.
///
/// <para><b>It drops what it cannot place, and that is the point.</b> A citation off the end of the
/// curve is not clamped to the last chapter: a mark drawn on chapter 20 for a problem the reader
/// found in chapter 40 is indistinguishable, on the page, from a real one. A book with named rather
/// than numbered chapters places nothing at all and draws the plain tension line, which is the chart
/// the report had before F39 — a smaller thing than a chart that is confidently wrong.</para>
/// </summary>
public static partial class ChapterCitation
{
    public static ChapterRange? Locate(string? citation, IReadOnlyList<ChapterTension> curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (curve.Count == 0 || Span(citation) is not { } chapters)
        {
            return null;
        }

        var start = IndexOf(chapters.From, curve);
        if (start is null)
        {
            return null;
        }

        if (chapters.To == chapters.From)
        {
            return new ChapterRange(start.Value, start.Value);
        }

        // A range that runs off the end keeps the part that is on the chart: it genuinely covers
        // chapters the reader can see, unlike a single citation past the end.
        var end = IndexOf(chapters.To, curve) ?? (chapters.To > Largest(curve) ? curve.Count - 1 : start.Value);
        return new ChapterRange(start.Value, Math.Max(end, start.Value));
    }

    /// <summary>
    /// The chapter <i>numbers</i> a citation names, with no chart to place them on — which is what a
    /// mark on the manuscript needs, since the page it is drawn on has no pacing curve.
    /// </summary>
    public static ChapterRange? Span(string? citation)
    {
        if (string.IsNullOrWhiteSpace(citation))
        {
            return null;
        }

        // A range only when the two numbers are actually joined as one — "Ch. 12-15", "Chapters 3
        // to 5". First-and-last would read "Ch. 7 · Scene 3" as chapters 7 through 3, which is both
        // backwards and about a scene.
        var span = Range().Match(citation);
        if (span.Success
            && int.TryParse(span.Groups[1].Value, out var from)
            && int.TryParse(span.Groups[2].Value, out var to)
            && to >= from)
        {
            return new ChapterRange(from, to);
        }

        var single = Numbers().Match(citation);
        return single.Success && int.TryParse(single.Value, out var only)
            ? new ChapterRange(only, only)
            : null;
    }

    /// <summary>The highest chapter number the curve actually labels.</summary>
    private static int Largest(IReadOnlyList<ChapterTension> curve) =>
        curve.Select(point => Numbers().Match(point.Chapter ?? string.Empty))
            .Where(match => match.Success)
            .Select(match => int.TryParse(match.Value, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>The revision plan's own word for how much a finding matters.</summary>
    public static ReadinessTier Tier(string? priority) =>
        priority?.Trim().ToLowerInvariant() switch
        {
            "must" => ReadinessTier.Must,
            "should" => ReadinessTier.Should,
            _ => ReadinessTier.Consider,
        };

    /// <summary>Where a chapter number sits on the curve, matched by the number in its own label.</summary>
    private static int? IndexOf(int chapter, IReadOnlyList<ChapterTension> curve)
    {
        for (var i = 0; i < curve.Count; i++)
        {
            var match = Numbers().Match(curve[i].Chapter ?? string.Empty);
            if (match.Success && int.TryParse(match.Value, out var labelled) && labelled == chapter)
            {
                return i;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    [GeneratedRegex(@"(\d+)\s*(?:-|\u2013|\u2014|to|through|and)\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Range();
}
