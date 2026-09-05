using Android.Graphics;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>
/// The one place a <see cref="PhotoEdit"/> becomes pixels.
///
/// <para>Everything about <i>what</i> to do lives in the Domain where it is tested; this
/// only carries it out. The order matters and is fixed here: crop, then turn, then colour —
/// cropping after a turn would mean the owner's rect no longer describes what they framed.</para>
/// </summary>
public static class PhotoRender
{
    /// <summary>
    /// Applies the edit. Returns null when there is nothing to do, which is the caller's
    /// signal to send the original bytes untouched rather than re-encode them for nothing.
    /// </summary>
    public static Bitmap? Apply(Bitmap source, PhotoEdit edit)
    {
        if (edit.IsUnchanged)
        {
            return null;
        }

        var working = source;

        if (!edit.Crop.IsWhole)
        {
            var left = (int)Math.Round(edit.Crop.Left * source.Width);
            var top = (int)Math.Round(edit.Crop.Top * source.Height);
            var width = Math.Max(1, (int)Math.Round((edit.Crop.Right - edit.Crop.Left) * source.Width));
            var height = Math.Max(1, (int)Math.Round((edit.Crop.Bottom - edit.Crop.Top) * source.Height));
            // Rounding can push the far edge one pixel past the bitmap.
            width = Math.Min(width, source.Width - left);
            height = Math.Min(height, source.Height - top);
            working = Bitmap.CreateBitmap(working, left, top, width, height)!;
        }

        if (edit.QuarterTurns != 0)
        {
            var matrix = new Matrix();
            matrix.PostRotate(edit.QuarterTurns * 90);
            var turned = Bitmap.CreateBitmap(
                working, 0, 0, working.Width, working.Height, matrix, filter: true)!;
            if (!ReferenceEquals(working, source))
            {
                working.Recycle();
            }
            working = turned;
        }

        if (edit.Tint == 0 && edit.Filter == PhotoFilter.None)
        {
            return ReferenceEquals(working, source) ? source.Copy(source.GetConfig()!, false) : working;
        }

        var painted = Bitmap.CreateBitmap(working.Width, working.Height, Bitmap.Config.Argb8888!)!;
        using (var canvas = new Canvas(painted))
        using (var paint = new Paint())
        {
            paint.SetColorFilter(new ColorMatrixColorFilter(ColourFor(edit)));
            canvas.DrawBitmap(working, 0, 0, paint);
        }

        if (!ReferenceEquals(working, source))
        {
            working.Recycle();
        }
        return painted;
    }

    // One matrix for both the filter and the tint, because two passes over the pixels would
    // round twice and drift.
    private static ColorMatrix ColourFor(PhotoEdit edit)
    {
        var matrix = new ColorMatrix();
        switch (edit.Filter)
        {
            case PhotoFilter.BlackAndWhite:
                matrix.SetSaturation(0);
                break;
            case PhotoFilter.Faded:
                // Lifted blacks and pulled-back contrast: the look of a print left in the sun.
                matrix.Set([
                    0.86f, 0, 0, 0, 22,
                    0, 0.86f, 0, 0, 22,
                    0, 0, 0.86f, 0, 26,
                    0, 0, 0, 1, 0,
                ]);
                break;
            case PhotoFilter.Bright:
                matrix.Set([
                    1.12f, 0, 0, 0, 8,
                    0, 1.12f, 0, 0, 8,
                    0, 0, 1.12f, 0, 8,
                    0, 0, 0, 1, 0,
                ]);
                break;
        }

        if (edit.Tint != 0)
        {
            // Warm lifts red and drops blue; cool does the reverse. Six units a step is
            // enough to see and small enough that the ends are not a colour cast.
            var shift = edit.Tint * 6f;
            var tint = new ColorMatrix();
            tint.Set([
                1, 0, 0, 0, shift,
                0, 1, 0, 0, 0,
                0, 0, 1, 0, -shift,
                0, 0, 0, 1, 0,
            ]);
            matrix.PostConcat(tint);
        }
        return matrix;
    }
}
