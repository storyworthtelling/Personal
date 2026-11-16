namespace Codex.Domain.Voice;

/// <summary>What the voice is asked for: the occasion, and the specifics to reflect.</summary>
public sealed record VoiceAsk(string Purpose, string Context);

/// <summary>
/// A seat whose baseline is written in code asks the voice to say that line the owner's way (audit W10.2):
/// Post's empty Mailbox and Plate's empty Darkroom. The baseline is also the fallback, so with no profile or
/// key the screen reads as it always did, and a line with no words in it (the ladder's silent last rung) is
/// shown as it is.
/// </summary>
public static class VoiceRewrite
{
    public static VoiceAsk? Ask(string occasion, string baseline)
    {
        if (!baseline.Any(char.IsLetter))
        {
            return null;
        }

        return new VoiceAsk(
            $"{occasion}; say the baseline line your own way, keeping what it means and how far it goes. "
            + "Add no facts and no instruction it does not already give.",
            $"Baseline: {baseline}");
    }
}
