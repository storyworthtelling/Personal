namespace Storyworthtelling.Voice;

// What situational message to write. Purpose names the occasion ("welcome", "book complete", "nothing to
// show yet"); Context gives the specifics the message should reflect (counts, a title, a name). MaxWords
// keeps the result to a UI-sized sentence or two — situational messaging, never a wall of text.
public sealed record MessageRequest(string Purpose, string Context = "", int MaxWords = 40);

// The model-backed half of the voice feature: learning the voice from documents, and writing messages in
// it. Implemented by a Claude client; kept an interface so apps can fake it in tests and so a keyless app
// can supply a no-op that always yields the neutral fallback.
public interface IVoiceClient
{
    // Refines the voice profile from one newly-uploaded document, folding it into the existing profile
    // (pass VoiceProfile.Empty for the first document). Returns the updated profile.
    Task<VoiceProfile> RefineAsync(VoiceProfile existing, string documentText, CancellationToken cancellationToken);

    // Writes one situational message in the owner's voice for the given request. Returns plain prose (no
    // markup) sized to the request. The profile is assumed non-empty (VoicedMessenger guards that).
    Task<string> ComposeAsync(VoiceProfile profile, MessageRequest request, CancellationToken cancellationToken);
}
