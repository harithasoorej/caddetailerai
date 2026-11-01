using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;

namespace CadDetailerAi
{
    internal static class RuleChecker
    {
        private static readonly Regex FirstNumber =
            new Regex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);

        public static List<RuleFinding> CheckDrawing(Database database, IList<string> notes)
        {
            RuleConfiguration rules = LoadRules();
            List<RuleFinding> findings = new List<RuleFinding>();
            List<Line> lines = new List<Line>();
            List<Polyline> closedPolylines = new List<Polyline>();
            List<Dimension> dimensions = new List<Dimension>();

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                LayerTable layers = (LayerTable)transaction.GetObject(
                    database.LayerTableId, OpenMode.ForRead);
                BlockTable blockTable = (BlockTable)transaction.GetObject(
                    database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in modelSpace)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (entity == null)
                        continue;

                    LayerTableRecord layer = layers.Has(entity.Layer)
                        ? (LayerTableRecord)transaction.GetObject(layers[entity.Layer], OpenMode.ForRead)
                        : null;
                    CheckLayer(entity, layer, rules, findings);

                    DBText dbText = entity as DBText;
                    MText mText = entity as MText;
                    if (dbText != null && dbText.Height < rules.MinimumTextHeight)
                        Add(findings, "Warning", "Text height is below " +
                            rules.MinimumTextHeight.ToString(CultureInfo.InvariantCulture), entity);
                    if (mText != null && mText.TextHeight < rules.MinimumTextHeight)
                        Add(findings, "Warning", "Text height is below " +
                            rules.MinimumTextHeight.ToString(CultureInfo.InvariantCulture), entity);

                    Line line = entity as Line;
                    if (line != null)
                        lines.Add(line);
                    Polyline polyline = entity as Polyline;
                    if (polyline != null && polyline.Closed)
                        closedPolylines.Add(polyline);
                    Dimension dimension = entity as Dimension;
                    if (dimension != null)
                        dimensions.Add(dimension);
                }

                CheckDuplicateLines(lines, rules.DuplicateLineTolerance, findings);
                CheckUndimensionedPolylines(closedPolylines, dimensions,
                    rules.DimensionSearchDistance, findings);
                transaction.Commit();
            }

            CheckUnits(database, notes, findings);
            return findings;
        }

        public static List<RuleFinding> CheckActions(
            IList<DraftAction> actions, RuleConfiguration rules)
        {
            List<RuleFinding> findings = new List<RuleFinding>();
            List<DraftAction> lines = actions.Where(a =>
                String.Equals(a.Type, "line", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (DraftAction action in actions)
            {
                if (String.Equals(action.Type, "text", StringComparison.OrdinalIgnoreCase) &&
                    action.Height.HasValue && action.Height.Value < rules.MinimumTextHeight)
                {
                    findings.Add(new RuleFinding
                    {
                        Severity = "Warning",
                        Message = "Draft text height is below " +
                            rules.MinimumTextHeight.ToString(CultureInfo.InvariantCulture),
                        Handle = "(draft)"
                    });
                }
            }

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (SameLine(lines[i], lines[j], rules.DuplicateLineTolerance))
                        findings.Add(new RuleFinding
                        {
                            Severity = "Warning",
                            Message = "Draft contains duplicate overlapping lines.",
                            Handle = "(draft)"
                        });
                }
            }
            return findings;
        }

        public static List<RuleFinding> CheckColumns(
            IList<ColumnData> columns, IList<string> notes, RuleConfiguration rules)
        {
            List<RuleFinding> findings = new List<RuleFinding>();
            string allNotes = String.Join(" ", notes ?? new List<string>());
            bool noteMillimeters = HasUnit(allNotes, "mm", "millimeter", "millimeters");
            bool noteInches = HasUnit(allNotes, "inch", "inches");
            double? noteCover = FindNoteCoverMillimeters(allNotes);
            int? noteMinimumBars = FindNoteMinimumBars(allNotes);

            foreach (ColumnData column in columns)
            {
                string mark = String.IsNullOrWhiteSpace(column.Mark) ? "(unknown)" : column.Mark.Trim();
                int bars;
                Match barMatch = FirstNumber.Match(column.MainBars ?? String.Empty);
                if (!barMatch.Success || !Int32.TryParse(barMatch.Value, out bars))
                {
                    AddColumnFinding(findings, mark, "Cannot determine the main-bar count.");
                }
                else if (bars < rules.MinimumColumnBars)
                {
                    AddColumnFinding(findings, mark, "Main-bar count " + bars +
                        " is below the minimum " + rules.MinimumColumnBars + ".");
                }
                if (barMatch.Success && Int32.TryParse(barMatch.Value, out bars) &&
                    noteMinimumBars.HasValue && bars < noteMinimumBars.Value)
                    AddColumnFinding(findings, mark, "Main-bar count " + bars +
                        " conflicts with General Notes, which require at least " +
                        noteMinimumBars.Value + " bars.");

                double? cover = ParseMillimeters(column.Cover);
                if (!cover.HasValue)
                    AddColumnFinding(findings, mark, "Cover is not a valid numeric value.");
                else
                {
                    if (cover.Value < rules.MinimumColumnCover)
                        AddColumnFinding(findings, mark, "Cover " +
                            cover.Value.ToString(CultureInfo.InvariantCulture) +
                            " mm is below the minimum " +
                            rules.MinimumColumnCover.ToString(CultureInfo.InvariantCulture) + " mm.");
                    if (noteCover.HasValue && cover.Value < noteCover.Value)
                        AddColumnFinding(findings, mark, "Cover conflicts with General Notes, which require at least " +
                            noteCover.Value.ToString(CultureInfo.InvariantCulture) + " mm.");

                    bool columnMillimeters = HasUnit(column.Cover + " " + column.Width + " " + column.Depth,
                        "mm", "millimeter", "millimeters");
                    bool columnInches = HasUnit(column.Cover + " " + column.Width + " " + column.Depth,
                        "inch", "inches") ||
                        (column.Cover + " " + column.Width + " " + column.Depth).Contains("\"");
                    if ((noteMillimeters && columnInches) || (noteInches && columnMillimeters))
                        AddColumnFinding(findings, mark, "Column dimensions/cover units conflict with General Notes.");
                }
            }
            return findings;
        }

        public static RuleConfiguration LoadRules()
        {
            if (!File.Exists(Settings.RulesPath))
                throw new FileNotFoundException("rules.json was not found next to the plugin DLL.",
                    Settings.RulesPath);
            RuleConfiguration rules = JsonConvert.DeserializeObject<RuleConfiguration>(
                File.ReadAllText(Settings.RulesPath));
            if (rules == null || rules.MinimumTextHeight < 0 ||
                rules.DimensionSearchDistance < 0 || rules.DuplicateLineTolerance < 0 ||
                rules.AllowedLayerNames == null || rules.MinimumColumnBars < 1 ||
                rules.MinimumColumnCover < 0 || Double.IsNaN(rules.MinimumColumnCover) ||
                Double.IsInfinity(rules.MinimumColumnCover))
                throw new InvalidDataException("rules.json contains invalid rule settings.");
            return rules;
        }

        private static bool HasUnit(string text, params string[] units)
        {
            foreach (string unit in units)
            {
                if (text.IndexOf(unit, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static double? FindNoteCoverMillimeters(string notes)
        {
            Match match = Regex.Match(notes,
                @"cover[\s\S]{0,50}?(\d+(?:\.\d+)?)\s*(mm|millimeters?|cm|inches?|in\b)?",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;
            double value;
            if (!Double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value))
                return null;
            string unit = match.Groups[2].Value;
            if (unit.StartsWith("in", StringComparison.OrdinalIgnoreCase))
                value *= 25.4;
            else if (unit.Equals("cm", StringComparison.OrdinalIgnoreCase))
                value *= 10;
            return value;
        }

        private static int? FindNoteMinimumBars(string notes)
        {
            Match match = Regex.Match(notes,
                @"(?:minimum|min\.?|at\s+least|not\s+less\s+than)\s*(?:of\s*)?(\d+)\s*(?:nos?\.?\s*)?(?:main\s*)?(?:reinforcing\s*)?(?:longitudinal\s*)?bars",
                RegexOptions.IgnoreCase);
            int value;
            return match.Success && Int32.TryParse(match.Groups[1].Value, out value)
                ? (int?)value
                : null;
        }

        private static double? ParseMillimeters(string value)
        {
            Match match = FirstNumber.Match(value ?? String.Empty);
            double parsed;
            if (!match.Success || !Double.TryParse(match.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsed))
                return null;
            string suffix = (value ?? String.Empty).Substring(match.Index + match.Length).Trim();
            if (suffix.StartsWith("in", StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith("\"", StringComparison.OrdinalIgnoreCase))
                parsed *= 25.4;
            else if (suffix.StartsWith("cm", StringComparison.OrdinalIgnoreCase))
                parsed *= 10;
            return parsed;
        }

        private static void AddColumnFinding(
            List<RuleFinding> findings, string mark, string message)
        {
            findings.Add(new RuleFinding
            {
                Severity = "Warning",
                Message = "Column " + mark + ": " + message,
                Handle = mark
            });
        }

        private static void CheckLayer(
            Entity entity, LayerTableRecord layer, RuleConfiguration rules,
            List<RuleFinding> findings)
        {
            if (rules.AllowedLayerNames.Count > 0 &&
                !rules.AllowedLayerNames.Contains(entity.Layer, StringComparer.OrdinalIgnoreCase))
                Add(findings, "Warning", "Layer '" + entity.Layer +
                    "' is not in the allowed layer list.", entity);

            if (!(entity is DBText) && !(entity is MText))
                return;
            if (String.Equals(entity.Layer, "0", StringComparison.OrdinalIgnoreCase))
                Add(findings, "Warning", "Text is on layer 0.", entity);
            if (layer != null && (layer.IsOff || layer.IsFrozen))
                Add(findings, "Warning", "Text is on a frozen or off layer.", entity);
        }

        private static void CheckDuplicateLines(
            IList<Line> lines, double tolerance, List<RuleFinding> findings)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (SameLine(lines[i].StartPoint, lines[i].EndPoint,
                        lines[j].StartPoint, lines[j].EndPoint, tolerance))
                        Add(findings, "Warning", "Duplicate overlapping line.", lines[j]);
                }
            }
        }

        private static bool SameLine(DraftAction first, DraftAction second, double tolerance)
        {
            Point3d a1 = new Point3d(first.X1.Value, first.Y1.Value, 0);
            Point3d a2 = new Point3d(first.X2.Value, first.Y2.Value, 0);
            Point3d b1 = new Point3d(second.X1.Value, second.Y1.Value, 0);
            Point3d b2 = new Point3d(second.X2.Value, second.Y2.Value, 0);
            return SameLine(a1, a2, b1, b2, tolerance);
        }

        private static bool SameLine(
            Point3d a1, Point3d a2, Point3d b1, Point3d b2, double tolerance)
        {
            return (a1.DistanceTo(b1) <= tolerance && a2.DistanceTo(b2) <= tolerance) ||
                   (a1.DistanceTo(b2) <= tolerance && a2.DistanceTo(b1) <= tolerance);
        }

        private static void CheckUndimensionedPolylines(
            IList<Polyline> polylines, IList<Dimension> dimensions,
            double distance, List<RuleFinding> findings)
        {
            foreach (Polyline polyline in polylines)
            {
                Extents3d polylineExtents;
                try
                {
                    polylineExtents = polyline.GeometricExtents;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    continue;
                }

                bool hasDimension = false;
                foreach (Dimension dimension in dimensions)
                {
                    try
                    {
                        if (BoxesNear(polylineExtents, dimension.GeometricExtents, distance))
                        {
                            hasDimension = true;
                            break;
                        }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        // An unusable dimension extent cannot satisfy this check.
                    }
                }
                if (!hasDimension)
                    Add(findings, "Warning", "Closed polyline has no nearby dimension.",
                        polyline);
            }
        }

        private static bool BoxesNear(Extents3d a, Extents3d b, double distance)
        {
            return a.MinPoint.X - distance <= b.MaxPoint.X &&
                a.MaxPoint.X + distance >= b.MinPoint.X &&
                a.MinPoint.Y - distance <= b.MaxPoint.Y &&
                a.MaxPoint.Y + distance >= b.MinPoint.Y;
        }

        private static void CheckUnits(
            Database database, IList<string> notes, List<RuleFinding> findings)
        {
            string allNotes = String.Join(" ", notes ?? new List<string>());
            bool millimeters = allNotes.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                allNotes.IndexOf("millimeter", StringComparison.OrdinalIgnoreCase) >= 0;
            bool inches = allNotes.IndexOf("inch", StringComparison.OrdinalIgnoreCase) >= 0;
            if (millimeters && database.Insunits != UnitsValue.Millimeters)
                findings.Add(new RuleFinding
                {
                    Severity = "Warning",
                    Message = "General Notes specify millimeters, but drawing units are " +
                        database.Insunits + ".",
                    Handle = "(drawing)"
                });
            else if (!millimeters && inches && database.Insunits != UnitsValue.Inches)
                findings.Add(new RuleFinding
                {
                    Severity = "Warning",
                    Message = "General Notes specify inches, but drawing units are " +
                        database.Insunits + ".",
                    Handle = "(drawing)"
                });
        }

        private static void Add(
            List<RuleFinding> findings, string severity, string message, Entity entity)
        {
            findings.Add(new RuleFinding
            {
                Severity = severity,
                Message = message,
                Handle = entity.Handle.ToString()
            });
        }
    }
}
