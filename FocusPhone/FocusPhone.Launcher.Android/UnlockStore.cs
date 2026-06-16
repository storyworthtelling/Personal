using System.Text.Json;
using Android.Content;
using FocusPhone.Domain.Unlock;

namespace FocusPhone.Launcher;

/// <summary>
/// Persists the unlock state machine in device-protected storage so the
/// countdown is available before first unlock after a reboot and cannot be
/// lost to process death.
/// </summary>
public class UnlockStore
{
    private const string PreferencesName = "unlock";
    private const string DataKey = "unlockData";

    private readonly ISharedPreferences preferences;

    public UnlockStore(Context context)
    {
        var deviceProtected = context.CreateDeviceProtectedStorageContext()!;
        preferences = deviceProtected.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public UnlockStateMachine LoadMachine(IClock clock)
    {
        var json = preferences.GetString(DataKey, null);
        var data = json is null ? null : JsonSerializer.Deserialize<UnlockData>(json);
        return new UnlockStateMachine(clock, data);
    }

    public void Save(UnlockStateMachine machine)
    {
        preferences.Edit()!
            .PutString(DataKey, JsonSerializer.Serialize(machine.Export()))!
            .Apply();
    }
}

public class SystemClockAdapter : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
