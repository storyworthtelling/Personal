using Microsoft.Extensions.Logging;
using Index.Domain.Time;
using Index.Domain.Voice;
using Storyworthtelling.CallSheet;
using Storyworthtelling.Voice;

namespace Index.Infrastructure.Voice;

/// <summary>
/// Turns a seat's facts into one sentence in the owner's voice, or into nothing — and "nothing" is a hidden
/// line, never a blank gap, an error or a wait. It goes through the shared <see cref="VoicedMessenger"/>
/// (the profile from Drive, the Claude client, the neutral fallback that never throws), and adds the
/// three things a seat needs that the messenger does not:
///
/// <list type="bullet">
/// <item>It checks, before any call, that there is something to say it with — facts, a signed-in account and
/// a key. A missing key is a hidden line, not a rejected request.</item>
/// <item>It composes by the suite's one rule (<see cref="SeatPlan"/>, audit W10.3): at most once per (seat, facts,
/// day), kept across a restart in the seat memory file. The pages ask on every appearance, every side switch and
/// every re-render; the line only has anything new to say when the facts change or the day turns. An empty
/// attempt is believed for <see cref="SeatPlan.RetryAfter"/> so a tab switch straight after one is not a retry
/// storm, and then tried again — a transient outage must not hide the line until tomorrow.</item>
/// <item>It checks the reply (<see cref="VoiceReplyCheck"/>) before returning it.</item>
/// </list>
///
/// <para><b>What reaches a log: the seat's name, and that is all.</b> Never the facts, the request, the
/// profile or the sentence — the profile is the owner's writing and the facts are their finances.</para>
///
/// <para>Deliberately no <c>ConfigureAwait(false)</c>: the page awaits this from the UI thread and updates
/// its label on return.</para>
/// </summary>
public sealed class VoiceSeatComposer
{
    private readonly VoicedMessenger messenger;
    private readonly DriveVoiceOptions voiceOptions;
    private readonly IClock clock;
    private readonly Func<bool> isSignedIn;
    private readonly SeatMemoryFile memory;
    private readonly ILogger<VoiceSeatComposer>? logger;

    private readonly SemaphoreSlim gate = new(1, 1);

    public VoiceSeatComposer(
        VoicedMessenger messenger, DriveVoiceOptions voiceOptions, IClock clock, Func<bool> isSignedIn,
        SeatMemoryFile memory, ILogger<VoiceSeatComposer>? logger = null)
    {
        this.memory = memory;
        this.messenger = messenger;
        this.voiceOptions = voiceOptions;
        this.clock = clock;
        this.isSignedIn = isSignedIn;
        this.logger = logger;
    }

    /// <summary>The sentence for this seat, or null when the seat should stay hidden. Never throws.</summary>
    public async Task<string?> ComposeAsync(VoiceFacts facts, CancellationToken cancellationToken)
    {
        var prompt = VoiceSeatPrompts.Build(facts);
        if (prompt is null)
        {
            return null;
        }

        if (!isSignedIn())
        {
            logger?.LogDebug("Voice seat {Seat} stays hidden: no signed-in account", facts.Seat);
            return null;
        }

        var line = VoicedOccasions.Line(facts.Seat);
        var now = clock.UtcNow;
        var today = SeatPlan.Today(now.LocalDateTime);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var remembered = memory.Get(line, facts.Hash);
            switch (SeatPlan.Decide(line, voiceReady: true, facts.Hash, hasFacts: true, remembered, now, today))
            {
                case SeatAction.Reuse:
                    return remembered!.Line;
                case SeatAction.Fallback:
                    return null;
            }

            var composed = await ComposeUncachedAsync(facts, prompt, cancellationToken);
            memory.Put(line, facts.Hash, SeatPlan.Remember(facts.Hash, today, composed, now));
            return composed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string?> ComposeUncachedAsync(
        VoiceFacts facts, VoiceSeatPrompt prompt, CancellationToken cancellationToken)
    {
        try
        {
            await voiceOptions.RefreshAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(voiceOptions.Value.ApiKey))
            {
                logger?.LogDebug("Voice seat {Seat} stays hidden: no key", facts.Seat);
                return null;
            }

            var reply = await messenger.ComposeAsync(
                new MessageRequest(prompt.Occasion, prompt.Context, prompt.MaxWords, VoicedOccasions.Line(facts.Seat).HighVisibility, VoicedOccasions.Line(facts.Seat).Seat),
                fallback: string.Empty, cancellationToken);

            var line = VoiceReplyCheck.Accept(facts.Seat, reply);
            if (line is null)
            {
                // No profile, a failed call and a reply set aside all arrive here; the messenger has already
                // absorbed the cause and nothing about it belongs in a log.
                logger?.LogDebug("Voice seat {Seat} has nothing to show", facts.Seat);
            }

            return line;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The key could not be read from the vault (offline, wrong passphrase). A hidden line, noted by
            // the seat's name and the exception's type only — its message is not ours to repeat.
            logger?.LogWarning("Voice seat {Seat} stays hidden: the voice settings could not be read ({Kind})",
                facts.Seat, ex.GetType().Name);
            return null;
        }
    }
}
