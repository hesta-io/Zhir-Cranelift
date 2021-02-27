using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FabricJSParser
{
    public class Table
    {
        public List<Region> Rows { get; set; }
        public List<Region> Columns { get; set; }

        public Region GetRegion()
        {
            if (Rows.Count == 0)
                return new Region(0, 0, 0, 0);

            var topMostRow = Rows.OrderBy(r => r.Top).First();
            var column = Columns.First();

            return new Region(topMostRow.Left, topMostRow.Top, topMostRow.Width, column.Height);
        }
    }

    public class Region
    {
        public Region(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public double Left { get; }
        public double Right => Left + Width;

        public double Top { get; }
        public double Bottom => Top + Height;

        public double Width { get; }
        public double Height { get; }

        public bool Contains(int x, int y) =>
            x >= Left && x <= Left + Width &&
            y >= Top && y <= Top + Height;

    }

    class FabricJSCanvas
    {
        public string version { get; set; }
        public Group[] objects { get; set; }
    }

    class Group
    {
        public string type { get; set; }
        public string version { get; set; }
        public string originX { get; set; }
        public string originY { get; set; }
        public double left { get; set; }
        public double top { get; set; }
        public double width { get; set; }
        public double height { get; set; }
        public string fill { get; set; }
        public object stroke { get; set; }
        public double strokeWidth { get; set; }
        public object strokeDashArray { get; set; }
        public string strokeLineCap { get; set; }
        public double strokeDashOffset { get; set; }
        public string strokeLineJoin { get; set; }
        public double strokeMiterLimit { get; set; }
        public double scaleX { get; set; }
        public double scaleY { get; set; }
        public double angle { get; set; }
        public bool flipX { get; set; }
        public bool flipY { get; set; }
        public double opacity { get; set; }
        public object shadow { get; set; }
        public bool visible { get; set; }
        public string backgroundColor { get; set; }
        public string fillRule { get; set; }
        public string padoubleFirst { get; set; }
        public string globalCompositeOperation { get; set; }
        public double skewX { get; set; }
        public double skewY { get; set; }
        public Shape[] objects { get; set; }
    }

    class Shape
    {
        public Region ToRegion(double parentLeft, double parentTop)
        {
            return new Region(left + parentLeft, top + parentTop, width, height);
        }

        public bool IsLine() => type == "line";
        public bool IsVerticalLine() => IsLine() &&
            x1 == x2 && y1 != y2;
        public bool IsHorizontalLine() => IsLine() &&
            x1 != x2 && y1 == y2;

        public bool IsRect() => type == "rect";

        public bool IsVerticalRect() => IsRect() &&
            height > width;

        public bool IsHorizontalRect() => IsRect() &&
            height <= width;

        public bool Contains(int x, int y) =>
            x >= left && x <= left + width &&
            y >= top && y <= top + height;

        /// <summary>
        /// rect, line, circle,
        /// </summary>
        public string type { get; set; }
        public string version { get; set; }
        public string originX { get; set; }
        public string originY { get; set; }
        public double left { get; set; }
        public double top { get; set; }
        public double width { get; set; }
        public double height { get; set; }
        public string fill { get; set; }
        public string stroke { get; set; }
        public double strokeWidth { get; set; }
        public object strokeDashArray { get; set; }
        public string strokeLineCap { get; set; }
        public double strokeDashOffset { get; set; }
        public string strokeLineJoin { get; set; }
        public double strokeMiterLimit { get; set; }
        public double scaleX { get; set; }
        public double scaleY { get; set; }
        public double angle { get; set; }
        public bool flipX { get; set; }
        public bool flipY { get; set; }
        public double opacity { get; set; }
        public object shadow { get; set; }
        public bool visible { get; set; }
        public string backgroundColor { get; set; }
        public string fillRule { get; set; }
        public string paintFirst { get; set; }
        public string globalCompositeOperation { get; set; }
        public double skewX { get; set; }
        public double skewY { get; set; }
        public double rx { get; set; }
        public double ry { get; set; }
        public double x1 { get; set; }
        public double x2 { get; set; }
        public double y1 { get; set; }
        public double y2 { get; set; }
    }

}
