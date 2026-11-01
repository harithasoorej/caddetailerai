using System;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace CadDetailerAi
{
    internal static class DraftManager
    {
        public static int Accept(Database database, string targetLayerName)
        {
            if (String.IsNullOrWhiteSpace(targetLayerName) ||
                targetLayerName.IndexOfAny(new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', '=', ',' }) >= 0)
                throw new ArgumentException("The target layer name is invalid.", "targetLayerName");

            int updated = 0;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                LayerTable layers = (LayerTable)transaction.GetObject(
                    database.LayerTableId, OpenMode.ForRead);
                if (!layers.Has(targetLayerName))
                {
                    layers.UpgradeOpen();
                    LayerTableRecord layer = new LayerTableRecord { Name = targetLayerName };
                    layers.Add(layer);
                    transaction.AddNewlyCreatedDBObject(layer, true);
                }

                BlockTable blockTable = (BlockTable)transaction.GetObject(
                    database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in modelSpace)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (entity == null || !String.Equals(
                        entity.Layer, "AI_DRAFT", StringComparison.OrdinalIgnoreCase))
                        continue;
                    entity.UpgradeOpen();
                    entity.Layer = targetLayerName;
                    entity.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    updated++;
                }
                transaction.Commit();
            }
            return updated;
        }

        public static int Reject(Database database)
        {
            int erased = 0;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)transaction.GetObject(
                    database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in modelSpace)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (entity == null || !String.Equals(
                        entity.Layer, "AI_DRAFT", StringComparison.OrdinalIgnoreCase))
                        continue;
                    entity.UpgradeOpen();
                    entity.Erase();
                    erased++;
                }
                transaction.Commit();
            }
            return erased;
        }
    }
}
