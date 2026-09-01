using Android.Content;

namespace Score;

/// <summary>
/// Downloads and caches one TIDAL track's cover art locally, in the same "art"
/// cache directory <see cref="MusicTagReader"/> already writes Drive-embedded
/// covers into — one directory, one eviction story, and
/// <c>Codex.Ui.CoverCache.Get</c> only ever decodes a local file path, never a URL
/// (it is deliberately a synchronous, disk-only decoder — see its own remarks), so
/// a saved TIDAL track's artwork has to land on disk the same way a Drive track's
/// does before anything in the UI can show it.
/// </summary>
public sealed class TidalArtworkCache(Context context)
{
    private readonly string directory = System.IO.Path.Combine(context.CacheDir!.AbsolutePath, "art");

    /// <summary>Downloads the image at <paramref name="url"/> and returns the local
    /// path it was cached at, or null on any failure — artwork is always optional,
    /// never something a caller should treat as required.</summary>
    public async Task<string?> DownloadAsync(string trackId, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
            var bytes = await http.GetByteArrayAsync(url, cancellationToken);
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, SafeName(trackId) + ".art");
            await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return path;
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("TidalArtworkCache", $"DownloadAsync({trackId}) threw: {e}");
            return null;
        }
    }

    // Mirrors MusicTagReader.SafeName exactly — same cache directory, same filename rule.
    private static string SafeName(string trackId) =>
        string.Concat(trackId.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
}
