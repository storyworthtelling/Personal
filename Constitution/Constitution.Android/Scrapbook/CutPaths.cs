using Android.Graphics;
using Path = Android.Graphics.Path;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;

namespace Constitution.Scrapbook;

/// <summary>Turns the pure cut edges into an Android clip path, in the scrap's own dp space.</summary>
internal static class CutPaths
{
    /// <summary>The kind's cut edge laid over its paper rectangle; a plain rectangle for a kind that is not cut.</summary>
    public static Path For(ScrapKind kind, PaperRect paper)
    {
        var path = new Path();
        var corners = ScrapCuts.Resolve(kind, paper.Width, paper.Height);
        if (corners.Count == 0)
        {
            path.AddRect((float)paper.X, (float)paper.Y, (float)(paper.X + paper.Width), (float)(paper.Y + paper.Height), Path.Direction.Cw!);
            return path;
        }

        path.MoveTo((float)(paper.X + corners[0].X), (float)(paper.Y + corners[0].Y));
        for (var i = 1; i < corners.Count; i++)
        {
            path.LineTo((float)(paper.X + corners[i].X), (float)(paper.Y + corners[i].Y));
        }

        path.Close();
        return path;
    }
}
