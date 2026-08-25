using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using MediaController = AndroidX.Media3.Session.MediaController;

namespace Score;

/// <summary>One line of the live queue: what it is, and whether it is the one playing.</summary>
public sealed record QueuedItem(string Title, string? Subtitle, bool IsCurrent);

/// <summary>What kind of thing the player was last asked to play.</summary>
public enum MediaKind
{
    None,
    Music,
    Podcast,
}

/// <summary>
/// Everything that talks to the media session, and the only thing that holds the
/// <see cref="MediaController"/>.
///
/// It exists because both areas need the player and neither owns it: Music starts a
/// list of tracks, Podcasts starts a queue of episodes with a per-show speed and a
/// resume point, and the shell's ticker drives the now-playing bar from whatever is
/// current. Keeping the controller here is what stops each of those reaching for a
/// field on the Activity.
///
/// It deliberately knows nothing about episodes, tracks, shows or catalogs — callers
/// hand it built <see cref="MediaItem"/>s. The one thing it does remember is
/// <see cref="Kind"/>, because the listening log must not count a podcast as music
/// and asking the podcast area "is something of yours playing?" would put that
/// knowledge back in the shell.
/// </summary>
public sealed class ScorePlayback(Activity host)
{
    // A skip is 30 seconds. Podcast-shaped by origin — the workout and music screens
    // want their own steps — but this is the only skip the app has today.
    private const long SkipMs = 30_000;

    public MediaController? Controller { get; private set; }

    /// <summary>What was last started. None until something plays.</summary>
    public MediaKind Kind { get; private set; }

    /// <summary>
    /// What the current item's playback settings are filed under — a podcast's feed
    /// id, null for music. It travels with the start rather than being looked up,
    /// so the Playing screen can offer per-show speed without having to know what a
    /// show is.
    /// </summary>
    public string? SettingsKey { get; private set; }

    public bool IsPlaying => Controller?.IsPlaying == true;

    public string? CurrentTitle => Controller?.MediaMetadata?.Title?.ToString();

    public string? CurrentMediaId => Controller?.CurrentMediaItem?.MediaId;

    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, Controller?.CurrentPosition ?? 0));

    public TimeSpan Duration =>
        Controller?.Duration is { } ms && ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;

    /// <summary>
    /// Binds to the playback service. The controller is built asynchronously, so it
    /// stays null for a moment after a cold start — callers check
    /// <see cref="Controller"/> rather than assuming.
    /// </summary>
    public void Connect()
    {
        var token = new SessionToken(host, new ComponentName(host, Java.Lang.Class.FromType(typeof(PlaybackService))));
        var future = new MediaController.Builder(host, token).BuildAsync();
        future.AddListener(new Java.Lang.Runnable(() =>
        {
            Controller = (MediaController)future.Get()!;
        }), AndroidX.Core.Content.ContextCompat.GetMainExecutor(host));
    }

    public void Release() => Controller?.Release();

    /// <summary>
    /// Starts a list at an index. Speed and skip-silence are set on every start
    /// rather than left as they were, so a podcast at 1.4× can't carry its speed
    /// into the next album.
    /// </summary>
    public void Start(
        MediaKind kind, IList<MediaItem> items, int index, long startMs, float speed, bool skipSilence,
        string? settingsKey = null)
    {
        if (Controller is null || items.Count == 0)
        {
            return;
        }

        Kind = kind;
        SettingsKey = settingsKey;
        Controller.SetMediaItems(items, index, startMs);
        Controller.SetPlaybackSpeed(speed);
        Controller.Prepare();
        Controller.Play();
        SendSkipSilence(skipSilence);
    }

    /// <summary>
    /// Applies a speed / skip-silence change to what is already playing, without
    /// restarting it. This is the per-show settings screen changing its mind mid-episode.
    /// </summary>
    public void ApplySettings(float speed, bool skipSilence)
    {
        Controller?.SetPlaybackSpeed(speed);
        SendSkipSilence(skipSilence);
    }

    /// <summary>
    /// What the player is holding, in order, with the current item marked.
    ///
    /// Read from the live timeline rather than from either area's stored queue,
    /// because the timeline is by definition the medium that is playing — which is
    /// exactly what the Queue panel is meant to show. The stored queues exist to
    /// survive a restart; this exists to be looked at.
    /// </summary>
    public IReadOnlyList<QueuedItem> Queued()
    {
        if (Controller is not { } controller)
        {
            return [];
        }

        var current = controller.CurrentMediaItemIndex;
        var items = new List<QueuedItem>();
        for (var i = 0; i < controller.MediaItemCount; i++)
        {
            var item = controller.GetMediaItemAt(i);
            items.Add(new QueuedItem(
                item?.MediaMetadata?.Title?.ToString() ?? string.Empty,
                item?.MediaMetadata?.Artist?.ToString(),
                i == current));
        }

        return items;
    }

    /// <summary>Jumps to a position in what is already playing.</summary>
    public void PlayQueuedAt(int index)
    {
        if (Controller is { } controller && index >= 0 && index < controller.MediaItemCount)
        {
            controller.SeekTo(index, 0L);
            controller.Play();
        }
    }

    public void TogglePlayPause()
    {
        if (Controller is null)
        {
            return;
        }

        if (Controller.IsPlaying)
        {
            Controller.Pause();
        }
        else
        {
            Controller.Play();
        }
    }

    public void SkipBack() => Controller?.SeekTo(Math.Max(0, (Controller?.CurrentPosition ?? 0) - SkipMs));

    public void SkipForward() => Controller?.SeekTo((Controller?.CurrentPosition ?? 0) + SkipMs);

    // Skip-silence lives in the service's audio processor, not on the controller, so
    // it travels as a command intent rather than a session call.
    private void SendSkipSilence(bool enabled)
    {
        var intent = new Intent(host, typeof(PlaybackService));
        intent.SetAction(AudioPlaybackCommands.ActionSetSkipSilence);
        intent.PutExtra(AudioPlaybackCommands.ExtraEnabled, enabled);
        host.StartService(intent);
    }
}
