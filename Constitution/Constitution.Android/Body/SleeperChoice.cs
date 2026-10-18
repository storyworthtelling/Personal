using Android.Content;
using Constitution.Domain;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Body;

/// <summary>
/// "Which side of the bed is mine" over the app's private preferences, under the frozen key
/// <see cref="ConstitutionIdentity.SleeperSideKey"/>. The household's Sleep Number login is shared, so the person is
/// not: this is the only thing that tells the bed whose night to fetch. Until it is set, the bed is not used and the
/// watch is read alone. Settings, Connections sets it (the Bed side row), and the same word is kept in the personal vault.
/// </summary>
public sealed class SleeperChoice(Context context) : ISleeperChoice
{
    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(ConstitutionIdentity.SettingsPreferencesName, FileCreationMode.Private)!;

    public BedSide? Side
    {
        get => SleeperSideCodec.Read(Preferences.GetString(ConstitutionIdentity.SleeperSideKey, null));
        set
        {
            using var editor = Preferences.Edit()!;
            editor.PutString(ConstitutionIdentity.SleeperSideKey, SleeperSideCodec.WriteChoice(value));
            editor.Apply();
        }
    }
}
