using System.Globalization;
using System.Text.RegularExpressions;

namespace Storyworthtelling.Materials;

public enum PathVerb
{
    Move,
    Line,
    Cubic,
    Close,
}

/// <summary>
/// One absolute drawing command: the point it ends at and, for a cubic, its two control points. Coordinates are
/// rounded to four places, so a glyph parses to the same numbers on every platform.
/// </summary>
public readonly record struct PathCommand(PathVerb Verb, float X = 0, float Y = 0, float X1 = 0, float Y1 = 0, float X2 = 0, float Y2 = 0);

/// <summary>
/// SVG path data reduced to moves, lines, cubics and closes, so each kit builds the same path from it with the
/// four calls every canvas has. Arcs become cubics (at most a quarter turn each), horizontal and vertical lines
/// become lines, smooth cubics get their reflected control point, and relative commands become absolute.
/// Anything else (quadratics) is refused with the letter named: the glyph set does not use them.
/// </summary>
public static partial class PathData
{
    public static IReadOnlyList<PathCommand> Parse(string data)
    {
        var tokens = Token().Matches(data).Select(m => m.Value).ToList();
        var commands = new List<PathCommand>();
        var index = 0;
        char verb = ' ';
        float x = 0, y = 0, startX = 0, startY = 0, lastControlX = 0, lastControlY = 0;
        var lastWasCubic = false;

        float Number() => index < tokens.Count && !char.IsLetter(tokens[index][0])
            ? float.Parse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture)
            : throw new FormatException($"Path data ended or turned to a command where '{verb}' needed a number: {data}");

        bool MoreNumbers() => index < tokens.Count && !char.IsLetter(tokens[index][0]);

        while (index < tokens.Count)
        {
            if (char.IsLetter(tokens[index][0]))
            {
                verb = tokens[index++][0];
            }
            else if (verb == ' ')
            {
                throw new FormatException($"Path data must start with a command: {data}");
            }

            var relative = char.IsLower(verb);
            float Rx(float value) => relative ? x + value : value;
            float Ry(float value) => relative ? y + value : value;

            switch (char.ToUpperInvariant(verb))
            {
                case 'M':
                    x = Rx(Number());
                    y = Ry(Number());
                    startX = x;
                    startY = y;
                    commands.Add(Point(PathVerb.Move, x, y));
                    lastWasCubic = false;
                    // Further pairs after a move are lines (SVG).
                    verb = relative ? 'l' : 'L';
                    continue;
                case 'L':
                    {
                        var nx = Rx(Number());
                        var ny = Ry(Number());
                        x = nx;
                        y = ny;
                        commands.Add(Point(PathVerb.Line, x, y));
                        lastWasCubic = false;
                        break;
                    }
                case 'H':
                    x = relative ? x + Number() : Number();
                    commands.Add(Point(PathVerb.Line, x, y));
                    lastWasCubic = false;
                    break;
                case 'V':
                    y = relative ? y + Number() : Number();
                    commands.Add(Point(PathVerb.Line, x, y));
                    lastWasCubic = false;
                    break;
                case 'C':
                    {
                        var x1 = Rx(Number());
                        var y1 = Ry(Number());
                        var x2 = Rx(Number());
                        var y2 = Ry(Number());
                        var ex = Rx(Number());
                        var ey = Ry(Number());
                        commands.Add(Cubic(x1, y1, x2, y2, ex, ey));
                        (lastControlX, lastControlY, x, y, lastWasCubic) = (x2, y2, ex, ey, true);
                        break;
                    }
                case 'S':
                    {
                        var x1 = lastWasCubic ? (2 * x) - lastControlX : x;
                        var y1 = lastWasCubic ? (2 * y) - lastControlY : y;
                        var x2 = Rx(Number());
                        var y2 = Ry(Number());
                        var ex = Rx(Number());
                        var ey = Ry(Number());
                        commands.Add(Cubic(x1, y1, x2, y2, ex, ey));
                        (lastControlX, lastControlY, x, y, lastWasCubic) = (x2, y2, ex, ey, true);
                        break;
                    }
                case 'A':
                    {
                        var rx = Number();
                        var ry = Number();
                        var rotation = Number();
                        var large = Number() != 0;
                        var sweep = Number() != 0;
                        var ex = Rx(Number());
                        var ey = Ry(Number());
                        commands.AddRange(Arc(x, y, rx, ry, rotation, large, sweep, ex, ey));
                        (x, y, lastWasCubic) = (ex, ey, false);
                        break;
                    }
                case 'Z':
                    commands.Add(new PathCommand(PathVerb.Close));
                    (x, y, lastWasCubic) = (startX, startY, false);
                    // A close takes no numbers; whatever follows must be a command.
                    if (MoreNumbers())
                    {
                        throw new FormatException($"Numbers followed 'Z' in path data: {data}");
                    }
                    continue;
                default:
                    throw new FormatException($"Path command '{verb}' is not one this parser draws: {data}");
            }

            if (!MoreNumbers())
            {
                continue;
            }
        }

        return commands;
    }

    private static PathCommand Point(PathVerb verb, float x, float y) => new(verb, Round(x), Round(y));

    private static PathCommand Cubic(double x1, double y1, double x2, double y2, double x, double y) =>
        new(PathVerb.Cubic, Round(x), Round(y), Round(x1), Round(y1), Round(x2), Round(y2));

    private static float Round(double value) => (float)Math.Round(value, 4);

    // SVG's endpoint arc as cubics (SVG 1.1 implementation notes, F.6.5), split so no piece turns more than 90°.
    private static IEnumerable<PathCommand> Arc(double x0, double y0, double rx, double ry, double rotationDegrees, bool large, bool sweep, double x, double y)
    {
        if (rx == 0 || ry == 0 || (x0 == x && y0 == y))
        {
            yield return new PathCommand(PathVerb.Line, Round(x), Round(y));
            yield break;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        var phi = rotationDegrees * Math.PI / 180;
        var cos = Math.Cos(phi);
        var sin = Math.Sin(phi);

        var dx = (x0 - x) / 2;
        var dy = (y0 - y) / 2;
        var x1p = (cos * dx) + (sin * dy);
        var y1p = (-sin * dx) + (cos * dy);

        var lambda = (x1p * x1p / (rx * rx)) + (y1p * y1p / (ry * ry));
        if (lambda > 1)
        {
            var scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        var numerator = (rx * rx * ry * ry) - (rx * rx * y1p * y1p) - (ry * ry * x1p * x1p);
        var denominator = (rx * rx * y1p * y1p) + (ry * ry * x1p * x1p);
        var factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (large == sweep ? -1 : 1);
        var cxp = factor * rx * y1p / ry;
        var cyp = -factor * ry * x1p / rx;
        var cx = (cos * cxp) - (sin * cyp) + ((x0 + x) / 2);
        var cy = (sin * cxp) + (cos * cyp) + ((y0 + y) / 2);

        double Angle(double ux, double uy, double vx, double vy)
        {
            var angle = Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
            return angle;
        }

        var theta = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        var pieces = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9);
        var step = delta / pieces;
        var k = 4.0 / 3 * Math.Tan(step / 4);

        (double, double) On(double angle) =>
            ((cos * rx * Math.Cos(angle)) - (sin * ry * Math.Sin(angle)) + cx, (sin * rx * Math.Cos(angle)) + (cos * ry * Math.Sin(angle)) + cy);
        (double, double) Tangent(double angle) =>
            ((-cos * rx * Math.Sin(angle)) - (sin * ry * Math.Cos(angle)), (-sin * rx * Math.Sin(angle)) + (cos * ry * Math.Cos(angle)));

        for (var i = 0; i < pieces; i++)
        {
            var a1 = theta + (i * step);
            var a2 = a1 + step;
            var (p1x, p1y) = On(a1);
            var (t1x, t1y) = Tangent(a1);
            var (p2x, p2y) = i == pieces - 1 ? (x, y) : On(a2);
            var (t2x, t2y) = Tangent(a2);
            yield return Cubic(p1x + (k * t1x), p1y + (k * t1y), p2x - (k * t2x), p2y - (k * t2y), p2x, p2y);
        }
    }

    [GeneratedRegex(@"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")]
    private static partial Regex Token();
}
