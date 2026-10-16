using Android.App;
using Android.Content;
using Codex.Ui.Voice;
using Pulp.Domain.Calendar;
using Pulp.Domain.Household;

namespace Pulp.Calendar;

/// <summary>
/// Where the owner's calendar consent stands. Pulp's calendar Google account asks for only the two scopes
/// the client names (<c>GoogleCalendarClient.Scopes</c>), and Android shows the consent screen only when a
/// token is first asked for with them — so "connected" is a flag this class keeps once that consent has been
/// given (<see cref="ConnectAsync"/>), cleared when Google later refuses the token. Until it is set the
/// nightly writer is handed the "waits" role and never asks Google at all.
///
/// <para>The words come from <see cref="CalendarScreenConnection"/>, so Settings and the Vault page cannot
/// each invent their own. <b>The consent button belongs on the Vault page</b> (see the report): it calls
/// <see cref="ConnectAsync"/> from its activity and shows <see cref="Status"/> beside it.</para>
/// </summary>
public sealed class CalendarConnection(VoiceGoogleAccount account)
{
    private const string PrefsName = "pulp-calendar";
    private const string ConnectedKey = "connected";
    private const string UnsharedKey = "unshared";

    public bool Connected => Prefs().GetBoolean(ConnectedKey, false);

    /// <summary>"Calendar not connected yet" until the owner has consented; the other phone's and unknown roles say their own.</summary>
    public string Status(HouseholdFileRole role) => CalendarScreenConnection.Sentence(role, Connected, Unshared());

    /// <summary>Keeps tonight's refused addresses for the status sentence (D359); an empty list clears an old problem.</summary>
    public void NoteUnshared(IReadOnlyList<string> accounts)
    {
        var editor = Prefs().Edit()!;
        editor.PutString(UnsharedKey, string.Join('\n', accounts));
        editor.Apply();
    }

    private static IReadOnlyList<string> Unshared() =>
        (Prefs().GetString(UnsharedKey, string.Empty) ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether to draw the consent button: the owner's phone, not yet connected.</summary>
    public bool OffersConnect(HouseholdFileRole role) => CalendarScreenConnection.OffersConnect(role, Connected);

    /// <summary>
    /// Asks Google for the calendar scopes through the account chooser and the consent screen, and keeps
    /// the flag only if that finished. Throws what the sign-in throws (a cancelled chooser, a refused
    /// consent) and leaves the flag as it was, so the caller can say so on its page.
    /// </summary>
    public async Task ConnectAsync(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var stored = VoiceGoogleAccount.Email;
        var chosen = await account.SignIn(activity);

        // The sign-in keeps its pick in the one stored Google account Drive reads (the vault, the voice
        // profile), so a different pick would repoint them all. Put the stored one back and say so.
        if (CalendarScreenConnection.JudgeAccount(stored, chosen) == CalendarAccountVerdict.Refuse)
        {
            RestoreDriveAccount(stored);
            throw new CalendarAccountRefusedException(CalendarScreenConnection.DifferentAccount(stored));
        }

        Set(true);
    }

    /// <summary>Google refused the token: the consent was withdrawn or never reached this account.</summary>
    public void Disconnected() => Set(false);

    // The account's own preference, which Codex.Ui.Android keeps private; named here rather than edited there.
    private static void RestoreDriveAccount(string email)
    {
        var editor = Application.Context.GetSharedPreferences("voice", FileCreationMode.Private)!.Edit()!;
        editor.PutString("googleAccountEmail", email);
        editor.Apply();
    }

    private static ISharedPreferences Prefs() =>
        Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    private static void Set(bool connected)
    {
        var editor = Prefs().Edit()!;
        editor.PutBoolean(ConnectedKey, connected);
        editor.Apply();
    }
}

/// <summary>The chosen Google account is not the one Drive already uses; the message is a sentence for the owner.</summary>
public sealed class CalendarAccountRefusedException : InvalidOperationException
{
    public CalendarAccountRefusedException(string message) : base(message)
    {
    }
}
