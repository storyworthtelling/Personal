using System.Text.Json;
using Android.Content;
using Codex.Domain.Apps;

namespace Flyleaf;

/// <summary>
/// Persists the timed-release windows (<see cref="TimedReleases"/>) in SharedPreferences, committed before
/// the suspension they describe is changed, so a Flyleaf that dies in between still knows on its next start
/// which services to put back. Absent or garbled loads as no windows: nothing is then released.
/// </summary>
internal sealed class TimedReleaseStore
{
    private const string WindowsKey = "windows";

    private readonly ISharedPreferences preferences;

    public TimedReleaseStore(Context context)
    {
        preferences = context.GetSharedPreferences(TimedReleaseHandoff.PreferencesName, FileCreationMode.Private)!;
    }

    public IReadOnlyDictionary<string, long> Load()
    {
        var json = preferences.GetString(WindowsKey, null);
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? new Dictionary<string, long>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }
    }

    public void Save(IReadOnlyDictionary<string, long> windows) =>
        preferences.Edit()!.PutString(WindowsKey, JsonSerializer.Serialize(windows))!.Commit();
}
