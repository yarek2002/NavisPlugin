using System;
using System.IO;
using System.Xml.Serialization;

namespace ClashManager.Automation
{
    /// <summary>
    /// Настройки автоматизации
    /// </summary>
    [Serializable]
    public class AutomationSettings
    {
        /// <summary>
        /// Путь к Navisworks
        /// </summary>
        public string NavisworksPath { get; set; } = @"C:\Program Files\Autodesk\Navisworks Manage 2021\Roamer.exe";
        
        /// <summary>
        /// Таймаут выполнения в минутах
        /// </summary>
        public int TimeoutMinutes { get; set; } = 30;
        
        /// <summary>
        /// Включать ли обновление тестов
        /// </summary>
        public bool RefreshTests { get; set; } = true;
        
        /// <summary>
        /// Включать ли автогруппировку
        /// </summary>
        public bool RunMagicWand { get; set; } = true;
        
        /// <summary>
        /// Включать ли авто-наименование
        /// </summary>
        public bool RunAutoNaming { get; set; } = true;
        
        /// <summary>
        /// Включать ли экспорт отчета
        /// </summary>
        public bool ExportReport { get; set; } = true;
        
        /// <summary>
        /// Закрывать ли Navisworks после завершения
        /// </summary>
        public bool CloseNavisworks { get; set; } = true;
        
        /// <summary>
        /// Формат отчета (TXT, CSV, XML, HTML)
        /// </summary>
        public ReportFormat ReportFormat { get; set; } = ReportFormat.HTML;
        
        /// <summary>
        /// Включать ли детальную информацию в отчет
        /// </summary>
        public bool IncludeDetailedInfo { get; set; } = true;
        
        /// <summary>
        /// Использовать ли встроенные настройки отчета Navisworks
        /// </summary>
        public bool UseNavisworksReportSettings { get; set; } = true;
        
        /// <summary>
        /// Путь к шаблону отчета (если используется)
        /// </summary>
        public string ReportTemplatePath { get; set; } = "";
        
        /// <summary>
        /// Включать ли изображения в HTML отчет
        /// </summary>
        public bool IncludeImagesInHtml { get; set; } = true;
        
        /// <summary>
        /// Включать ли CSS стили в HTML отчет
        /// </summary>
        public bool IncludeCssInHtml { get; set; } = true;
        
        /// <summary>
        /// Сохраняет настройки в файл
        /// </summary>
        public void SaveToFile(string filePath)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(AutomationSettings));
                using (var writer = new StreamWriter(filePath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при сохранении настроек: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Загружает настройки из файла
        /// </summary>
        public static AutomationSettings LoadFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new AutomationSettings();
                }
                
                var serializer = new XmlSerializer(typeof(AutomationSettings));
                using (var reader = new StreamReader(filePath))
                {
                    return (AutomationSettings)serializer.Deserialize(reader);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при загрузке настроек: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Создает настройки по умолчанию
        /// </summary>
        public static AutomationSettings CreateDefault()
        {
            return new AutomationSettings();
        }
    }
    
    /// <summary>
    /// Формат отчета
    /// </summary>
    public enum ReportFormat
    {
        TXT,
        CSV,
        XML,
        HTML
    }
}
