using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;

namespace CadDetailerAi
{
    internal static class DrawingReader
    {
        public static DrawingSnapshot Read(Database database, ObjectId[] selectedIds)
        {
            DrawingSnapshot result = new DrawingSnapshot();
            Extents3d? combinedExtents = null;
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> blocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                List<ObjectId> modelSpaceIds = GetModelSpaceIds(database, transaction);
                IEnumerable<ObjectId> ids = selectedIds == null
                    ? (IEnumerable<ObjectId>)modelSpaceIds
                    : FilterModelSpaceSelection(selectedIds, modelSpaceIds);
                foreach (ObjectId id in ids)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (entity == null)
                        continue;

                    result.EntityCount++;
                    Increment(result.EntityCounts, entity.GetType().Name);
                    layers.Add(entity.Layer);

                    DBText dbText = entity as DBText;
                    MText mText = entity as MText;
                    if (dbText != null)
                        result.Text.Add(dbText.TextString);
                    else if (mText != null)
                        result.Text.Add(mText.Contents);

                    Dimension dimension = entity as Dimension;
                    if (dimension != null)
                        result.DimensionValues.Add(dimension.DimensionText);

                    BlockReference blockReference = entity as BlockReference;
                    if (blockReference != null)
                    {
                        BlockTableRecord definition = (BlockTableRecord)transaction.GetObject(
                            blockReference.BlockTableRecord, OpenMode.ForRead);
                        blocks.Add(definition.Name);
                    }

                    try
                    {
                        Extents3d extents = entity.GeometricExtents;
                        if (!combinedExtents.HasValue)
                            combinedExtents = extents;
                        else
                        {
                            Extents3d current = combinedExtents.Value;
                            current.AddExtents(extents);
                            combinedExtents = current;
                        }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        // Some entities do not expose usable geometric extents.
                    }
                }
                transaction.Commit();
            }

            result.Layers.AddRange(layers);
            result.BlockNames.AddRange(blocks);
            if (combinedExtents.HasValue)
            {
                Extents3d extents = combinedExtents.Value;
                result.Extents = new DrawingExtents
                {
                    MinX = extents.MinPoint.X,
                    MinY = extents.MinPoint.Y,
                    MaxX = extents.MaxPoint.X,
                    MaxY = extents.MaxPoint.Y
                };
            }
            return result;
        }

        public static string ToCompactJson(DrawingSnapshot snapshot)
        {
            return JsonConvert.SerializeObject(snapshot, Formatting.None);
        }

        private static List<ObjectId> GetModelSpaceIds(
            Database database, Transaction transaction)
        {
            BlockTable blockTable = (BlockTable)transaction.GetObject(
                database.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            List<ObjectId> ids = new List<ObjectId>();
            foreach (ObjectId id in modelSpace)
                ids.Add(id);
            return ids;
        }

        private static List<ObjectId> FilterModelSpaceSelection(
            ObjectId[] selectedIds, IList<ObjectId> modelSpaceIds)
        {
            HashSet<ObjectId> inModelSpace = new HashSet<ObjectId>(modelSpaceIds);
            List<ObjectId> filtered = new List<ObjectId>();
            foreach (ObjectId id in selectedIds)
            {
                if (inModelSpace.Contains(id))
                    filtered.Add(id);
            }
            return filtered;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            if (!counts.ContainsKey(key))
                counts[key] = 0;
            counts[key]++;
        }
    }
}
