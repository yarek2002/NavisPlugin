using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;

namespace ClashManager.AutoNaming
{
    /// <summary>
    /// Настройки авто-наименования для конкретного теста
    /// </summary>
    public class TestAutoNamingSettings
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

        public TestAutoNamingSettings()
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
    }

    /// <summary>
    /// Контейнер настроек авто-наименования для всех тестов
    /// </summary>
    public class AutoNamingSettings
    {
        public Dictionary<Guid, TestAutoNamingSettings> TestSettings { get; set; } = new Dictionary<Guid, TestAutoNamingSettings>();

        /// <summary>
        /// Получает настройки для конкретного теста
        /// </summary>
        public TestAutoNamingSettings GetTestSettings(Guid testGuid)
        {
            return TestSettings.ContainsKey(testGuid) ? TestSettings[testGuid] : null;
        }

        /// <summary>
        /// Устанавливает настройки для конкретного теста
        /// </summary>
        public void SetTestSettings(Guid testGuid, TestAutoNamingSettings settings)
        {
            if (settings == null)
            {
                TestSettings.Remove(testGuid);
            }
            else
            {
                TestSettings[testGuid] = settings;
            }
        }

        /// <summary>
        /// Устанавливает настройки для нескольких тестов
        /// </summary>
        public void SetTestSettings(List<Guid> testGuids, TestAutoNamingSettings settings)
        {
            foreach (var testGuid in testGuids)
            {
                SetTestSettings(testGuid, settings);
            }
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

                foreach (var kvp in TestSettings)
                {
                    var testGuid = kvp.Key;
                    var settings = kvp.Value;

                    lines.Add($"[{testGuid}]");
                    lines.Add($"IncludeParam1={settings.IncludeParam1}");
                    lines.Add($"Param1Name={settings.Param1Name ?? string.Empty}");
                    lines.Add($"IncludeParam2={settings.IncludeParam2}");
                    lines.Add($"Param2Name={settings.Param2Name ?? string.Empty}");
                    lines.Add($"IncludeParam3={settings.IncludeParam3}");
                    lines.Add($"Param3Name={settings.Param3Name ?? string.Empty}");
                    lines.Add($"IncludeParam4={settings.IncludeParam4}");
                    lines.Add($"Param4Name={settings.Param4Name ?? string.Empty}");
                    lines.Add($"IncludeParam5={settings.IncludeParam5}");
                    lines.Add($"Param5Name={settings.Param5Name ?? string.Empty}");
                    lines.Add($"Separator={settings.Separator ?? " | "}");
                    lines.Add(""); // Empty line between tests
                }

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
                Guid currentTestGuid = Guid.Empty;
                TestAutoNamingSettings currentTestSettings = null;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    // Check if this is a test GUID header
                    if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                    {
                        var guidString = trimmedLine.Trim('[', ']');
                        if (Guid.TryParse(guidString, out Guid testGuid))
                        {
                            // Save previous test settings if any
                            if (currentTestSettings != null && currentTestGuid != Guid.Empty)
                            {
                                settings.TestSettings[currentTestGuid] = currentTestSettings;
                            }

                            // Start new test settings
                            currentTestGuid = testGuid;
                            currentTestSettings = new TestAutoNamingSettings();
                        }
                        continue;
                    }

                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(trimmedLine))
                        continue;

                    // Parse key-value pairs
                    if (trimmedLine.Contains("=") && currentTestSettings != null)
                    {
                        var parts = trimmedLine.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();

                            switch (key)
                            {
                                case "IncludeParam1":
                                    bool.TryParse(value, out bool include1);
                                    currentTestSettings.IncludeParam1 = include1;
                                    break;
                                case "Param1Name":
                                    currentTestSettings.Param1Name = value;
                                    break;
                                case "IncludeParam2":
                                    bool.TryParse(value, out bool include2);
                                    currentTestSettings.IncludeParam2 = include2;
                                    break;
                                case "Param2Name":
                                    currentTestSettings.Param2Name = value;
                                    break;
                                case "IncludeParam3":
                                    bool.TryParse(value, out bool include3);
                                    currentTestSettings.IncludeParam3 = include3;
                                    break;
                                case "Param3Name":
                                    currentTestSettings.Param3Name = value;
                                    break;
                                case "IncludeParam4":
                                    bool.TryParse(value, out bool include4);
                                    currentTestSettings.IncludeParam4 = include4;
                                    break;
                                case "Param4Name":
                                    currentTestSettings.Param4Name = value;
                                    break;
                                case "IncludeParam5":
                                    bool.TryParse(value, out bool include5);
                                    currentTestSettings.IncludeParam5 = include5;
                                    break;
                                case "Param5Name":
                                    currentTestSettings.Param5Name = value;
                                    break;
                                case "Separator":
                                    currentTestSettings.Separator = value;
                                    break;
                            }
                        }
                    }
                }

                // Save the last test settings
                if (currentTestSettings != null && currentTestGuid != Guid.Empty)
                {
                    settings.TestSettings[currentTestGuid] = currentTestSettings;
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
