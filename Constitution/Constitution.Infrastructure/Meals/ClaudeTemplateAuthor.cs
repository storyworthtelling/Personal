using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Workouts;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// New rotating breakfast, lunch and lunchbox lines "proposed by Claude" (D110), asked of the text model over the Messages API. One request:
/// the fixed instruction (<see cref="SystemPrompt"/>) and one user message built from a <see cref="TemplateRequest"/> and nothing else. The
/// key is one request header and the model is the shared Claude option; both are read from the options when the call is made, so a key
/// restored from the vault since the last call takes effect on the next.
///
/// <para><b>What leaves the phone:</b> words. The slots that need lines, how many, the household's approved, turned-down and waiting lines
/// (each a slot word and a line of up to forty characters) and its lasting cuisine and ingredient tastes as words, every list capped. No
/// name, no written reason, no id, no date, no Body reading and no recipe title is added here, and <see cref="TemplateRequest"/> carries none
/// (D98). Nothing is stored on this object.</para>
///
/// <para><b>It never guesses and it never decides.</b> No key is <see cref="AuthorUnavailable.NotConfigured"/> before anything is sent.
/// The reply is read only by <see cref="TemplateProposalParser"/> (bounded, tolerant), which has no way to say a line is approved: the
/// drafts carry a slot and words, and the library takes them as proposed. A reply with no usable line is
/// <see cref="AuthorUnavailable.ReplyUnusable"/>, never an empty success. The credential-health report is told only what the service said
/// about the key (401 or 403); cancellation by the caller propagates.</para>
/// </summary>
public sealed class ClaudeTemplateAuthor(HttpClient http, IOptions<ClaudeVisionOptions> options, ICredentialHealthSink? health = null) : ITemplateAuthor
{
    /// <summary>The fixed instruction. A constant: no digits, no names and nothing the household wrote.</summary>
    public const string SystemPrompt =
        "You write short, plain-words household meal lines for a weekly paper sheet. You are told which slots need new lines, how many "
        + "lines to write, the lines the household has already approved, turned down or still has waiting, and a few lasting tastes as "
        + "cuisine and ingredient words. Write only new lines that suit those tastes and differ from every line already listed, and never "
        + "one like a line the household turned down. Each line is a few plain words naming an ordinary breakfast, lunch or lunchbox item, "
        + "short enough to print on two short lines, with no quantities, no brands and no names of people. Use only the slot names you "
        + "were given. Reply with strict JSON and nothing else, in exactly this shape: "
        + "{\"templates\":[{\"slot\":\"<slot name>\",\"words\":\"<the line>\"}]} and never more lines than were asked for.";

    /// <summary>The most lines of each kind that are told to the model.</summary>
    public const int MostApproved = 30;

    public const int MostRejected = 30;

    public const int MostWaiting = 12;

    public const int MostTastes = 30;

    /// <summary>The most bulleted lines a request can hold in all.</summary>
    public const int MostLines = MostApproved + MostRejected + MostWaiting + MostTastes;

    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaximumReplyTokens = 800;

    public async Task<TemplateAuthored> ProposeAsync(TemplateRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new TemplateAuthored.Unavailable(AuthorUnavailable.NotConfigured);
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = settings.Model,
            ["max_tokens"] = MaximumReplyTokens,
            ["system"] = new[] { new { type = "text", text = SystemPrompt } },
            ["messages"] = new[] { new { role = "user", content = new[] { new { type = "text", text = UserMessage(request) } } } },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        message.Headers.Add("x-api-key", settings.ApiKey);
        message.Headers.Add("anthropic-version", AnthropicVersion);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = JsonContent.Create(payload);

        string body;
        System.Net.HttpStatusCode status;
        try
        {
            using var response = await http.SendAsync(message, cancellation).ConfigureAwait(false);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // The client's own timeout, not the caller stopping: the service did not answer in time.
            return new TemplateAuthored.Unavailable(AuthorUnavailable.TimedOut);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            return new TemplateAuthored.Unavailable(AuthorUnavailable.Offline);
        }

        if ((int)status is < 200 or > 299)
        {
            if (AuthFailure.IsHttpCredentialRejection((int)status, body))
            {
                health?.MarkFailing(CredentialIds.Claude, $"{(int)status} {status}");
                return new TemplateAuthored.Unavailable(AuthorUnavailable.KeyRejected);
            }

            return new TemplateAuthored.Unavailable(AuthorUnavailable.Offline);
        }

        health?.MarkOk(CredentialIds.Claude);
        var drafts = TemplateProposalParser.Parse(TextFrom(body), request);
        return drafts.Count == 0
            ? new TemplateAuthored.Unavailable(AuthorUnavailable.ReplyUnusable)
            : new TemplateAuthored.Drafts(drafts);
    }

    // Only what a TemplateRequest carries, as words, each list capped.
    private static string UserMessage(TemplateRequest request)
    {
        var text = new StringBuilder();
        var count = Math.Clamp(request.Count, 1, TemplateRequest.MaxCount);
        text.Append("Write ").Append(count).AppendLine(" new lines.");
        text.Append("Slots needing lines: ")
            .AppendLine(string.Join(", ", request.Wanted.Distinct().Select(MealTemplateWords.Slots.Word)));
        Section(text, "Already approved:", request.Approved, MostApproved);
        Section(text, "Turned down:", request.Rejected, MostRejected);
        Section(text, "Still waiting:", request.Pending, MostWaiting);

        text.AppendLine("Lasting tastes:");
        var tastes = request.Tastes.Take(MostTastes).ToList();
        foreach (var taste in tastes)
        {
            text.Append("- ").Append(Words(taste.Kind, TasteNote.MaxSubjectLength)).Append(": ")
                .Append(Words(taste.Subject, TasteNote.MaxSubjectLength)).Append(" (").Append(Words(taste.Stance, TasteNote.MaxSubjectLength)).AppendLine(")");
        }

        if (tastes.Count == 0)
        {
            text.AppendLine("- none");
        }

        return text.ToString();
    }

    private static void Section(StringBuilder text, string heading, IReadOnlyList<TemplateKnown> known, int most)
    {
        text.AppendLine(heading);
        var lines = known.Take(most).ToList();
        foreach (var line in lines)
        {
            text.Append("- ").Append(MealTemplateWords.Slots.Word(line.Slot)).Append(": ").AppendLine(Words(line.Words, MealTemplate.MaxWordsLength));
        }

        if (lines.Count == 0)
        {
            text.AppendLine("- none");
        }
    }

    // One line of single-spaced words, cut to a bound: a stray newline cannot start a new instruction.
    private static string Words(string? value, int longest)
    {
        var words = MealTemplate.Normalize(value);
        return words.Length > longest ? words[..longest] : words;
    }

    private static string? TextFrom(string body)
    {
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object
                && parsed.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("type", out var type) && type.GetString() == "text"
                        && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString();
                    }
                }
            }

            return null;
        }
        catch (JsonException)
        {
            // A body that is not JSON holds no answer; the reply is then unusable and proposes nothing.
            return null;
        }
    }
}
