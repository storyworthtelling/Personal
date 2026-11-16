using Folio.Domain.Reading;
using Microsoft.Maui.Graphics;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// One reader's scores across drafts as a small line beside its score (R13, R37: on the phone only).
/// The points are <see cref="ScoreTrend"/>, tested away from the device; this only paints them, in ink,
/// with the latest draft's point filled. Colour comes from <c>PaperColorTokens</c>, as the readiness
/// chart's does, so it paints without a running app.
/// </summary>
public sealed class ScoreTrendDrawable : IDrawable
{
    public const float Width = 64f;
    public const float Height = 22f;

    private const float Inset = 3f;
    private const float MarkerRadius = 2.5f;

    public required IReadOnlyList<DraftScore> History { get; init; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var points = ScoreTrend.Points(History, dirtyRect.Width - (2 * Inset), dirtyRect.Height - (2 * Inset));
        if (points.Count == 0)
            return;

        canvas.StrokeColor = Color.FromArgb(PaperColorTokens.InkSoft);
        canvas.StrokeSize = 1.25f;
        var path = new PathF();
        path.MoveTo(points[0].X + Inset, points[0].Y + Inset);
        foreach (var point in points.Skip(1))
            path.LineTo(point.X + Inset, point.Y + Inset);
        canvas.DrawPath(path);

        var last = points[^1];
        canvas.FillColor = Color.FromArgb(PaperColorTokens.Ink);
        canvas.FillCircle(last.X + Inset, last.Y + Inset, MarkerRadius);
    }
}
