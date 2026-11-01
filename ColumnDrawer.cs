using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CadDetailerAi
{
    internal static class ColumnDrawer
    {
        private static readonly Regex Number =
            new Regex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);
        private static readonly Regex GridPattern =
            new Regex(@"^\s*([A-Za-z]+)\s*[-/ ]?\s*(\d+)\s*$", RegexOptions.Compiled);

        public static List<string> Validate(
            IList<ColumnData> columns, PluginSettings settings)
        {
            List<string> errors = new List<string>();
            foreach (ColumnData column in columns)
            {
                string mark = String.IsNullOrWhiteSpace(column.Mark) ? "(unknown)" : column.Mark;
                double width = 0;
                double depth = 0;
                try
                {
                    width = Measurement(column.Width) * settings.ColumnScale;
                }
                catch (InvalidOperationException)
                {
                    errors.Add(mark + ": Width must be a positive number.");
                }
                try
                {
                    depth = Measurement(column.Depth) * settings.ColumnScale;
                }
                catch (InvalidOperationException)
                {
                    errors.Add(mark + ": Depth must be a positive number.");
                }
                try
                {
                    Measurement(column.Cover);
                }
                catch (InvalidOperationException)
                {
                    errors.Add(mark + ": Cover must be a positive number.");
                }
                Match barCount = Number.Match(column.MainBars ?? String.Empty);
                int parsedBars;
                if (!barCount.Success || !Int32.TryParse(barCount.Value, out parsedBars) ||
                    parsedBars < 1)
                    errors.Add(mark + ": MainBars must start with a positive bar count.");

                Point2d center = GridPoint(column.Grid, columns, settings);
                if (!IsSafe(center.X) || !IsSafe(center.Y) ||
                    (width > 0 && !IsSafe(center.X - width / 2)) ||
                    (width > 0 && !IsSafe(center.X + width / 2)) ||
                    (depth > 0 && !IsSafe(center.Y - depth / 2)) ||
                    (depth > 0 && !IsSafe(center.Y + depth / 2)))
                    errors.Add(mark + ": Grid position or scaled dimensions exceed safe drawing limits.");
            }
            return errors;
        }

        public static void Draw(
            Database database, IList<ColumnData> columns, PluginSettings settings)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                EnsureLayer(transaction, database, "AI_DRAFT",
                    Color.FromColorIndex(ColorMethod.ByAci, 1));
                EnsureLayer(transaction, database, "S-COLS",
                    Color.FromColorIndex(ColorMethod.ByAci, 4));
                EnsureLayer(transaction, database, "S-SCHEDULE",
                    Color.FromColorIndex(ColorMethod.ByAci, 2));
                EnsureLayer(transaction, database, "S-DETAIL",
                    Color.FromColorIndex(ColorMethod.ByAci, 3));

                BlockTable blockTable = (BlockTable)transaction.GetObject(
                    database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (ColumnData column in columns)
                {
                    Point2d center = GridPoint(column.Grid, columns, settings);
                    double width = Measurement(column.Width) * settings.ColumnScale;
                    double depth = Measurement(column.Depth) * settings.ColumnScale;
                    AddRectangle(transaction, modelSpace, center.X - width / 2,
                        center.Y - depth / 2, center.X + width / 2,
                        center.Y + depth / 2);
                    AddText(transaction, modelSpace, column.Mark,
                        center.X + width / 2, center.Y + depth / 2,
                        settings.ColumnTextHeight * settings.ColumnScale);
                }

                DrawSchedule(transaction, modelSpace, columns, settings);
                DrawTypicalDetail(transaction, modelSpace, columns[0], settings);
                transaction.Commit();
            }
        }

        private static void DrawSchedule(
            Transaction transaction, BlockTableRecord modelSpace,
            IList<ColumnData> columns, PluginSettings settings)
        {
            string[] headings =
            {
                "Mark", "Width", "Depth", "Grid", "MainBars", "Stirrups", "Cover", "Levels"
            };
            double[] widths = { 18, 18, 18, 18, 30, 30, 18, 24 };
            double scale = settings.ColumnScale;
            double rowHeight = Math.Max(settings.ColumnTextHeight * 2.2, 5) * scale;
            double x = settings.ColumnScheduleOriginX;
            double top = settings.ColumnScheduleOriginY;
            double totalWidth = widths.Sum() * scale;
            int rowCount = columns.Count + 1;

            for (int row = 0; row <= rowCount; row++)
            {
                double y = top - row * rowHeight;
                AddLine(transaction, modelSpace, x, y, x + totalWidth, y);
            }
            double columnX = x;
            AddLine(transaction, modelSpace, columnX, top, columnX, top - rowCount * rowHeight);
            for (int i = 0; i < widths.Length; i++)
            {
                columnX += widths[i] * scale;
                AddLine(transaction, modelSpace, columnX, top, columnX, top - rowCount * rowHeight);
            }

            for (int row = 0; row < rowCount; row++)
            {
                ColumnData data = row == 0 ? null : columns[row - 1];
                columnX = x;
                for (int col = 0; col < headings.Length; col++)
                {
                    string value = data == null ? headings[col] : Field(data, col);
                    AddText(transaction, modelSpace, value,
                        columnX + settings.ColumnTextHeight * scale * 0.25,
                        top - (row + 1) * rowHeight + rowHeight * 0.3,
                        settings.ColumnTextHeight * scale);
                    columnX += widths[col] * scale;
                }
            }
        }

        private static void DrawTypicalDetail(
            Transaction transaction, BlockTableRecord modelSpace,
            ColumnData column, PluginSettings settings)
        {
            double scale = settings.ColumnScale;
            double width = Measurement(column.Width) * scale;
            double depth = Measurement(column.Depth) * scale;
            double x = settings.ColumnDetailOriginX;
            double y = settings.ColumnDetailOriginY;
            double cover = Measurement(column.Cover) * scale;
            double barRadius = Math.Max(settings.ColumnTextHeight * scale * 0.18, 1);
            double inset = Math.Min(cover, Math.Min(width, depth) * 0.35);

            AddText(transaction, modelSpace, "TYPICAL COLUMN SECTION " + column.Mark,
                x, y + depth + settings.ColumnTextHeight * scale,
                settings.ColumnTextHeight * scale);
            AddRectangle(transaction, modelSpace, x, y, x + width, y + depth);
            AddRectangle(transaction, modelSpace, x + inset, y + inset,
                x + width - inset, y + depth - inset);

            int bars = ParseBarCount(column.MainBars);
            List<Point2d> barPoints = PerimeterBars(x + inset, y + inset,
                x + width - inset, y + depth - inset, bars);
            foreach (Point2d point in barPoints)
                AddCircle(transaction, modelSpace, point.X, point.Y, barRadius);

            double elevationX = x + width + Math.Max(width * 0.8, 150 * scale);
            double elevationWidth = Math.Max(width * 0.6, 100 * scale);
            double elevationHeight = Math.Max(settings.TypicalLapLength * scale * 2,
                settings.TypicalStirrupSpacing * scale * 6);
            AddText(transaction, modelSpace, "TYPICAL COLUMN ELEVATION " + column.Mark,
                elevationX, y + elevationHeight + settings.ColumnTextHeight * scale,
                settings.ColumnTextHeight * scale);
            AddLine(transaction, modelSpace, elevationX, y,
                elevationX, y + elevationHeight);
            AddLine(transaction, modelSpace, elevationX + elevationWidth, y,
                elevationX + elevationWidth, y + elevationHeight);
            AddLine(transaction, modelSpace, elevationX, y,
                elevationX + elevationWidth, y);
            AddLine(transaction, modelSpace, elevationX, y + elevationHeight,
                elevationX + elevationWidth, y + elevationHeight);

            double spacing = settings.TypicalStirrupSpacing * scale;
            for (double tieY = y + spacing; tieY < y + elevationHeight; tieY += spacing)
                AddLine(transaction, modelSpace, elevationX, tieY,
                    elevationX + elevationWidth, tieY);

            AddLine(transaction, modelSpace, elevationX,
                y + settings.TypicalLapLength * scale,
                elevationX + elevationWidth, y + settings.TypicalLapLength * scale);
            AddLine(transaction, modelSpace, elevationX,
                y + settings.TypicalLapLength * scale * 2,
                elevationX + elevationWidth, y + settings.TypicalLapLength * scale * 2);

            AddText(transaction, modelSpace,
                "TIES: " + column.Stirrups + " @ " +
                settings.TypicalStirrupSpacing.ToString(CultureInfo.InvariantCulture),
                elevationX, y - settings.ColumnTextHeight * scale * 1.5,
                settings.ColumnTextHeight * scale);
            AddText(transaction, modelSpace,
                "LAP = " + settings.TypicalLapLength.ToString(CultureInfo.InvariantCulture),
                elevationX, y - settings.ColumnTextHeight * scale * 3,
                settings.ColumnTextHeight * scale);
        }

        private static List<Point2d> PerimeterBars(
            double left, double bottom, double right, double top, int count)
        {
            List<Point2d> points = new List<Point2d>();
            if (count < 4)
                count = 4;
            points.Add(new Point2d(left, bottom));
            points.Add(new Point2d(right, bottom));
            points.Add(new Point2d(right, top));
            points.Add(new Point2d(left, top));

            int remaining = count - 4;
            int edges = 4;
            for (int edge = 0; edge < edges && remaining > 0; edge++)
            {
                int onEdge = (int)Math.Ceiling((double)remaining / (edges - edge));
                Point2d start = points[edge];
                Point2d end = points[(edge + 1) % 4];
                for (int i = 1; i <= onEdge; i++)
                {
                    double fraction = (double)i / (onEdge + 1);
                    points.Add(new Point2d(
                        start.X + (end.X - start.X) * fraction,
                        start.Y + (end.Y - start.Y) * fraction));
                }
                remaining -= onEdge;
            }
            return points;
        }

        private static Point2d GridPoint(
            string grid, IList<ColumnData> columns, PluginSettings settings)
        {
            Match match = GridPattern.Match(grid ?? String.Empty);
            if (match.Success)
            {
                long xIndex = 0;
                foreach (char character in match.Groups[1].Value.ToUpperInvariant())
                {
                    xIndex = xIndex * 26 + character - 'A' + 1;
                    if (xIndex > 1000000)
                        return new Point2d(Double.PositiveInfinity, Double.PositiveInfinity);
                }
                double x = (xIndex - 1) * settings.ColumnGridSpacing;
                int yIndex;
                if (Int32.TryParse(match.Groups[2].Value, out yIndex) && yIndex > 0)
                    return new Point2d(settings.ColumnPlanOriginX + x,
                        settings.ColumnPlanOriginY + (yIndex - 1) * settings.ColumnGridSpacing);
            }

            List<string> grids = columns.Select(c => c.Grid ?? String.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            int index = grids.FindIndex(value =>
                String.Equals(value, grid, StringComparison.OrdinalIgnoreCase));
            return new Point2d(settings.ColumnPlanOriginX +
                Math.Max(index, 0) * settings.ColumnGridSpacing, settings.ColumnPlanOriginY);
        }

        private static double Measurement(string value)
        {
            Match match = Number.Match(value ?? String.Empty);
            double parsed;
            if (!match.Success || !Double.TryParse(match.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsed) || parsed <= 0 ||
                Double.IsInfinity(parsed) || Double.IsNaN(parsed) || parsed > 100000000)
                throw new InvalidOperationException("Invalid positive column measurement: " + value);
            string suffix = (value ?? String.Empty).Substring(match.Index + match.Length).Trim();
            if (suffix.StartsWith("in", StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith("\"", StringComparison.OrdinalIgnoreCase))
                parsed *= 25.4;
            else if (suffix.StartsWith("cm", StringComparison.OrdinalIgnoreCase))
                parsed *= 10;
            return parsed;
        }

        private static int ParseBarCount(string value)
        {
            Match match = Number.Match(value ?? String.Empty);
            int count;
            return match.Success && Int32.TryParse(match.Value, out count) && count > 0
                ? Math.Min(count, 100)
                : 4;
        }

        private static bool IsSafe(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value) &&
                Math.Abs(value) <= 1000000000;
        }

        private static string Field(ColumnData data, int index)
        {
            switch (index)
            {
                case 0: return data.Mark;
                case 1: return data.Width;
                case 2: return data.Depth;
                case 3: return data.Grid;
                case 4: return data.MainBars;
                case 5: return data.Stirrups;
                case 6: return data.Cover;
                default: return data.Levels;
            }
        }

        private static void EnsureLayer(
            Transaction transaction, Database database, string name, Color color)
        {
            LayerTable layers = (LayerTable)transaction.GetObject(
                database.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name))
                return;
            if (!layers.IsWriteEnabled)
                layers.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord { Name = name, Color = color };
            layers.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
        }

        private static void AddRectangle(
            Transaction transaction, BlockTableRecord modelSpace,
            double left, double bottom, double right, double top)
        {
            AddLine(transaction, modelSpace, left, bottom, right, bottom);
            AddLine(transaction, modelSpace, right, bottom, right, top);
            AddLine(transaction, modelSpace, right, top, left, top);
            AddLine(transaction, modelSpace, left, top, left, bottom);
        }

        private static void AddLine(
            Transaction transaction, BlockTableRecord modelSpace,
            double x1, double y1, double x2, double y2)
        {
            Line line = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0))
            {
                Layer = "AI_DRAFT"
            };
            modelSpace.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
        }

        private static void AddText(
            Transaction transaction, BlockTableRecord modelSpace,
            string text, double x, double y, double height)
        {
            DBText entity = new DBText
            {
                Position = new Point3d(x, y, 0),
                TextString = text,
                Height = height,
                Layer = "AI_DRAFT"
            };
            modelSpace.AppendEntity(entity);
            transaction.AddNewlyCreatedDBObject(entity, true);
        }

        private static void AddCircle(
            Transaction transaction, BlockTableRecord modelSpace,
            double x, double y, double radius)
        {
            Circle circle = new Circle(new Point3d(x, y, 0), Vector3d.ZAxis, radius)
            {
                Layer = "AI_DRAFT"
            };
            modelSpace.AppendEntity(circle);
            transaction.AddNewlyCreatedDBObject(circle, true);
        }
    }
}
