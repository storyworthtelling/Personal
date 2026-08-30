using System.Net;
using System.Text;
using Codex.Infrastructure.Email;
using Xunit;

namespace Codex.Tests.Email;

/// <summary>
/// The Gmail API path, which replaces IMAP/SMTP with an app password. What it buys is
/// exactly two things the old path could not do honestly: a REAL trash (recoverable for
/// 30 days, rather than IMAP's move-to-a-folder-named-Trash) and server-side search.
/// Transport only — the mapping is still the unit-tested GmailMessageMapper.
/// </summary>
public class GmailApiClientTests
{
    private const string Base = "https://gmail.googleapis.com/gmail/v1/users/me";

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string RawMessage(string from, string to, string subject, string body) =>
        $"From: {from}\r\nTo: {to}\r\nSubject: {subject}\r\nMessage-Id: <m1@example.com>\r\n" +
        $"Date: Sat, 29 Aug 2026 10:00:00 +0000\r\n\r\n{body}";

    private static GmailApiClient Client(FakeHttpMessageHandler handler, string address = "owner@gmail.com") =>
        new(address, () => Task.FromResult("token-123"), new HttpClient(handler));

    [Fact]
    public async Task FetchesRecentMailAndMapsIt()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages?q=%28in%3Ainbox%20OR%20in%3Asent%29%20after%3A1756461600&maxResults=200",
            """{"messages":[{"id":"g1"}]}""");
        handler.Respond($"{Base}/messages/g1?format=raw",
            $$"""{"id":"g1","raw":"{{Base64Url(RawMessage("her@example.com", "owner@gmail.com", "Dinner", "Seven?"))}}"}""");

        var items = await Client(handler).FetchRecentAsync(DateTimeOffset.FromUnixTimeSeconds(1756461600));

        var item = Assert.Single(items);
        Assert.Equal("Dinner", item.Subject);
        Assert.True(item.Incoming);
        Assert.Equal("her@example.com", item.ContactKey);
    }

    [Fact]
    public async Task SendsTheTokenAsABearerHeader()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages?q=%28in%3Ainbox%20OR%20in%3Asent%29%20after%3A1756461600&maxResults=200",
            """{"messages":[]}""");

        await Client(handler).FetchRecentAsync(DateTimeOffset.FromUnixTimeSeconds(1756461600));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token-123", request.Headers.Authorization.Parameter);
    }

    // An empty mailbox comes back with the "messages" key absent entirely, not as an
    // empty array — reading it as a list without checking would throw on a clean inbox.
    [Fact]
    public async Task AnEmptyMailboxIsNotAnError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages?q=%28in%3Ainbox%20OR%20in%3Asent%29%20after%3A1756461600&maxResults=200",
            """{"resultSizeEstimate":0}""");

        Assert.Empty(await Client(handler).FetchRecentAsync(DateTimeOffset.FromUnixTimeSeconds(1756461600)));
    }

    // Filing means a real trash. The cache holds RFC Message-Ids (it always has, and
    // changing that would orphan every message already synced), while the API trashes by
    // Gmail's own id — so the id is looked up first with the rfc822msgid: operator.
    [Fact]
    public async Task TrashesByLookingUpTheRfcMessageId()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages?q=rfc822msgid%3A%3Cm1%40example.com%3E&maxResults=5",
            """{"messages":[{"id":"g1"}]}""");
        handler.Respond($"{Base}/messages/g1/trash", """{"id":"g1"}""");

        Assert.True(await Client(handler).TrashAsync("<m1@example.com>"));

        Assert.Contains(handler.Requests, r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsoluteUri.EndsWith("/messages/g1/trash", StringComparison.Ordinal));
    }

    // A message the account no longer has is not a failure to report: the owner's intent
    // was that it be gone, and it is.
    [Fact]
    public async Task TrashingSomethingAlreadyGoneSucceedsQuietly()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages?q=rfc822msgid%3A%3Cmissing%40example.com%3E&maxResults=5",
            """{"messages":[]}""");

        Assert.True(await Client(handler).TrashAsync("<missing@example.com>"));
    }

    [Fact]
    public async Task SendsMailAsBase64UrlMime()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages/send", """{"id":"g9"}""");

        await Client(handler).SendAsync("her@example.com", "Dinner", "Seven?");

        var payload = Assert.Single(handler.Bodies);
        Assert.Contains("\"raw\":", payload, StringComparison.Ordinal);
        // Base64url, so the standard alphabet's + and / must never appear.
        var raw = System.Text.Json.JsonDocument.Parse(payload).RootElement.GetProperty("raw").GetString()!;
        Assert.DoesNotContain('+', raw);
        Assert.DoesNotContain('/', raw);
    }

    // Server-side search is the other thing the API buys: mail is searched by Gmail, not
    // by scanning what happens to be cached on the phone.
    [Fact]
    public async Task SearchesOnTheServer()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages?q=plumber&maxResults=50", """{"messages":[{"id":"g4"}]}""");
        handler.Respond($"{Base}/messages/g4?format=raw",
            $$"""{"id":"g4","raw":"{{Base64Url(RawMessage("plumber@example.com", "owner@gmail.com", "Quote", "Attached"))}}"}""");

        var results = await Client(handler).SearchAsync("plumber");

        Assert.Equal("Quote", Assert.Single(results).Subject);
    }

    // A rejected token is a credential problem and must be reported as one, so the
    // settings screen can reveal the field again. A 500 is not — a blip must never make
    // Post claim the owner's account is broken.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public async Task OnlyAnAuthRejectionFlagsTheCredential(HttpStatusCode status, bool expectFlagged)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/messages?q=%28in%3Ainbox%20OR%20in%3Asent%29%20after%3A1756461600&maxResults=200",
            "{}", status: status);
        var health = new RecordingHealthSink();
        var client = new GmailApiClient(
            "owner@gmail.com", () => Task.FromResult("token-123"), new HttpClient(handler), health);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.FetchRecentAsync(DateTimeOffset.FromUnixTimeSeconds(1756461600)));

        Assert.Equal(expectFlagged, health.Rejected);
    }

    private sealed class RecordingHealthSink : Storyworthtelling.Voice.Health.ICredentialHealthSink
    {
        public bool Rejected { get; private set; }

        public void MarkOk(string credentialId)
        {
        }

        public void MarkFailing(string credentialId, string message) => Rejected = true;
    }
}
