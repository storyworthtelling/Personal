using System.Globalization;
using System.Text.RegularExpressions;

namespace Codex.Domain.Apps;

/// <summary>A point in the glyph's 0–100 viewBox.</summary>
public readonly record struct GlyphPoint(float X, float Y);

/// <summary>
/// Parses an AI-generated bookend glyph path (the validated "d" from <see cref="GlyphPath"/>) into
/// flattened polylines the head fills solid as the bookend's bronze statue, without pulling an SVG
/// engine into the launcher. Supports the move/line/horizontal/vertical/cubic/quadratic/close commands
/// (absolute and relative); an arc or smooth-curve command it can't flatten makes it return null, and
/// the head then falls back to the pencil monogram. Curves are flattened to short segments — rough is
/// fine at bookend size. Pure, so the parsing is unit-tested without a device.
/// </summary>
public static partial class SvgGlyph
{
    /// <summary>The glyph coordinate space: paths are authored in a 0–ViewBox square.</summary>
    public const float ViewBox = 100f;

    private const int CurveSegments = 10;
    private const int MaxPoints = 4000;

    [GeneratedRegex(@"[MmLlHhVvCcSsQqTtAaZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?")]
    private static partial Regex Token();

    /// <summary>The subpaths as polylines in the 0–100 viewBox, or null if the path can't be flattened.</summary>
    public static IReadOnlyList<IReadOnlyList<GlyphPoint>>? Parse(string? d)
    {
        if (string.IsNullOrWhiteSpace(d))
        {
            return null;
        }

        var tokens = Token().Matches(d).Select(m => m.Value).ToList();
        var subpaths = new List<IReadOnlyList<GlyphPoint>>();
        var current = new List<GlyphPoint>();
        float cx = 0, cy = 0, sx = 0, sy = 0;
        var i = 0;

        void Commit()
        {
            if (current.Count > 1)
            {
                subpaths.Add(current);
            }
            current = [];
        }

        while (i < tokens.Count)
        {
            var command = tokens[i];
            if (command.Length != 1 || !char.IsLetter(command[0]))
            {
                return null; // expected a command here — malformed
            }
            i++;
            var relative = char.IsLower(command[0]);
            switch (char.ToUpperInvariant(command[0]))
            {
                case 'M':
                    if (!ReadPoint(tokens, ref i, relative, cx, cy, out cx, out cy))
                    {
                        return null;
                    }
                    Commit();
                    sx = cx; sy = cy;
                    current.Add(new GlyphPoint(cx, cy));
                    // Extra coordinate pairs after a move are implicit line-tos.
                    while (HasNumber(tokens, i))
                    {
                        if (!ReadPoint(tokens, ref i, relative, cx, cy, out cx, out cy)) return null;
                        current.Add(new GlyphPoint(cx, cy));
                    }
                    break;
                case 'L':
                    while (HasNumber(tokens, i))
                    {
                        if (!ReadPoint(tokens, ref i, relative, cx, cy, out cx, out cy)) return null;
                        current.Add(new GlyphPoint(cx, cy));
                    }
                    break;
                case 'H':
                    while (HasNumber(tokens, i))
                    {
                        if (!ReadNumber(tokens, ref i, out var x)) return null;
                        cx = relative ? cx + x : x;
                        current.Add(new GlyphPoint(cx, cy));
                    }
                    break;
                case 'V':
                    while (HasNumber(tokens, i))
                    {
                        if (!ReadNumber(tokens, ref i, out var y)) return null;
                        cy = relative ? cy + y : y;
                        current.Add(new GlyphPoint(cx, cy));
                    }
                    break;
                case 'C':
                    while (HasNumber(tokens, i))
                    {
                        if (!ReadPoint(tokens, ref i, relative, cx, cy, out var x1, out var y1) ||
                            !ReadPoint(tokens, ref i, relative, cx, cy, out var x2, out var y2) ||
                            !ReadPoint(tokens, ref i, relative, cx, cy, out var ex, out var ey))
                        {
                            return null;
                        }
                        FlattenCubic(current, cx, cy, x1, y1, x2, y2, ex, ey);
                        cx = ex; cy = ey;
                    }
                    break;
                case 'Q':
                    while (HasNumber(tokens, i))
                    {
                        if (!ReadPoint(tokens, ref i, relative, cx, cy, out var x1, out var y1) ||
                            !ReadPoint(tokens, ref i, relative, cx, cy, out var ex, out var ey))
                        {
                            return null;
                        }
                        FlattenQuadratic(current, cx, cy, x1, y1, ex, ey);
                        cx = ex; cy = ey;
                    }
                    break;
                case 'Z':
                    current.Add(new GlyphPoint(sx, sy));
                    cx = sx; cy = sy;
                    break;
                default:
                    return null; // S/T/A and anything else — let the head use the monogram
            }
            if (current.Count + subpaths.Sum(s => s.Count) > MaxPoints)
            {
                return null;
            }
        }

        Commit();
        return subpaths.Count == 0 ? null : subpaths;
    }

    private static bool HasNumber(IReadOnlyList<string> tokens, int i) =>
        i < tokens.Count && (char.IsDigit(tokens[i][0]) || tokens[i][0] is '-' or '.' or '+');

    private static bool ReadNumber(IReadOnlyList<string> tokens, ref int i, out float value)
    {
        if (!HasNumber(tokens, i) ||
            !float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }
        i++;
        return true;
    }

    private static bool ReadPoint(
        IReadOnlyList<string> tokens, ref int i, bool relative, float baseX, float baseY,
        out float x, out float y)
    {
        x = y = 0;
        if (!ReadNumber(tokens, ref i, out var rx) || !ReadNumber(tokens, ref i, out var ry))
        {
            return false;
        }
        x = relative ? baseX + rx : rx;
        y = relative ? baseY + ry : ry;
        return true;
    }

    private static void FlattenCubic(
        List<GlyphPoint> into, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        for (var s = 1; s <= CurveSegments; s++)
        {
            var t = (float)s / CurveSegments;
            var u = 1 - t;
            var x = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            var y = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
            into.Add(new GlyphPoint(x, y));
        }
    }

    private static void FlattenQuadratic(
        List<GlyphPoint> into, float x0, float y0, float x1, float y1, float x2, float y2)
    {
        for (var s = 1; s <= CurveSegments; s++)
        {
            var t = (float)s / CurveSegments;
            var u = 1 - t;
            var x = u * u * x0 + 2 * u * t * x1 + t * t * x2;
            var y = u * u * y0 + 2 * u * t * y1 + t * t * y2;
            into.Add(new GlyphPoint(x, y));
        }
    }
}
