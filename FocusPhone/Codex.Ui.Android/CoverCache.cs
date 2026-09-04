using Android.Graphics;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// Decoded cover art, kept small and kept bounded.
///
/// A library list scrolls past hundreds of covers and rebinds the same handful
/// constantly, so decoding per bind is the jank. Decoding everything and holding it
/// is the other failure — a thousand full-size covers is not something a phone has
/// room for. So: decode downsampled to the window that will show it, and hold the
/// most recent few.
///
/// Deliberately not an async loader. The art is already on local disk (the scanner
/// cached it beside the catalog), so a downsampled decode is cheap enough to do on
/// the bind; adding a background queue would buy placeholder flicker and a race
/// against view recycling for nothing.
/// </summary>
public static class CoverCache
{
    // Enough for a screen of rows and the scroll either side of it, several times
    // over. Small bitmaps, so this is a modest budget rather than a tuned one.
    private const int Capacity = 96;

    private static readonly Dictionary<string, Bitmap> Cached = [];
    private static readonly LinkedList<string> Order = new();
    private static readonly Lock Gate = new();

    /// <summary>
    /// The cover at <paramref name="path"/>, sized for a window of
    /// <paramref name="targetPx"/>. Null when there is no art, or the file is gone,
    /// or it will not decode — all of which the artwork window answers with the
    /// bookplate, so none of them is an error worth reporting.
    /// </summary>
    public static Bitmap? Get(string? path, int targetPx)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var key = $"{path}@{targetPx}";
        lock (Gate)
        {
            if (Cached.TryGetValue(key, out var hit))
            {
                Touch(key);
                return hit;
            }
        }

        var decoded = Decode(path, targetPx);
        if (decoded is null)
        {
            return null;
        }

        lock (Gate)
        {
            // Another bind may have decoded the same cover while this one was working.
            if (Cached.TryGetValue(key, out var raced))
            {
                decoded.Recycle();
                Touch(key);
                return raced;
            }

            Cached[key] = decoded;
            Order.AddFirst(key);
            Evict();
            return decoded;
        }
    }

    private static Bitmap? Decode(string path, int targetPx)
    {
        try
        {
            // Two passes: measure with InJustDecodeBounds, then decode downsampled.
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, bounds);

            var options = new BitmapFactory.Options
            {
                InSampleSize = SampleSize.For(bounds.OutWidth, bounds.OutHeight, targetPx),
            };
            return BitmapFactory.DecodeFile(path, options);
        }
        catch (Exception e) when (e is IOException or OutOfMemoryException or Java.Lang.OutOfMemoryError)
        {
            return null;
        }
    }

    private static void Touch(string key)
    {
        Order.Remove(key);
        Order.AddFirst(key);
    }

    private static void Evict()
    {
        while (Order.Count > Capacity && Order.Last is { } oldest)
        {
            Order.RemoveLast();
            if (Cached.Remove(oldest.Value, out var bitmap))
            {
                // Nothing holds a reference once the row rebinds, and an ImageView
                // showing a recycled bitmap would draw nothing rather than crash —
                // but leaving them to the GC is the safer of the two, so it does.
                _ = bitmap;
            }
        }
    }
}
