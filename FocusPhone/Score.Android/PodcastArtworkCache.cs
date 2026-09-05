using Android.Content;
using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;

namespace Score;

/// <summary>
/// Show artwork, on disk, where the list rows can actually decode it.
///
/// The feed parser has been reading &lt;itunes:image&gt; all along and nothing ever
/// consumed it: <see cref="Episode.ImageUrl"/> went into the cache and no screen
/// asked for it. What artwork did appear came from the media session's embedded ID3
/// tags, which is why it showed for some shows and not others regardless of what
/// their feed published — a show whose audio carries no embedded art was simply blank.
///
/// It lands on disk because <c>Codex.Ui.CoverCache</c> is deliberately a synchronous,
/// disk-only decoder (see its own remarks), so a URL has to become a file before a row
/// can show it. Same "art" directory as Drive covers and TIDAL covers — one directory,
/// one eviction story.
///
/// Cached per SHOW rather than per episode. Episode art is usually the show's art
/// repeated, so a file per episode would be the same picture a few hundred times.
/// </summary>
public sealed class PodcastArtworkCache(Context context)
{
    private readonly string directory = System.IO.Path.Combine(context.CacheDir!.AbsolutePath, "art");

    /// <summary>
    /// The local path for this feed's artwork, downloading it if it is not already
    /// held. Null whenever there is no art or it cannot be fetched — artwork is
    /// always optional, and a show without it falls back to the bookplate.
    /// </summary>
    public async Task<string?> PathAsync(string feedId, string? imageUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var path = System.IO.Path.Combine(directory, SafeName(feedId) + ".art");
        if (System.IO.File.Exists(path))
        {
            return path;
        }

        try
        {
            using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());

            // The same identity the feed and the audio send. These CDNs answer 403 to
            // a client that does not introduce itself, and the artwork is served by
            // the hosts that taught us that.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(PodcastHttp.UserAgent);

            var bytes = await http.GetByteArrayAsync(imageUrl, cancellationToken);
            System.IO.Directory.CreateDirectory(directory);
            await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return path;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            AudioDiagnostics.LogWarning(
                "PodcastArtworkCache", $"Could not fetch artwork for {feedId}.", e);
            return null;
        }
    }

    /// <summary>The path this feed's art would be at, without fetching it.</summary>
    public string? ExistingPath(string feedId)
    {
        var path = System.IO.Path.Combine(directory, SafeName(feedId) + ".art");
        return System.IO.File.Exists(path) ? path : null;
    }

    // Mirrors MusicTagReader.SafeName and TidalArtworkCache.SafeName exactly — same
    // cache directory, so the same filename rule or two of them collide.
    private static string SafeName(string feedId) =>
        string.Concat(feedId.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
}
