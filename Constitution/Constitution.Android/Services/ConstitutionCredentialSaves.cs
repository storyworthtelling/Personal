using Android.Content;
using Constitution.Domain;
using Constitution.Domain.Credentials;

namespace Constitution.Services;

/// <summary>
/// When each hand-entered credential was last saved here, kept beside the connections in the app's
/// private preferences under the frozen keys of <see cref="CredentialSaves"/>. A health judgement
/// recorded before this moment describes the credential that was replaced
/// (<c>ConnectionNotices.Judge</c>).
/// </summary>
public sealed class ConstitutionCredentialSaves(Context context)
{
    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(ConstitutionIdentity.SettingsPreferencesName, FileCreationMode.Private)!;

    /// <summary>When this credential was last saved by hand, or null when it never was (or has no such key).</summary>
    public DateTimeOffset? SavedAt(string credentialId)
    {
        var key = CredentialSaves.KeyFor(credentialId);
        if (key is null)
        {
            return null;
        }

        var stored = Preferences.GetString(key, string.Empty);
        return long.TryParse(stored, out var ticks) ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    /// <summary>Records that this credential was just saved.</summary>
    public void MarkSaved(string credentialId, DateTimeOffset now)
    {
        var key = CredentialSaves.KeyFor(credentialId)
            ?? throw new ArgumentException($"'{credentialId}' is not a credential saved by hand here.", nameof(credentialId));
        using var editor = Preferences.Edit()!;
        editor.PutString(key, now.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        editor.Apply();
    }
}
