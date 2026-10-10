using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Graphics;
using Codex.Ui;
using Pulp.App;

namespace Pulp.Title;

/// <summary>
/// The title's official poster (D37: posters only, never a still), fetched once into the cache folder
/// and decoded downsampled through the kit's <see cref="CoverCache"/>. Off the UI thread; a poster that
/// cannot be fetched leaves the artwork window's bookplate and a line in Diagnostics, never an error on
/// the page.
/// </summary>
internal static class TitlePoster
{
    private static readonly HttpClient Http = new(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<Bitmap?> LoadAsync(Context context, string? url, int targetPx, PulpDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var folder = System.IO.Path.Combine(context.CacheDir?.AbsolutePath ?? context.FilesDir!.AbsolutePath, "posters");
        var path = System.IO.Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..24] + ".img");

        if (!File.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(folder);
                var bytes = await Http.GetByteArrayAsync(uri, cancellationToken);
                var partial = path + ".part";
                await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
                File.Move(partial, path, overwrite: true);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                diagnostics.Warn("poster", $"The poster at {uri.Host} could not be fetched; the bookplate stands in.", exception);
                return null;
            }
        }

        return CoverCache.Get(path, targetPx);
    }
}
