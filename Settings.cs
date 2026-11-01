using System;
using System.IO;
using Newtonsoft.Json;

namespace CadDetailerAi
{
    internal static class Settings
    {
        public static string AssemblyDirectory
        {
            get { return Path.GetDirectoryName(typeof(Settings).Assembly.Location); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(AssemblyDirectory, "settings.json"); }
        }

        public static string RulesPath
        {
            get { return Path.Combine(AssemblyDirectory, "rules.json"); }
        }

        public static string ColumnsPath
        {
            get { return Path.Combine(AssemblyDirectory, "columns.json"); }
        }

        public static PluginSettings Load()
        {
            if (!File.Exists(SettingsPath))
                return new PluginSettings();

            PluginSettings settings = JsonConvert.DeserializeObject<PluginSettings>(
                File.ReadAllText(SettingsPath));
            if (settings == null)
                throw new InvalidDataException("settings.json does not contain a settings object.");

            if (String.IsNullOrWhiteSpace(settings.OllamaUrl) ||
                String.IsNullOrWhiteSpace(settings.ModelName) ||
                settings.TimeoutSeconds < 1 ||
                settings.LargeDrawingEntityThreshold < 1 ||
                !IsPositiveFinite(settings.ColumnScale) ||
                !IsPositiveFinite(settings.ColumnTextHeight) ||
                !IsPositiveFinite(settings.ColumnGridSpacing) ||
                !IsPositiveFinite(settings.TypicalStirrupSpacing) ||
                !IsPositiveFinite(settings.TypicalLapLength) ||
                !IsFinite(settings.ColumnPlanOriginX) || !IsFinite(settings.ColumnPlanOriginY) ||
                !IsFinite(settings.ColumnScheduleOriginX) || !IsFinite(settings.ColumnScheduleOriginY) ||
                !IsFinite(settings.ColumnDetailOriginX) || !IsFinite(settings.ColumnDetailOriginY) ||
                settings.ColumnScale > 1000 || settings.ColumnTextHeight > 10000 ||
                settings.ColumnGridSpacing > 10000000 ||
                settings.TypicalStirrupSpacing > 10000000 ||
                settings.TypicalLapLength > 10000000)
                throw new InvalidDataException("settings.json contains invalid Ollama, threshold, or column drawing settings.");

            Uri ollamaUri;
            if (!Uri.TryCreate(settings.OllamaUrl, UriKind.Absolute, out ollamaUri) ||
                !String.Equals(ollamaUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                !ollamaUri.IsLoopback)
                throw new InvalidDataException(
                    "OllamaUrl must use HTTP on this computer (for example, http://localhost:11434).");

            return settings;
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0 && IsFinite(value);
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value) &&
                Math.Abs(value) <= 1000000000;
        }
    }
}
