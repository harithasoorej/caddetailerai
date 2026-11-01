using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;

namespace CadDetailerAi
{
    internal static class NotesReader
    {
        private static readonly Regex NumberedNote =
            new Regex(@"^\s*(?:\(?\d{1,3}\)?[.)]|[A-Z]\.)\s+(.+)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<string> Read(Database database, ObjectId[] selectedIds)
        {
            List<string> notes = new List<string>();
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                if (selectedIds != null)
                {
                    foreach (ObjectId id in selectedIds)
                    {
                        Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                        AddEntityNotes(entity, notes);
                    }
                }
                else
                {
                    BlockTable blockTable = (BlockTable)transaction.GetObject(
                        database.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                        blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in modelSpace)
                        AddEntityNotes(transaction.GetObject(id, OpenMode.ForRead, false) as Entity, notes);
                }
                transaction.Commit();
            }

            return notes;
        }

        private static void AddEntityNotes(Entity entity, List<string> notes)
        {
            if (entity == null)
                return;
            string value = entity is DBText
                ? ((DBText)entity).TextString
                : entity is MText ? ((MText)entity).Contents : null;
            if (String.IsNullOrWhiteSpace(value))
                return;

            string[] lines = value.Replace("\\P", "\n").Replace("\r", "").Split('\n');
            foreach (string sourceLine in lines)
            {
                string line = Regex.Replace(sourceLine, @"\\[A-Za-z][^;]*;", "").Trim();
                Match match = NumberedNote.Match(line);
                if (match.Success)
                    notes.Add(match.Groups[1].Value.Trim());
            }
        }
    }
}
