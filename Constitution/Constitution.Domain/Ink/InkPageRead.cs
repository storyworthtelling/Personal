using Constitution.Domain.Documents;

namespace Constitution.Domain.Ink;

/// <summary>A pen mark in page points, as a geometric read reports it (see <see cref="InkPageRead.FromMarks"/>).</summary>
public sealed record InkMark(InkRect Bounds, double Confidence, string? Text = null)
{
    public double Confidence { get; init; } = InkConfidence.Check(Confidence, nameof(Confidence));
}

/// <summary>
/// One page as the vision read left it: either read, with an answer per target it reported, or not
/// read at all, with the reason. The two are different things on purpose. "Box empty" is a read that
/// says so; "page not read" is no read, and nothing on it resolves, so a failed call can never pass for
/// a quiet morning. A page that was read but left a target out is the same thing for that target.
/// </summary>
public sealed class InkPageRead
{
    private readonly Dictionary<string, InkTargetRead> reads;

    private InkPageRead(bool wasRead, string whyNotRead, Dictionary<string, InkTargetRead> reads)
    {
        WasRead = wasRead;
        WhyNotRead = whyNotRead;
        this.reads = reads;
    }

    public bool WasRead { get; }

    /// <summary>Empty when the page was read.</summary>
    public string WhyNotRead { get; }

    /// <summary>The page was not read: the call failed, the document had no annotations rendered, anything.</summary>
    public static InkPageRead NotRead(string why) =>
        new(false, string.IsNullOrWhiteSpace(why) ? "The page was not read." : why.Trim(), []);

    /// <summary>The page was read and these are the targets it reported; the rest were not read.</summary>
    public static InkPageRead Read(IEnumerable<InkTargetRead> targetReads)
    {
        var byId = new Dictionary<string, InkTargetRead>(StringComparer.Ordinal);
        foreach (var read in targetReads)
        {
            if (!byId.TryAdd(read.TargetId, read))
            {
                throw new ArgumentException(
                    $"The target '{read.TargetId}' was read twice; two answers for one box cannot both be the owner's.",
                    nameof(targetReads));
            }
        }

        return new InkPageRead(true, string.Empty, byId);
    }

    /// <summary>The read for one target, or null when the page was read but did not report it.</summary>
    public InkTargetRead? For(string targetId) => reads.GetValueOrDefault(targetId);

    /// <summary>
    /// A page read as marks in points: each mark is placed with <see cref="InkReadout.TargetsTouching"/>
    /// onto every target it touches, so a circle across two lanes marks both. A target no mark touches
    /// reads as empty at <paramref name="confidenceWhenEmpty"/>, which the caller must state because a
    /// list of marks cannot say how sure it is that nothing else is there. A target several marks touch
    /// is as doubtful as its weakest mark.
    /// </summary>
    public static InkPageRead FromMarks(InkReadout readout, IEnumerable<InkMark> marks, double confidenceWhenEmpty)
    {
        ArgumentNullException.ThrowIfNull(readout);
        InkConfidence.Check(confidenceWhenEmpty, nameof(confidenceWhenEmpty));

        var touching = readout.Map.Targets.ToDictionary(t => t.Id, _ => new List<InkMark>(), StringComparer.Ordinal);
        foreach (var mark in marks)
        {
            foreach (var target in readout.TargetsTouching(mark.Bounds))
            {
                touching[target.Id].Add(mark);
            }
        }

        var reads = readout.Map.Targets.Select(target =>
        {
            var here = touching[target.Id];
            if (here.Count == 0)
            {
                return new InkTargetRead(target.Id, false, confidenceWhenEmpty);
            }

            var words = string.Join(' ', here.Select(m => m.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
            return new InkTargetRead(target.Id, true, here.Min(m => m.Confidence), words);
        });

        return Read(reads);
    }
}
