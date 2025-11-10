using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;

namespace ClashManager.AutoNaming
{
    public class AutoNamingSettings
    {
        public bool IncludeParam1 { get; set; }
        public string Param1Name { get; set; }

        public bool IncludeParam2 { get; set; }
        public string Param2Name { get; set; }

        public bool IncludeParam3 { get; set; }
        public string Param3Name { get; set; }

        public bool IncludeParam4 { get; set; }
        public string Param4Name { get; set; }

        public bool IncludeParam5 { get; set; }
        public string Param5Name { get; set; }

        private string _separator;
        public string Separator
        {
            get => _separator;
            set
            {
                _separator = value;
                // Ensure separator has spaces around |
                if (!string.IsNullOrEmpty(_separator) && _separator.Contains("|") && !_separator.Contains(" | "))
                {
                    _separator = " | ";
                }
            }
        }

        public AutoNamingSettings()
        {
            Separator = " | ";
        }

        public List<string> GetActiveParameters()
        {
            var parameters = new List<string>();

            if (IncludeParam1 && !string.IsNullOrWhiteSpace(Param1Name))
                parameters.Add(Param1Name);

            if (IncludeParam2 && !string.IsNullOrWhiteSpace(Param2Name))
                parameters.Add(Param2Name);

            if (IncludeParam3 && !string.IsNullOrWhiteSpace(Param3Name))
                parameters.Add(Param3Name);

            if (IncludeParam4 && !string.IsNullOrWhiteSpace(Param4Name))
                parameters.Add(Param4Name);

            if (IncludeParam5 && !string.IsNullOrWhiteSpace(Param5Name))
                parameters.Add(Param5Name);

            return parameters;
        }

        /// <summary>
        /// Сохраняет настройки в JSON файл рядом с текущим NWF проектом
        /// </summary>
        public void SaveToFile()
        {
            try
            {
                string filePath = GetSettingsFilePath();
                if (string.IsNullOrEmpty(filePath))
                    return;

                var lines = new List<string>();
                lines.Add($"IncludeParam1={IncludeParam1}");
                lines.Add($"Param1Name={Param1Name ?? string.Empty}");
                lines.Add($"IncludeParam2={IncludeParam2}");
                lines.Add($"Param2Name={Param2Name ?? string.Empty}");
                lines.Add($"IncludeParam3={IncludeParam3}");
                lines.Add($"Param3Name={Param3Name ?? string.Empty}");
                lines.Add($"IncludeParam4={IncludeParam4}");
                lines.Add($"Param4Name={Param4Name ?? string.Empty}");
                lines.Add($"IncludeParam5={IncludeParam5}");
                lines.Add($"Param5Name={Param5Name ?? string.Empty}");
                lines.Add($"Separator={Separator ?? " | "}");

                File.WriteAllLines(filePath, lines);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving auto naming settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает настройки из JSON файла рядом с текущим NWF проектом
        /// </summary>
        public static AutoNamingSettings LoadFromFile()
        {
            var settings = new AutoNamingSettings();

            try
            {
                string filePath = GetSettingsFilePath();
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return settings;

                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("="))
                        continue;

                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    switch (key)
                    {
                        case "IncludeParam1":
                            bool.TryParse(value, out bool include1);
                            settings.IncludeParam1 = include1;
                            break;
                        case "Param1Name":
                            settings.Param1Name = value;
                            break;
                        case "IncludeParam2":
                            bool.TryParse(value, out bool include2);
                            settings.IncludeParam2 = include2;
                            break;
                        case "Param2Name":
                            settings.Param2Name = value;
                            break;
                        case "IncludeParam3":
                            bool.TryParse(value, out bool include3);
                            settings.IncludeParam3 = include3;
                            break;
                        case "Param3Name":
                            settings.Param3Name = value;
                            break;
                        case "IncludeParam4":
                            bool.TryParse(value, out bool include4);
                            settings.IncludeParam4 = include4;
                            break;
                        case "Param4Name":
                            settings.Param4Name = value;
                            break;
                        case "IncludeParam5":
                            bool.TryParse(value, out bool include5);
                            settings.IncludeParam5 = include5;
                            break;
                        case "Param5Name":
                            settings.Param5Name = value;
                            break;
                        case "Separator":
                            settings.Separator = value;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading auto naming settings: {ex.Message}");
            }

            return settings;
        }

        /// <summary>
        /// Получает путь к файлу настроек рядом с текущим NWF проектом
        /// </summary>
        private static string GetSettingsFilePath()
        {
            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null || string.IsNullOrEmpty(doc.FileName))
                    return null;

                string nwfDirectory = Path.GetDirectoryName(doc.FileName);
                string nwfFileName = Path.GetFileNameWithoutExtension(doc.FileName);
                return Path.Combine(nwfDirectory, $"{nwfFileName}_auto_naming_settings.json");
            }
            catch
            {
                return null;
            }
        }
    }
}
