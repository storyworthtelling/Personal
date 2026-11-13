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

    // New messages are fetched a few at a time rather than one after another (efficiency audit §2).
    private const int ParallelFetches = 4;

    private const string WindowLabels = "(in:inbox OR in:sent)";

    private readonly string accountAddress;
    private readonly Func<Task<string>> accessToken;
    private readonly HttpClient http;
    private readonly ICredentialHealthSink? health;
    private readonly GmailMessageMapper mapper;
    private readonly Action<bool, bool>? observeBodyShape;

    /// <param name="observeBodyShape">
    /// Told, per message, whether it carried a plain-text part and an HTML part. Only the
    /// MIME level knows this — by the time a message is a timeline item its body is already
    /// the readable form, so the distinction is gone. It is what says whether the shared
    /// web view is urgent or theoretical, and it is optional so no existing caller changes.
    /// </param>
    public GmailApiClient(
        string accountAddress,
        Func<Task<string>> accessToken,
        HttpClient http,
        ICredentialHealthSink? health = null,
        Action<bool, bool>? observeBodyShape = null)
    {
        this.accountAddress = GmailCredentials.NormalizeAddress(accountAddress);
        this.accessToken = accessToken;
        this.http = http;
        this.health = health;
        this.observeBodyShape = observeBodyShape;
        mapper = new GmailMessageMapper(this.accountAddress);
    }

    /// <summary>
    /// Recent inbox and sent mail — both directions of the conversation — newer than the
    /// cutoff.
    /// </summary>
    public Task<IReadOnlyList<TimelineItem>> FetchRecentAsync(
        DateTimeOffset since, CancellationToken cancellation = default) =>
        FetchQueryAsync(WindowQuery(since), SyncPageSize, cancellation);

    /// <summary>
    /// The inbox and sent mail newer than <paramref name="since"/>, fetching only messages <paramref name="index"/>
    /// does not already hold (efficiency audit §2). Gmail's history id is read before the listing, so a message that
    /// arrives during it is in the history read next time rather than lost between the two.
    /// </summary>
    public async Task<GmailMirror> MirrorAsync(DateTimeOffset since, IGmailIdIndex index, CancellationToken cancellation = default)
    {
        var historyId = await HistoryIdAsync(cancellation);
        var ids = await ListIdsAsync(WindowQuery(since), SyncPageSize, cancellation);

        var present = new List<string>();
        var unknown = new List<string>();
        foreach (var id in ids)
        {
            if (index.SourceIdFor(id) is { } known)
            {
                present.Add(known);
            }
            else
            {
                unknown.Add(id);
            }
        }

        var fetched = await FetchNewAsync(unknown, index, cancellation);
        present.AddRange(fetched.Select(item => item.SourceId));
        return new GmailMirror(fetched, present.Distinct(StringComparer.Ordinal).ToList(), historyId);
    }

    /// <summary>
    /// What was added to the inbox or sent since <paramref name="historyId"/>, from Gmail's history rather than a fresh
    /// listing, fetching only messages <paramref name="index"/> does not hold. Null when Gmail no longer has history that
    /// far back (it keeps about a week): the caller lists the window instead.
    /// </summary>
    public async Task<GmailChanges?> ChangesSinceAsync(string historyId, IGmailIdIndex index, CancellationToken cancellation = default)
    {
        var added = new List<string>();
        string? latest = null;
        string? pageToken = null;
        do
        {
            var url = $"{Base}/history?startHistoryId={Uri.EscapeDataString(historyId)}&historyTypes=messageAdded"
                + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var document = await SendAsync(request, cancellation, allowNotFound: true);
            if (document is null)
            {
                return null;
            }

            var root = document.RootElement;
            latest = root.TryGetProperty("historyId", out var id) ? id.GetString() : latest;
            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (root.TryGetProperty("history", out var history))
            {
                added.AddRange(AddedToInboxOrSent(history));
            }
        }
        while (!string.IsNullOrEmpty(pageToken));

        var unknown = added.Distinct(StringComparer.Ordinal).Where(id => index.SourceIdFor(id) is null).ToList();
        return new GmailChanges(await FetchNewAsync(unknown, index, cancellation), latest ?? historyId);
    }

    private static IEnumerable<string> AddedToInboxOrSent(JsonElement history)
    {
        foreach (var entry in history.EnumerateArray())
        {
            if (!entry.TryGetProperty("messagesAdded", out var messagesAdded))
            {
                continue;
            }
            foreach (var added in messagesAdded.EnumerateArray())
            {
                var message = added.GetProperty("message");
                var labels = message.TryGetProperty("labelIds", out var ids)
                    ? ids.EnumerateArray().Select(label => label.GetString()).ToList()
                    : [];
                if ((labels.Contains("INBOX") || labels.Contains("SENT")) && message.GetProperty("id").GetString() is { } id)
                {
                    yield return id;
                }
            }
        }
    }

    private static string WindowQuery(DateTimeOffset since) => $"{WindowLabels} after:{since.ToUnixTimeSeconds()}";

    private async Task<string?> HistoryIdAsync(CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/profile");
        using var document = await SendAsync(request, cancellation);
        return document.RootElement.TryGetProperty("historyId", out var id) ? id.GetString() : null;
    }

    // Each message once, a few at a time, in the order Gmail listed them; each remembered as soon as it is mapped.
    private async Task<IReadOnlyList<TimelineItem>> FetchNewAsync(IReadOnlyList<string> ids, IGmailIdIndex? index, CancellationToken cancellation)
    {
        var results = new TimelineItem?[ids.Count];
        using var gate = new SemaphoreSlim(ParallelFetches);
        await Task.WhenAll(ids.Select(async (id, position) =>
        {
            await gate.WaitAsync(cancellation);
            try
            {
                results[position] = await LoadItemAsync(id, cancellation);
            }
            finally
            {
                gate.Release();
            }
        }));

        var items = new List<TimelineItem>();
        for (var position = 0; position < ids.Count; position++)
        {
            if (results[position] is { } item)
            {
                index?.Remember(ids[position], item.SourceId);
                items.Add(item);
            }
        }
        return items;
    }

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

    /// <summary>
    /// One attachment's bytes, fetched only when the owner asks to open it. The message is
    /// re-read whole rather than the part being addressed by id, because the cache stores
    /// attachment METADATA and not Gmail's part ids — and re-reading one message on a tap
    /// is cheaper than carrying another identifier through the schema for every message
    /// that will never be opened.
    /// </summary>
    public async Task<byte[]?> FetchAttachmentAsync(
        string rfcMessageId, string fileName, CancellationToken cancellation = default)
    {
        var ids = await ListIdsAsync($"rfc822msgid:{rfcMessageId}", 5, cancellation);
        foreach (var id in ids)
        {
            if (await LoadRawAsync(id, cancellation) is not { } message)
            {
                continue;
            }

            var part = message.Attachments
                .OfType<MimePart>()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.FileName, fileName, StringComparison.Ordinal));
            if (part is null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            await part.Content.DecodeToAsync(buffer, cancellation);
            return buffer.ToArray();
        }

        return null;
    }

    private async Task<IReadOnlyList<TimelineItem>> FetchQueryAsync(
        string query, int pageSize, CancellationToken cancellation) =>
        await FetchNewAsync(await ListIdsAsync(query, pageSize, cancellation), null, cancellation);

    // A message without its attachments' bytes (GmailFullMessage), mapped as before.
    private async Task<TimelineItem?> LoadItemAsync(string id, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/messages/{id}?format=full");
        using var document = await SendAsync(request, cancellation);
        if (!document.RootElement.TryGetProperty("payload", out _))
        {
            return null;
        }

        var message = GmailFullMessage.ToMime(document.RootElement);
        var (hasText, hasHtml) = GmailMessageMapper.BodyShape(message);
        observeBodyShape?.Invoke(hasText, hasHtml);
        return mapper.Map(message);
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

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellation) =>
        (await SendAsync(request, cancellation, allowNotFound: false))!;

    // Null only for a 404 the caller allowed (an expired history id).
    private async Task<JsonDocument?> SendAsync(HttpRequestMessage request, CancellationToken cancellation, bool allowNotFound)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken());
        using var response = await http.SendAsync(request, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

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
