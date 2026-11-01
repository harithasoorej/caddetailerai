using System;
using System.Collections.Generic;

namespace CadDetailerAi
{
    public sealed class DraftResponse
    {
        public List<string> Warnings { get; set; }
        public List<DraftAction> Actions { get; set; }

        public DraftResponse()
        {
            Warnings = new List<string>();
            Actions = new List<DraftAction>();
        }
    }

    public sealed class DraftAction
    {
        public string Type { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? X1 { get; set; }
        public double? Y1 { get; set; }
        public double? X2 { get; set; }
        public double? Y2 { get; set; }
        public double? Height { get; set; }
        public double? Offset { get; set; }
        public string Content { get; set; }
    }

    public sealed class RuleConfiguration
    {
        public double MinimumTextHeight { get; set; }
        public List<string> AllowedLayerNames { get; set; }
        public double DimensionSearchDistance { get; set; }
        public double DuplicateLineTolerance { get; set; }
        public int MinimumColumnBars { get; set; }
        public double MinimumColumnCover { get; set; }

        public RuleConfiguration()
        {
            AllowedLayerNames = new List<string>();
            MinimumColumnBars = 4;
            MinimumColumnCover = 25;
        }
    }

    public sealed class RuleFinding
    {
        public string Severity { get; set; }
        public string Message { get; set; }
        public string Handle { get; set; }
    }

    public sealed class DrawingSnapshot
    {
        public List<string> Layers { get; set; }
        public List<string> Text { get; set; }
        public List<string> DimensionValues { get; set; }
        public List<string> BlockNames { get; set; }
        public Dictionary<string, int> EntityCounts { get; set; }
        public DrawingExtents Extents { get; set; }
        public int EntityCount { get; set; }

        public DrawingSnapshot()
        {
            Layers = new List<string>();
            Text = new List<string>();
            DimensionValues = new List<string>();
            BlockNames = new List<string>();
            EntityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class DrawingExtents
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
    }

    public sealed class PluginSettings
    {
        public string OllamaUrl { get; set; }
        public string ModelName { get; set; }
        public int TimeoutSeconds { get; set; }
        public int LargeDrawingEntityThreshold { get; set; }
        public double ColumnScale { get; set; }
        public double ColumnTextHeight { get; set; }
        public double ColumnPlanOriginX { get; set; }
        public double ColumnPlanOriginY { get; set; }
        public double ColumnGridSpacing { get; set; }
        public double ColumnScheduleOriginX { get; set; }
        public double ColumnScheduleOriginY { get; set; }
        public double ColumnDetailOriginX { get; set; }
        public double ColumnDetailOriginY { get; set; }
        public double TypicalStirrupSpacing { get; set; }
        public double TypicalLapLength { get; set; }

        public PluginSettings()
        {
            OllamaUrl = "http://localhost:11434";
            ModelName = "qwen2.5:7b";
            TimeoutSeconds = 120;
            LargeDrawingEntityThreshold = 500;
            ColumnScale = 1.0;
            ColumnTextHeight = 2.5;
            ColumnPlanOriginX = 0;
            ColumnPlanOriginY = 0;
            ColumnGridSpacing = 6000;
            ColumnScheduleOriginX = 20000;
            ColumnScheduleOriginY = 0;
            ColumnDetailOriginX = 20000;
            ColumnDetailOriginY = -10000;
            TypicalStirrupSpacing = 150;
            TypicalLapLength = 600;
        }
    }

    public sealed class ColumnData
    {
        private string _mark;
        private string _width;
        private string _depth;
        private string _grid;
        private string _mainBars;
        private string _stirrups;
        private string _cover;
        private string _levels;

        public string Mark { get { return Display(_mark); } set { _mark = value; } }
        public string Width { get { return Display(_width); } set { _width = value; } }
        public string Depth { get { return Display(_depth); } set { _depth = value; } }
        public string Grid { get { return Display(_grid); } set { _grid = value; } }
        public string MainBars { get { return Display(_mainBars); } set { _mainBars = value; } }
        public string Stirrups { get { return Display(_stirrups); } set { _stirrups = value; } }
        public string Cover { get { return Display(_cover); } set { _cover = value; } }
        public string Levels { get { return Display(_levels); } set { _levels = value; } }

        public ColumnData()
        {
            Mark = "?";
            Width = "?";
            Depth = "?";
            Grid = "?";
            MainBars = "?";
            Stirrups = "?";
            Cover = "?";
            Levels = "?";
        }

        public bool HasMissingValues()
        {
            return IsMissing(Mark) || IsMissing(Width) || IsMissing(Depth) ||
                IsMissing(Grid) || IsMissing(MainBars) || IsMissing(Stirrups) ||
                IsMissing(Cover) || IsMissing(Levels);
        }

        private static bool IsMissing(string value)
        {
            return String.IsNullOrWhiteSpace(value) || value.Trim() == "?";
        }

        private static string Display(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "?" : value;
        }
    }
}
