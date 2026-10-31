using Constitution.Domain.Documents;
using Constitution.Domain.Ink;

namespace Constitution.Domain.ReadBack;

/// <summary>
/// One page of a delivered document as the read-back needs to know it: where it sits in the PDF, the
/// map of boxes the painter drew (<see cref="InkReadout"/>, the very instance family the painter used),
/// and what the book says each box means (<see cref="InkRule"/>). The vision read is told only about the
/// boxes the rules read, and the resolver applies exactly those rules and no others.
/// </summary>
public sealed class ExpectedPage
{
    public ExpectedPage(int number, InkReadout readout, IReadOnlyList<InkRule> rules)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentNullException.ThrowIfNull(readout);
        ArgumentNullException.ThrowIfNull(rules);

        Number = number;
        Readout = readout;
        Rules = rules.ToList();

        var asked = Rules.SelectMany(rule => RuleTargets.For(rule, readout.Map)).Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        AskedTargets = readout.Map.Targets.Where(target => asked.Contains(target.Id)).ToList();
    }

    /// <summary>The page's place in the PDF, counting from 1.</summary>
    public int Number { get; }

    public InkReadout Readout { get; }

    public IReadOnlyList<InkRule> Rules { get; }

    /// <summary>The boxes the rules read, in page order, each once.</summary>
    public IReadOnlyList<InkTarget> AskedTargets { get; }
}
