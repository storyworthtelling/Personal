using Microsoft.Extensions.Logging;
using Index.Domain.Time;
using Index.Domain.Voice;
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
/// <item>It composes at most once per (seat, facts, day). The pages ask on every appearance, every side
/// switch and every re-render; the line only has anything new to say when the facts change or the day
/// turns, which is the rate Folio's desk line is composed at for the same reason. A failure is remembered
/// for <see cref="FailureMemoryMinutes"/> so a tab switch straight after one is not a retry storm, and then
/// tried again — a transient outage must not hide the line until tomorrow.</item>
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
    public const int FailureMemoryMinutes = 10;

    private readonly VoicedMessenger messenger;
    private readonly DriveVoiceOptions voiceOptions;
    private readonly IClock clock;
    private readonly Func<bool> isSignedIn;
    private readonly ILogger<VoiceSeatComposer>? logger;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Remembered> remembered = [];

    public VoiceSeatComposer(
        VoicedMessenger messenger, DriveVoiceOptions voiceOptions, IClock clock, Func<bool> isSignedIn,
        ILogger<VoiceSeatComposer>? logger = null)
    {
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

        var key = $"{facts.Seat}|{facts.Hash}|{Today()}";
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (remembered.TryGetValue(key, out var seen) && seen.StillStands(clock.UtcNow))
            {
                return seen.Line;
            }

            var line = await ComposeUncachedAsync(facts, prompt, cancellationToken);
            remembered[key] = line is null
                ? Remembered.Failed(clock.UtcNow.AddMinutes(FailureMemoryMinutes))
                : Remembered.Composed(line);
            return line;
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
                new MessageRequest(prompt.Occasion, prompt.Context, prompt.MaxWords, HighVisibility: true),
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

    private string Today() => DateOnly.FromDateTime(clock.UtcNow.LocalDateTime).ToString("yyyy-MM-dd");

    private readonly record struct Remembered(string? Line, DateTimeOffset? RetryAfter)
    {
        public static Remembered Composed(string line) => new(line, null);

        public static Remembered Failed(DateTimeOffset retryAfter) => new(null, retryAfter);

        public bool StillStands(DateTimeOffset now) => Line is not null || now < RetryAfter;
    }
}
