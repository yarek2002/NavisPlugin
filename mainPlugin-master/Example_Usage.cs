// Пример использования автоматизации выгрузки отчетов
// Этот файл показывает, как использовать созданные классы

using System;
using System.Windows.Forms;
using ClashManager.Automation;
using ClashManager.Externals;

namespace ExampleUsage
{
    public class AutomationExample
    {
        /// <summary>
        /// Пример простого использования автоматизации
        /// </summary>
        public void SimpleAutomationExample()
        {
            // Выбираем папку для выгрузки
            var dialog = new FolderBrowserDialog();
            dialog.Description = "Выберите папку для выгрузки отчета";
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string outputPath = dialog.SelectedPath;
                
                try
                {
                    // Создаем автоматизацию с настройками по умолчанию
                    var automation = new ReportAutomation(outputPath);
                    
                    // Запускаем полный цикл автоматизации
                    automation.ExecuteFullAutomation();
                    
                    MessageBox.Show("Автоматизация завершена успешно!", "Готово");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
                }
            }
        }
        
        /// <summary>
        /// Пример использования с настройками
        /// </summary>
        public void CustomSettingsExample()
        {
            // Создаем настройки
            var settings = new AutomationSettings
            {
                RefreshTests = true,
                RunMagicWand = true,
                RunAutoNaming = false, // Отключаем авто-наименование
                ExportReport = true,
                ReportFormat = ReportFormat.HTML, // Экспортируем в HTML
                UseNavisworksReportSettings = true, // Используем настройки Navisworks
                IncludeDetailedInfo = true,
                IncludeCssInHtml = true,
                IncludeImagesInHtml = true,
                CloseNavisworks = false // Не закрываем Navisworks
            };
            
            // Сохраняем настройки в файл
            settings.SaveToFile(@"C:\temp\automation_settings.xml");
            
            // Выбираем папку для выгрузки
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string outputPath = dialog.SelectedPath;
                
                try
                {
                    // Создаем автоматизацию с нашими настройками
                    var automation = new ReportAutomation(outputPath, settings);
                    
                    // Запускаем автоматизацию
                    automation.ExecuteFullAutomation();
                    
                    MessageBox.Show("Автоматизация с настройками завершена!", "Готово");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
                }
            }
        }
        
        /// <summary>
        /// Пример загрузки настроек из файла
        /// </summary>
        public void LoadSettingsExample()
        {
            try
            {
                // Загружаем настройки из файла
                var settings = AutomationSettings.LoadFromFile(@"C:\temp\automation_settings.xml");
                
                // Выбираем папку для выгрузки
                var dialog = new FolderBrowserDialog();
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string outputPath = dialog.SelectedPath;
                    
                    // Создаем автоматизацию с загруженными настройками
                    var automation = new ReportAutomation(outputPath, settings);
                    
                    // Запускаем автоматизацию
                    automation.ExecuteFullAutomation();
                    
                    MessageBox.Show("Автоматизация с загруженными настройками завершена!", "Готово");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке настроек: {ex.Message}", "Ошибка");
            }
        }
        
        /// <summary>
        /// Пример использования через команду плагина
        /// </summary>
        public void PluginCommandExample()
        {
            // Этот пример показывает, как использовать команду из интерфейса плагина
            var command = new ReportAutomationCmd();
            command.Execute();
        }
    }
}

// Пример настройки для разных сценариев использования
namespace AutomationScenarios
{
    public class ScenarioSettings
    {
        /// <summary>
        /// Настройки для быстрого экспорта (только отчет)
        /// </summary>
        public static AutomationSettings QuickExport()
        {
            return new AutomationSettings
            {
                RefreshTests = false,
                RunMagicWand = false,
                RunAutoNaming = false,
                ExportReport = true,
                ReportFormat = ReportFormat.HTML,
                UseNavisworksReportSettings = true,
                IncludeDetailedInfo = false
            };
        }
        
        /// <summary>
        /// Настройки для полной обработки
        /// </summary>
        public static AutomationSettings FullProcessing()
        {
            return new AutomationSettings
            {
                RefreshTests = true,
                RunMagicWand = true,
                RunAutoNaming = true,
                ExportReport = true,
                ReportFormat = ReportFormat.HTML,
                UseNavisworksReportSettings = true,
                IncludeDetailedInfo = true,
                IncludeCssInHtml = true,
                IncludeImagesInHtml = true,
                CloseNavisworks = true
            };
        }
        
        /// <summary>
        /// Настройки для пакетной обработки
        /// </summary>
        public static AutomationSettings BatchProcessing()
        {
            return new AutomationSettings
            {
                RefreshTests = true,
                RunMagicWand = true,
                RunAutoNaming = true,
                ExportReport = true,
                ReportFormat = ReportFormat.HTML,
                UseNavisworksReportSettings = true,
                IncludeDetailedInfo = true,
                IncludeCssInHtml = true,
                IncludeImagesInHtml = true,
                CloseNavisworks = true,
                TimeoutMinutes = 60 // Увеличиваем таймаут для больших файлов
            };
        }
    }
}
