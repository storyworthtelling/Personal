using Android.App;
using Codex.Ui;

namespace Pulp.Components;

/// <summary>
/// The one way a poster grid asks for its pictures: load the poster off the UI thread, then hand the
/// bitmap to the artwork window back on it. The child's pages each pasted this; it lives here once.
/// </summary>
public static class PosterArt
{
    public static Action<ArtworkWindow, string, int> Loader(Activity host, Posters posters)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(posters);
        return (window, url, width) => _ = Task.Run(async () =>
        {
            var bitmap = await posters.LoadAsync(url, width, CancellationToken.None);
            host.RunOnUiThread(() => window.SetArtwork(bitmap));
        });
    }
}
