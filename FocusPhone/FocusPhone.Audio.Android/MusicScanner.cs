using FocusPhone.Audio.Google;
using FocusPhone.Infrastructure.Storage;

namespace FocusPhone.Audio;

/// <summary>
/// Full library scan: list every audio file under the chosen Drive folder, read
/// each one's tags, and upsert the resulting tracks into the catalog cache. The
/// blocking per-file reads run off the UI thread; progress is reported as
/// (done, total) so the Music pane can show a status sentence.
/// </summary>
public sealed class MusicScanner
{
    private readonly DriveMusicClient driveClient;
    private readonly DeviceAccountTokenProvider tokens;
    private readonly MusicTagReader tagReader;

    public MusicScanner(DriveMusicClient driveClient, DeviceAccountTokenProvider tokens, MusicTagReader tagReader)
    {
        this.driveClient = driveClient;
        this.tokens = tokens;
        this.tagReader = tagReader;
    }

    public async Task ScanAsync(
        string folderId, MusicCatalogCache cache, Action<int, int> onProgress, CancellationToken cancellationToken)
    {
        var files = await driveClient.ListAudioRecursiveAsync(folderId, cancellationToken);

        await Task.Run(() =>
        {
            cache.Clear();
            var total = files.Count;
            var done = 0;
            onProgress(0, total);

            using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var stream = new DriveSeekableStream(file.Id, file.Size, tokens, http);
                    cache.Upsert([tagReader.Read(file.Id, file.Name, stream)]);
                }
                catch
                {
                    // Skip a file we can't read; the rest of the library still scans.
                }

                onProgress(++done, total);
            }
        }, cancellationToken);
    }
}
