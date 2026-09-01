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

    /// <summary>Appends events to the stored log (trimmed to the cap).</summary>
    public void Append(IEnumerable<PolicyEvent> events) => Save(Load().Record(events));

    /// <summary>Records a single event now (used by owner actions like block/unblock).</summary>
    public void Record(PolicyEventKind kind, string summary) =>
        Append([new PolicyEvent(DateTimeOffset.UtcNow, kind, summary)]);
}
