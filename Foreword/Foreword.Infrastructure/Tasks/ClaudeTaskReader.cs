using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Foreword.Domain.Health;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;
using Foreword.Infrastructure.Handwriting;
using Storyworthtelling.Voice.Health;

namespace Foreword.Infrastructure.Tasks;

/// <summary>
/// Reads the owner's own sentence into a structured rule (T6 → T4).
///
/// <para>Every failure here is quiet and returns nothing. The caller keeps the sentence anyway as an
/// unparsed once task and reads it again on the next run that reaches Claude — so a rule typed on a
/// dead network becomes a rule without anyone retyping it, and a model that will not answer costs
/// the rule's structure rather than the rule.</para>
/// </summary>
public class ClaudeTaskReader(
    HttpClient httpClient,
    IOptions<AnthropicOptions> options,
    ILogger<ClaudeTaskReader> logger,
    ICredentialHealthSink? healthSink = null) : ITaskReadingClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string Instruction =
        "You turn one sentence from a personal planner into a structured task rule. Respond with " +
        "ONLY a JSON object and no prose. Fields: " +
        "{\"title\": string (short, imperative, no scheduling words — \"Mow the lawn\", not " +
        "\"Mow the lawn on Saturday\"), " +
        "\"kind\": \"rule\" (repeats on a fixed schedule) | \"smart\" (places itself in a window by " +
        "conditions) | \"once\" (happens once), " +
        "\"repeat\": \"daily\" | \"weekly\" (rule only), " +
        "\"on\": a day name (a weekly rule only), " +
        "\"window\": \"anyDay\" | \"weekend\" | \"weekdays\" (smart only), " +
        "\"prefers\": a day name or null (smart only), " +
        "\"dryHoursNeeded\": integer or null — consecutive hours without rain the task needs; null " +
        "when the weather is irrelevant to it, " +
        "\"seasonFrom\" / \"seasonTo\": month numbers 1-12, or null for all year (a season may wrap " +
        "the new year, e.g. 11 to 2), " +
        "\"ifNoDay\": \"printAnyway\" | \"skipTheWeek\" — what to do when no day in the window " +
        "qualifies; prefer printAnyway unless the sentence says the task genuinely cannot be done, " +
        "\"due\": an ISO date or null (once only). " +
        "Choose \"smart\" only when the sentence gives a window AND a condition. A sentence with a " +
        "plain schedule is a \"rule\"; one with neither is \"once\".";

    private readonly AnthropicOptions options = options.Value;

    public async Task<ForewordTask?> Read(string sentence)
    {
        // Nothing to read, and asking a model to read nothing spends a call to be told so.
        if (string.IsNullOrWhiteSpace(sentence) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return null;
        }

        var payload = new
        {
            model = options.Model,
            max_tokens = 512,
            system = Instruction,
            messages = new[] { new { role = "user", content = sentence } },
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl)
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, null))
                {
                    healthSink?.MarkFailing(
                        CredentialIds.Anthropic, $"{(int)response.StatusCode} {response.StatusCode}");
                }

                logger.LogWarning("Task reading failed: {Status}", response.StatusCode);
                return null;
            }

            healthSink?.MarkOk(CredentialIds.Anthropic);
            return Parse(await response.Content.ReadAsStringAsync(), sentence);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Task reading failed for: {Sentence}", sentence);
            return null;
        }
    }

    private static ForewordTask? Parse(string body, string sentence)
    {
        if (Text(body) is not { } text || Object(text) is not { } rule)
        {
            return null;
        }

        var title = String(rule, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var kind = String(rule, "kind").ToLowerInvariant() switch
        {
            "rule" => TaskKind.Rule,
            "smart" => TaskKind.Smart,
            _ => TaskKind.Once,
        };

        return new ForewordTask
        {
            Id = Guid.NewGuid().ToString("n"),
            Title = title,
            Kind = kind,
            Repeat = String(rule, "repeat").ToLowerInvariant() == "weekly" ? Repeat.Weekly : Repeat.Daily,
            On = Day(String(rule, "on")),
            Window = String(rule, "window").ToLowerInvariant() switch
            {
                "weekend" => TaskWindow.Weekend,
                "weekdays" => TaskWindow.Weekdays,
                _ => TaskWindow.AnyDay,
            },
            Prefers = Day(String(rule, "prefers")),
            DryHoursNeeded = Int(rule, "dryHoursNeeded"),
            Season = Season(rule),
            IfNoDay = String(rule, "ifNoDay").ToLowerInvariant() == "skiptheweek"
                ? NoDayFallback.SkipTheWeek
                : NoDayFallback.PrintAnyway,
            Due = Date(String(rule, "due")),

            // Kept whatever happens, so the rule can be read again later and so the edit screen can
            // offer "retype it in your own words" against what they actually wrote.
            Written = sentence,
        };
    }

    /// <summary>The assistant's text, out of the Messages response.</summary>
    private static string? Text(string body)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("content", out var content))
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var text) && text.GetString() is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// The JSON object inside the reply. Found rather than required: models wrap JSON in prose
    /// however firmly they are told not to, and a rule lost to a sentence of preamble is a rule the
    /// owner has to type twice.
    /// </summary>
    private static JsonElement? Object(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(text[start..(end + 1)]).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SeasonRange Season(JsonElement rule) =>
        Int(rule, "seasonFrom") is { } from && Int(rule, "seasonTo") is { } to
        && from is >= 1 and <= 12 && to is >= 1 and <= 12
            ? new SeasonRange(from, to)
            : SeasonRange.AllYear;

    private static string String(JsonElement rule, string name) =>
        rule.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int? Int(JsonElement rule, string name) =>
        rule.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static DayOfWeek? Day(string name) =>
        Enum.TryParse<DayOfWeek>(name, ignoreCase: true, out var day) ? day : null;

    private static DateOnly? Date(string value) =>
        DateOnly.TryParse(value, out var date) ? date : null;
}
