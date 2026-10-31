using System.Text;
using Constitution.Domain.Voice;
using Microsoft.Extensions.Logging;
using Storyworthtelling.Voice;

namespace Constitution.Infrastructure.Voice;

/// <summary>
/// Turns a seat's facts into one sentence in the owner's voice — or into the plain sentence it came
/// with. <b>Never hidden, never blank, never an exception</b>: with no profile, no key, an unreadable
/// Drive, a failed call or a reply with nothing in it, the seat shows its fallback and the page has
/// something to say. It goes through the shared <see cref="VoicedMessenger"/> (the profile from Drive,
/// the Claude client) and adds what a seat needs that the messenger does not: the closed list's
/// purpose, the facts as the context, the key refreshed from the vault, and a result that says which
/// face to set the line in.
///
/// <para><b>Errors are never voiced, structurally.</b> No method here takes or returns an exception
/// or an error description — only a <see cref="SeatRequest"/>, which has none — so there is no way to
/// hand one a failure. A guard fails if that ever changes, and a second one fails if anything other
/// than this class builds a model request.</para>
///
/// <para><b>What reaches a log: the seat's id, and the kind of failure, and that is all.</b> Never
/// the facts, the fallback, the profile or the sentence — the profile is the owner's writing and the
/// facts are their health. A failure message is not repeated either; it is not ours to publish.</para>
///
/// <para>Cancellation is the caller leaving the screen, not a failure, and propagates. Composing at
/// most once per facts and day is C-9.2.3's, layered over this; this composes every time it is asked.</para>
/// </summary>
public sealed class SeatComposer : ISeatComposer
{
    private readonly VoicedMessenger messenger;
    private readonly DriveVoiceOptions voiceOptions;
    private readonly ILogger<SeatComposer>? logger;

    public SeatComposer(VoicedMessenger messenger, DriveVoiceOptions voiceOptions, ILogger<SeatComposer>? logger = null)
    {
        this.messenger = messenger;
        this.voiceOptions = voiceOptions;
        this.logger = logger;
    }

    /// <summary>The line for this seat: voiced if it can be, otherwise the request's plain sentence.</summary>
    public async Task<SeatLine> ComposeAsync(SeatRequest request, CancellationToken cancellationToken)
    {
        var occasion = VoicedOccasions.For(request.Seat);
        var voiced = await TryVoiceAsync(occasion, request, cancellationToken);

        return voiced is null
            ? new SeatLine(request.Seat, request.Fallback, IsVoiced: false)
            : new SeatLine(request.Seat, voiced, IsVoiced: true);
    }

    private async Task<string?> TryVoiceAsync(VoicedOccasion occasion, SeatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await voiceOptions.RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The key could not be read from the vault (offline, wrong passphrase). The plain sentence
            // shows; the log gets the seat's id and the exception's type only.
            logger?.LogWarning("Voice seat {Seat} shows its plain sentence: the voice settings could not be read ({Kind})",
                occasion.Id, ex.GetType().Name);
            return null;
        }

        if (string.IsNullOrWhiteSpace(voiceOptions.Value.ApiKey))
        {
            logger?.LogDebug("Voice seat {Seat} shows its plain sentence: no key", occasion.Id);
            return null;
        }

        // The messenger absorbs no profile, a failed call and an offline device, and logs the cause itself.
        // An empty fallback is how this tells "voiced" from "fell back": the real fallback is ours to give.
        var reply = await messenger.ComposeAsync(
            new MessageRequest(occasion.Purpose, Context(request), occasion.MaxWords, HighVisibility: true),
            fallback: string.Empty, cancellationToken);

        if (string.IsNullOrWhiteSpace(reply))
        {
            logger?.LogDebug("Voice seat {Seat} shows its plain sentence: nothing was composed", occasion.Id);
            return null;
        }

        return reply.Trim();
    }

    private static string Context(SeatRequest request)
    {
        var context = new StringBuilder("Facts, in order:");
        foreach (var fact in request.Facts)
        {
            context.Append("\n- ").Append(fact);
        }

        context.Append("\nSay only what these facts say, in a single sentence. Add no figure, name or advice that is not stated.");
        return context.ToString();
    }
}
