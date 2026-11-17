using System.Text;
using Storyworthtelling.CallSheet;

namespace Storyworthtelling.Voice;

/// <summary>
/// One seat on the suite's voice-line Call Sheet, written in the owner's voice by the suite's one rule
/// (<see cref="SeatPlan"/>, audit W10.3): the sheet's purpose, word limit and tier; the seat's facts, in order, as the
/// context; once per seat, facts and day, kept across a restart in the seat memory file; an empty attempt believed for
/// <see cref="SeatPlan.RetryAfter"/>. For a head with no seat composer of its own (Foreword's welcome and its three
/// printed lines). Never throws: no profile, no key, a key that cannot be read or a failed call is an empty line, and
/// the seat hides.
/// </summary>
public sealed class SeatVoice(
    VoicedMessenger messenger,
    Func<CancellationToken, Task> refreshKey,
    SeatMemoryFile memory,
    Func<DateTimeOffset>? clock = null)
{
    public async Task<string> ComposeAsync(
        VoiceLine line, IReadOnlyList<string> facts, CancellationToken cancellationToken, string subject = "")
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(facts);
        var now = (clock ?? (() => DateTimeOffset.Now))();
        var today = SeatPlan.Today(now.LocalDateTime);
        var hash = SeatPlan.Hash(line, facts);
        var remembered = memory.Get(line, subject);
        switch (SeatPlan.Decide(line, voiceReady: true, hash, hasFacts: facts.Count > 0, remembered, now, today))
        {
            case SeatAction.Reuse:
                return remembered!.Line!;
            case SeatAction.Fallback:
                return string.Empty;
        }

        var composed = await ComposeNowAsync(line, facts, cancellationToken);
        memory.Put(line, subject, SeatPlan.Remember(hash, today, composed, now));
        return composed;
    }

    private async Task<string> ComposeNowAsync(VoiceLine line, IReadOnlyList<string> facts, CancellationToken cancellationToken)
    {
        try
        {
            await refreshKey(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The key could not be read (offline, a wrong passphrase): the seat hides, and is asked again soon.
            return string.Empty;
        }

        return await messenger.ComposeAsync(
            new MessageRequest(line.Purpose, Context(line, facts), line.MaxWords, line.HighVisibility, line.Seat),
            fallback: string.Empty, cancellationToken);
    }

    private static string Context(VoiceLine line, IReadOnlyList<string> facts)
    {
        var context = new StringBuilder();
        if (line.Guidance is { } guidance)
        {
            context.Append(guidance).Append("\n\n");
        }

        context.Append("Facts, in order:");
        foreach (var fact in facts)
        {
            context.Append("\n- ").Append(fact);
        }

        context.Append("\nSay only what these facts say. Add no figure, name or claim that isn’t stated.");
        return context.ToString();
    }
}
