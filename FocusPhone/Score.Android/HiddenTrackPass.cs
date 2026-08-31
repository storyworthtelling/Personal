using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui.Voice;
using Score.Google;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// Runs hidden-track detection and Claude naming for one long track, folding the
/// result into the rows <see cref="MusicScanner"/> should write for it.
///
/// Built lazily by the scanner rather than injected, because it needs nothing the
/// scanner doesn't already hold — the same device token provider that reads tags —
/// and everything below it (the vault, the shared Claude key) is best-effort exactly
/// like <see cref="Score.Google.MusicTasteService"/>, which this mirrors.
/// </summary>
public sealed class HiddenTrackPass
{
    // Below this, decoding the whole file to look for a hidden track costs more than
    // it is ever likely to find — a hidden track lives at the end of an unusually
    // long final track, not on an ordinary three-minute song.
    public static readonly TimeSpan LongTrackThreshold = TimeSpan.FromMinutes(7);

    // A quiet passage inside a song rarely runs this long; a hidden track's leading
    // silence usually does. Levels are normalized 0..1 RMS (AudioLevelReader.ReadLevels).
    private const float SilenceThreshold = 0.02f;
    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinSegment = TimeSpan.FromSeconds(30);

    private readonly AudioLevelReader levelReader = new();
    private readonly IHiddenTrackNamer namer;
    private readonly DriveVoiceOptions options;

    public HiddenTrackPass(DeviceAccountTokenProvider tokens)
    {
        Func<CancellationToken, Task<DriveService>> driveFactory = async _ =>
        {
            var token = await tokens.GetAccessToken();
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                ApplicationName = "Score",
                HttpClientFactory = new AndroidGoogleHttpClientFactory()
            });
        };

        var vaultStore = new DriveSecretsVaultStore(new DriveTextFile(
            driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
        options = new DriveVoiceOptions(new VaultVoiceConfigStore(vaultStore, () => VaultPassphraseStore.Passphrase));
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
        namer = new HiddenTrackNamer(http, options);
    }

    public bool IsWorthScanning(Track track) => track.Duration >= LongTrackThreshold;

    public async Task<IReadOnlyList<Track>> RunAsync(
        Track track, System.IO.Stream stream, CancellationToken cancellationToken)
    {
        var (levels, frameDuration) = await Task.Run(
            () => levelReader.ReadLevels(stream, cancellationToken), cancellationToken);
        var gaps = HiddenTrackDetector.FindGaps(levels, frameDuration, SilenceThreshold, MinGap);
        var segments = HiddenTrackSplitter.Plan(track.Duration, gaps, MinSegment);
        if (segments.Count <= 1)
        {
            return [track];
        }

        // Naming is on top of a real split: the split still lands even when the vault
        // key isn't set or Claude is unreachable, the same "best-effort, never blocks
        // the real work" rule MusicTasteService follows for the rest of Discovery.
        string? hiddenTitle = null;
        try
        {
            await options.RefreshAsync(cancellationToken);
            var result = await namer.NameAsync(track.AlbumName, track.ArtistName, track.Title, cancellationToken);
            hiddenTitle = result.Found ? result.Title : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Unnamed rather than unsplit.
        }

        return HiddenTrackSplit.Apply(track, segments, hiddenTitle);
    }
}
