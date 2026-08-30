using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Codex.Domain.Timeline;
using MimeKit;
using Codex.Domain.Credentials;
using Storyworthtelling.Voice.Health;

namespace Codex.Infrastructure.Email;

/// <summary>
/// Gmail over its own HTTP API, on an OAuth token rather than an app password.
///
/// This replaces the IMAP/SMTP client for two reasons that the old path could not fake.
/// <b>A real trash:</b> IMAP could only move a message into a folder called Trash, whereas
/// <c>messages/{id}/trash</c> is the same delete the Gmail app performs, recoverable for 30
/// days — which is what makes filing safe enough to be a first-class verb. And <b>server-side
/// search</b>: mail is searched by Gmail rather than by scanning whatever happens to be
/// cached on the phone. It also takes the app password out of the vault entirely.
///
/// Transport only. Messages are fetched as raw RFC822 and handed to the existing,
/// unit-tested <see cref="GmailMessageMapper"/>, so the API move changes how mail arrives
/// and nothing about what it becomes.
/// </summary>
public class GmailApiClient
{
    private const string Base = "https://gmail.googleapis.com/gmail/v1/users/me";

    // The owner runs near inbox-zero and the first pull is deliberately the CURRENT
    // inboxes only — no archived history — so a couple of hundred is a ceiling that will
    // not be reached rather than a page size to iterate.
    private const int SyncPageSize = 200;
    private const int SearchPageSize = 50;

    private readonly string accountAddress;
    private readonly Func<Task<string>> accessToken;
    private readonly HttpClient http;
    private readonly ICredentialHealthSink? health;
    private readonly GmailMessageMapper mapper;

    public GmailApiClient(
        string accountAddress,
        Func<Task<string>> accessToken,
        HttpClient http,
        ICredentialHealthSink? health = null)
    {
        this.accountAddress = GmailCredentials.NormalizeAddress(accountAddress);
        this.accessToken = accessToken;
        this.http = http;
        this.health = health;
        mapper = new GmailMessageMapper(this.accountAddress);
    }

    /// <summary>
    /// Recent inbox and sent mail — both directions of the conversation — newer than the
    /// cutoff.
    /// </summary>
    public Task<IReadOnlyList<TimelineItem>> FetchRecentAsync(
        DateTimeOffset since, CancellationToken cancellation = default) =>
        FetchQueryAsync(
            $"(in:inbox OR in:sent) after:{since.ToUnixTimeSeconds()}", SyncPageSize, cancellation);

    /// <summary>
    /// Full-text search, run by Gmail. Takes Gmail's own query syntax, because narrowing
    /// it to something Post invents would make the one real advantage here unreachable.
    /// </summary>
    public Task<IReadOnlyList<TimelineItem>> SearchAsync(
        string query, CancellationToken cancellation = default) =>
        FetchQueryAsync(query, SearchPageSize, cancellation);

    /// <summary>
    /// Moves a message to Gmail's trash — the real one, recoverable for 30 days.
    ///
    /// The cache keys mail by RFC Message-Id and always has; changing that would orphan
    /// every message already synced. The API trashes by Gmail's own id, so the id is
    /// looked up first with the <c>rfc822msgid:</c> operator. Returns true when the
    /// message is gone, INCLUDING when the account never had it: the owner's intent was
    /// that it not be there, and it is not.
    /// </summary>
    public async Task<bool> TrashAsync(string rfcMessageId, CancellationToken cancellation = default)
    {
        var ids = await ListIdsAsync($"rfc822msgid:{rfcMessageId}", 5, cancellation);
        foreach (var id in ids)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/messages/{id}/trash");
            await SendAsync(request, cancellation);
        }
        return true;
    }

    /// <summary>
    /// Sends an email. Quick reply passes just to/subject/body; a full compose adds cc,
    /// attachments, and the In-Reply-To header so the reply threads with what it answers.
    /// </summary>
    public async Task SendAsync(string to, string subject, string body,
        IReadOnlyList<string>? cc = null, IReadOnlyList<string>? attachmentPaths = null,
        string? inReplyTo = null, CancellationToken cancellation = default)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(accountAddress));
        message.To.Add(MailboxAddress.Parse(to));
        foreach (var address in cc ?? [])
        {
            message.Cc.Add(MailboxAddress.Parse(address));
        }
        message.Subject = subject;
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            message.InReplyTo = inReplyTo;
            message.References.Add(inReplyTo);
        }

        var builder = new BodyBuilder { TextBody = body };
        foreach (var path in attachmentPaths ?? [])
        {
            await builder.Attachments.AddAsync(path, cancellation);
        }
        message.Body = builder.ToMessageBody();

        using var buffer = new MemoryStream();
        await message.WriteToAsync(buffer, cancellation);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/messages/send")
        {
            Content = JsonContent.Create(new { raw = ToBase64Url(buffer.ToArray()) }),
        };
        await SendAsync(request, cancellation);
    }

    private async Task<IReadOnlyList<TimelineItem>> FetchQueryAsync(
        string query, int pageSize, CancellationToken cancellation)
    {
        var items = new List<TimelineItem>();
        foreach (var id in await ListIdsAsync(query, pageSize, cancellation))
        {
            if (await LoadRawAsync(id, cancellation) is { } message)
            {
                items.Add(mapper.Map(message));
            }
        }
        return items;
    }

    private async Task<IReadOnlyList<string>> ListIdsAsync(
        string query, int pageSize, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{Base}/messages?q={Uri.EscapeDataString(query)}&maxResults={pageSize}");
        using var document = await SendAsync(request, cancellation);

        // An empty mailbox omits "messages" entirely rather than returning an empty array,
        // so this must be a lookup rather than an indexer.
        if (!document.RootElement.TryGetProperty("messages", out var messages))
        {
            return [];
        }
        return messages.EnumerateArray()
            .Select(message => message.GetProperty("id").GetString())
            .OfType<string>()
            .ToList();
    }

    private async Task<MimeMessage?> LoadRawAsync(string id, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/messages/{id}?format=raw");
        using var document = await SendAsync(request, cancellation);
        if (!document.RootElement.TryGetProperty("raw", out var raw) || raw.GetString() is not { } encoded)
        {
            return null;
        }

        using var buffer = new MemoryStream(FromBase64Url(encoded));
        return await MimeMessage.LoadAsync(buffer, cancellation);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken());
        using var response = await http.SendAsync(request, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);

        if (!response.IsSuccessStatusCode)
        {
            // Only an auth rejection says anything about the credential. Throttling, a 5xx
            // and a dropped connection are all things that pass, and treating one as a bad
            // token would reveal the credential field for a blip.
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
            {
                health?.MarkFailing(CredentialIds.GmailAccount, $"Gmail rejected the account ({(int)response.StatusCode}).");
            }
            throw new HttpRequestException(
                $"Gmail returned {(int)response.StatusCode} for {request.RequestUri}: {Truncate(body)}");
        }

        health?.MarkOk(CredentialIds.GmailAccount);
        return string.IsNullOrWhiteSpace(body)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(body);
    }

    private static string Truncate(string body) =>
        body.Length <= 400 ? body : body[..400] + "…";

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string encoded)
    {
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }
}
