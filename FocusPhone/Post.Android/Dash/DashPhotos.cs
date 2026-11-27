using Android.Graphics;
using Android.Media;
using Codex.Ui;
using Watch.Domain.Dash;

namespace Post;

/// <summary>
/// A photo shrunk for the wrist (P14 B5, W-5.1.3), by <see cref="PhotoFit"/>'s rules: decoded no larger than it needs
/// (<see cref="PhotoFit.SampleFor"/>), stood upright by its EXIF orientation (<see cref="PhotoFit.TurnFor"/>), scaled to
/// each of <see cref="PhotoFit.Sizes"/> in turn and written as a JPEG at each of <see cref="PhotoFit.Qualities"/> until
/// one fits one message. Written again rather than passed on, so none of the picture's own metadata (where and when it
/// was taken) reaches the watch; held in memory only, never written down. Off the main thread: decoding is slow.
/// </summary>
internal static class DashPhotos
{
    private const string Tag = "Dash"; // not copy: a log tag

    /// <summary>The photo as a JPEG that fits <see cref="DashLimits.PhotoBytes"/>, or null when it will not decode or fit.</summary>
    public static byte[]? Shrink(byte[] original)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(original, 0, original.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            return null;
        }

        var decoding = new BitmapFactory.Options { InSampleSize = PhotoFit.SampleFor(bounds.OutWidth, bounds.OutHeight) };
        using var decoded = BitmapFactory.DecodeByteArray(original, 0, original.Length, decoding);
        if (decoded is null)
        {
            return null;
        }

        var degrees = PhotoFit.TurnFor(Orientation(original));
        using var turned = degrees == 0 ? null : Turned(decoded, degrees);
        var upright = turned ?? decoded;
        foreach (var (width, height) in PhotoFit.Sizes(upright.Width, upright.Height))
        {
            // At its own size the photo is written as it is: the platform would hand the same bitmap back, and disposing
            // it here would leave nothing for the next, smaller size.
            using var scaled = width == upright.Width && height == upright.Height ? null : Bitmap.CreateScaledBitmap(upright, width, height, true);
            if (Written(scaled ?? upright) is { } jpeg)
            {
                return jpeg;
            }
        }
        return null;
    }

    // The first quality that fits one message.
    private static byte[]? Written(Bitmap bitmap)
    {
        foreach (var quality in PhotoFit.Qualities)
        {
            using var jpeg = new MemoryStream();
            if (bitmap.Compress(Bitmap.CompressFormat.Jpeg!, quality, jpeg) && PhotoFit.Fits((int)jpeg.Length))
            {
                return jpeg.ToArray();
            }
        }
        return null;
    }

    // The orientation the camera wrote, or upright when there is none to read.
    private static int Orientation(byte[] original)
    {
        try
        {
            using var stream = new MemoryStream(original, writable: false);
            return new ExifInterface(stream).GetAttributeInt(ExifInterface.TagOrientation, 1);
        }
        catch (Java.IO.IOException e)
        {
            HeadLog.Quiet(Tag, "DASH a photo’s orientation wouldn’t read; it goes as it was taken", e);
            return 1;
        }
    }

    private static Bitmap Turned(Bitmap decoded, int degrees)
    {
        using var turn = new Matrix();
        turn.PostRotate(degrees);
        return Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, turn, true);
    }
}
