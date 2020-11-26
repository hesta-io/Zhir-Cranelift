using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Cranelift.Helpers
{
    public enum TextDirection
    {
        LeftToRight,
        RightToLeft,
    }

    public class HocrParagraph
    {
        public string Language { get; set; }
        public List<HocrLine> Lines { get; set; }
        public TextDirection Direction { get; set; }
        public HocrRect? BoundingBox { get; set; }

        public override string ToString() => string.Join("\n", Lines);
    }

    public class HocrPage
    {
        public List<HocrParagraph> Paragraphs { get; set; }
        public bool ShouldPredictSizes { get; set; }
    }

    public class HocrLine
    {
        public HocrRect? BoundingBox { get; set; }
        public List<HocrWord> Words { get; set; }

        public override string ToString() => string.Join(" ", Words);
    }

    public struct HocrPoint
    {
        public HocrPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    public struct HocrRect
    {
        public HocrRect(HocrPoint bottomLeft, HocrPoint topRight)
        {
            BottomLeft = bottomLeft;
            TopRight = topRight;
        }

        public HocrPoint BottomLeft { get; }
        public HocrPoint TopRight { get; }
    }

    // https://en.wikipedia.org/wiki/HOCR
    // https://stackoverflow.com/a/49283965/7003797
    // https://github.com/kba/hocr-spec/blob/master/1.1/spec.md
    public class HocrWord
    {
        public string Text { get; set; }
        public double? FontSize { get; set; }
        public double? Confidence { get; set; }
        public HocrRect? BoundingBox { get; set; }

        public override string ToString() => Text;
    }

    public static class HocrParser
    {
        public static string GetClass(this XElement element)
        {
            return element.Attribute("class")?.Value;
        }

        public static string GetTitle(this XElement element)
        {
            return element.Attribute("title")?.Value;
        }

        static XName Name(string element)
        {
            return XName.Get(element, "http://www.w3.org/1999/xhtml");
        }

        public static HocrPage Parse(string hocr, bool predictSizes)
        {
            var document = XDocument.Parse(hocr);
            var descs = document.Root.Descendants().ToArray();
            var page = document.Root.Descendants(Name("div")).FirstOrDefault(e => e.GetClass() == "ocr_page");
            if (page is null) return null;

            var areas = page.Descendants(Name("div")).Where(d => d.GetClass() == "ocr_carea").ToArray();

            var paragraphs = areas.SelectMany(a => a.Descendants(Name("p")).Where(e => e.GetClass() == "ocr_par"))
                                  .Select(p => ParseParagraph(p))
                                  .ToList();

            return new HocrPage
            {
                Paragraphs = paragraphs,
                ShouldPredictSizes = predictSizes
            };
        }

        private static HocrParagraph ParseParagraph(XElement p)
        {
            var paragraph = new HocrParagraph();
            var dir = p.Attribute("dir")?.Value;
            paragraph.Direction = dir == "rtl" ? TextDirection.RightToLeft : TextDirection.LeftToRight;
            paragraph.Language = p.Attribute("lang")?.Value;

            paragraph.BoundingBox = ParseBoundingBox(ParseTitle(p.GetTitle()));

            paragraph.Lines = new List<HocrLine>();

            foreach (var l in p.Descendants(Name("span")))
            {
                var line = new HocrLine();
                line.BoundingBox = ParseBoundingBox(ParseTitle(p.GetTitle()));
                line.Words = new List<HocrWord>();

                foreach (var w in l.Descendants(Name("span")))
                {
                    var word = new HocrWord();
                    var title = w.GetTitle();

                    var properties = ParseTitle(w.GetTitle());

                    word.BoundingBox = ParseBoundingBox(properties);
                    word.Confidence = ParseNumber(properties, "x_wconf");
                    word.FontSize = ParseNumber(properties, "x_fsize");
                    word.Text = w.Value;

                    line.Words.Add(word);
                }

                paragraph.Lines.Add(line);
            }

            return paragraph;
        }

        private static double? ParseNumber(Dictionary<string, string> properties, string name)
        {
            if (properties.TryGetValue(name, out var raw) && double.TryParse(raw, out var number))
            {
                return number;
            }

            return null;
        }

        private static HocrRect? ParseBoundingBox(Dictionary<string, string> properties)
        {
            if (properties.TryGetValue("bbox", out var bbox))
            {
                var numbers = bbox.Split(' ');
                if (double.TryParse(numbers[0], out var x0) &&
                    double.TryParse(numbers[1], out var y0) &&
                    double.TryParse(numbers[2], out var x1) &&
                    double.TryParse(numbers[3], out var y1))
                {
                    return new HocrRect(new HocrPoint(x0, y0), new HocrPoint(x1, y1));
                }
            }

            return null;
        }

        private static Dictionary<string, string> ParseTitle(string title)
        {
            var dict = new Dictionary<string, string>();
            if (title is null)
            {
                return dict;
            }

            var parts = title.Split(";");

            foreach (var p in parts)
            {
                var part = p.Trim();
                var index = part.IndexOf(' ');

                dict[part.Substring(0, index)] = part.Substring(index + 1);
            }

            return dict;
        }
    }
}
