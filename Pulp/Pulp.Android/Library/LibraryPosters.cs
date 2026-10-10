using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Graphics;
using Codex.Ui;
using Pulp.App;

namespace Pulp.Library;

/// <summary>
/// The posters on Library rows (mockups 04 and 05). The catalogue gives a poster as a URL; this keeps
/// a copy in the app's cache folder (named by a hash of the URL) and hands the kit's
/// <see cref="CoverCache"/> the file, so a list paints from disk after its first visit and a decode is
/// downsampled to the row's window. Called off the UI thread. A poster that cannot be fetched is
/// logged to Diagnostics and the row keeps the kit's bookplate — never an error on the page.
/// </summary>
internal static class LibraryPosters
{
    private static readonly HttpClient Http = new(new Xamarin.Android.Net.AndroidMessageHandler())
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static async Task<Bitmap?> LoadAsync(Context context, string? url, int targetPx, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var folder = System.IO.Path.Combine(context.CacheDir?.AbsolutePath ?? context.FilesDir!.AbsolutePath, "posters");
        var path = System.IO.Path.Combine(folder, Hash(url) + ".img");
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
                PulpServices.For(context).Diagnostics.Warn("posters", $"A poster could not be fetched ({uri.Host}); the row keeps its bookplate.", exception);
                return null;
            }
        }

        return CoverCache.Get(path, targetPx);
    }

    private static string Hash(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
}
