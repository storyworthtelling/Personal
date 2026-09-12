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
/// <para>One of the four declared <see cref="IPrayerReader"/>s, and the only <i>composer</i> among
/// them. It has its own system prompt and its own call rather than borrowing the shared voice stack,
/// and that is the point: the voice profile is a shared file every app reads, so a composer that
/// went through it would be a path from the journal into something shared. This one touches neither
/// <c>IVoiceProfileStore</c> nor <c>VoicedMessenger</c>, which a guard asserts by reading this
/// file.</para>
///
/// <para>Failure leaves the page with no line, rather than a neutral sentence pretending to be one
/// — the same rule the rest of the morning's voice follows.</para>
/// </summary>
public sealed class ClaudePastoralComposer : IPastoralComposer, IPrayerReader
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string Instruction =
        "You write one short line at the top of a private prayer page, in the voice of the person "
        + "whose page it is — plain, unsentimental, the way somebody talks to themselves. It "
        + "introduces the list below it. Never more than one sentence. Never a prayer, never "
        + "advice, never scripture, never encouragement of the greeting-card kind. Do not name "
        + "anybody on the list. Respond with the line and nothing else.";

    private readonly HttpClient httpClient;
    private readonly AnthropicOptions options;
    private readonly ILogger<ClaudePastoralComposer> logger;

    public ClaudePastoralComposer(
        HttpClient httpClient,
        IOptions<AnthropicOptions> options,
        ILogger<ClaudePastoralComposer> logger)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
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
            using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
            request.Headers.Add("x-api-key", options.ApiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(new
            {
                model = options.Model,
                max_tokens = 200,
                system = new[]
                {
                    new
                    {
                        type = "text",
                        text = Instruction,
                        cache_control = new { type = "ephemeral" },
                    },
                },
                messages = new[]
                {
                    new { role = "user", content = Situation(brief) },
                },
            });

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Pastoral line not composed: {Status}", (int)response.StatusCode);
                return PrayerText.Empty;
            }

            return FirstLine(await response.Content.ReadAsStringAsync(cancellationToken));
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
    private static PrayerText FirstLine(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);

        var blocks = document.RootElement.GetProperty("content").EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
            .ToArray();

        if (blocks.Length == 0)
        {
            return PrayerText.Empty;
        }

        var text = blocks[0].GetProperty("text").GetString() ?? string.Empty;

        var line = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(line) ? PrayerText.Empty : PrayerText.Of(line);
    }
}
