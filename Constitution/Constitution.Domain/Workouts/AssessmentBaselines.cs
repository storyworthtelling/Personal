using System.Globalization;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Domain.Workouts;

/// <summary>
/// The numbers read back from the assessment pages: what the owner wrote or circled, by measure id. A measure with
/// no usable answer (blank, not read, unparseable, a word outside the four) is in <see cref="Missing"/> and nowhere
/// else: an unreadable number is never zero, because a zero baseline would plan from a person who did nothing.
/// </summary>
public sealed record AssessmentBaselines(
    IReadOnlyDictionary<string, decimal> Numbers,
    IReadOnlyDictionary<string, string> Choices,
    IReadOnlyList<string> Missing)
{
    /// <summary>
    /// The baselines as the pages print them. Every test page paints its number box as <c>result</c> and its circled word as
    /// <c>choice</c> (<see cref="WorkoutIds"/>), so the measure an answer belongs to is told by its page: test <c>n</c> of the
    /// document is on <see cref="AssessmentDocument.PageOfTest"/>, in the same order as the session's measures. A page the read
    /// never got to has no answer, and its measure is missing.
    /// </summary>
    public static AssessmentBaselines FromPages(IReadOnlyList<AssessmentMeasure> measures, IEnumerable<PageResolutions> pages)
    {
        ArgumentNullException.ThrowIfNull(measures);
        ArgumentNullException.ThrowIfNull(pages);

        var byNumber = pages.ToDictionary(page => page.Number);
        var keyed = new List<InkResolution>();
        for (var index = 0; index < measures.Count; index++)
        {
            if (!byNumber.TryGetValue(AssessmentDocument.PageOfTest(index), out var page))
            {
                continue;
            }

            var measure = measures[index];
            foreach (var resolution in page.Resolutions)
            {
                switch (resolution)
                {
                    case InkResolution.Written written when written.SubjectId == WorkoutIds.Result:
                        keyed.Add(written with { SubjectId = measure.Id });
                        break;
                    case InkResolution.Circled circled when circled.SubjectId == WorkoutIds.Choice:
                        keyed.Add(circled with { SubjectId = measure.Id });
                        break;
                }
            }
        }

        return Read(measures, keyed);
    }

    /// <summary>The answers already keyed by measure id (<see cref="FromPages"/> does that keying from the pages as printed; use it for a read-back).</summary>

    public static AssessmentBaselines Read(IEnumerable<AssessmentMeasure> measures, IEnumerable<InkResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(measures);
        ArgumentNullException.ThrowIfNull(resolutions);

        var answers = resolutions.ToList();
        var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var measure in measures)
        {
            var found = false;
            switch (measure)
            {
                case NumberMeasure:
                    var written = answers.OfType<InkResolution.Written>().FirstOrDefault(item => item.SubjectId == measure.Id);
                    if (written is not null && TryNumber(written.Text, out var value))
                    {
                        numbers[measure.Id] = value;
                        found = true;
                    }

                    break;
                case CircleMeasure circle:
                    var circled = answers.OfType<InkResolution.Circled>().FirstOrDefault(item => item.SubjectId == measure.Id);
                    if (circled is not null && circle.Words.Contains(circled.Word, StringComparer.Ordinal))
                    {
                        choices[measure.Id] = circled.Word;
                        found = true;
                    }

                    break;
                default:
                    throw new ArgumentException($"The measure {measure.Id} is of a kind the read-back does not know.", nameof(measures));
            }

            if (!found)
            {
                missing.Add(measure.Id);
            }
        }

        return new AssessmentBaselines(numbers, choices, missing);
    }

    // Plain digits with an optional decimal point; no sign (so no negative) and no thousands separator, so handwriting read as "1,5" is not guessed at.
    private static bool TryNumber(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}
