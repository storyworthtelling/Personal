namespace Codex.Domain.Plate;

/// <summary>How long and how large the caller allows a requested video to be; null where it set no limit.</summary>
public sealed record VideoErrandLimits(TimeSpan? Duration, long? SizeBytes);

/// <summary>How a recording for another app ended.</summary>
public enum VideoRecordingEnd
{
    /// <summary>The owner stopped it.</summary>
    Finished,

    /// <summary>The caller's duration limit stopped it. The file is complete.</summary>
    DurationLimit,

    /// <summary>The caller's size limit stopped it. The file is complete.</summary>
    SizeLimit,

    /// <summary>It stopped before anything usable was written.</summary>
    NothingRecorded,

    Failed,
}

/// <summary>
/// The video errand (audit W7.4): another app sends <c>VIDEO_CAPTURE</c> and waits in
/// <c>onActivityResult</c>. Plate records to its own cache, then hands the file back the way the photo
/// errand does — into the caller's <c>EXTRA_OUTPUT</c> when it gave one, else into the camera roll.
/// </summary>
public static class VideoErrand
{
    /// <summary><c>MediaStore.EXTRA_DURATION_LIMIT</c>, in seconds.</summary>
    public const string ExtraDurationLimit = "android.intent.extra.durationLimit";

    /// <summary><c>MediaStore.EXTRA_SIZE_LIMIT</c>, in bytes.</summary>
    public const string ExtraSizeLimit = "android.intent.extra.sizeLimit";

    public static VideoErrandLimits Limits(int durationSeconds, long sizeBytes) => new(
        durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : null,
        sizeBytes > 0 ? sizeBytes : null);

    /// <summary>A recording stopped by a limit the caller set is the video it asked for.</summary>
    public static bool IsUsable(VideoRecordingEnd end) =>
        end is VideoRecordingEnd.Finished or VideoRecordingEnd.DurationLimit or VideoRecordingEnd.SizeLimit;
}
