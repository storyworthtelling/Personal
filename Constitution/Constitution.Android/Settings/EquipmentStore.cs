using Android.Content;
using Constitution.Domain;
using Constitution.Domain.Equipment;

namespace Constitution.Settings;

/// <summary>
/// Which equipment this install has ticked, kept in the frozen settings file under the frozen
/// <see cref="ConstitutionIdentity.EquipmentOwnedKey"/>. Never stored means everything is ticked
/// (see <see cref="EquipmentSet.Decode"/>).
/// </summary>
public sealed class EquipmentStore(Context context)
{
    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(ConstitutionIdentity.SettingsPreferencesName, FileCreationMode.Private)!;

    public EquipmentSet Read() =>
        EquipmentSet.Decode(Preferences.GetString(ConstitutionIdentity.EquipmentOwnedKey, null));

    public void Write(EquipmentSet set)
    {
        using var editor = Preferences.Edit()!;
        editor.PutString(ConstitutionIdentity.EquipmentOwnedKey, set.Encode());
        editor.Apply();
    }
}
