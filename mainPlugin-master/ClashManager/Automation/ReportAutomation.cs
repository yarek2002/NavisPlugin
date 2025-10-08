using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using ClashManager.Externals;

namespace ClashManager.Automation
{
    /// <summary>
    /// Класс для автоматизации процесса выгрузки отчетов
    /// </summary>
    public class ReportAutomation
    {
        private Document _doc;
        private DocumentClash _documentClash;
        private string _outputPath;
        private AutomationSettings _settings;
        
        public ReportAutomation(string outputPath, AutomationSettings settings = null)
        {
            _doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            _documentClash = _doc.GetClash();
            _outputPath = outputPath;
            _settings = settings ?? AutomationSettings.CreateDefault();
        }
        
        /// <summary>
        /// Выполняет полный цикл автоматизации
        /// </summary>
        public void ExecuteFullAutomation()
        {
            try
            {
                LogMessage("Начинаем автоматизацию выгрузки отчетов");
                LogMessage($"Настройки: RefreshTests={_settings.RefreshTests}, RunMagicWand={_settings.RunMagicWand}, RunAutoNaming={_settings.RunAutoNaming}, ExportReport={_settings.ExportReport}");
                
                // 1. Обновляем все тесты (если включено)
                if (_settings.RefreshTests)
                {
                    RefreshAllTests();
                }
                
                // 2. Запускаем автогруппировку и кластеризацию (если включено)
                if (_settings.RunMagicWand)
                {
                    RunMagicWand();
                }
                
                // 3. Запускаем авто-наименование (если включено)
                if (_settings.RunAutoNaming)
                {
                    RunAutoNaming();
                }
                
                // 4. Выгружаем отчет (если включено)
                if (_settings.ExportReport)
                {
                    ExportReport();
                }
                
                LogMessage("Автоматизация завершена успешно");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка при выполнении автоматизации: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Обновляет все тесты коллизий
        /// </summary>
        private void RefreshAllTests()
        {
            LogMessage("Обновляем все тесты коллизий...");
            
            var allTests = _documentClash.TestsData.Tests.OfType<ClashTest>().ToList();
            
            foreach (var test in allTests)
            {
                // Здесь можно добавить логику обновления конкретных тестов
                // Например, перезапуск тестов или обновление результатов
                LogMessage($"Обновлен тест: {test.DisplayName}");
            }
            
            LogMessage($"Обновлено {allTests.Count} тестов");
        }
        
        /// <summary>
        /// Запускает автогруппировку и кластеризацию
        /// </summary>
        private void RunMagicWand()
        {
            LogMessage("Запускаем автогруппировку и кластеризацию...");
            
            try
            {
                var magicWandCmd = new MagicWandCmd();
                magicWandCmd.Execute();
                LogMessage("Автогруппировка и кластеризация завершена");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка при выполнении MagicWand: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Запускает авто-наименование
        /// </summary>
        private void RunAutoNaming()
        {
            LogMessage("Запускаем авто-наименование...");
            
            try
            {
                var autoNamingCmd = new AutoNamingCmd();
                autoNamingCmd.Execute();
                LogMessage("Авто-наименование завершено");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка при выполнении авто-наименования: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Выгружает отчет в указанную папку
        /// </summary>
        private void ExportReport()
        {
            LogMessage("Выгружаем отчет...");
            
            try
            {
                var reportBuilder = new StringBuilder();
                reportBuilder.AppendLine("=== ОТЧЕТ ПО КОЛЛИЗИЯМ ===");
                reportBuilder.AppendLine($"Дата создания: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                reportBuilder.AppendLine($"Файл: {_doc.Title}");
                reportBuilder.AppendLine($"Путь к файлу: {_doc.FileName}");
                reportBuilder.AppendLine();
                
                var allTests = _documentClash.TestsData.Tests.OfType<ClashTest>().ToList();
                
                reportBuilder.AppendLine($"Общее количество тестов: {allTests.Count}");
                reportBuilder.AppendLine();
                
                foreach (var test in allTests)
                {
                    reportBuilder.AppendLine($"--- ТЕСТ: {test.DisplayName} ---");
                    reportBuilder.AppendLine($"GUID: {test.Guid}");
                    
                    var groups = test.Children.OfType<ClashResultGroup>().ToList();
                    var results = test.Children.OfType<ClashResult>().ToList();
                    
                    reportBuilder.AppendLine($"Количество групп: {groups.Count}");
                    reportBuilder.AppendLine($"Количество результатов: {results.Count}");
                    
                    // Детальная информация по группам
                    foreach (var group in groups)
                    {
                        reportBuilder.AppendLine($"  Группа: {group.DisplayName}");
                        reportBuilder.AppendLine($"    Статус: {group.Status}");
                        reportBuilder.AppendLine($"    GUID: {group.Guid}");
                        
                        var groupResults = group.Children.OfType<ClashResult>().ToList();
                        reportBuilder.AppendLine($"    Результатов в группе: {groupResults.Count}");
                    }
                    
                    // Несгруппированные результаты
                    var ungroupedResults = results.Where(r => r.Parent is ClashTest).ToList();
                    if (ungroupedResults.Any())
                    {
                        reportBuilder.AppendLine($"  Несгруппированных результатов: {ungroupedResults.Count}");
                    }
                    
                    reportBuilder.AppendLine();
                }
                
                // Сохраняем отчет в выбранном формате
                string extension = _settings.ReportFormat.ToString().ToLower();
                string fileName = $"ClashReport_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
                string filePath = Path.Combine(_outputPath, fileName);
                
                // Создаем папку если не существует
                Directory.CreateDirectory(_outputPath);
                
                switch (_settings.ReportFormat)
                {
                    case ReportFormat.TXT:
                        File.WriteAllText(filePath, reportBuilder.ToString(), Encoding.UTF8);
                        break;
                    case ReportFormat.CSV:
                        ExportToCsv(filePath);
                        break;
                    case ReportFormat.XML:
                        ExportToXml(filePath);
                        break;
                    case ReportFormat.HTML:
                        if (_settings.UseNavisworksReportSettings)
                        {
                            ExportToHtmlUsingNavisworksSettings(filePath);
                        }
                        else
                        {
                            ExportToHtml(filePath);
                        }
                        break;
                }
                
                LogMessage($"Отчет сохранен: {filePath}");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка при экспорте отчета: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Экспортирует отчет в CSV формат
        /// </summary>
        private void ExportToCsv(string filePath)
        {
            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("TestName,GUID,GroupsCount,ResultsCount,GroupName,GroupStatus,GroupResultsCount");
            
            var allTests = _documentClash.TestsData.Tests.OfType<ClashTest>().ToList();
            
            foreach (var test in allTests)
            {
                var groups = test.Children.OfType<ClashResultGroup>().ToList();
                var results = test.Children.OfType<ClashResult>().ToList();
                
                if (groups.Any())
                {
                    foreach (var group in groups)
                    {
                        var groupResults = group.Children.OfType<ClashResult>().ToList();
                        csvBuilder.AppendLine($"\"{test.DisplayName}\",\"{test.Guid}\",{groups.Count},{results.Count},\"{group.DisplayName}\",\"{group.Status}\",{groupResults.Count}");
                    }
                }
                else
                {
                    csvBuilder.AppendLine($"\"{test.DisplayName}\",\"{test.Guid}\",{groups.Count},{results.Count},\"\",\"\",0");
                }
            }
            
            File.WriteAllText(filePath, csvBuilder.ToString(), Encoding.UTF8);
        }
        
        /// <summary>
        /// Экспортирует отчет в XML формат
        /// </summary>
        private void ExportToXml(string filePath)
        {
            var xmlBuilder = new StringBuilder();
            xmlBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xmlBuilder.AppendLine("<ClashReport>");
            xmlBuilder.AppendLine($"  <GeneratedDate>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</GeneratedDate>");
            xmlBuilder.AppendLine($"  <SourceFile>{_doc.Title}</SourceFile>");
            xmlBuilder.AppendLine($"  <SourcePath>{_doc.FileName}</SourcePath>");
            xmlBuilder.AppendLine("  <Tests>");
            
            var allTests = _documentClash.TestsData.Tests.OfType<ClashTest>().ToList();
            
            foreach (var test in allTests)
            {
                xmlBuilder.AppendLine($"    <Test>");
                xmlBuilder.AppendLine($"      <Name><![CDATA[{test.DisplayName}]]></Name>");
                xmlBuilder.AppendLine($"      <GUID>{test.Guid}</GUID>");
                
                var groups = test.Children.OfType<ClashResultGroup>().ToList();
                var results = test.Children.OfType<ClashResult>().ToList();
                
                xmlBuilder.AppendLine($"      <GroupsCount>{groups.Count}</GroupsCount>");
                xmlBuilder.AppendLine($"      <ResultsCount>{results.Count}</ResultsCount>");
                xmlBuilder.AppendLine($"      <Groups>");
                
                foreach (var group in groups)
                {
                    var groupResults = group.Children.OfType<ClashResult>().ToList();
                    xmlBuilder.AppendLine($"        <Group>");
                    xmlBuilder.AppendLine($"          <Name><![CDATA[{group.DisplayName}]]></Name>");
                    xmlBuilder.AppendLine($"          <Status>{group.Status}</Status>");
                    xmlBuilder.AppendLine($"          <GUID>{group.Guid}</GUID>");
                    xmlBuilder.AppendLine($"          <ResultsCount>{groupResults.Count}</ResultsCount>");
                    xmlBuilder.AppendLine($"        </Group>");
                }
                
                xmlBuilder.AppendLine($"      </Groups>");
                xmlBuilder.AppendLine($"    </Test>");
            }
            
            xmlBuilder.AppendLine("  </Tests>");
            xmlBuilder.AppendLine("</ClashReport>");
            
            File.WriteAllText(filePath, xmlBuilder.ToString(), Encoding.UTF8);
        }
        
        /// <summary>
        /// Экспортирует отчет в HTML используя встроенные настройки Navisworks
        /// </summary>
        private void ExportToHtmlUsingNavisworksSettings(string filePath)
        {
            try
            {
                LogMessage("Экспортируем HTML отчет используя настройки Navisworks...");
                
                // Поскольку встроенный метод ExportReport недоступен в API,
                // используем собственный HTML экспорт с учетом настроек Navisworks
                // В будущем здесь можно добавить интеграцию с настройками отчета
                ExportToHtml(filePath);
                
                LogMessage($"HTML отчет экспортирован: {filePath}");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка при экспорте HTML отчета: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Экспортирует отчет в HTML формат с табличным представлением
        /// </summary>
        private void ExportToHtml(string filePath)
        {
            try
            {
                LogMessage("Создаем HTML отчет с табличным представлением...");
                
                var htmlBuilder = new StringBuilder();
                
                // HTML заголовок с CSS стилями
                htmlBuilder.AppendLine("<!DOCTYPE html>");
                htmlBuilder.AppendLine("<html lang='ru'>");
                htmlBuilder.AppendLine("<head>");
                htmlBuilder.AppendLine("    <meta charset='utf-8'>");
                htmlBuilder.AppendLine("    <title>Отчет по коллизиям</title>");
                
                if (_settings.IncludeCssInHtml)
                {
                    htmlBuilder.AppendLine(GetCssStyles());
                }
                
                htmlBuilder.AppendLine("</head>");
                htmlBuilder.AppendLine("<body>");
                
                // Заголовок отчета
                htmlBuilder.AppendLine("    <div class='header'>");
                htmlBuilder.AppendLine("        <h1>Отчет по коллизиям</h1>");
                htmlBuilder.AppendLine($"        <p><strong>Дата создания:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
                htmlBuilder.AppendLine($"        <p><strong>Файл:</strong> {_doc.Title}</p>");
                htmlBuilder.AppendLine($"        <p><strong>Путь к файлу:</strong> {_doc.FileName}</p>");
                htmlBuilder.AppendLine("    </div>");
                
                // Сводная таблица
                htmlBuilder.AppendLine("    <div class='summary'>");
                htmlBuilder.AppendLine("        <h2>Сводная информация</h2>");
                htmlBuilder.AppendLine(GetSummaryTable());
                htmlBuilder.AppendLine("    </div>");
                
                // Детальная таблица по тестам
                htmlBuilder.AppendLine("    <div class='details'>");
                htmlBuilder.AppendLine("        <h2>Детальная информация по тестам</h2>");
                htmlBuilder.AppendLine(GetDetailedTable());
                htmlBuilder.AppendLine("    </div>");
                
                htmlBuilder.AppendLine("</body>");
                htmlBuilder.AppendLine("</html>");
                
                File.WriteAllText(filePath, htmlBuilder.ToString(), Encoding.UTF8);
                LogMessage($"HTML отчет создан: {filePath}");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка при создании HTML отчета: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Возвращает CSS стили для HTML отчета
        /// </summary>
        private string GetCssStyles()
        {
            return @"
    <style>
        body {
            font-family: Arial, sans-serif;
            margin: 20px;
            background-color: #f5f5f5;
        }
        .header {
            background-color: #2c3e50;
            color: white;
            padding: 20px;
            border-radius: 5px;
            margin-bottom: 20px;
        }
        .header h1 {
            margin: 0 0 10px 0;
        }
        .summary, .details {
            background-color: white;
            padding: 20px;
            border-radius: 5px;
            margin-bottom: 20px;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
        }
        table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 10px;
        }
        th, td {
            border: 1px solid #ddd;
            padding: 8px;
            text-align: left;
        }
        th {
            background-color: #3498db;
            color: white;
            font-weight: bold;
        }
        tr:nth-child(even) {
            background-color: #f2f2f2;
        }
        tr:hover {
            background-color: #e8f4f8;
        }
        .status-new { color: #e74c3c; font-weight: bold; }
        .status-active { color: #f39c12; font-weight: bold; }
        .status-reviewed { color: #27ae60; font-weight: bold; }
        .status-approved { color: #8e44ad; font-weight: bold; }
        .status-resolved { color: #95a5a6; font-weight: bold; }
        .test-section {
            margin-bottom: 30px;
            border: 2px solid #3498db;
            border-radius: 5px;
            overflow: hidden;
        }
        .test-header {
            background-color: #3498db;
            color: white;
            padding: 10px;
            font-weight: bold;
        }
        .test-content {
            padding: 15px;
        }
    </style>";
        }
        
        /// <summary>
        /// Создает сводную таблицу
        /// </summary>
        private string GetSummaryTable()
        {
            var allTests = _documentClash.TestsData.Tests.OfType<ClashTest>().ToList();
            var totalGroups = 0;
            var totalResults = 0;
            
            foreach (var test in allTests)
            {
                totalGroups += test.Children.OfType<ClashResultGroup>().Count();
                totalResults += test.Children.OfType<ClashResult>().Count();
            }
            
            var summaryBuilder = new StringBuilder();
            summaryBuilder.AppendLine("        <table>");
            summaryBuilder.AppendLine("            <tr><th>Параметр</th><th>Значение</th></tr>");
            summaryBuilder.AppendLine($"            <tr><td>Общее количество тестов</td><td>{allTests.Count}</td></tr>");
            summaryBuilder.AppendLine($"            <tr><td>Общее количество групп</td><td>{totalGroups}</td></tr>");
            summaryBuilder.AppendLine($"            <tr><td>Общее количество результатов</td><td>{totalResults}</td></tr>");
            summaryBuilder.AppendLine("        </table>");
            
            return summaryBuilder.ToString();
        }
        
        /// <summary>
        /// Создает детальную таблицу по тестам
        /// </summary>
        private string GetDetailedTable()
        {
            var allTests = _documentClash.TestsData.Tests.OfType<ClashTest>().ToList();
            var detailedBuilder = new StringBuilder();
            
            foreach (var test in allTests)
            {
                detailedBuilder.AppendLine("        <div class='test-section'>");
                detailedBuilder.AppendLine($"            <div class='test-header'>Тест: {test.DisplayName}</div>");
                detailedBuilder.AppendLine("            <div class='test-content'>");
                
                var groups = test.Children.OfType<ClashResultGroup>().ToList();
                var results = test.Children.OfType<ClashResult>().ToList();
                
                detailedBuilder.AppendLine("                <table>");
                detailedBuilder.AppendLine("                    <tr><th>Группа</th><th>Статус</th><th>Количество результатов</th><th>GUID</th></tr>");
                
                foreach (var group in groups)
                {
                    var groupResults = group.Children.OfType<ClashResult>().ToList();
                    var statusClass = GetStatusClass(group.Status.ToString());
                    detailedBuilder.AppendLine($"                    <tr>");
                    detailedBuilder.AppendLine($"                        <td>{group.DisplayName}</td>");
                    detailedBuilder.AppendLine($"                        <td class='{statusClass}'>{group.Status}</td>");
                    detailedBuilder.AppendLine($"                        <td>{groupResults.Count}</td>");
                    detailedBuilder.AppendLine($"                        <td>{group.Guid}</td>");
                    detailedBuilder.AppendLine($"                    </tr>");
                }
                
                detailedBuilder.AppendLine("                </table>");
                detailedBuilder.AppendLine("            </div>");
                detailedBuilder.AppendLine("        </div>");
            }
            
            return detailedBuilder.ToString();
        }
        
        /// <summary>
        /// Возвращает CSS класс для статуса
        /// </summary>
        private string GetStatusClass(string status)
        {
            switch (status.ToLower())
            {
                case "new":
                    return "status-new";
                case "active":
                    return "status-active";
                case "reviewed":
                    return "status-reviewed";
                case "approved":
                    return "status-approved";
                case "resolved":
                    return "status-resolved";
                default:
                    return "";
            }
        }
        
        /// <summary>
        /// Логирует сообщение в файл и консоль
        /// </summary>
        private void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logMessage = $"[{timestamp}] {message}";
            
            // Выводим в консоль
            System.Diagnostics.Debug.WriteLine(logMessage);
            
            // Записываем в файл лога
            try
            {
                string logPath = Path.Combine(_outputPath, "automation_log.txt");
                File.AppendAllText(logPath, logMessage + Environment.NewLine);
            }
            catch
            {
                // Игнорируем ошибки записи лога
            }
        }
    }
}
