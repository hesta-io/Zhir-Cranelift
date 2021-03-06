using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using System.Collections.Generic;
using System.IO;
using System.Linq;

using UglyToad.PdfPig.Writer;

namespace Cranelift.Common.Helpers
{
    public class DocumentHelper
    {
        public Stream CreateWordDocument(IEnumerable<HocrPage> pages)
        {
            var stream = new MemoryStream();

            using (WordprocessingDocument wordDocument =
                WordprocessingDocument.Create(stream,
                WordprocessingDocumentType.Document,
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
                    foreach (var p in page.Paragraphs)
                    {
                        var paragraph = new Paragraph();
                        if (p.Direction == TextDirection.RightToLeft)
                        {
                            paragraph.ParagraphProperties = new ParagraphProperties
                            {
                                BiDi = new BiDi(),
                                TextDirection = new DocumentFormat.OpenXml.Wordprocessing.TextDirection()
                                {
                                    Val = TextDirectionValues.TopToBottomRightToLeft
                                }
                            };
                        }

                        var fontSize = p.Lines.SelectMany(l => l.Words).GroupBy(w => w.FontSize)
                                         .Where(w => w.Key != null)
                                         .OrderByDescending(g => g.Count())
                                         .ThenBy(g => g.Key)
                                         .Select(g => g.Key)
                                         .FirstOrDefault();

                        if (fontSize < 20)
                            fontSize = 12;
                        else if (fontSize < 30)
                            fontSize = 24;
                        else if (fontSize < 40)
                            fontSize = 34;
                        else if (fontSize < 50)
                            fontSize = 44;
                        else if (fontSize < 60)
                            fontSize = 54;

                        // NOTE: Run.RunProperties.FontSize's unit is Half-Point!
                        fontSize *= 2;

                        var lastLine = p.Lines[p.Lines.Count - 1];
                        foreach (var line in p.Lines)
                        {
                            int count = 0;
                            foreach (var word in line.Words)
                            {
                                var r = new Run();
                                r.RunProperties = new RunProperties();

                                if (word.FontSize != null && page.ShouldPredictSizes)
                                {
                                    var value = fontSize.ToString();

                                    r.RunProperties.FontSizeComplexScript = new FontSizeComplexScript { Val = value };
                                    r.RunProperties.FontSize = new FontSize { Val = value };
                                }

                                r.RunProperties.RunFonts = new RunFonts();
                                r.RunProperties.RunFonts.ComplexScript = "Calibri";
                                r.RunProperties.RunFonts.HighAnsi = r.RunProperties.RunFonts.Ascii = "Calibri";

                                // Highlight low confidence words!
                                if (word.Confidence != null && word.Confidence < 0.5)
                                {
                                    r.RunProperties.Highlight = new Highlight
                                    {
                                        Val = HighlightColorValues.Yellow
                                    };
                                }
                                if (line != lastLine || count++ < line.Words.Count - 1)
                                {
                                    word.Text += " ";
                                }
                                // TODO: Normalize text!
                                var text = new Text(word.Text) { Space = SpaceProcessingModeValues.Preserve };
                                r.Append(text);

                                paragraph.Append(r);
                            }
                        }

                        body.Append(paragraph);
                    }

                    foreach (var table in page.Tables)
                    {
                        var paragraphs = page.Paragraphs
                            .Where(p => table.BoundingBox.AlmostContains(p.BoundingBox.Value))
                            .ToArray();

                        var allWords = paragraphs.SelectMany(p => p.Lines.SelectMany(l => l.Words)).ToArray();

                        var rows = table.Cells.Max(c => c.EndRow) + 1;
                        var columns = table.Cells.Max(c => c.EndColumn) + 1;

                        var wordTable = new Table();
                        var tableGrid = new TableGrid();

                        var columnWidthTwips = ((int)((double)(a4Size.Width - (a4Size.Width * 0.2)) / columns)).ToString();

                        for (var c = 0; c < columns; c++)
                        {
                            tableGrid.AppendChild(new GridColumn() { Width = new StringValue(columnWidthTwips) });
                        }

                        wordTable.AppendChild(tableGrid);

                        for (int r = 0; r < rows; r++)
                        {
                            var row = new TableRow();
                            var cells = table.Cells.Where(c => c.StartRow == r && c.EndRow == r).ToList();

                            for (int c = 0; c < columns; c++)
                            {
                                var icdar19Cell = cells.FirstOrDefault(x => x.StartColumn == c && x.EndColumn == c);
                                var wordCell = new TableCell();

                                // Specify the width property of the table cell.
                                wordCell.Append(new TableCellProperties(new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "2400" }));

                                if (icdar19Cell != null)
                                {
                                    var words = allWords.Where(w => icdar19Cell.BoundingBox.AlmostContains(w.BoundingBox.Value))
                                                        .OrderByDescending(w => w.BoundingBox.Value.X)
                                                        .ThenByDescending(w => w.BoundingBox.Value.Y)
                                                        .ToList();

                                    foreach (var word in words)
                                    {
                                        var text = new Text(word.Text) { Space = SpaceProcessingModeValues.Preserve };
                                        wordCell.Append(new Paragraph(new Run(text)));
                                    }
                                }

                                row.Append(wordCell);
                            }

                            wordTable.Append(row);
                        }

                        body.Append(wordTable);
                    }

                    var pageBreak = new Paragraph(new Run(new Break() { Type = BreakValues.Page }));
                    body.Append(pageBreak);
                }
            }

            stream.Seek(0, SeekOrigin.Begin);

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
