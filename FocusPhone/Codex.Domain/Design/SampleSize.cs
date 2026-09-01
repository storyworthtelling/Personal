namespace Codex.Domain.Design;

/// <summary>
/// How far to downsample a cover while decoding it.
///
/// A library list scrolls past hundreds of covers, and a 40dp window has no use for
/// a 1000px JPEG — decoding at full size is both the memory and the jank. Android's
/// decoder only halves, so this is always a power of two.
///
/// Pure arithmetic, and worth having away from the device: the two ways to get it
/// wrong (sampling by the longer side, or looping on an image that failed to read)
/// are both silent.
/// </summary>
public static class SampleSize
{
    /// <summary>
    /// The largest halving that still leaves both sides at or above
    /// <paramref name="targetPx"/>. Returns 1 — decode as-is — for an unreadable
    /// image or a meaningless target, rather than guessing.
    /// </summary>
    public static int For(int sourceWidth, int sourceHeight, int targetPx)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetPx <= 0)
        {
            return 1;
        }

        // The shorter side decides. The decoder halves both axes together, so
        // sampling by the longer one would take a wide cover past the target on its
        // short side and leave the window to upscale a blurred image.
        var shorter = Math.Min(sourceWidth, sourceHeight);

        var sample = 1;
        while (shorter / (sample * 2) >= targetPx)
        {
            sample *= 2;
        }

        return sample;
    }
}
