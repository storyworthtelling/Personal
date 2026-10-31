using Codex.Ui.Voice;

namespace Constitution.Services;

/// <summary>
/// The one place the head builds its Google account. The account already carries the shared Drive
/// scope the vault needs; Constitution adds the full calendar scope the plan names, because it
/// places workouts and cook blocks on the owner's calendars. Every screen that signs in goes
/// through here so a token obtained on one screen covers the calls made from another.
/// </summary>
public static class ConstitutionGoogle
{
    public static VoiceGoogleAccount Create() => new VoiceGoogleAccount("https://www.googleapis.com/auth/calendar");
}
