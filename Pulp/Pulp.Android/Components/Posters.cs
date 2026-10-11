using Android.Graphics;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain.Catalogue;

namespace Pulp.Components;

/// <summary>
/// Pulp's one poster loader (D37: posters only, never a still). The catalogue gives a poster as an
/// address; the first screen to need it fetches it once, over the composition root's one
/// <see cref="HttpClient"/>, into the cache folder under <see cref="PosterCacheName"/>'s name, and every
/// later paint — on any screen — reads that file, decoded downsampled through the kit's
/// <see cref="CoverCache"/>. Https only. Called off the UI thread.
///
/// <para>A poster that cannot be fetched is a line in Diagnostics and no picture: the artwork window
/// keeps its bookplate (D119), which is the drawn answer to missing art, never an error on the page.
/// Release C's four screens each had a private copy of this (two hash lengths, two folders, three
/// clients); <c>OneSourceTests</c> keeps it one.</para>
/// </summary>
public sealed class Posters
{
    private readonly HttpClient http;
    private readonly string directory;
    private readonly PulpDiagnostics diagnostics;

    public Posters(HttpClient http, string cacheRoot, PulpDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        directory = System.IO.Path.Combine(cacheRoot, "posters");
    }

    /// <summary>The poster's local file, fetching it first if this phone has not; null when there is none to show.</summary>
    public async Task<string?> PathForAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (PosterCacheName.For(url) is not { } name)
        {
            diagnostics.Warn("posters", $"A poster address was not https and was not fetched: {url}");
            return null;
        }

        var path = System.IO.Path.Combine(directory, name);
        if (File.Exists(path))
        {
            return path;
        }

        // Two screens can ask for one uncached poster at once. Each fetch writes its own temporary
        // file and renames it into place whole (a rename in one folder is atomic), so a reader sees
        // either no file or a finished one — never another fetch's half-written bytes.
        var partial = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            Directory.CreateDirectory(directory);
            var bytes = await http.GetByteArrayAsync(new Uri(url), cancellationToken);
            await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch (Exception exception) when ((exception is HttpRequestException or IOException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            diagnostics.Warn("posters", $"A poster could not be fetched ({new Uri(url).Host}); the bookplate stands in.", exception);
            return null;
        }
        finally
        {
            // Gone after a successful move; left behind only by a fetch that failed part-way.
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    /// <summary>The poster decoded for a window <paramref name="targetPx"/> tall; null leaves the bookplate.</summary>
    public async Task<Bitmap?> LoadAsync(string? url, int targetPx, CancellationToken cancellationToken) =>
        await PathForAsync(url, cancellationToken) is { } path ? CoverCache.Get(path, targetPx) : null;
}
