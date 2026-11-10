using System.Text;
using System.Text.Json;
using Codex.Domain.Apps;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Claude;
using Storyworthtelling.Voice.Health;

namespace Codex.Infrastructure.Apps;

/// <summary>Generates an engraved-bookend glyph for an owner-created shelf (feature #11).</summary>
public interface IShelfGlyphClient
{
    /// <summary>An SVG path (0–100 viewBox) for the shelf name, or null to fall back to the monogram.</summary>
    Task<string?> GenerateAsync(string shelfName, CancellationToken cancellationToken);
}

/// <summary>
/// The Claude-backed shelf-glyph client. Reuses the shared <see cref="VoiceOptions"/> key + cheap
/// model tier (like the Taste/Discovery clients) to turn a shelf name into a single monochrome SVG
/// path the head engraves on the bronze bookend. It is <em>self-degrading</em>: no key, an HTTP
/// failure, or a reply that doesn't validate as a lone path all return null, and the head then
/// draws the offline pencil monogram (<see cref="ShelfMonogram"/>) instead — so shelf creation
/// never blocks on the model.
/// </summary>
public sealed class ShelfGlyphClient : IShelfGlyphClient
{
    private const string Instruction =
        "You design tiny solid bronze statues for a bookshelf's category labels — a small figure that " +
        "stands on the shelf as the bookend itself. Given a shelf name, respond with ONLY the SVG path " +
        "data (the value of a single \"d\" attribute) for one simple, iconic SILHOUETTE representing " +
        "that name: a single CLOSED, FILLABLE shape (end with Z), not a line drawing or open strokes — " +
        "it will be rendered solid-filled, so an outline that doesn't enclose a real area will render " +
        "as a blob or nothing. Authored inside a 0 0 100 100 viewBox. No <svg> wrapper, no attributes, " +
        "no code fence, no words — just the path data beginning with M. Keep the silhouette simple and " +
        "legible at small size.";

    private readonly ClaudeMessagesClient claude;
    private readonly IOptions<VoiceOptions> optionsAccessor;

    private readonly Action<string>? log;

    /// <param name="log">One line per failed call (audit 08 §23): the call, the failure kind, status and request id.</param>
    public ShelfGlyphClient(
        HttpClient httpClient, IOptions<VoiceOptions> options, ICredentialHealthSink? health = null, Action<string>? log = null)
    {
        this.log = log;
        claude = new ClaudeMessagesClient(httpClient, () => options.Value.ApiKey, health);
        optionsAccessor = options;
    }

    public async Task<string?> GenerateAsync(string shelfName, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(shelfName))
        {
            return null; // no key or nothing to draw — the head uses the monogram
        }

        try
        {
            var reply = await claude.SendAsync(
                ClaudeRequest.Plain(options.FastModel, 400, Instruction, $"Shelf name: {shelfName}"), cancellationToken);
            return GlyphPath.Extract(reply.Text);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log?.Invoke(ClaudeCallLine.Failed("shelf-glyph", e));
            return null;
        }
    }

}
