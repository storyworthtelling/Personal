namespace Storyworthtelling.Voice;

// A distilled description of the owner's writing voice, learned from the documents they upload and used
// to generate situational app messages that sound like them. Kept compact so the whole profile can ride
// in every message-generation prompt. Refined as more documents are analyzed (IVoiceClient.RefineAsync),
// and stored as JSON in the owner's Drive so every com.storyworthtelling app shares one voice.
public sealed record VoiceProfile(
    // A short paragraph describing the voice: tone, register, attitude, cadence.
    string Summary,
    // Short descriptors ("wry", "warm", "economical", "plain-spoken").
    IReadOnlyList<string> Traits,
    // Characteristic phrasings drawn from the owner's own writing, to anchor the voice concretely. These
    // are STICKY: each refine adds a couple from the new document and never drops the ones already learned,
    // so the concrete anchors only accumulate as more books feed the profile.
    IReadOnlyList<string> Examples,
    // Things that would read as not-them ("corporate boilerplate", "exclamation marks", "hype").
    IReadOnlyList<string> Avoid,
    // Recurring structural habits — sentence shapes, favored transitions, punctuation tics ("em-dash
    // asides", "short declarative openers", "lists of three"). The "how", where Traits are the "what".
    IReadOnlyList<string> SignatureMoves,
    // How many documents have fed this profile — provenance, and a signal of how settled the voice is.
    int DocumentsAnalyzed)
{
    public static VoiceProfile Empty { get; } = new(string.Empty, [], [], [], [], 0);

    // Nothing has been learned yet, so there's no voice to write in — the caller should fall back to its
    // neutral copy.
    public bool IsEmpty => DocumentsAnalyzed <= 0 || string.IsNullOrWhiteSpace(Summary);
}
