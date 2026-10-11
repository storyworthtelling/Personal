using Android.Content;
using Codex.Domain.Apps;
using Codex.Domain.Policy;

namespace Flyleaf;

/// <summary>
/// Persists the locked hand-off that is running (D297): a thin SharedPreferences adapter over
/// <see cref="LockedSessionCodec"/>. It is the crash-safety of the whole feature, written before
/// anything is changed and cleared only after everything is put back, so a Flyleaf that dies in
/// between still knows on its next start what to undo. Absent or garbled loads as no record.
/// </summary>
internal sealed class LockedSessionStore
{
    private const string RecordKey = "record";

    private readonly ISharedPreferences preferences;

    public LockedSessionStore(Context context)
    {
        preferences = context.GetSharedPreferences(LockedOpenHandoff.PreferencesName, FileCreationMode.Private)!;
    }

    public LockedSessionRecord? Load() =>
        LockedSessionCodec.TryDecode(preferences.GetString(RecordKey, null), out var record) ? record : null;

    /// <summary>Committed, not applied: the record must be on disk before the policy it describes is changed.</summary>
    public void Save(LockedSessionRecord record) =>
        preferences.Edit()!.PutString(RecordKey, LockedSessionCodec.Encode(record))!.Commit();

    public void Clear() => preferences.Edit()!.Remove(RecordKey)!.Commit();
}
