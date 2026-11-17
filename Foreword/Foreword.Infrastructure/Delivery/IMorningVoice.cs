using Storyworthtelling.CallSheet;
using Storyworthtelling.Voice;

namespace Foreword.Infrastructure.Delivery;

/// <summary>
/// The morning's printed lines in the owner's voice (audit W10.4): the brief's opening, the day's narrator and the
/// checklist's coach, each a seat on the suite's voice-line Call Sheet told only its facts in words. An empty string
/// when there is no voice, and the page simply has no line. Takes strings only, so nothing from the prayer page can
/// reach it.
/// </summary>
public interface IMorningVoice
{
    Task<string> LineAsync(string seat, IReadOnlyList<string> facts, CancellationToken cancellationToken);
}

/// <summary>The morning's lines through the shared <see cref="SeatVoice"/>: the suite's one rule, kept across a restart.</summary>
public sealed class SeatMorningVoice(SeatVoice voice) : IMorningVoice
{
    public Task<string> LineAsync(string seat, IReadOnlyList<string> facts, CancellationToken cancellationToken) =>
        voice.ComposeAsync(CallSheet.For("Foreword", seat), facts, cancellationToken);
}
