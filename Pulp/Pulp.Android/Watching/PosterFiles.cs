using System.Security.Cryptography;
using System.Text;
using Android.Content;

namespace Pulp.Watching;

/// <summary>
/// Posters as local files, because <see cref="Codex.Ui.CoverCache"/> decodes from disk: the catalogue
/// carries a poster's URL, so the first time a row needs one it is fetched into the cache directory
/// under a name made from the URL, and every later paint reads the file. Called off the UI thread.
/// A poster that cannot be fetched is logged and left out — the artwork window shows the bookplate
/// (D119), which is the drawn answer to missing art, not an error.
/// </summary>
public sealed class PosterFiles
{
    private static readonly HttpClient Http = new(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string directory;
    private readonly Action<string, Exception> warn;

    public PosterFiles(Context context, Action<string, Exception> warn)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = context.CacheDir?.AbsolutePath ?? throw new InvalidOperationException("Pulp has no cache directory to keep posters in.");
        directory = Path.Combine(root, "posters");
        this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    /// <summary>The poster's local path, fetching it first if this phone has not; null when there is none to show.</summary>
    public async Task<string?> PathForAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32].ToLowerInvariant();
        var path = Path.Combine(directory, name + ".img");
        if (File.Exists(path))
        {
            return path;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var bytes = await Http.GetByteArrayAsync(uri, cancellationToken);
            var partial = path + ".part";
            await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch (Exception exception) when ((exception is HttpRequestException or IOException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            warn($"The poster at {uri.Host} could not be fetched; the row shows the bookplate.", exception);
            return null;
        }
    }
}
