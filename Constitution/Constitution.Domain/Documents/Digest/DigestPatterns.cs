using Constitution.Domain.Body;

namespace Constitution.Domain.Documents;

/// <summary>Plain Pearson correlation. Lives under the digest and nowhere else (G24): a correlation appears only in its patterns block.</summary>
public static class Correlation
{
    /// <summary>The Pearson r of two equal-length samples; 0 when either has no spread.</summary>
    public static double Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count != b.Count || a.Count < 2)
        {
            throw new ArgumentException("Two samples of the same length, at least two long, are needed.", nameof(b));
        }

        var meanA = a.Average();
        var meanB = b.Average();
        double cross = 0, spreadA = 0, spreadB = 0;
        for (var index = 0; index < a.Count; index++)
        {
            var da = a[index] - meanA;
            var db = b[index] - meanB;
            cross += da * db;
            spreadA += da * da;
            spreadB += db * db;
        }

        return spreadA <= 0 || spreadB <= 0 ? 0 : cross / Math.Sqrt(spreadA * spreadB);
    }
}

public sealed record DigestPattern(BodySignal Lead, BodySignal Follow, double R, string Sentence);

/// <summary>
/// The patterns block (C-2.6.2): signals that moved together across the owner's twelve weeks, said as "weeks with more X
/// had more Y" and headed "not causes". Needs eight weeks both signals were read, and a strong relationship; shows at most
/// three. The wording states what happened in the same weeks and nothing about why.
/// </summary>
public static class DigestPatterns
{
    public const string Heading = "Patterns, not causes";

    public const int EnoughSharedWeeks = 8;
    public const double Strong = 0.7;
    public const int Most = 3;

    public static IReadOnlyList<DigestPattern> Find(IReadOnlyList<DigestSeries> series)
    {
        ArgumentNullException.ThrowIfNull(series);
        var found = new List<DigestPattern>();
        for (var i = 0; i < series.Count; i++)
        {
            for (var j = i + 1; j < series.Count; j++)
            {
                var (lead, follow) = Rank(series[i].Signal) <= Rank(series[j].Signal) ? (series[i], series[j]) : (series[j], series[i]);
                var a = new List<double>();
                var b = new List<double>();
                for (var week = 0; week < lead.Weeks.Count && week < follow.Weeks.Count; week++)
                {
                    if (lead.Weeks[week] is { } x && follow.Weeks[week] is { } y)
                    {
                        a.Add(x);
                        b.Add(y);
                    }
                }

                if (a.Count < EnoughSharedWeeks)
                {
                    continue;
                }

                var r = Correlation.Pearson(a, b);
                if (Math.Abs(r) < Strong)
                {
                    continue;
                }

                var sentence = r > 0
                    ? $"Weeks with {Phrase(lead.Signal, true)} also had {Phrase(follow.Signal, true)}."
                    : $"Weeks with {Phrase(lead.Signal, true)} had {Phrase(follow.Signal, false)}.";
                found.Add(new DigestPattern(lead.Signal, follow.Signal, r, sentence));
            }
        }

        return found.OrderByDescending(pattern => Math.Abs(pattern.R)).Take(Most).ToList();
    }

    /// <summary>What the owner does leads; what the body shows follows.</summary>
    private static int Rank(BodySignal signal) => signal switch
    {
        BodySignal.Steps => 0,
        BodySignal.ActiveMinutes => 1,
        BodySignal.Strength => 2,
        BodySignal.Calories => 3,
        BodySignal.Sleep => 4,
        _ => 5 + (int)signal,
    };

    private static string Phrase(BodySignal signal, bool higher) => signal switch
    {
        BodySignal.Steps => higher ? "more steps" : "fewer steps",
        BodySignal.ActiveMinutes => higher ? "more active minutes" : "fewer active minutes",
        BodySignal.Sleep => higher ? "more sleep" : "less sleep",
        BodySignal.Strength => higher ? "heavier lifting" : "lighter lifting",
        BodySignal.Calories => higher ? "more calories" : "fewer calories",
        BodySignal.Weight => higher ? "a higher weight" : "a lower weight",
        BodySignal.BodyComposition => higher ? "a higher body fat reading" : "a lower body fat reading",
        BodySignal.RestingHeartRate => higher ? "a higher resting heart rate" : "a lower resting heart rate",
        BodySignal.HeartRateVariability => higher ? "a higher heart rate variability" : "a lower heart rate variability",
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "That is not one of Body's signals."),
    };
}
