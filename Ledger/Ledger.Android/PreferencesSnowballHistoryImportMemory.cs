using Index.Domain.Model;

namespace Index.Android;

// The device-side home of "the recorded history is already on this phone": the local settings, under the
// frozen "SnowballHistoryImported" key. A thin adapter so the decision stays in Index.Infrastructure,
// where it is tested.
public sealed class PreferencesSnowballHistoryImportMemory : ISnowballHistoryImportMemory
{
    public bool Imported => SettingsStore.SnowballHistoryImported;

    public void MarkImported() => SettingsStore.SnowballHistoryImported = true;
}
