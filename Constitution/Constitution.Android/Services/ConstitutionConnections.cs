using Android.Content;
using Constitution.Domain;
using Constitution.Infrastructure.Vault;

namespace Constitution.Services;

/// <summary>
/// This install's local copy of the connections, over the app-private SharedPreferences file the
/// settings live in (<see cref="ConstitutionIdentity.SettingsPreferencesName"/>) under frozen literal
/// keys. Secrets are stored in the clear in app-private preferences, as the sibling heads do
/// (Post's <c>AccountStore</c> keeps the Gmail app password this way); the vault on Drive is the
/// off-device backup, never the source of truth on the phone. A value never entered reads as the
/// empty string, which is how "missing" is told from "set".
/// </summary>
public sealed class ConstitutionConnections(Context context) : IConstitutionConnections
{
    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(ConstitutionIdentity.SettingsPreferencesName, FileCreationMode.Private)!;

    public string SleepNumberLogin
    {
        get => Read(ConstitutionIdentity.SleepNumberLoginKey);
        set => Write(ConstitutionIdentity.SleepNumberLoginKey, value);
    }

    public string SleepNumberPassword
    {
        get => Read(ConstitutionIdentity.SleepNumberPasswordKey);
        set => Write(ConstitutionIdentity.SleepNumberPasswordKey, value);
    }

    public string RemarkableDeviceToken
    {
        get => Read(ConstitutionIdentity.RemarkableTokenKey);
        set => Write(ConstitutionIdentity.RemarkableTokenKey, value);
    }

    public string ClaudeApiKey
    {
        get => Read(ConstitutionIdentity.ClaudeKeyKey);
        set => Write(ConstitutionIdentity.ClaudeKeyKey, value);
    }

    public string GeminiApiKey
    {
        get => Read(ConstitutionIdentity.GeminiKeyKey);
        set => Write(ConstitutionIdentity.GeminiKeyKey, value);
    }

    public string HealthConnectChoicesJson
    {
        get => Read(ConstitutionIdentity.HealthConnectChoicesKey);
        set => Write(ConstitutionIdentity.HealthConnectChoicesKey, value);
    }

    public string WorkScheduleJson
    {
        get => Read(ConstitutionIdentity.WorkScheduleKey);
        set => Write(ConstitutionIdentity.WorkScheduleKey, value);
    }

    public string SleeperSide
    {
        get => Read(ConstitutionIdentity.SleeperSideKey);
        set => Write(ConstitutionIdentity.SleeperSideKey, value);
    }

    private string Read(string key) => Preferences.GetString(key, string.Empty) ?? string.Empty;

    private void Write(string key, string? value)
    {
        using var editor = Preferences.Edit()!;
        editor.PutString(key, value ?? string.Empty);
        editor.Apply();
    }
}
