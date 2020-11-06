using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using System.Collections.Generic;
using System.IO;

using UglyToad.PdfPig.Writer;

namespace Cranelift.Helpers
{
    public class DocumentHelper
    {
        public Stream CreateWordDocument(string[] pages)
        {
            var stream = new MemoryStream();

            using (WordprocessingDocument wordDocument =
                WordprocessingDocument.Create(stream,
                DocumentFormat.OpenXml.WordprocessingDocumentType.Document,
                autoSave: true))
            {
                var mainPart = wordDocument.AddMainDocumentPart();
                mainPart.Document = new Document();

                var body = mainPart.Document.Body = new Body();

                var sectionProperties = new SectionProperties();
                var a4Size = new PageSize { Width = ToTwips(8.25), Height = ToTwips(11.75) };
                sectionProperties.Append(a4Size);

                foreach (var page in pages)
                {
                    var lines = page.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var isRtlText = CharHelper.StartsWithRtlCharacter(line);

                        var paragraph = new Paragraph();
                        var r = new Run();
                        var text = new Text(line);

                        if (isRtlText)
                        {
                            paragraph.ParagraphProperties = new ParagraphProperties
                            {
                                BiDi = new BiDi(),
                                TextDirection = new TextDirection()
                                {
                                    Val = TextDirectionValues.TopToBottomRightToLeft
                                }
                            };
                        }

                        r.Append(text);
                        paragraph.Append(r);
                        body.Append(paragraph);
                    }

                    var pageBreak = new Paragraph(new Run(new Break() { Type = BreakValues.Page }));
                    body.Append(pageBreak);
                }
            }

            //stream.Position = 0;

            //return stream.ToArray();
            return stream;
        }

        private static uint ToTwips(double v)
        {
            // 1 inch = 72 points
            const int PointsInInch = 72;
            // 1 point = 20 twips
            const int TwipsInPoint = 20;

            return (uint)(v * PointsInInch * TwipsInPoint);
        }

        public byte[] MergePages(IReadOnlyList<byte[]> files)
        {
            return PdfMerger.Merge(files);
        }
    }
}
