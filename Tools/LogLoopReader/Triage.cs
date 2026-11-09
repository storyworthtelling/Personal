namespace LogLoopReader;

public enum TriageOutcome
{
    Fix,
    Report,
    NoAction,
}

public enum TriageStatus
{
    /// <summary>Needs a decision this week.</summary>
    Pending,

    /// <summary>Decided in an earlier week and not worth asking again.</summary>
    Carried,

    /// <summary>Routine information, counted in the digest and never asked about.</summary>
    Folded,
}

/// <summary>One line of the weekly checklist. It holds a fingerprint and the decision, never log text.</summary>
public sealed record TriageItem(
    string Fingerprint,
    string App,
    string Kind,
    int Count,
    TriageStatus Status,
    TriageOutcome? Outcome = null,
    string? Class = null);

public sealed record TriageEntry(TriageOutcome Outcome, string Class, string Decided);

/// <summary>
/// What earlier weeks decided, keyed by fingerprint (LOG-LOOP-PLAN.md §7.1). Fingerprints are hashes of shapes and
/// are safe to commit; nothing else about a log line is stored here.
/// </summary>
public sealed class TriageLedger
{
    private readonly SortedDictionary<string, TriageEntry> entries;

    private TriageLedger(SortedDictionary<string, TriageEntry> entries) => this.entries = entries;

    public static TriageLedger Empty { get; } = new(new SortedDictionary<string, TriageEntry>(StringComparer.Ordinal));

    public TriageEntry? Get(string fingerprint) => entries.GetValueOrDefault(fingerprint);

    public TriageLedger With(string fingerprint, TriageOutcome outcome, string triageClass, string decided)
    {
        var copy = new SortedDictionary<string, TriageEntry>(entries, StringComparer.Ordinal)
        {
            [fingerprint] = new TriageEntry(outcome, triageClass, decided),
        };
        return new TriageLedger(copy);
    }

    // One line per fingerprint: "<fingerprint> <Outcome> <Class> <yyyy-MM-dd>". Plain text on purpose: a quoted
    // 64-hex string is the shape of an API key, and the repo's own key guard (rightly) refuses to tell the two apart.
    public string ToText() =>
        string.Concat(entries.Select(e => $"{e.Key} {e.Value.Outcome} {e.Value.Class} {e.Value.Decided}\n"));

    public static TriageLedger FromText(string text)
    {
        var read = new SortedDictionary<string, TriageEntry>(StringComparer.Ordinal);
        foreach (var line in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || !Enum.TryParse<TriageOutcome>(parts[1], out var outcome))
            {
                throw new ArgumentException("The triage ledger has a line that is not '<fingerprint> <Outcome> <Class> <date>'.");
            }
            read[parts[0]] = new TriageEntry(outcome, parts[2], parts[3]);
        }
        return read.Count == 0 ? Empty : new TriageLedger(read);
    }
}

public sealed record TriageCheckResult(bool Complete, IReadOnlyList<string> Open);

/// <summary>
/// Makes "every fingerprint has an outcome" something the reader can check rather than something the session
/// intends. <see cref="Plan"/> writes the checklist, <see cref="Check"/> refuses to call it finished while a line is
/// open, and <see cref="Merge"/> carries the decisions into next week's ledger.
/// </summary>
public static class Triage
{
    public static IReadOnlyList<TriageItem> Plan(IEnumerable<DigestGroup> groups, TriageLedger ledger) =>
        groups.Select(g => PlanOne(g, ledger)).ToList();

    private static TriageItem PlanOne(DigestGroup group, TriageLedger ledger)
    {
        TriageItem Item(TriageStatus status, TriageOutcome? outcome = null, string? triageClass = null) =>
            new(group.Fingerprint, group.App, group.Kind, group.Count, status, outcome, triageClass);

        // Only a plain message at INFO is routine. An exception, tap verdict or sign-in is never folded, whatever its level.
        if (group.Kind == "message" && group.Levels.All(l => l == "INFO"))
        {
            return Item(TriageStatus.Folded);
        }
        var earlier = ledger.Get(group.Fingerprint);
        // A fix that did not take, and a report nobody has acted on, are the two things worth seeing again.
        return earlier is { Outcome: TriageOutcome.NoAction }
            ? Item(TriageStatus.Carried, earlier.Outcome, earlier.Class)
            : Item(TriageStatus.Pending);
    }

    public static TriageCheckResult Check(IEnumerable<TriageItem> items)
    {
        var open = items
            .Where(i => i.Status == TriageStatus.Pending && (i.Outcome is null || string.IsNullOrWhiteSpace(i.Class)))
            .Select(i => i.Fingerprint)
            .ToList();
        return new TriageCheckResult(open.Count == 0, open);
    }

    /// <summary>One list per app that has something to decide, so each subagent edits a file nobody else touches.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TriageItem>> ByApp(IEnumerable<TriageItem> items) =>
        items.Where(i => i.Status == TriageStatus.Pending)
            .GroupBy(i => i.App, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TriageItem>)g.ToList(), StringComparer.Ordinal);

    public sealed record Tally(int Fix, int Report, int NoAction, int Untriaged);

    /// <summary>The counts the report states; <c>Untriaged</c> is what the auto-merge gate reads.</summary>
    public static Tally Counts(IEnumerable<TriageItem> items)
    {
        var pending = items.Where(i => i.Status == TriageStatus.Pending).ToList();
        return new Tally(
            pending.Count(i => i.Outcome == TriageOutcome.Fix),
            pending.Count(i => i.Outcome == TriageOutcome.Report),
            pending.Count(i => i.Outcome == TriageOutcome.NoAction),
            pending.Count(i => i.Outcome is null || string.IsNullOrWhiteSpace(i.Class)));
    }

    public static TriageLedger Merge(TriageLedger ledger, IEnumerable<TriageItem> items, string decided)
    {
        foreach (var item in items.Where(i => i.Status == TriageStatus.Pending && i.Outcome is not null && i.Class is not null))
        {
            ledger = ledger.With(item.Fingerprint, item.Outcome!.Value, item.Class!, decided);
        }
        return ledger;
    }
}
