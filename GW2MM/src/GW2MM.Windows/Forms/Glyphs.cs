using System.Drawing.Drawing2D;

namespace GW2MM.Windows.Forms;

internal enum Glyph
{
    None, Plus, Gear, Info, EditTable, Trash, Reload, Search, Play, Pause, Stop, Chat, ChevronDown, ChevronRight, CaretUp, CaretDown,
    Sliders, Metronome, Send, Note, Notes, People, Equalizer, Plug, Keyboard, Folder, FileMusic, Check, Close, More, Shuffle, Repeat,
    Next, ArrowUp, ArrowDown, Warning, Diamond, Question,
}

/// <summary>
/// The icon set, drawn as vector shapes on a 16×16 grid (1.5 px round strokes) so it stays sharp at any DPI. Shapes
/// follow the design mock-ups; nothing depends on icon fonts, which Windows versions and Wine render differently.
/// </summary>
internal static class Glyphs
{
    public static void Draw(Graphics g, Glyph glyph, RectangleF box, Color color, float stroke = 1.5f)
    {
        if (glyph == Glyph.None) return;
        var k = Math.Min(box.Width, box.Height) / 16f;
        var ox = box.X + (box.Width - 16 * k) / 2;
        var oy = box.Y + (box.Height - 16 * k) / 2;
        PointF P(float x, float y) => new(ox + x * k, oy + y * k);
        var k96 = k / Theme.Scale;
        var sw = stroke * Theme.Scale * Math.Clamp(MathF.Sqrt(k96), 0.8f, 1.3f);

        var state = g.Save();
        Theme.Smooth(g);
        using var pen = new Pen(color, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var thin = new Pen(color, sw * 0.88f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var flat = new Pen(color, sw * 0.88f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        using var fill = new SolidBrush(color);

        void Line(Pen p, params PointF[] pts) => g.DrawLines(p, pts);
        void Circle(PointF c, float r, bool filled = false)
        {
            var rect = new RectangleF(c.X - r * k, c.Y - r * k, 2 * r * k, 2 * r * k);
            if (filled) g.FillEllipse(fill, rect); else g.DrawEllipse(pen, rect);
        }
        void Oval(PointF c, float rx, float ry) => g.FillEllipse(fill, c.X - rx * k, c.Y - ry * k, 2 * rx * k, 2 * ry * k);
        void RoundRect(float x0, float y0, float x1, float y1, float r, bool filled)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            using var path = Theme.Round(new RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y), r * k);
            if (filled) g.FillPath(fill, path); else g.DrawPath(pen, path);
        }
        void Solid(params PointF[] pts)
        {
            // Filled with softly rounded corners, like the mock-ups' triangles.
            g.FillPolygon(fill, pts);
            using var edge = new Pen(color, 1.4f * k) { LineJoin = LineJoin.Round };
            g.DrawPolygon(edge, pts);
        }

        switch (glyph)
        {
            case Glyph.Plus:
                Line(pen, P(8, 2.5f), P(8, 13.5f));
                Line(pen, P(2.5f, 8), P(13.5f, 8));
                break;
            case Glyph.Gear:
            {
                var c = P(8, 8);
                var pts = new List<PointF>();
                const int teeth = 8;
                for (var i = 0; i < teeth * 4; i++)
                {
                    var a = i / (teeth * 4f) * 2 * MathF.PI - MathF.PI / 2 + MathF.PI / (teeth * 4) * 0.5f;
                    var r = (i % 4) is 0 or 1 ? 7.0f * k : 5.3f * k;
                    pts.Add(new PointF(c.X + r * MathF.Cos(a), c.Y + r * MathF.Sin(a)));
                }
                g.DrawPolygon(pen, pts.ToArray());
                Circle(c, 2.3f);
                break;
            }
            case Glyph.Info:
                Circle(P(8, 8), 6.6f);
                Circle(P(8, 5.0f), 1.0f, filled: true);
                Line(pen, P(8, 7.4f), P(8, 11.2f));
                break;
            case Glyph.Question:
                Circle(P(8, 8), 6.6f);
                Line(pen, P(5.9f, 6.2f), P(6.4f, 4.9f), P(8, 4.3f), P(9.7f, 4.9f), P(10.2f, 6.3f), P(9.4f, 7.6f), P(8, 8.6f), P(8, 9.4f));
                Circle(P(8, 11.6f), 0.95f, filled: true);
                break;
            case Glyph.EditTable:
            {
                // A table with a pencil beside its lower right corner.
                RoundRect(1.5f, 2.5f, 11.6f, 11.0f, 1.8f, false);
                Line(flat, P(2.0f, 5.8f), P(11.1f, 5.8f));
                Line(flat, P(5.6f, 5.8f), P(5.6f, 10.6f));
                using var pencil = new Pen(color, sw * 1.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                Line(pencil, P(10.4f, 14.4f), P(15.0f, 9.8f));
                g.FillPolygon(fill, new[] { P(8.6f, 15.6f), P(9.4f, 13.4f), P(10.8f, 14.8f) });
                break;
            }
            case Glyph.Trash:
                Line(pen, P(2.5f, 4.2f), P(13.5f, 4.2f));
                Line(pen, P(6.2f, 4.0f), P(6.4f, 2.4f), P(9.6f, 2.4f), P(9.8f, 4.0f));
                g.DrawPolygon(pen, new[] { P(3.8f, 4.6f), P(12.2f, 4.6f), P(11.4f, 13.6f), P(4.6f, 13.6f) });
                Line(thin, P(6.6f, 7.0f), P(6.8f, 11.2f));
                Line(thin, P(9.4f, 7.0f), P(9.2f, 11.2f));
                break;
            case Glyph.Reload:
            {
                var c = P(8, 8.4f);
                var r = 5.6f * k;
                g.DrawArc(pen, c.X - r, c.Y - r, 2 * r, 2 * r, -40, 290);
                var t = -40f * MathF.PI / 180f;
                var ex = c.X + r * MathF.Cos(t);
                var ey = c.Y + r * MathF.Sin(t);
                Line(pen, new PointF(ex - 3.6f * k, ey - 0.6f * k), new PointF(ex + 0.2f * k, ey + 0.2f * k), new PointF(ex + 0.6f * k, ey - 3.6f * k));
                break;
            }
            case Glyph.Search:
                Circle(P(7, 7), 4.6f);
                Line(pen, P(10.4f, 10.4f), P(13.8f, 13.8f));
                break;
            case Glyph.Play:
                Solid(P(4.6f, 2.8f), P(13.4f, 8.0f), P(4.6f, 13.2f));
                break;
            case Glyph.Pause:
                RoundRect(4.0f, 3.0f, 6.4f, 13.0f, 0.9f, true);
                RoundRect(9.6f, 3.0f, 12.0f, 13.0f, 0.9f, true);
                break;
            case Glyph.Stop:
                RoundRect(3.6f, 3.6f, 12.4f, 12.4f, 1.8f, true);
                break;
            case Glyph.Next:
                Solid(P(3.0f, 3.2f), P(10.2f, 8.0f), P(3.0f, 12.8f));
                RoundRect(11.2f, 3.2f, 13.2f, 12.8f, 0.8f, true);
                break;
            case Glyph.Chat:
                RoundRect(1.8f, 2.4f, 14.2f, 11.2f, 2.6f, false);
                g.FillPolygon(fill, new[] { P(4.6f, 10.6f), P(4.6f, 14.0f), P(8.0f, 10.9f) });
                break;
            case Glyph.ChevronDown:
                Line(pen, P(4.0f, 6.2f), P(8.0f, 10.2f), P(12.0f, 6.2f));
                break;
            case Glyph.ChevronRight:
                Line(pen, P(6.2f, 4.0f), P(10.2f, 8.0f), P(6.2f, 12.0f));
                break;
            case Glyph.CaretUp:
                g.FillPolygon(fill, new[] { P(4.5f, 10.0f), P(8.0f, 5.6f), P(11.5f, 10.0f) });
                break;
            case Glyph.CaretDown:
                g.FillPolygon(fill, new[] { P(4.5f, 6.0f), P(8.0f, 10.4f), P(11.5f, 6.0f) });
                break;
            case Glyph.Sliders:
                foreach (var (x, knob) in new[] { (3.5f, 10.5f), (8.0f, 5.0f), (12.5f, 9.0f) })
                {
                    Line(thin, P(x, 2.2f), P(x, knob - 2.1f));
                    Line(thin, P(x, knob + 2.1f), P(x, 13.8f));
                    Circle(P(x, knob), 2.1f);
                }
                break;
            case Glyph.Metronome:
                g.DrawPolygon(pen, new[] { P(6.3f, 1.9f), P(9.7f, 1.9f), P(13.3f, 14.0f), P(2.7f, 14.0f) });
                Line(flat, P(4.0f, 10.6f), P(12.0f, 10.6f));
                Line(pen, P(8.0f, 10.6f), P(12.8f, 3.4f));
                Circle(P(11.2f, 5.8f), 1.5f, filled: true);
                break;
            case Glyph.Send:
                Solid(P(1.8f, 2.6f), P(14.6f, 8.0f), P(1.8f, 13.4f), P(3.8f, 8.0f));
                break;
            case Glyph.Note:
                Oval(P(5.4f, 12.2f), 2.6f, 2.0f);
                Line(flat, P(7.7f, 12.0f), P(7.7f, 2.2f));
                Line(pen, P(7.7f, 2.6f), P(12.4f, 4.6f), P(12.4f, 7.4f), P(7.7f, 5.6f));
                break;
            case Glyph.Notes:
                Oval(P(4.4f, 12.4f), 2.2f, 1.75f);
                Oval(P(11.6f, 11.0f), 2.2f, 1.75f);
                Line(flat, P(6.4f, 12.2f), P(6.4f, 3.6f));
                Line(flat, P(13.6f, 10.8f), P(13.6f, 2.2f));
                g.FillPolygon(fill, new[] { P(5.7f, 3.0f), P(14.3f, 1.4f), P(14.3f, 4.4f), P(5.7f, 6.0f) });
                break;
            case Glyph.People:
                Circle(P(6.0f, 5.6f), 2.4f, filled: true);
                Circle(P(11.4f, 6.2f), 2.0f, filled: true);
                g.FillPie(fill, P(1.4f, 9.4f).X, P(1.4f, 9.4f).Y, 9.2f * k, 8.6f * k, 180, 180);
                g.FillPie(fill, P(7.8f, 10.0f).X, P(7.8f, 10.0f).Y, 7.2f * k, 7.0f * k, 180, 180);
                break;
            case Glyph.Equalizer:
                foreach (var (x, h) in new[] { (3.0f, 6.0f), (6.6f, 10.0f), (10.2f, 4.0f), (13.4f, 7.6f) })
                    RoundRect(x - 1.1f, 13.5f - h, x + 1.1f, 13.5f, 0.8f, true);
                break;
            case Glyph.Plug:
                Line(pen, P(6.0f, 1.8f), P(6.0f, 4.6f));
                Line(pen, P(10.0f, 1.8f), P(10.0f, 4.6f));
                RoundRect(3.6f, 4.6f, 12.4f, 9.4f, 2.2f, false);
                Line(pen, P(8.0f, 9.6f), P(8.0f, 12.0f), P(10.4f, 14.2f));
                break;
            case Glyph.Keyboard:
                RoundRect(1.4f, 3.6f, 14.6f, 12.6f, 2f, false);
                foreach (var y in new[] { 6.4f, 8.6f })
                foreach (var x in new[] { 4.2f, 6.6f, 9.0f, 11.4f })
                    Circle(P(x, y), 0.75f, filled: true);
                Line(thin, P(5.4f, 10.6f), P(10.6f, 10.6f));
                break;
            case Glyph.Folder:
                g.DrawPolygon(pen, new[] { P(1.6f, 3.4f), P(6.2f, 3.4f), P(7.6f, 5.0f), P(14.4f, 5.0f), P(14.4f, 12.8f), P(1.6f, 12.8f) });
                break;
            case Glyph.FileMusic:
                g.DrawPolygon(pen, new[] { P(3.2f, 1.6f), P(9.6f, 1.6f), P(13.0f, 5.0f), P(13.0f, 14.4f), P(3.2f, 14.4f) });
                Line(thin, P(9.4f, 1.8f), P(9.4f, 5.2f), P(12.8f, 5.2f));
                Oval(P(6.9f, 11.5f), 1.45f, 1.15f);
                Line(thin, P(8.2f, 11.4f), P(8.2f, 6.8f), P(10.2f, 7.6f));
                break;
            case Glyph.Check:
                Line(pen, P(3.4f, 8.4f), P(6.6f, 11.4f), P(12.8f, 4.8f));
                break;
            case Glyph.Close:
                Line(pen, P(4, 4), P(12, 12));
                Line(pen, P(12, 4), P(4, 12));
                break;
            case Glyph.More:
                foreach (var x in new[] { 3.4f, 8.0f, 12.6f }) Circle(P(x, 8), 1.25f, filled: true);
                break;
            case Glyph.Shuffle:
                Line(pen, P(1.8f, 4.6f), P(5.0f, 4.6f), P(10.6f, 11.4f), P(13.6f, 11.4f));
                Line(pen, P(1.8f, 11.4f), P(5.0f, 11.4f), P(10.6f, 4.6f), P(13.6f, 4.6f));
                Line(pen, P(11.8f, 2.6f), P(13.8f, 4.6f), P(11.8f, 6.6f));
                Line(pen, P(11.8f, 9.4f), P(13.8f, 11.4f), P(11.8f, 13.4f));
                break;
            case Glyph.Repeat:
                Line(pen, P(2.6f, 8.4f), P(2.6f, 6.0f), P(4.0f, 4.6f), P(13.0f, 4.6f));
                Line(pen, P(11.0f, 2.6f), P(13.0f, 4.6f), P(11.0f, 6.6f));
                Line(pen, P(13.4f, 7.6f), P(13.4f, 10.0f), P(12.0f, 11.4f), P(3.0f, 11.4f));
                Line(pen, P(5.0f, 9.4f), P(3.0f, 11.4f), P(5.0f, 13.4f));
                break;
            case Glyph.ArrowUp:
                Line(pen, P(8, 13), P(8, 3.4f));
                Line(pen, P(4.2f, 7.0f), P(8, 3.2f), P(11.8f, 7.0f));
                break;
            case Glyph.ArrowDown:
                Line(pen, P(8, 3), P(8, 12.6f));
                Line(pen, P(4.2f, 9.0f), P(8, 12.8f), P(11.8f, 9.0f));
                break;
            case Glyph.Warning:
                g.DrawPolygon(pen, new[] { P(8, 2.0f), P(14.6f, 13.6f), P(1.4f, 13.6f) });
                Line(pen, P(8, 6.4f), P(8, 9.6f));
                Circle(P(8, 11.7f), 0.95f, filled: true);
                break;
            case Glyph.Diamond:
                g.FillPolygon(fill, new[] { P(8, 3.5f), P(12.5f, 8), P(8, 12.5f), P(3.5f, 8) });
                break;
        }
        g.Restore(state);
    }

    /// <summary>A glyph rendered into a bitmap (for menu items and other places that take an Image).</summary>
    public static Bitmap Image(Glyph glyph, Color color, int size96 = 16)
    {
        var px = Theme.S(size96);
        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        Draw(g, glyph, new RectangleF(0, 0, px, px), color);
        return bmp;
    }
}
