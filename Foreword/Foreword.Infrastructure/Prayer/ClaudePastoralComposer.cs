using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Claude;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Handwriting;

namespace Foreword.Infrastructure.Prayer;

/// <summary>
/// The prayer page's own line, in the owner's hand at the top of P1.
///
/// <para>One of the declared <see cref="IPrayerReader"/>s, and the only <i>composer</i> among
/// them. It writes in the owner's learned voice (audit W10.2) but has its own system prompt and its own
/// call rather than borrowing the shared messenger: the voice profile is a shared file every app reads,
/// so a composer that could write it would be a path from the journal into something shared. It is
/// handed the read half of the store only, and a guard reads this file for any way to write it back.</para>
///
/// <para>Failure leaves the page with no line, rather than a neutral sentence pretending to be one
/// — the same rule the rest of the morning's voice follows.</para>
/// </summary>
public sealed class ClaudePastoralComposer : IPastoralComposer, IPrayerReader
{
    private const string Instruction =
        "You write one short line at the top of a private prayer page, in the voice of the person "
        + "whose page it is, described by the PROFILE that follows — the way they talk to themselves. It "
        + "introduces the list below it. Never more than one sentence. Never a prayer, never "
        + "advice, never scripture, never encouragement of the greeting-card kind. Do not name "
        + "anybody on the list. Respond with the line and nothing else.";

    private readonly ClaudeMessagesClient claude;
    private readonly AnthropicOptions options;
    private readonly IVoiceProfileSource voice;
    private readonly ILogger<ClaudePastoralComposer> logger;

    public ClaudePastoralComposer(
        HttpClient httpClient,
        IOptions<AnthropicOptions> options,
        IVoiceProfileSource voice,
        ILogger<ClaudePastoralComposer> logger,
        ICredentialHealthSink? healthSink = null)
    {
        claude = new ClaudeMessagesClient(httpClient, () => options.Value.ApiKey, healthSink);
        this.options = options.Value;
        this.voice = voice;
        this.logger = logger;
    }

    public async Task<PrayerText> Compose(PastoralBrief brief, CancellationToken cancellationToken)
    {
        if (!brief.HasSomethingToSay)
        {
            return PrayerText.Empty;
        }

        try
        {
            // No learned voice, no line: the same rule every seat follows.
            var profile = await voice.LoadAsync(cancellationToken);
            if (profile.IsEmpty)
            {
                return PrayerText.Empty;
            }

            // The instruction and the voice lead, cached together; the morning's situation follows.
            var system = new[]
            {
                ClaudeBlock.Text(Instruction),
                ClaudeBlock.Text(VoiceProfilePrompt.Block(profile), cache: true),
            };
            var reply = await claude.SendAsync(
                new ClaudeRequest(options.Model, 200, system, [ClaudeBlock.Text(Situation(brief))]),
                cancellationToken);
            return FirstLine(reply.Text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pastoral line not composed");
            return PrayerText.Empty;
        }
    }

    private string Situation(PastoralBrief brief)
    {
        var said = new List<string>
        {
            brief.Held == 1 ? "One thing is being held." : $"{brief.Held} things are being held.",
        };

        if (brief.AnsweredThisMorning > 0)
        {
            said.Add(brief.AnsweredThisMorning == 1
                ? "One of them has been answered and comes off the list this morning."
                : $"{brief.AnsweredThisMorning} of them have been answered and come off the list this morning.");
        }

        if (!string.IsNullOrWhiteSpace(brief.LongestHeld))
        {
            said.Add($"The one held longest has been {brief.LongestHeld}.");
        }

        if (!brief.MostRecentEntry.IsEmpty)
        {
            said.Add($"The last thing written in the journal was: {brief.MostRecentEntry.ReadBy(this)}");
        }

        return string.Join(" ", said);
    }

    /// <summary>
    /// One line, whatever came back. The page has room for a sentence, and a paragraph in the
    /// calligraphy hand at Title size would run over the list it is meant to introduce.
    /// </summary>
    private static PrayerText FirstLine(string text)
    {

        var line = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(line) ? PrayerText.Empty : PrayerText.Of(line);
    }
}
