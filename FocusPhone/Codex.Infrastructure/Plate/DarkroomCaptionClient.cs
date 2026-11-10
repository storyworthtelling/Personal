using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Claude;
using System.Text;
using System.Text.Json;
using Codex.Domain.Plate;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;

namespace Codex.Infrastructure.Plate;

/// <summary>Writes the line that sits over the pictures waiting in the Darkroom.</summary>
public interface IDarkroomCaptionClient
{
    /// <summary>
    /// One short line in the owner's voice about what is waiting, or null when there is no
    /// key, no answer, or anything at all goes wrong.
    /// </summary>
    Task<string?> CaptionAsync(IReadOnlyList<byte[]> jpegs, CancellationToken cancellationToken);
}

/// <summary>
/// The Claude-backed caption. Raw HTTP against the Messages API, like every other Claude
/// client in this suite — <c>ShelfGlyphClient</c>, <c>TasteProfileClient</c>,
/// <c>DiscoveryClient</c>, <c>MailReadingClient</c> and <c>VoiceClient</c> all do the same,
/// and one of six switching to the SDK would be the inconsistency rather than the fix.
///
/// <para>It reuses the shared vault key and the cheap model tier from
/// <see cref="VoiceOptions"/>: this is a small, frequent call against a fixed voice, which is
/// exactly what that tier is for.</para>
///
/// <para><b>Self-degrading, like the glyph client.</b> No key, an HTTP failure, or an empty
/// reply all return null, and the Darkroom simply shows no line. A caption is a pleasant
/// extra on a screen full of the owner's own pictures; it must never be the reason the room
/// does not open.</para>
///
/// <para><b>It cannot name anyone, by design and by policy.</b> Claude refuses to identify
/// people in images, so the line describes what is there rather than who — which is why
/// recognising a face has to happen on the phone instead.</para>
/// </summary>
public sealed class DarkroomCaptionClient : IDarkroomCaptionClient
{

    /// <summary>
    /// Enough to say what they have in common, and few enough to keep the request small: the
    /// pictures are sent as base64 in the body, so every extra one is real bytes on a phone
    /// radio.
    /// </summary>
    public const int MostPictures = 4;

    private const string Instruction =
        "You write one short line for a photographer looking at their own unsorted pictures, "
        + "in their voice: dry, warm, never instructional. Say something true about what is in "
        + "front of them. Do not name or identify any person. Do not tell them what to do with "
        + "the pictures. Reply with the line only — no quotes, no preamble, at most fifteen words.";

    private readonly ClaudeMessagesClient claude;
    private readonly IOptions<VoiceOptions> optionsAccessor;

    private readonly Action<string>? log;

    /// <param name="log">One line per failed call (audit 08 §23): the call, the failure kind, status and request id.</param>
    public DarkroomCaptionClient(
        HttpClient httpClient, IOptions<VoiceOptions> options, ICredentialHealthSink? health = null, Action<string>? log = null)
    {
        this.log = log;
        claude = new ClaudeMessagesClient(httpClient, () => options.Value.ApiKey, health);
        optionsAccessor = options;
    }

    public async Task<string?> CaptionAsync(
        IReadOnlyList<byte[]> jpegs, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || jpegs.Count == 0)
        {
            return null;
        }

        var pictures = jpegs.Take(MostPictures).ToList();

        // The pictures come before the words: Claude reads an image-then-text request best.
        var user = pictures.Select(jpeg => ClaudeBlock.Image("image/jpeg", Convert.ToBase64String(jpeg)))
            .Append(ClaudeBlock.Text(DarkroomVoice.CaptionPrompt(pictures.Count)))
            .ToList();

        try
        {
            var reply = await claude.SendAsync(
                new ClaudeRequest(options.FastModel, options.ComposeMaxTokens, [ClaudeBlock.Text(Instruction)], user),
                cancellationToken);
            var line = reply.Text.Trim().Trim('"');
            return line.Length > 0 ? line : null;
        }
        catch (Exception e)
        {
            // Every failure is the same failure here: no line. A caption is a pleasant extra
            // on a screen full of the owner's own pictures, never a reason the room fails.
            log?.Invoke(ClaudeCallLine.Failed("darkroom-caption", e));
            return null;
        }
    }
}
