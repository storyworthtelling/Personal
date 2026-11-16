namespace Codex.Domain.Voice;

/// <summary>What the voice is asked for: the seat's line on the Call Sheet, and the specifics to reflect.</summary>
public sealed record VoiceAsk(Storyworthtelling.CallSheet.VoiceLine Line, string Context)
{
    public string Purpose => Line.Purpose;
}

/// <summary>
/// A seat whose baseline is written in code asks the voice to say that line the owner's way (audit W10.2):
/// Post's empty Mailbox and Plate's empty Darkroom. What the voice is told is the seat's entry on the suite's
/// voice-line Call Sheet (audit W10.3). The baseline is also the fallback, so with no profile or key the screen
/// reads as it always did, and a line with no words in it (the ladder's silent last rung) is shown as it is.
/// </summary>
public static class VoiceRewrite
{
    public static VoiceAsk? Ask(Storyworthtelling.CallSheet.VoiceLine line, string baseline) =>
        baseline.Any(char.IsLetter) ? new VoiceAsk(line, $"Baseline: {baseline}") : null;
}
