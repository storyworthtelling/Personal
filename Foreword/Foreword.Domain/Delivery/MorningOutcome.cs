namespace Foreword.Domain.Delivery;

/// <summary>
/// What a morning did, one document at a time (audit 11 §6). A failed upload of The Day used to be swallowed and the
/// run recorded "Delivered", so the owner woke to no calendar page while Settings said it had gone. Each document now
/// reports its own outcome, <see cref="Summary"/> names what did not arrive and why, and <see cref="AnyFailed"/> is
/// what sends the run round again.
/// </summary>
public sealed class MorningOutcome
{
    private enum State
    {
        Delivered,
        Earlier,
        Failed,
        Skipped,
    }

    private readonly List<(MorningDocument Document, State State, string? Detail)> entries = [];

    public MorningOutcome Delivered(MorningDocument document, string? detail = null) => Add(document, State.Delivered, detail);

    /// <summary>Already on the tablet from an earlier run this morning, so not sent again.</summary>
    public MorningOutcome AlreadyDelivered(MorningDocument document) => Add(document, State.Earlier, null);

    public MorningOutcome Failed(MorningDocument document, string reason) => Add(document, State.Failed, reason);

    /// <summary>Not due this morning (the bonus puzzle outside the first Sunday, prayer with no Google account).</summary>
    public MorningOutcome Skipped(MorningDocument document) => Add(document, State.Skipped, null);

    public bool AnyFailed => entries.Any(entry => entry.State == State.Failed);

    public bool Failed(MorningDocument document) => entries.Any(entry => entry.Document == document && entry.State == State.Failed);

    /// <summary>"The Day did not arrive (no network). Delivered: the brief (puzzle only), prayer".</summary>
    public string Summary()
    {
        var failed = entries.Where(e => e.State == State.Failed)
            .Select(e => $"{Capitalised(Name(e.Document))} did not arrive ({e.Detail})");
        var delivered = entries.Where(e => e.State is State.Delivered or State.Earlier)
            .Select(e => e.State == State.Earlier ? $"{Name(e.Document)} (earlier this morning)"
                : string.IsNullOrWhiteSpace(e.Detail) ? Name(e.Document) : $"{Name(e.Document)} ({e.Detail})")
            .ToList();

        var sentences = failed.ToList();
        if (delivered.Count > 0)
        {
            sentences.Add("Delivered: " + string.Join(", ", delivered));
        }
        return sentences.Count == 0 ? "Nothing was due" : string.Join(". ", sentences);
    }

    private MorningOutcome Add(MorningDocument document, State state, string? detail)
    {
        entries.Add((document, state, detail));
        return this;
    }

    private static string Name(MorningDocument document) => document switch
    {
        MorningDocument.Brief => "the brief",
        MorningDocument.Bonus => "the bonus puzzle",
        MorningDocument.Day => "The Day",
        _ => "prayer",
    };

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
