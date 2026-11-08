using System.Text.Json;
using System.Text.Json.Serialization;

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
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

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

    public string ToJson() => JsonSerializer.Serialize(entries, Json);

    public static TriageLedger FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }
        var read = JsonSerializer.Deserialize<Dictionary<string, TriageEntry>>(json, Json) ?? [];
        return new TriageLedger(new SortedDictionary<string, TriageEntry>(read, StringComparer.Ordinal));
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

    public static TriageLedger Merge(TriageLedger ledger, IEnumerable<TriageItem> items, string decided)
    {
        foreach (var item in items.Where(i => i.Status == TriageStatus.Pending && i.Outcome is not null && i.Class is not null))
        {
            ledger = ledger.With(item.Fingerprint, item.Outcome!.Value, item.Class!, decided);
        }
        return ledger;
    }
}
