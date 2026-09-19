using Folio.Domain.Analysis;
using Folio.Domain.Readiness;

namespace Folio.Domain.Report;

/// <summary>A finding placed on the chart: the chapters it covers, and what it is worth doing.</summary>
public readonly record struct PacingFinding(int FirstChapter, int LastChapter, ReadinessTier Severity)
{
    /// <summary>A finding on one chapter is a point on the line, not a stretch of it.</summary>
    public bool IsPoint => LastChapter <= FirstChapter;
}

/// <summary>
/// One stretch of the tension line drawn in one manner. A null <paramref name="Severity"/> is the
/// plain line: nothing is wrong along here.
/// </summary>
public readonly record struct TensionRun(int FromChapter, int ToChapter, ReadinessTier? Severity);

/// <summary>A single-chapter finding: a dot on the line where it sits.</summary>
public readonly record struct FindingDot(int Chapter, ReadinessTier Severity);

/// <summary>
/// A stretch the reader will feel sag, taking the hue of the finding that sits on it — or none,
/// which is its own statement: the tension drops here and no reader remarked on it.
/// </summary>
public readonly record struct SagBand(int FromChapter, int ToChapter, ReadinessTier? Severity);

/// <summary>Everything drawn over the tension curve. No member of it is a fill; see below.</summary>
public sealed record PacingOverlay(
    IReadOnlyList<TensionRun> Runs,
    IReadOnlyList<FindingDot> Dots,
    IReadOnlyList<SagBand> Sags);

/// <summary>
/// The pacing chart is tension <i>and</i> the findings: a finding anchored to a range of chapters
/// recolours that run of the line, a single-chapter finding is a dot, and a sag takes the hue of
/// whatever finding sits on it (F39).
///
/// <para><b>It stays a line, and the reason was measured rather than assumed.</b> Drawn as four
/// filled severity bands the chart is illegible, but not for the reason a contrast floor usually
/// catches: in the Document profile the four severity tones are deliberately <i>equiluminant</i> —
/// Must <c>#8D585A</c>, Should <c>#79663F</c>, Consider <c>#5B6B7E</c>, Great <c>#5D7048</c>, all
/// between luminance 0.134 and 0.144 — so two adjacent fills measure <b>1.01–1.03:1</b> at their
/// shared edge. The boundary between two severities would exist in hue alone, which DESIGN.md §16
/// forbids for anything printed. Desaturated to the rM2 ramp they separate, but the order inverts
/// (Must is the darkest, Should the lightest), so weight cannot be read as severity either.</para>
///
/// <para>A line escapes all of it: each run is separated by position, carries its own
/// <see cref="SeverityStroke"/> — a weight and a dash, the §16 non-colour cue — and the severities
/// are named in the key rather than inferred from the picture.</para>
/// </summary>
public static class PacingChart
{
    /// <summary>At or under this, a chapter reads as slack.</summary>
    public const double SagFloor = 0.35;

    /// <summary>One slack chapter is a breath; this many in a row is a sag.</summary>
    public const int SagRun = 2;

    public static PacingOverlay Overlay(
        IReadOnlyList<ChapterTension> curve, IReadOnlyList<PacingFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(findings);

        if (curve.Count == 0)
        {
            return new PacingOverlay([], [], []);
        }

        // One severity per segment between two chapters, so the runs below cannot overlap or leave a
        // gap by construction — the drawn line is continuous and every point of it is accounted for.
        var segments = new ReadinessTier?[Math.Max(curve.Count - 1, 0)];
        foreach (var finding in findings.Where(finding => !finding.IsPoint))
        {
            var from = Math.Max(finding.FirstChapter, 0);
            var to = Math.Min(finding.LastChapter, curve.Count - 1);
            for (var i = from; i < to; i++)
            {
                segments[i] = Worse(segments[i], finding.Severity);
            }
        }

        var dots = findings
            .Where(finding => finding.IsPoint
                && finding.FirstChapter >= 0 && finding.FirstChapter < curve.Count)
            .GroupBy(finding => finding.FirstChapter)
            .OrderBy(group => group.Key)
            .Select(group => new FindingDot(group.Key, group.Min(finding => finding.Severity)))
            .ToList();

        return new PacingOverlay(Coalesce(segments, curve.Count), dots, Sags(curve, segments));
    }

    /// <summary>
    /// The worse of two severities wins a segment. <see cref="ReadinessTier"/> is declared worst
    /// first, so this is the lower value — a Must under a Consider is still a Must, and a run that
    /// took the first finding it met would print the milder of the two.
    /// </summary>
    private static ReadinessTier? Worse(ReadinessTier? held, ReadinessTier arriving) =>
        held is { } current && current <= arriving ? current : arriving;

    private static List<TensionRun> Coalesce(ReadinessTier?[] segments, int chapters)
    {
        if (segments.Length == 0)
        {
            return [new TensionRun(0, 0, null)];
        }

        var runs = new List<TensionRun>();
        var start = 0;
        for (var i = 1; i <= segments.Length; i++)
        {
            if (i < segments.Length && segments[i] == segments[start])
            {
                continue;
            }

            runs.Add(new TensionRun(start, i, segments[start]));
            start = i;
        }

        return runs;
    }

    private static List<SagBand> Sags(IReadOnlyList<ChapterTension> curve, ReadinessTier?[] segments)
    {
        var sags = new List<SagBand>();
        var start = -1;

        for (var i = 0; i <= curve.Count; i++)
        {
            var slack = i < curve.Count && curve[i].Tension <= SagFloor;
            if (slack)
            {
                start = start < 0 ? i : start;
                continue;
            }

            if (start >= 0 && i - start >= SagRun)
            {
                ReadinessTier? severity = null;
                for (var s = start; s < i && s < segments.Length; s++)
                {
                    severity = segments[s] is { } tier ? Worse(severity, tier) : severity;
                }

                sags.Add(new SagBand(start, i - 1, severity));
            }

            start = -1;
        }

        return sags;
    }
}
