using System.Globalization;
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
/// The vision call that reads the journal page.
///
/// <para>This is one of the four declared <see cref="IPrayerReader"/>s, and the one that makes the
/// honest limit of "private" concrete: the page <b>is</b> read by a model, which was stated plainly
/// at gate 1 and accepted. What private means is that nothing from it reaches Google Tasks, the
/// calendar, the voice profile or any shared file — it goes to the encrypted archive and stops
/// there.</para>
///
/// <para><b>The two regions are told to the model, not inferred by it.</b> Foreword drew the page,
/// so it knows where the free space ends and the add strip begins (D17) — and the coordinates come
/// from <see cref="JournalRegions"/>, the same source the page was painted from. Asking a model to
/// judge whether a line "sounds like a request" would make a private thought's destination depend
/// on its wording, and the way that fails is a page printing something the owner never meant to
/// print.</para>
///
/// <para>Every failure is quiet and reads nothing, because the morning trashes the sheet once it has
/// been read: a failure that claimed to have read it would destroy the only copy.</para>
/// </summary>
public sealed class ClaudeJournalTranscriber : IJournalTranscriber, IPrayerReader
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    // US Letter portrait, which is what the prayer document is composed at.
    private const float PageWidth = 612f;
    private const float PageHeight = 792f;

    private readonly HttpClient httpClient;
    private readonly AnthropicOptions options;
    private readonly ILogger<ClaudeJournalTranscriber> logger;

    public ClaudeJournalTranscriber(
        HttpClient httpClient,
        IOptions<AnthropicOptions> options,
        ILogger<ClaudeJournalTranscriber> logger)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<JournalRead> Read(byte[] annotatedPdf, DateOnly date)
    {
        if (annotatedPdf.Length == 0)
        {
            return JournalRead.Nothing;
        }

        try
        {
            var response = await Ask(annotatedPdf);
            if (!response.IsSuccessStatusCode)
            {
                // Deliberately not reported to the credential health sink. A rejected key would
                // reveal the Anthropic field on the Settings screen, which is right — but this call
                // shares the key with the handwriting extractor, which already does that, and doing
                // it twice is two reports of one fact.
                logger.LogWarning(
                    "Journal transcription failed for {Date}: {Status}", date, (int)response.StatusCode);
                return JournalRead.Nothing;
            }

            return Parse(await response.Content.ReadAsStringAsync(), date);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal transcription failed for {Date}", date);
            return JournalRead.Nothing;
        }
    }

    private Task<HttpResponseMessage> Ask(byte[] annotatedPdf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            model = options.Model,
            max_tokens = 2048,
            system = new[]
            {
                new
                {
                    type = "text",
                    text = Instruction(),
                    cache_control = new { type = "ephemeral" },
                },
            },
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "document",
                            source = new
                            {
                                type = "base64",
                                media_type = "application/pdf",
                                data = Convert.ToBase64String(annotatedPdf),
                            },
                        },
                        new { type = "text", text = "Transcribe the hand-written ink." },
                    },
                },
            },
        });

        return httpClient.SendAsync(request);
    }

    /// <summary>
    /// The instruction, with the region boundary written into it from the region map rather than
    /// described in prose. The number is the whole point: the model is being told where the line is,
    /// not asked to find it.
    /// </summary>
    private static string Instruction()
    {
        var regions = JournalRegions.For(PageWidth, PageHeight);
        var free = regions[0];
        var adding = regions[1];

        return
            "You are reading one page of a printed journal that has been hand-written on with a "
            + "stylus. Ignore all printed text and the printed rules. Transcribe ONLY the "
            + "hand-written ink, and split it by where it sits on the page. The page is "
            + $"{(int)PageWidth} by {(int)PageHeight} points, measured from the top-left. "
            + $"The '{JournalRegions.Journalling}' region runs from y={(int)free.Top} to "
            + $"y={(int)(free.Top + free.Height)}. The '{JournalRegions.Adding}' region, headed "
            + $"\"Add to the list\", runs from y={(int)adding.Top} to "
            + $"y={(int)(adding.Top + adding.Height)} and holds up to {JournalRegions.AddLines} "
            + "separate short lines. Respond with ONLY a JSON object (no prose): "
            + $"{{\"{JournalRegions.Journalling}\": string — everything written in the upper region, "
            + "as one block with the line breaks preserved, or \"\" if nothing is written there; "
            + $"\"{JournalRegions.Adding}\": array of strings — one per written line in the lower "
            + "region, or [] if none. Do not move a line between regions on the strength of what it "
            + "says. Where it is written is what decides.";
    }

    private JournalRead Parse(string responseJson, DateOnly date)
    {
        using var document = JsonDocument.Parse(responseJson);

        var text = document.RootElement
            .GetProperty("content")
            .EnumerateArray()
            .FirstOrDefault(block =>
                block.TryGetProperty("type", out var type) && type.GetString() == "text")
            .GetProperty("text")
            .GetString() ?? string.Empty;

        // The object is found inside the reply, because a model wraps one in prose about half the
        // time and handing the whole reply to the parser turns a good answer into a failure.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            logger.LogWarning("Journal transcription for {Date} contained no JSON object", date);
            return JournalRead.Nothing;
        }

        using var parsed = JsonDocument.Parse(text[start..(end + 1)]);
        var root = parsed.RootElement;

        var journalled = root.TryGetProperty(JournalRegions.Journalling, out var free)
            && free.ValueKind == JsonValueKind.String
                ? free.GetString()
                : null;

        var added = new List<PrayerText>();
        if (root.TryGetProperty(JournalRegions.Adding, out var lines)
            && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in lines.EnumerateArray())
            {
                var written = line.ValueKind == JsonValueKind.String ? line.GetString() : null;
                if (!string.IsNullOrWhiteSpace(written))
                {
                    added.Add(PrayerText.Of(written.Trim()));
                }
            }
        }

        logger.LogInformation(
            "Journal read for {Date}: {Added} added line(s), journalling {State}",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            added.Count,
            string.IsNullOrWhiteSpace(journalled) ? "blank" : "written");

        return new JournalRead(
            Succeeded: true,
            Journalled: string.IsNullOrWhiteSpace(journalled)
                ? PrayerText.Empty
                : PrayerText.Of(journalled.Trim()),
            Added: added);
    }
}
