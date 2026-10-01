using System;
using Avalonia;
using Avalonia.Media;

namespace ProtonBackup.UI.Views;

/// <summary>
/// Icons for the sidebar's bottom-row Log/Settings buttons. Neither app had
/// these before (they used to be plain text items in the main nav list), so
/// there is no existing glyph to port; built with plain trigonometry rather
/// than a hand-transcribed StreamGeometry path string, which would be easy to
/// get subtly wrong with no way to preview it while writing it. Mirrors
/// icons.py's render_gear/render_log in the Python port.
/// </summary>
public static class NavIcons
{
    /// <summary>
    /// A ring (two concentric circles filled with the odd-even rule, rather
    /// than a boolean subtract) with evenly spaced rectangular teeth around
    /// it. An earlier version was a solid disc with teeth and no hole, which
    /// at a glance read more like a sun/asterisk than a gear; the odd-even
    /// trick gets the same hollow-center look as a real subtract, using only
    /// GeometryGroup.FillRule — an API both ports already had reliable
    /// access to (see icons.py's render_gear in the Python port).
    /// </summary>
    public static Geometry Gear(double size = 18)
    {
        double cx = size / 2, cy = size / 2;
        double outerR = size * 0.28;
        double innerR = size * 0.16;
        double toothLen = size * 0.12;
        double toothHalfWidth = size * 0.075;
        const int toothCount = 8;

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new EllipseGeometry(new Rect(cx - outerR, cy - outerR, outerR * 2, outerR * 2)));
        group.Children.Add(new EllipseGeometry(new Rect(cx - innerR, cy - innerR, innerR * 2, innerR * 2)));

        for (var i = 0; i < toothCount; i++)
        {
            var angle = 2 * Math.PI / toothCount * i;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            // Perpendicular direction, for the tooth's left/right edges.
            var px = -sin;
            var py = cos;

            var innerCenter = new Point(cx + cos * outerR, cy + sin * outerR);
            var outerCenter = new Point(cx + cos * (outerR + toothLen), cy + sin * (outerR + toothLen));

            var tooth = new StreamGeometry();
            using (var ctx = tooth.Open())
            {
                ctx.BeginFigure(new Point(innerCenter.X + px * toothHalfWidth, innerCenter.Y + py * toothHalfWidth), true);
                ctx.LineTo(new Point(outerCenter.X + px * toothHalfWidth, outerCenter.Y + py * toothHalfWidth));
                ctx.LineTo(new Point(outerCenter.X - px * toothHalfWidth, outerCenter.Y - py * toothHalfWidth));
                ctx.LineTo(new Point(innerCenter.X - px * toothHalfWidth, innerCenter.Y - py * toothHalfWidth));
                ctx.EndFigure(true);
            }
            group.Children.Add(tooth);
        }

        return group;
    }

    public static Geometry Log(double size = 18)
    {
        var group = new GeometryGroup { FillRule = FillRule.NonZero };
        var dotR = size * 0.06;
        var lineH = size * 0.09;
        var dotX = size * 0.16;
        var lineLeft = size * 0.34;
        var lineRight = size * 0.86;
        foreach (var y in new[] { size * 0.28, size * 0.5, size * 0.72 })
        {
            group.Children.Add(new EllipseGeometry(new Rect(dotX - dotR, y - dotR, dotR * 2, dotR * 2)));
            group.Children.Add(new RectangleGeometry(new Rect(lineLeft, y - lineH / 2, lineRight - lineLeft, lineH)));
        }
        return group;
    }
}
