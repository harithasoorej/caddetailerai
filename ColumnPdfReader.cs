using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CadDetailerAi
{
    internal static class ColumnPdfReader
    {
        private sealed class PdfRow
        {
            public double Top { get; set; }
            public List<Word> Words { get; private set; }

            public PdfRow()
            {
                Words = new List<Word>();
            }
        }

        private sealed class HeaderColumn
        {
            public string Name { get; set; }
            public double X { get; set; }
        }

        private static readonly Regex SizePattern =
            new Regex(@"^\s*(\d+(?:\.\d+)?)\s*(?:[xX×/]\s*)(\d+(?:\.\d+)?)",
                RegexOptions.Compiled);

        public static List<ColumnData> Extract(string path, out bool hasText)
        {
            List<ColumnData> extracted = new List<ColumnData>();
            hasText = false;
            using (PdfDocument document = PdfDocument.Open(path))
            {
                foreach (Page page in document.GetPages())
                {
                    List<Word> words = page.GetWords().ToList();
                    if (words.Count == 0)
                        continue;
                    hasText = true;
                    List<PdfRow> rows = GroupRows(words);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        List<HeaderColumn> headers;
                        if (!TryReadHeader(rows[i], out headers))
                            continue;

                        for (int rowIndex = i + 1; rowIndex < rows.Count; rowIndex++)
                        {
                            List<HeaderColumn> nextHeader;
                            if (TryReadHeader(rows[rowIndex], out nextHeader))
                                break;

                            ColumnData data = ReadDataRow(rows[rowIndex], headers);
                            if (data != null)
                                extracted.Add(data);
                        }
                    }
                }
            }
            return Deduplicate(extracted);
        }

        private static List<PdfRow> GroupRows(IList<Word> words)
        {
            List<PdfRow> rows = new List<PdfRow>();
            foreach (Word word in words.OrderByDescending(w => w.BoundingBox.Top)
                .ThenBy(w => w.BoundingBox.Left))
            {
                PdfRow row = rows.Count == 0 ? null : rows[rows.Count - 1];
                if (row == null || Math.Abs(row.Top - word.BoundingBox.Top) > 2.5)
                {
                    row = new PdfRow { Top = word.BoundingBox.Top };
                    rows.Add(row);
                }
                row.Words.Add(word);
            }
            return rows;
        }

        private static bool TryReadHeader(PdfRow row, out List<HeaderColumn> headers)
        {
            headers = new List<HeaderColumn>();
            foreach (Word word in row.Words)
            {
                string normalized = Normalize(word.Text);
                string field = HeaderField(normalized);
                if (field != null && !headers.Any(h => h.Name == field))
                    headers.Add(new HeaderColumn { Name = field, X = (word.BoundingBox.Left + word.BoundingBox.Right) / 2.0 });
            }
            headers = headers.OrderBy(h => h.X).ToList();
            bool hasSize = headers.Any(h => h.Name == "Size") ||
                (headers.Any(h => h.Name == "Width") && headers.Any(h => h.Name == "Depth"));
            return hasSize && headers.Any(h => h.Name == "Mark") &&
                headers.Any(h => h.Name == "Bars") &&
                headers.Any(h => h.Name == "Stirrups") &&
                headers.Any(h => h.Name == "Level");
        }

        private static string HeaderField(string normalized)
        {
            switch (normalized)
            {
                case "mark":
                case "columnmark":
                case "col":
                    return "Mark";
                case "size":
                case "section":
                    return "Size";
                case "width":
                case "breadth":
                    return "Width";
                case "depth":
                case "length":
                    return "Depth";
                case "grid":
                case "location":
                    return "Grid";
                case "bars":
                case "mainbars":
                case "reinforcement":
                case "longitudinalbars":
                    return "Bars";
                case "stirrups":
                case "ties":
                case "links":
                    return "Stirrups";
                case "cover":
                case "clearcover":
                    return "Cover";
                case "level":
                case "levels":
                case "storey":
                case "story":
                    return "Level";
                default:
                    return null;
            }
        }

        private static ColumnData ReadDataRow(PdfRow row, IList<HeaderColumn> headers)
        {
            if (row.Words.Count == 0)
                return null;
            Dictionary<string, StringBuilder> cells =
                new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            foreach (HeaderColumn header in headers)
                cells[header.Name] = new StringBuilder();

            foreach (Word word in row.Words.OrderBy(w => w.BoundingBox.Left))
            {
                double x = (word.BoundingBox.Left + word.BoundingBox.Right) / 2.0;
                int columnIndex = FindColumn(headers, x);
                HeaderColumn header = headers[columnIndex];
                if (cells[header.Name].Length > 0)
                    cells[header.Name].Append(' ');
                cells[header.Name].Append(word.Text);
            }

            string mark = Value(cells, "Mark");
            if (String.IsNullOrWhiteSpace(mark) || Normalize(mark) == "mark")
                return null;
            string width = Value(cells, "Width");
            string depth = Value(cells, "Depth");
            string size = Value(cells, "Size");
            if (!String.IsNullOrWhiteSpace(size))
            {
                Match match = SizePattern.Match(size);
                if (match.Success)
                {
                    width = match.Groups[1].Value;
                    depth = match.Groups[2].Value;
                }
                else
                {
                    width = size;
                    depth = "?";
                }
            }
            return new ColumnData
            {
                Mark = mark,
                Width = Missing(width),
                Depth = Missing(depth),
                Grid = Missing(Value(cells, "Grid")),
                MainBars = Missing(Value(cells, "Bars")),
                Stirrups = Missing(Value(cells, "Stirrups")),
                Cover = Missing(Value(cells, "Cover")),
                Levels = Missing(Value(cells, "Level"))
            };
        }

        private static int FindColumn(IList<HeaderColumn> headers, double x)
        {
            for (int i = 0; i < headers.Count - 1; i++)
            {
                double boundary = (headers[i].X + headers[i + 1].X) / 2.0;
                if (x < boundary)
                    return i;
            }
            return headers.Count - 1;
        }

        private static string Value(
            IDictionary<string, StringBuilder> cells, string key)
        {
            StringBuilder value;
            return cells.TryGetValue(key, out value) ? value.ToString().Trim() : String.Empty;
        }

        private static string Missing(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "?" : value.Trim();
        }

        private static string Normalize(string value)
        {
            return Regex.Replace(value ?? String.Empty, @"[^A-Za-z]", String.Empty)
                .ToLowerInvariant();
        }

        private static List<ColumnData> Deduplicate(List<ColumnData> input)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ColumnData> rows = new List<ColumnData>();
            foreach (ColumnData data in input)
            {
                string key = data.Mark + "|" + data.Grid + "|" + data.Levels;
                if (seen.Add(key))
                    rows.Add(data);
            }
            return rows;
        }
    }
}
