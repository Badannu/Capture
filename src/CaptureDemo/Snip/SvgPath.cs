using System.Drawing.Drawing2D;

namespace CaptureDemo.Snip;

/// <summary>
/// Minimal SVG path parser covering M/L/H/V/C/S/Q/T/A/Z (absolute and relative).
/// Icons are authored on a 24×24 grid and scaled at paint time, so every glyph
/// shares one optical weight instead of a pile of hand-tuned per-pixel magic numbers.
/// </summary>
internal static class SvgPath
{
    public const float Grid = 24f;

    public static GraphicsPath Parse(string data, float scale) => Parse(data, scale, 0f, 0f);

    /// <summary>Builds the path in 24-grid units, then scales and offsets it.</summary>
    public static GraphicsPath Parse(string data, float scale, float offsetX, float offsetY)
    {
        var path = new GraphicsPath(FillMode.Winding);
        if (string.IsNullOrWhiteSpace(data))
        {
            return path;
        }

        var reader = new Reader(data);
        var command = 'M';
        float cx = 0f, cy = 0f, startX = 0f, startY = 0f;
        float quadX = 0f, quadY = 0f, cubeX = 0f, cubeY = 0f;

        while (true)
        {
            reader.SkipSeparators();
            if (reader.End)
            {
                break;
            }

            if (char.IsLetter(reader.Peek()))
            {
                command = reader.Peek();
                reader.Advance();
            }
            else if (command is 'M')
            {
                command = 'L';
            }
            else if (command is 'm')
            {
                command = 'l';
            }

            var relative = char.IsLower(command);
            var kind = char.ToUpperInvariant(command);
            if (kind is 'Z')
            {
                path.CloseFigure();
                cx = startX;
                cy = startY;
                continue;
            }

            reader.SkipSeparators();
            if (reader.End)
            {
                break;
            }

            var ox = relative ? cx : 0f;
            var oy = relative ? cy : 0f;

            switch (kind)
            {
                case 'M':
                {
                    cx = ox + reader.Number();
                    cy = oy + reader.Number();
                    startX = cx;
                    startY = cy;
                    path.StartFigure();
                    break;
                }

                case 'L':
                {
                    var x = ox + reader.Number();
                    var y = oy + reader.Number();
                    path.AddLine(cx, cy, x, y);
                    cx = x;
                    cy = y;
                    break;
                }

                case 'H':
                {
                    var x = (relative ? cx : 0f) + reader.Number();
                    path.AddLine(cx, cy, x, cy);
                    cx = x;
                    break;
                }

                case 'V':
                {
                    var y = (relative ? cy : 0f) + reader.Number();
                    path.AddLine(cx, cy, cx, y);
                    cy = y;
                    break;
                }

                case 'C':
                {
                    var c1x = ox + reader.Number();
                    var c1y = oy + reader.Number();
                    var c2x = ox + reader.Number();
                    var c2y = oy + reader.Number();
                    var x = ox + reader.Number();
                    var y = oy + reader.Number();
                    path.AddBezier(cx, cy, c1x, c1y, c2x, c2y, x, y);
                    cubeX = c2x;
                    cubeY = c2y;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'S':
                {
                    var reflect = command is 'C' or 'S' or 'c' or 's';
                    var c1x = reflect ? (2f * cx) - cubeX : cx;
                    var c1y = reflect ? (2f * cy) - cubeY : cy;
                    var c2x = ox + reader.Number();
                    var c2y = oy + reader.Number();
                    var x = ox + reader.Number();
                    var y = oy + reader.Number();
                    path.AddBezier(cx, cy, c1x, c1y, c2x, c2y, x, y);
                    cubeX = c2x;
                    cubeY = c2y;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'Q':
                {
                    var qx = ox + reader.Number();
                    var qy = oy + reader.Number();
                    var x = ox + reader.Number();
                    var y = oy + reader.Number();
                    path.AddBezier(
                        cx,
                        cy,
                        cx + (2f / 3f * (qx - cx)),
                        cy + (2f / 3f * (qy - cy)),
                        x + (2f / 3f * (qx - x)),
                        y + (2f / 3f * (qy - y)),
                        x,
                        y);
                    quadX = qx;
                    quadY = qy;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'T':
                {
                    var reflect = command is 'Q' or 'T' or 'q' or 't';
                    var qx = reflect ? (2f * cx) - quadX : cx;
                    var qy = reflect ? (2f * cy) - quadY : cy;
                    var x = ox + reader.Number();
                    var y = oy + reader.Number();
                    path.AddBezier(
                        cx,
                        cy,
                        cx + (2f / 3f * (qx - cx)),
                        cy + (2f / 3f * (qy - cy)),
                        x + (2f / 3f * (qx - x)),
                        y + (2f / 3f * (qy - y)),
                        x,
                        y);
                    quadX = qx;
                    quadY = qy;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'A':
                {
                    var rx = reader.Number();
                    var ry = reader.Number();
                    var rotation = reader.Number();
                    var large = reader.Flag();
                    var sweep = reader.Flag();
                    var x = ox + reader.Number();
                    var y = oy + reader.Number();
                    ArcTo(path, cx, cy, rx, ry, rotation, large, sweep, x, y);
                    cx = x;
                    cy = y;
                    break;
                }

                default:
                    reader.Number();
                    break;
            }
        }

        // GDI+ applies p' = p * M and Matrix.Scale/Translate default to Prepend, so
        // translating first would scale the offset too and push every glyph off its
        // box (invisible at large sizes). Append keeps the offset in device units.
        using var transform = new Matrix();
        transform.Scale(scale, scale);
        transform.Translate(offsetX, offsetY, MatrixOrder.Append);
        path.Transform(transform);
        return path;
    }

    private static void ArcTo(
        GraphicsPath path,
        float x1,
        float y1,
        float rx,
        float ry,
        float rotationDegrees,
        bool largeArc,
        bool sweep,
        float x2,
        float y2)
    {
        if (Math.Abs(x1 - x2) < 1e-4f && Math.Abs(y1 - y2) < 1e-4f)
        {
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx < 1e-4f || ry < 1e-4f)
        {
            path.AddLine(x1, y1, x2, y2);
            return;
        }

        var phi = rotationDegrees * Math.PI / 180.0;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);
        var dx = (x1 - x2) / 2.0;
        var dy = (y1 - y2) / 2.0;
        var x1p = (cosPhi * dx) + (sinPhi * dy);
        var y1p = (-sinPhi * dx) + (cosPhi * dy);

        var lambda = ((x1p * x1p) / (rx * rx)) + ((y1p * y1p) / (ry * ry));
        if (lambda > 1.0)
        {
            var grow = Math.Sqrt(lambda);
            rx = (float)(rx * grow);
            ry = (float)(ry * grow);
        }

        var numerator = ((double)rx * rx * ry * ry) - ((double)rx * rx * y1p * y1p) - ((double)ry * ry * x1p * x1p);
        var denominator = ((double)rx * rx * y1p * y1p) + ((double)ry * ry * x1p * x1p);
        var coefficient = denominator <= 0.0 ? 0.0 : Math.Sqrt(Math.Max(0.0, numerator / denominator));
        if (largeArc == sweep)
        {
            coefficient = -coefficient;
        }

        var cxp = coefficient * rx * y1p / ry;
        var cyp = -coefficient * ry * x1p / rx;
        var centerX = (cosPhi * cxp) - (sinPhi * cyp) + ((x1 + x2) / 2.0);
        var centerY = (sinPhi * cxp) + (cosPhi * cyp) + ((y1 + y2) / 2.0);

        var ux = (x1p - cxp) / rx;
        var uy = (y1p - cyp) / ry;
        var vx = (-x1p - cxp) / rx;
        var vy = (-y1p - cyp) / ry;
        var start = Math.Atan2(uy, ux);
        var delta = Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)));
        var step = delta / segments;
        var alpha = 4.0 / 3.0 * Math.Tan(step / 4.0);

        for (var i = 0; i < segments; i++)
        {
            var a0 = start + (i * step);
            var a1 = a0 + step;
            var p0 = MapOnEllipse(centerX, centerY, cosPhi, sinPhi, rx, ry, a0);
            var p3 = MapOnEllipse(centerX, centerY, cosPhi, sinPhi, rx, ry, a1);
            var d0 = EllipseTangent(cosPhi, sinPhi, rx, ry, a0);
            var d3 = EllipseTangent(cosPhi, sinPhi, rx, ry, a1);
            path.AddBezier(
                (float)p0.X,
                (float)p0.Y,
                (float)(p0.X + (alpha * d0.X)),
                (float)(p0.Y + (alpha * d0.Y)),
                (float)(p3.X - (alpha * d3.X)),
                (float)(p3.Y - (alpha * d3.Y)),
                (float)p3.X,
                (float)p3.Y);
        }
    }

    private static (double X, double Y) MapOnEllipse(
        double centerX,
        double centerY,
        double cosPhi,
        double sinPhi,
        double rx,
        double ry,
        double angle)
    {
        var px = rx * Math.Cos(angle);
        var py = ry * Math.Sin(angle);
        return (centerX + (cosPhi * px) - (sinPhi * py), centerY + (sinPhi * px) + (cosPhi * py));
    }

    private static (double X, double Y) EllipseTangent(
        double cosPhi,
        double sinPhi,
        double rx,
        double ry,
        double angle)
    {
        var px = -rx * Math.Sin(angle);
        var py = ry * Math.Cos(angle);
        return ((cosPhi * px) - (sinPhi * py), (sinPhi * px) + (cosPhi * py));
    }

    private sealed class Reader
    {
        private readonly string _text;
        private int _index;

        public Reader(string text) => _text = text;

        public bool End => _index >= _text.Length;

        public char Peek() => _text[_index];

        public void Advance() => _index++;

        public void SkipSeparators()
        {
            while (_index < _text.Length && (char.IsWhiteSpace(_text[_index]) || _text[_index] == ','))
            {
                _index++;
            }
        }

        public bool Flag()
        {
            SkipSeparators();
            if (End)
            {
                return false;
            }

            var value = _text[_index] == '1';
            _index++;
            return value;
        }

        public float Number()
        {
            SkipSeparators();
            var begin = _index;
            if (!End && (_text[_index] == '-' || _text[_index] == '+'))
            {
                _index++;
            }

            while (!End && char.IsDigit(_text[_index]))
            {
                _index++;
            }

            if (!End && _text[_index] == '.')
            {
                _index++;
                while (!End && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            if (!End && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                _index++;
                if (!End && (_text[_index] == '-' || _text[_index] == '+'))
                {
                    _index++;
                }

                while (!End && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            if (_index == begin)
            {
                _index++;
                return 0f;
            }

            var span = _text.AsSpan(begin, _index - begin);
            return float.TryParse(
                span,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0f;
        }
    }
}
