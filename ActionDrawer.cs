using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CadDetailerAi
{
    internal static class ActionDrawer
    {
        private const double MaximumCoordinate = 1000000000;

        public static List<DraftAction> Validate(
            IList<DraftAction> actions, List<string> warnings)
        {
            List<DraftAction> valid = new List<DraftAction>();
            for (int i = 0; i < actions.Count; i++)
            {
                DraftAction action = actions[i];
                string error = ValidateOne(action);
                if (error == null)
                    valid.Add(action);
                else
                    warnings.Add("Skipped action " + (i + 1) + ": " + error);
            }
            return valid;
        }

        public static void Draw(Database database, IList<DraftAction> actions)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                LayerTable layers = (LayerTable)transaction.GetObject(
                    database.LayerTableId, OpenMode.ForRead);
                if (!layers.Has("AI_DRAFT"))
                {
                    layers.UpgradeOpen();
                    LayerTableRecord draftLayer = new LayerTableRecord
                    {
                        Name = "AI_DRAFT",
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 1)
                    };
                    layers.Add(draftLayer);
                    transaction.AddNewlyCreatedDBObject(draftLayer, true);
                }

                BlockTable blockTable = (BlockTable)transaction.GetObject(
                    database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (DraftAction action in actions)
                {
                    Entity entity = CreateEntity(action, database);
                    entity.Layer = "AI_DRAFT";
                    modelSpace.AppendEntity(entity);
                    transaction.AddNewlyCreatedDBObject(entity, true);
                }
                transaction.Commit();
            }
        }

        private static Entity CreateEntity(DraftAction action, Database database)
        {
            if (String.Equals(action.Type, "line", StringComparison.OrdinalIgnoreCase))
                return new Line(Point(action.X1.Value, action.Y1.Value),
                    Point(action.X2.Value, action.Y2.Value));
            if (String.Equals(action.Type, "text", StringComparison.OrdinalIgnoreCase))
            {
                return new DBText
                {
                    Position = Point(action.X.Value, action.Y.Value),
                    Height = action.Height.Value,
                    TextString = action.Content
                };
            }

            Point3d first = Point(action.X1.Value, action.Y1.Value);
            Point3d second = Point(action.X2.Value, action.Y2.Value);
            Point3d dimensionLine = new Point3d(
                (first.X + second.X) / 2.0,
                (first.Y + second.Y) / 2.0 + action.Offset.Value,
                0);
            return new AlignedDimension(first, second, dimensionLine, String.Empty, database.Dimstyle);
        }

        private static Point3d Point(double x, double y)
        {
            return new Point3d(x, y, 0);
        }

        private static string ValidateOne(DraftAction action)
        {
            if (action == null || String.IsNullOrWhiteSpace(action.Type))
                return "missing action type.";

            if (String.Equals(action.Type, "line", StringComparison.OrdinalIgnoreCase))
            {
                if (!Finite(action.X1) || !Finite(action.Y1) ||
                    !Finite(action.X2) || !Finite(action.Y2))
                    return "line coordinates must be finite numbers.";
                if (SamePoint(action.X1.Value, action.Y1.Value, action.X2.Value, action.Y2.Value))
                    return "line endpoints must be different.";
                return null;
            }

            if (String.Equals(action.Type, "text", StringComparison.OrdinalIgnoreCase))
            {
                if (!Finite(action.X) || !Finite(action.Y) || !Finite(action.Height) ||
                    action.Height.Value <= 0 || String.IsNullOrWhiteSpace(action.Content))
                    return "text requires finite coordinates, a positive height, and content.";
                if (action.Content.Length > 2000)
                    return "text content exceeds 2000 characters.";
                return null;
            }

            if (String.Equals(action.Type, "dimension", StringComparison.OrdinalIgnoreCase))
            {
                if (!Finite(action.X1) || !Finite(action.Y1) ||
                    !Finite(action.X2) || !Finite(action.Y2) || !Finite(action.Offset))
                    return "dimension coordinates and offset must be finite numbers.";
                if (SamePoint(action.X1.Value, action.Y1.Value, action.X2.Value, action.Y2.Value))
                    return "dimension endpoints must be different.";
                return null;
            }
            return "unsupported action type '" + action.Type + "'.";
        }

        private static bool Finite(double? value)
        {
            return value.HasValue && !Double.IsNaN(value.Value) &&
                !Double.IsInfinity(value.Value) &&
                Math.Abs(value.Value) <= MaximumCoordinate;
        }

        private static bool SamePoint(double x1, double y1, double x2, double y2)
        {
            return Math.Abs(x1 - x2) < 0.0000001 && Math.Abs(y1 - y2) < 0.0000001;
        }
    }
}
