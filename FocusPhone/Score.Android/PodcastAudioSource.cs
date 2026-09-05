using AndroidX.Media3.DataSource;
using Codex.Infrastructure.Podcasts;

namespace Score;

/// <summary>
/// The HTTP source podcast (and Drive) audio is actually fetched through.
///
/// A bare <see cref="DefaultHttpDataSource.Factory"/> is not enough for real podcast
/// hosting, and the two defaults it gets wrong are exactly the two the feed client had
/// already been taught (see <see cref="PodcastHttp"/>):
///
/// <b>Redirects.</b> ExoPlayer will not follow a redirect that changes protocol —
/// https to http or back — and refuses it instead. Almost no podcast link points at
/// the audio: it points at a measurement prefix (Podtrac, Chartable, Megaphone,
/// Simplecast) which bounces through one or more hops to a CDN, and those chains cross
/// protocol often enough that the shows using them simply never play while the shows
/// hosting their own files are fine. That is the reported symptom precisely — most
/// podcasts play, one publisher's never do.
///
/// <b>Identity.</b> The same CDNs that 403 a User-Agent-less feed request 403 a
/// User-Agent-less audio request. The feed parses, the show lists its episodes, and
/// the audio is refused.
/// </summary>
public static class PodcastAudioSource
{
    public static DefaultHttpDataSource.Factory Factory() =>
        new DefaultHttpDataSource.Factory()
            .SetUserAgent(PodcastHttp.UserAgent)!
            .SetAllowCrossProtocolRedirects(true)!;
}
