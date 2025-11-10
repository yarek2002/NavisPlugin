using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace ClashManager.AutoNaming
{
    public class TestNamingSettings
    {
        public Dictionary<Guid, string> TestCustomNames { get; set; } = new Dictionary<Guid, string>();

        public TestNamingSettings()
        {
        }

        /// <summary>
        /// Получает пользовательское имя для теста или пустую строку, если не задано
        /// </summary>
        public string GetCustomName(Guid testGuid)
        {
            return TestCustomNames.ContainsKey(testGuid) ? TestCustomNames[testGuid] : string.Empty;
        }

        /// <summary>
        /// Устанавливает пользовательское имя для теста
        /// </summary>
        public void SetCustomName(Guid testGuid, string customName)
        {
            if (string.IsNullOrWhiteSpace(customName))
            {
                TestCustomNames.Remove(testGuid);
            }
            else
            {
                TestCustomNames[testGuid] = customName;
            }
        }

        /// <summary>
        /// Сохраняет настройки в файл рядом с текущим NWF проектом
        /// </summary>
        public void SaveToFile()
        {
            try
            {
                string filePath = GetSettingsFilePath();
                if (string.IsNullOrEmpty(filePath))
                    return;

                var lines = new List<string>();
                foreach (var kvp in TestCustomNames)
                {
                    lines.Add($"{kvp.Key}|{kvp.Value}");
                }

                File.WriteAllLines(filePath, lines);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving test naming settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает настройки из файла рядом с текущим NWF проектом
        /// </summary>
        public static TestNamingSettings LoadFromFile()
        {
            var settings = new TestNamingSettings();

            try
            {
                string filePath = GetSettingsFilePath();
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return settings;

                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split('|');
                    if (parts.Length == 2 && Guid.TryParse(parts[0], out Guid testGuid))
                    {
                        settings.TestCustomNames[testGuid] = parts[1];
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading test naming settings: {ex.Message}");
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
                return Path.Combine(nwfDirectory, $"{nwfFileName}_test_naming_settings.txt");
            }
            catch
            {
                return null;
            }
        }
    }
}
