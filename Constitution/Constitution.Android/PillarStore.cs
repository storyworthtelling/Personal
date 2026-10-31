using Android.Content;
using Constitution.Domain;
using Constitution.Domain.Areas;

namespace Constitution;

/// <summary>
/// Which pillars this install shows, kept in the frozen settings file under the frozen keys.
/// Per install, not per household (book p.17). A pillar nobody has switched is on.
/// </summary>
public sealed class PillarStore(Context context)
{
    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(ConstitutionIdentity.SettingsPreferencesName, FileCreationMode.Private)!;

    public Pillars Read()
    {
        var preferences = Preferences;
        return new Pillars(
            Body: preferences.GetBoolean(ConstitutionIdentity.PillarBodyKey, true),
            Workouts: preferences.GetBoolean(ConstitutionIdentity.PillarWorkoutsKey, true),
            Meals: preferences.GetBoolean(ConstitutionIdentity.PillarMealsKey, true));
    }

    public void Write(Pillars pillars)
    {
        using var editor = Preferences.Edit()!;
        editor.PutBoolean(ConstitutionIdentity.PillarBodyKey, pillars.Body);
        editor.PutBoolean(ConstitutionIdentity.PillarWorkoutsKey, pillars.Workouts);
        editor.PutBoolean(ConstitutionIdentity.PillarMealsKey, pillars.Meals);
        editor.Apply();
    }
}
