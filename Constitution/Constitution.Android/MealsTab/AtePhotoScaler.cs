using Android.Graphics;

namespace Constitution.MealsTab;

/// <summary>
/// Reads the meal photograph the way it is allowed to leave the phone: shrunk so the longest side is about 1,500 pixels and
/// re-encoded as a fresh JPEG. Re-encoding writes the picture's pixels and nothing else, so the EXIF block (place, time, device)
/// of the camera's file does not come along; the estimator strips any block that does. The file on disk is not touched. Empty
/// when the file cannot be decoded, which the estimator reads as "could not estimate".
/// </summary>
internal static class AtePhotoScaler
{
    public const int LongestSide = 1568;
    private const int Quality = 85;

    public static byte[] Read(string path)
    {
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        var longest = Math.Max(bounds.OutWidth, bounds.OutHeight);
        if (longest <= 0)
        {
            return [];
        }

        var sample = 1;
        while (longest / (sample * 2) >= LongestSide)
        {
            sample *= 2;
        }

        using var options = new BitmapFactory.Options { InSampleSize = sample };
        using var bitmap = BitmapFactory.DecodeFile(path, options);
        if (bitmap is null)
        {
            return [];
        }

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Jpeg!, Quality, stream);
        return stream.ToArray();
    }
}
