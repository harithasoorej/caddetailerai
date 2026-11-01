using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CadDetailerAi
{
    internal static class ColumnsStore
    {
        public static List<ColumnData> Load()
        {
            if (!File.Exists(Settings.ColumnsPath))
                return new List<ColumnData>();
            List<ColumnData> columns = JsonConvert.DeserializeObject<List<ColumnData>>(
                File.ReadAllText(Settings.ColumnsPath));
            if (columns == null)
                throw new InvalidDataException("columns.json does not contain a column list.");
            columns.RemoveAll(c => c == null);
            Log("Loaded " + columns.Count + " rows from " + Settings.ColumnsPath);
            return columns;
        }

        public static void Save(IList<ColumnData> columns)
        {
            File.WriteAllText(Settings.ColumnsPath,
                JsonConvert.SerializeObject(columns, Formatting.Indented));
            Log("Saved " + columns.Count + " rows to " + Settings.ColumnsPath);
        }

        public static void Log(string message)
        {
            string path = Path.Combine(Path.GetTempPath(), "CadDetailerAi.log");
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                Environment.NewLine + "COLUMNS: " + message +
                Environment.NewLine + Environment.NewLine);
        }
    }
}
