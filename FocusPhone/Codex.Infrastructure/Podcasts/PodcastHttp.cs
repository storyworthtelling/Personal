namespace Codex.Infrastructure.Podcasts;

/// <summary>
/// What Score has to say about itself, and put up with, when talking to a podcast host.
///
/// It lives here rather than inside the feed client because the feed and the audio are
/// fetched by two entirely different stacks — <see cref="RssFeedClient"/> over HttpClient,
/// and the player over ExoPlayer's own HTTP data source — and they were not agreeing.
/// The feed client had learned both of these lessons; the player had learned neither, so
/// a show would list its episodes perfectly and then refuse to play any of them.
/// </summary>
public static class PodcastHttp
{
    /// <summary>
    /// Several podcast CDNs answer 403, or serve a redirect page instead of the
    /// content, to a client that sends no User-Agent — and neither .NET nor
    /// ExoPlayer sends a useful one by default.
    /// </summary>
    public const string UserAgent = "FocusPhone-Podcasts/1.0 (+https://storyworthtelling.com)";
}
