using System.Net;
using Codex.Infrastructure.Apps;
using Codex.Infrastructure.Email;
using Codex.Infrastructure.Plate;
using Codex.Infrastructure.Podcasts;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Logging;

/// <summary>
/// Audit 08 §23: the Codex clients answered "nothing" for a refused key, a 400 and an outage alike, with no line. Each
/// takes an optional sink and writes one line per failed call: the call, the failure kind, the status and request id,
/// never the reply.
/// </summary>
public class ClientLogSinkTests
{
    private const string Claude = "https://api.anthropic.com/v1/messages";
    private const string Refused = "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}";

    private static IOptions<VoiceOptions> Key() => Options.Create(new VoiceOptions { ApiKey = "test-key" });

    private static FakeHttpMessageHandler RefusingClaude()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Claude, Refused, status: HttpStatusCode.Unauthorized);
        return handler;
    }

    [Fact]
    public async Task TheMailReaderSaysWhyItKnewNothing()
    {
        var lines = new List<string>();

        await new MailReadingClient(new HttpClient(RefusingClaude()), Key(), log: lines.Add)
            .ReadAsync("a@example.com", "Hello", "Body", CancellationToken.None);

        Assert.Equal(["CLAUDE mail-reading failed: KeyRejected 401"], lines);
    }

    [Fact]
    public async Task TheShelfGlyphSaysWhyItDrewNothing()
    {
        var lines = new List<string>();

        await new ShelfGlyphClient(new HttpClient(RefusingClaude()), Key(), log: lines.Add)
            .GenerateAsync("Recipes", CancellationToken.None);

        Assert.Equal(["CLAUDE shelf-glyph failed: KeyRejected 401"], lines);
    }

    [Fact]
    public async Task TheCaptionSaysWhyItWroteNothing()
    {
        var lines = new List<string>();

        await new DarkroomCaptionClient(new HttpClient(RefusingClaude()), Key(), log: lines.Add)
            .CaptionAsync([[1, 2, 3]], CancellationToken.None);

        Assert.Equal(["CLAUDE darkroom-caption failed: KeyRejected 401"], lines);
    }

    [Fact]
    public async Task ThePodcastSearchSaysWhyItFoundNothing()
    {
        var lines = new List<string>();
        var handler = new FakeHttpMessageHandler();
        handler.Respond("https://itunes.apple.com/search?media=podcast&limit=25&term=garden", "oops", status: HttpStatusCode.InternalServerError);

        await new PodcastSearchClient(new HttpClient(handler), lines.Add).SearchAsync("garden", CancellationToken.None);

        Assert.Single(lines);
        Assert.StartsWith("PODCAST search failed: ", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("garden", lines[0], StringComparison.Ordinal);
    }
}
