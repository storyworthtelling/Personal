using System.Security.Cryptography;
using System.Text;
using Android.Graphics;
using Codex.Ui;
using Pulp.App;

namespace Pulp.Discover;

/// <summary>
/// Fetches a poster once into the app's cache directory and decodes it through the kit's
/// <see cref="CoverCache"/>, all off the UI thread. A poster that will not download is logged and the
/// card stays plain paper — a missing picture is not worth a notice.
/// </summary>
internal sealed class PosterLoader(string cacheDirectory, HttpClient http, PulpDiagnostics diagnostics)
{
    private readonly string directory = System.IO.Path.Combine(cacheDirectory, "discover-posters");

    public async Task<Bitmap?> LoadAsync(string url, int targetPx, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            diagnostics.Warn("discover", $"A poster address was not https and was not fetched: {url}");
            return null;
        }

        var file = System.IO.Path.Combine(directory, Hash(url) + ".img");
        if (!File.Exists(file))
        {
            try
            {
                var bytes = await http.GetByteArrayAsync(address, cancellationToken);
                Directory.CreateDirectory(directory);
                var temporary = file + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                File.Move(temporary, file, overwrite: true);
            }
            catch (HttpRequestException exception)
            {
                diagnostics.Warn("discover", $"A poster could not be fetched ({exception.StatusCode}); the card stays plain.", exception);
                return null;
            }
        }

        return CoverCache.Get(file, targetPx);
    }

    private static string Hash(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32].ToLowerInvariant();
}
