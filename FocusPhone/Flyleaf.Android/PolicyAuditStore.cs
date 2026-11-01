using Codex.Ui;
using Android.Content;
using Codex.Domain.Policy;
using Codex.Infrastructure.Policy;

namespace Flyleaf;

/// <summary>
/// Persists the rolling <see cref="PolicyAuditLog"/> — what the device-owner policy did and any
/// failures — so the Admin panel can review/export it and a silent block (like Gemini) is
/// diagnosable. A thin SharedPreferences adapter over <see cref="PolicyAuditLogCodec"/>; the
/// pure recording/trim/format logic is unit-tested. The preferences file name is a storage key.
/// </summary>
public class PolicyAuditStore
{
    private const string PreferencesName = "policyAudit";
    private const string LogKey = "log"; // JSON array of policy events

    private readonly ISharedPreferences preferences;

    public PolicyAuditStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public PolicyAuditLog Load() => PolicyAuditLogCodec.Decode(preferences.GetString(LogKey, null));

    public void Save(PolicyAuditLog log) =>
        preferences.Edit()!.PutString(LogKey, PolicyAuditLogCodec.Encode(log))!.Apply();

    /// <summary>
    /// Appends events to the stored log (trimmed to the cap), and writes each one the audit actually records
    /// to the log loop's file (LOG-LOOP-PLAN.md §4.5) — repeats collapsed the same way, so a failure that recurs
    /// on every policy pass is one line, not hundreds.
    /// </summary>
    public void Append(IEnumerable<PolicyEvent> events)
    {
        var batch = events.ToList();
        var log = Load();
        foreach (var fresh in log.NewlyRecorded(batch))
        {
            if (PolicyLogLines.IsProblem(fresh.Kind))
            {
                HeadDiagnostics.Current?.Warn("Policy", PolicyLogLines.Line(fresh));
            }
            else
            {
                HeadDiagnostics.Current?.Info("Policy", PolicyLogLines.Line(fresh));
            }
        }
        Save(log.Record(batch));
    }

    /// <summary>Records a single event now (used by owner actions like block/unblock).</summary>
    public void Record(PolicyEventKind kind, string summary) =>
        Append([new PolicyEvent(DateTimeOffset.UtcNow, kind, summary)]);
}
