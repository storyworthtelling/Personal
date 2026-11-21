using Watch.Domain.Face;

namespace FaceStudio.Tests;

/// <summary>A mask is the hour shape's silhouette, drawn where the face's lines are.</summary>
public class MaskTests
{
    [Fact]
    public void MaskCoversItsShape()
    {
        var radius = FaceGeometry.MorningRadius * FaceRead.Radius;
        var box = Masks.Box(radius);
        var mask = Masks.Draw(5, radius);
        var centre = PointFace.Canvas / 2 - box.X;

        Assert.Equal(box.Side, mask.Width);
        Assert.Equal(1, mask.AlphaAt(centre, centre), 3);
        Assert.Equal(0, mask.AlphaAt(0, box.Side - 1), 3);

        var top = HourShape.For(new TimeOnly(5, 0)).Vertices(radius)[0];
        Assert.True(mask.AlphaAt(centre, (int)(centre + top.Y + 3)) > 0.9, "Just inside the top point is filled.");
        Assert.True(mask.AlphaAt(centre, (int)(centre + top.Y - 3)) < 0.1, "Just above the top point is empty.");
    }

    [Fact]
    public void MaskNamesSaySidesAndHalf()
    {
        Assert.Equal("morning7", Masks.Name(7, afternoon: false));
        Assert.Equal("afternoon12", Masks.Name(12, afternoon: true));
    }

    [Fact]
    public void PngIsAPng()
    {
        var png = Masks.Draw(3, 40).Png();

        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png.Take(8));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }
}
