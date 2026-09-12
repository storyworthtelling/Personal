using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Handwriting;
using Foreword.Infrastructure.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// Reading the journal page back: what was written in the free space, and what was written under
/// "Add to the list".
///
/// <para>The two are told apart by <b>which region the line is in</b>, not by what it says — the
/// page was drawn by Foreword, so the coordinates are known and the model is told them (D17). Asking
/// a model to judge whether a sentence "sounds like a request" would make a private thought's
/// destination depend on its wording, and the failure would be a page printing something the owner
/// never meant to print.</para>
/// </summary>
public class JournalTranscriptionTests
{
    private sealed class Reader : IPrayerReader
    {
    }

    private static string Read(PrayerText text) => text.ReadBy(new Reader());

    private static readonly DateOnly Day = new(2026, 9, 6);

    private static readonly byte[] Page = [37, 80, 68, 70, 1, 2, 3];

    private static ClaudeJournalTranscriber Transcriber(
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            Options.Create(new AnthropicOptions { ApiKey = "key", Model = "m" }),
            NullLogger<ClaudeJournalTranscriber>.Instance);

    private static HttpResponseMessage Reply(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]}"""),
        };

    [Fact]
    public async Task TheFreeSpaceBecomesAJournalEntryAndTheStripBecomesRequests()
    {
        var transcriber = Transcriber(_ => Reply($$"""
            {"journalling": "Asked for patience with the mornings, and got a week of them.",
             "adding": ["Dad — the scan on the 14th", "The Kellers, moving"]}
            """));

        var read = await transcriber.Read(Page, Day);

        Assert.Equal(
            "Asked for patience with the mornings, and got a week of them.",
            Read(read.Journalled));
        Assert.Equal(
            ["Dad — the scan on the 14th", "The Kellers, moving"],
            read.Added.Select(Read));
    }

    /// <summary>
    /// The two regions are named to the model in the prompt, from the same source the page was
    /// painted from. If they ever disagree, this is where it shows — the prompt would be describing
    /// a page that is not the page.
    /// </summary>
    [Fact]
    public async Task ThePromptNamesBothRegionsAndTheirCoordinates()
    {
        string? body = null;

        var transcriber = Transcriber(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Reply("""{"journalling":"","adding":[]}""");
        });

        await transcriber.Read(Page, Day);

        Assert.NotNull(body);
        Assert.Contains(JournalRegions.Journalling, body!);
        Assert.Contains(JournalRegions.Adding, body!);

        var adding = JournalRegions.For(612, 792)[1];
        Assert.Contains(((int)adding.Top).ToString(System.Globalization.CultureInfo.InvariantCulture), body!);
    }

    /// <summary>
    /// A blank page is a blank page. Most mornings nothing is written, and a morning with nothing
    /// written must produce nothing rather than an entry saying so.
    /// </summary>
    [Fact]
    public async Task ABlankPageAddsNothing()
    {
        var transcriber = Transcriber(_ => Reply("""{"journalling":"   ","adding":["", "  "]}"""));

        var read = await transcriber.Read(Page, Day);

        Assert.True(read.Journalled.IsEmpty);
        Assert.Empty(read.Added);
    }

    /// <summary>
    /// The reply wrapped in prose, which is how a model actually answers about half the time. The
    /// object is found inside it rather than the whole reply being handed to the parser.
    /// </summary>
    [Fact]
    public async Task AReplyWrappedInProseStillReads()
    {
        var transcriber = Transcriber(_ => Reply("""
            Here is what I could read:
            {"journalling":"Still turning it over.","adding":["Ashley's trip"]}
            Let me know if you need more.
            """));

        var read = await transcriber.Read(Page, Day);

        Assert.Equal("Still turning it over.", Read(read.Journalled));
        Assert.Equal(["Ashley's trip"], read.Added.Select(Read));
    }

    /// <summary>
    /// Every failure here is <b>quiet and empty</b>. An outage, a refusal, a rate limit — the answer
    /// is the same: nothing was read this morning. The alternative is worse than losing a page: the
    /// journal page is trashed once it has been read, so a failure that <i>claimed</i> to have read
    /// it would destroy the only copy.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task EveryFailureIsQuietAndReadsNothing(HttpStatusCode status)
    {
        var transcriber = Transcriber(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{}"),
        });

        var read = await transcriber.Read(Page, Day);

        Assert.False(read.Succeeded);
        Assert.True(read.Journalled.IsEmpty);
        Assert.Empty(read.Added);
    }

    /// <summary>
    /// And a reply with no JSON object in it at all is the same — not an exception inside a
    /// delivery.
    /// </summary>
    [Fact]
    public async Task AReplyWithNoObjectInItReadsNothing()
    {
        var transcriber = Transcriber(_ => Reply("I could not make out the handwriting."));

        var read = await transcriber.Read(Page, Day);

        Assert.False(read.Succeeded);
        Assert.True(read.Journalled.IsEmpty);
    }

    /// <summary>
    /// <see cref="JournalRead.Succeeded"/> is the flag the delivery hangs the trash on. A read that
    /// worked and found nothing is <b>not</b> the same as a read that failed: the first means the
    /// page can be thrown away, the second means it must not be.
    /// </summary>
    [Fact]
    public async Task ABlankPageThatWasActuallyReadCountsAsSucceeded()
    {
        var transcriber = Transcriber(_ => Reply("""{"journalling":"","adding":[]}"""));

        var read = await transcriber.Read(Page, Day);

        Assert.True(read.Succeeded);
    }

    [Fact]
    public void TheTranscriberIsADeclaredReader()
    {
        Assert.IsAssignableFrom<IPrayerReader>(Transcriber(_ => Reply("{}")));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
