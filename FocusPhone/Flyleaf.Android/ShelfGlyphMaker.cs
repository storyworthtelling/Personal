using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;

namespace Flyleaf;

/// <summary>
/// Produces a bookend glyph id for an owner-created shelf (feature #11): the offline pencil
/// monogram immediately, and — best-effort, using the shared vault-backed Claude key — an
/// AI-generated vector when one can be produced and parsed. Returns a <see cref="Category.Glyph"/>
/// value the store persists: <c>"path:&lt;d&gt;"</c> for the AI vector or <c>"mono:&lt;letters&gt;"</c>
/// for the fallback. Never throws — any failure (no sign-in, no key, an unusable reply) yields the
/// monogram, so shelf creation never blocks on the model.
/// </summary>
public sealed class ShelfGlyphMaker(Func<Task<string>> accessToken)
{
    /// <summary>The immediate offline glyph id for a shelf name (drawn until the AI vector arrives).</summary>
    public static string Monogram(string shelfName) => "mono:" + ShelfMonogram.For(shelfName);

    /// <summary>The AI vector glyph id ("path:…"), or null when none could be produced/parsed.</summary>
    public async Task<string?> AiGlyphAsync(string shelfName, CancellationToken cancellationToken)
    {
        try
        {
            var options = Codex.Ui.Voice.VoiceOptionsAccess.ForHead(accessToken, "Flyleaf");
            await options.RefreshAsync(cancellationToken);
            using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler())
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var path = await new ShelfGlyphClient(http, options).GenerateAsync(shelfName, cancellationToken);
            return path is not null && SvgGlyph.Parse(path) is not null ? "path:" + path : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
