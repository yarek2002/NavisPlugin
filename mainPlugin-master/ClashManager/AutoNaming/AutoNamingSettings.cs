using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager.AutoNaming
{
    /// <summary>
    /// Элемент параметра для динамического списка
    /// </summary>
    public class ParameterItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _parameterName;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ParameterName
        {
            get => _parameterName;
            set
            {
                if (_parameterName != value)
                {
                    _parameterName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Разделитель внутри параметра
        /// </summary>
        public string ParameterSeparator
        {
            get => _parameterSeparator;
            set
            {
                if (_parameterSeparator != value)
                {
                    _parameterSeparator = value;
                    OnPropertyChanged();
                }
            }
        }
        private string _parameterSeparator;

        public ParameterItem()
        {
            IsEnabled = false;
            ParameterName = string.Empty;
            ParameterSeparator = ",";
        }

        public ParameterItem(bool isEnabled, string parameterName)
        {
            IsEnabled = isEnabled;
            ParameterName = parameterName ?? string.Empty;
            ParameterSeparator = ",";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Настройки авто-наименования для конкретного теста
    /// </summary>
    public class TestAutoNamingSettings
    {
        public List<ParameterItem> Parameters { get; set; } = new List<ParameterItem>();

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

        /// <summary>
        /// Режим полного наименования (параметры составляют полное имя вместо добавления к базовому)
        /// </summary>
        public bool UseCompleteCustomNaming { get; set; }

        public TestAutoNamingSettings()
        {
            Separator = " | ";
            UseCompleteCustomNaming = false;
            // Initialize with one empty parameter
            Parameters.Add(new ParameterItem());
        }

        public List<string> GetActiveParameters()
        {
            var parameters = new List<string>();

            foreach (var param in Parameters)
            {
                if (param.IsEnabled && !string.IsNullOrWhiteSpace(param.ParameterName))
                {
                    parameters.Add(param.ParameterName);
                }
            }

            return parameters;
        }

        /// <summary>
        /// Добавляет новый параметр
        /// </summary>
        public void AddParameter(string parameterName = null)
        {
            Parameters.Add(new ParameterItem(false, parameterName ?? string.Empty));
        }

        /// <summary>
        /// Удаляет параметр по индексу
        /// </summary>
        public void RemoveParameter(int index)
        {
            if (index >= 0 && index < Parameters.Count)
            {
                Parameters.RemoveAt(index);
            }
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

                // Get test names for better readability
                var testNames = GetTestNames();

                foreach (var kvp in TestSettings)
                {
                    var testGuid = kvp.Key;
                    var settings = kvp.Value;

                    // Add test name as comment for readability
                    string testName = testNames.ContainsKey(testGuid) ? testNames[testGuid] : "Unknown Test";
                    lines.Add($"# Test: {testName}");
                    lines.Add($"[{testGuid}]");

                    // Save parameters
                    for (int i = 0; i < settings.Parameters.Count; i++)
                    {
                        var param = settings.Parameters[i];
                        lines.Add($"Param{i}Enabled={param.IsEnabled}");
                        lines.Add($"Param{i}Name={param.ParameterName ?? string.Empty}");
                        lines.Add($"Param{i}Separator={param.ParameterSeparator ?? ","}");
                    }

                    lines.Add($"Separator={settings.Separator ?? " | "}");
                    lines.Add($"UseCompleteCustomNaming={settings.UseCompleteCustomNaming}");
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
        /// Загружает настройки из файла рядом с текущим NWF проектом
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
                var parameterBuffer = new Dictionary<int, ParameterItem>();

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
                                // Apply buffered parameters
                                foreach (var kvp in parameterBuffer.OrderBy(k => k.Key))
                                {
                                    currentTestSettings.Parameters.Add(kvp.Value);
                                }
                                settings.TestSettings[currentTestGuid] = currentTestSettings;
                                parameterBuffer.Clear();
                            }

                            // Start new test settings
                            currentTestGuid = testGuid;
                            currentTestSettings = new TestAutoNamingSettings();
                            currentTestSettings.Parameters.Clear(); // Clear the default parameter
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

                            // Check if this is a parameter setting
                            if (key.StartsWith("Param") && key.Contains("Enabled"))
                            {
                                var paramIndexStr = key.Replace("Param", "").Replace("Enabled", "");
                                if (int.TryParse(paramIndexStr, out int paramIndex))
                                {
                                    if (!parameterBuffer.ContainsKey(paramIndex))
                                    {
                                        parameterBuffer[paramIndex] = new ParameterItem();
                                    }
                                    bool.TryParse(value, out bool isEnabled);
                                    parameterBuffer[paramIndex].IsEnabled = isEnabled;
                                }
                            }
                            else if (key.StartsWith("Param") && key.Contains("Name"))
                            {
                                var paramIndexStr = key.Replace("Param", "").Replace("Name", "");
                                if (int.TryParse(paramIndexStr, out int paramIndex))
                                {
                                    if (!parameterBuffer.ContainsKey(paramIndex))
                                    {
                                        parameterBuffer[paramIndex] = new ParameterItem();
                                    }
                                    parameterBuffer[paramIndex].ParameterName = value;
                                }
                            }
                            else if (key.StartsWith("Param") && key.Contains("Separator"))
                            {
                                var paramIndexStr = key.Replace("Param", "").Replace("Separator", "");
                                if (int.TryParse(paramIndexStr, out int paramIndex))
                                {
                                    if (!parameterBuffer.ContainsKey(paramIndex))
                                    {
                                        parameterBuffer[paramIndex] = new ParameterItem();
                                    }
                                    parameterBuffer[paramIndex].ParameterSeparator = value;
                                }
                            }
                            else if (key == "Separator")
                            {
                                currentTestSettings.Separator = value;
                            }
                            else if (key == "UseCompleteCustomNaming")
                            {
                                bool.TryParse(value, out bool useCompleteCustomNaming);
                                currentTestSettings.UseCompleteCustomNaming = useCompleteCustomNaming;
                            }
                        }
                    }
                }

                // Save the last test settings
                if (currentTestSettings != null && currentTestGuid != Guid.Empty)
                {
                    // Apply buffered parameters
                    foreach (var kvp in parameterBuffer.OrderBy(k => k.Key))
                    {
                        currentTestSettings.Parameters.Add(kvp.Value);
                    }
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
        /// Получает названия тестов для улучшения читаемости файла настроек
        /// </summary>
        private static Dictionary<Guid, string> GetTestNames()
        {
            var testNames = new Dictionary<Guid, string>();

            try
            {
                var doc = Application.ActiveDocument;
                if (doc?.GetClash()?.TestsData?.Tests != null)
                {
                    foreach (var test in doc.GetClash().TestsData.Tests)
                    {
                        if (test is ClashTest clashTest)
                        {
                            testNames[clashTest.Guid] = clashTest.DisplayName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting test names: {ex.Message}");
            }

            return testNames;
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
