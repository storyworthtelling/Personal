using AndroidX.Media3.DataSource;
using FocusPhone.Audio.Google;

namespace FocusPhone.Audio;

/// <summary>
/// Rewrites <c>drive://{fileId}</c> media URIs to the Drive <c>alt=media</c> download
/// URL and injects a freshly-minted Bearer token on each open, so ExoPlayer streams
/// the owner's Drive files (with mid-playback token refresh). Podcast http(s) URIs
/// pass through untouched, so one factory serves both sides.
/// </summary>
public sealed class DriveMediaResolver : Java.Lang.Object, ResolvingDataSource.IResolver
{
    private readonly DeviceAccountTokenProvider tokens;

    public DriveMediaResolver(DeviceAccountTokenProvider tokens) => this.tokens = tokens;

    public DataSpec ResolveDataSpec(DataSpec dataSpec)
    {
        if (dataSpec.Uri?.Scheme != "drive")
        {
            return dataSpec;
        }

        var fileId = dataSpec.Uri.Host;
        var token = tokens.GetAccessToken().GetAwaiter().GetResult();
        var resolved = Android.Net.Uri.Parse($"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media")!;
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token };
        return dataSpec.WithUri(resolved).WithAdditionalHeaders(headers);
    }

    public Android.Net.Uri? ResolveReportedUri(Android.Net.Uri uri) => uri;
}
