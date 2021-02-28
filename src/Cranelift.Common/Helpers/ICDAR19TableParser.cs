using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Cranelift.Common.Helpers
{
    public class ICDAR19Cell
    {
        public int StartColumn { get; set; }
        public int EndColumn { get; set; }
        public int StartRow { get; set; }
        public int EndRow { get; set; }

        public Rect BoundingBox { get; set; }
    }

    public class ICDAR19Table
    {
        public Rect BoundingBox { get; set; }
        public List<ICDAR19Cell> Cells { get; set; }
    }

    // https://sci-hub.se/10.1109/ICDAR.2019.00243
    // https://github.com/DevashishPrasad/CascadeTabNet
    public static class ICDAR19TableParser
    {
        static XName Name(string element)
        {
            return XName.Get(element, "");
        }

        static int GetAttributeValue(XElement element, string attributeName)
        {
            var text = element.Attribute(Name(attributeName)).Value;
            if (int.TryParse(text, out var number))
                return number;

            return 0;
        }

        static Rect GetBoundingBox(XElement element)
        {
            var coords = element.Element(Name("Coords"));
            var points = coords.Attribute(Name("points")).Value;

            var parts = points.Split(' ');
            var list = new List<Point>();

            foreach (var part in parts)
            {
                var numbers = part.Split(',');
                if (int.TryParse(numbers[0], out var x) && int.TryParse(numbers[1], out var y))
                {
                    list.Add(new Point(x, y));
                }
            }

            var left = list.Min(p => p.X);
            var top = list.Min(p => p.Y);
            var right = list.Max(p => p.X);
            var bottom = list.Max(p => p.Y);

            return new Rect(new Point(left, top), new Point(right, bottom));
        }

        public static List<ICDAR19Table> ParseDocument(string xmlDocument)
        {
            var list = new List<ICDAR19Table>();

            var document = XDocument.Parse(xmlDocument);
            foreach (var xmlTable in document.Root.Descendants(Name("table")))
            {
                var table = new ICDAR19Table
                {
                    BoundingBox = GetBoundingBox(xmlTable),
                    Cells = new List<ICDAR19Cell>(),
                };

                foreach (var xmlCell in xmlTable.Elements("cell"))
                {
                    var cell = new ICDAR19Cell
                    {
                        BoundingBox = GetBoundingBox(xmlCell),
                        StartColumn = GetAttributeValue(xmlCell, "start-col"),
                        EndColumn = GetAttributeValue(xmlCell, "end-col"),
                        StartRow = GetAttributeValue(xmlCell, "start-row"),
                        EndRow = GetAttributeValue(xmlCell, "end-row"),
                    };

                    table.Cells.Add(cell);
                }

                list.Add(table);
            }

            return list;
        }
    }
}
