using System.Text.Json;

namespace Storyworthtelling.Voice;

/// <summary>
/// How a prompt carries the owner's voice: the profile as one labelled JSON block (audit W10.2). The compose
/// call, Plate's caption and Foreword's prayer line all send this, so a seat outside the messenger reads the
/// learned voice rather than guessing at it. Which book an example came from never leaves the phone.
/// </summary>
public static class VoiceProfilePrompt
{
    public static string Block(VoiceProfile profile) =>
        "PROFILE (JSON):\n" + JsonSerializer.Serialize(new
        {
            register = profile.Register,
            traits = profile.Traits,
            examples = profile.Examples.Select(example => example.Text),
            avoid = profile.Avoid,
            cadence = profile.Cadence,
            punctuation = profile.Punctuation,
        });
}
