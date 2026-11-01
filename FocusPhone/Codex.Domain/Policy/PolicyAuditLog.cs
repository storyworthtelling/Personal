namespace Codex.Domain.Policy;

/// <summary>What kind of policy event was recorded — the diagnostic vocabulary of the audit log.</summary>
public enum PolicyEventKind
{
    Applied,           // a policy pass ran (summary of counts)
    SuspendFailed,     // a package could not be suspended
    ReleaseFailed,     // an allowlisted/quarantined package could not be un-suspended
    GrantFailed,       // a permission grant did not take
    ConfigFailed,      // DNS / status bar / login-browser config did not apply yet
    AppBlocked,        // the owner blocked an app
    AppUnblocked,      // the owner unblocked an app
    CutoverFinalized,  // debugging locked down irreversibly
    NotDeviceOwner,    // policy skipped because the app isn't device owner
    UninstallFailed,   // the silent device-owner uninstall call threw, falling back to the visible uninstaller
}

/// <summary>One recorded policy action, with when and a human summary.</summary>
public readonly record struct PolicyEvent(DateTimeOffset TimestampUtc, PolicyEventKind Kind, string Summary);

/// <summary>
/// A rolling, reviewable/exportable record of what the device-owner policy did — so a mystery block
/// (like Gemini being suspended) is diagnosable instead of silent. Bounded to the most recent
/// <see cref="MaxEvents"/> entries. Pure so recording + trimming + the export format are unit-tested;
/// the head persists it and the Admin panel reviews/shares it.
/// </summary>
public sealed record PolicyAuditLog(IReadOnlyList<PolicyEvent> Events)
{
    public const int MaxEvents = 200;

    public static PolicyAuditLog Empty { get; } = new([]);

    /// <summary>
    /// Appends events, keeping only the newest <see cref="MaxEvents"/>. Consecutive duplicates
    /// (same kind + summary as the current last entry) are collapsed, so a failure that recurs on
    /// every policy pass — or a repeated block — is recorded once, not hundreds of times.
    /// </summary>
    public PolicyAuditLog Record(IEnumerable<PolicyEvent> events)
    {
        var combined = Events.ToList();
        foreach (var next in events)
        {
            if (combined.Count > 0 && combined[^1].Kind == next.Kind && combined[^1].Summary == next.Summary)
            {
                continue;
            }
            combined.Add(next);
        }
        if (combined.Count > MaxEvents)
        {
            combined = combined.Skip(combined.Count - MaxEvents).ToList();
        }
        return new PolicyAuditLog(combined);
    }

    public PolicyAuditLog Record(PolicyEvent single) => Record([single]);

    /// <summary>
    /// The events <see cref="Record"/> would actually add — consecutive repeats collapsed against the log and
    /// within the batch — so the log loop writes a failure that recurs every pass once, as the audit does.
    /// </summary>
    public IReadOnlyList<PolicyEvent> NewlyRecorded(IEnumerable<PolicyEvent> events)
    {
        var fresh = new List<PolicyEvent>();
        PolicyEvent? last = Events.Count > 0 ? Events[^1] : null;
        foreach (var next in events)
        {
            if (last is { } previous && previous.Kind == next.Kind && previous.Summary == next.Summary)
            {
                continue;
            }
            fresh.Add(next);
            last = next;
        }
        return fresh;
    }

    /// <summary>A plain-text export, newest first, one line per event.</summary>
    public string Format() =>
        Events.Count == 0
            ? "No policy events recorded yet."
            : string.Join("\n", Events
                .OrderByDescending(e => e.TimestampUtc)
                .Select(e => $"{e.TimestampUtc:yyyy-MM-dd HH:mm:ss}Z  [{e.Kind}]  {e.Summary}"));
}

/// <summary>Flyleaf's policy lines for the log loop (LOG-LOOP-PLAN.md §4.5).</summary>
public static class PolicyLogLines
{
    public static string Line(PolicyEvent policyEvent) => $"POLICY {policyEvent.Kind}: {policyEvent.Summary}";

    /// <summary>Whether something the policy asked for did not take — those are warnings, the rest information.</summary>
    public static bool IsProblem(PolicyEventKind kind) => kind is PolicyEventKind.SuspendFailed or PolicyEventKind.ReleaseFailed
        or PolicyEventKind.GrantFailed or PolicyEventKind.ConfigFailed or PolicyEventKind.NotDeviceOwner or PolicyEventKind.UninstallFailed;
}
