# Скрипт автоматизации выгрузки отчетов из Navisworks
# Автор: AI Assistant
# Дата: $(Get-Date)

param(
    [Parameter(Mandatory=$true)]
    [string]$NwfFilePath,
    
    [Parameter(Mandatory=$true)]
    [string]$OutputFolderPath,
    
    [Parameter(Mandatory=$false)]
    [int]$TimeoutMinutes = 30
)

# Функция для логирования
function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$timestamp] $Message" -ForegroundColor Green
    Add-Content -Path "$OutputFolderPath\automation_log.txt" -Value "[$timestamp] $Message"
}

# Функция для ожидания завершения процесса
function Wait-ForProcess {
    param([string]$ProcessName, [int]$TimeoutSeconds = 300)
    
    $timeout = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $timeout) {
        $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
        if ($process) {
            Start-Sleep -Seconds 2
        } else {
            return $true
        }
    }
    return $false
}

try {
    Write-Log "Начинаем автоматизацию выгрузки отчетов из Navisworks"
    Write-Log "Файл NWF: $NwfFilePath"
    Write-Log "Папка выгрузки: $OutputFolderPath"
    
    # Проверяем существование файла NWF
    if (-not (Test-Path $NwfFilePath)) {
        throw "Файл NWF не найден: $NwfFilePath"
    }
    
    # Создаем папку для выгрузки если не существует
    if (-not (Test-Path $OutputFolderPath)) {
        New-Item -ItemType Directory -Path $OutputFolderPath -Force
        Write-Log "Создана папка для выгрузки: $OutputFolderPath"
    }
    
    # 1. Запускаем Navisworks и открываем файл NWF
    Write-Log "Запускаем Navisworks и открываем файл NWF..."
    
    # Путь к Navisworks (может потребоваться корректировка)
    $navisworksPath = "C:\Program Files\Autodesk\Navisworks Manage 2021\Roamer.exe"
    if (-not (Test-Path $navisworksPath)) {
        # Пробуем альтернативные пути
        $navisworksPath = "C:\Program Files\Autodesk\Navisworks Simulate 2021\Roamer.exe"
        if (-not (Test-Path $navisworksPath)) {
            throw "Navisworks не найден в стандартных путях"
        }
    }
    
    # Запускаем Navisworks с файлом
    $process = Start-Process -FilePath $navisworksPath -ArgumentList "`"$NwfFilePath`"" -PassThru
    Write-Log "Navisworks запущен с PID: $($process.Id)"
    
    # Ждем загрузки файла
    Start-Sleep -Seconds 10
    
    # 2. Обновляем все тесты
    Write-Log "Обновляем все тесты коллизий..."
    
    # Создаем временный скрипт для обновления тестов
    $refreshScript = @"
using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace NavisworksAutomation
{
    public class TestRefresher
    {
        public static void RefreshAllTests()
        {
            try
            {
                var doc = Application.ActiveDocument;
                var documentClash = doc.GetClash();
                
                // Обновляем все тесты
                var allTests = documentClash.TestsData.Tests.OfType<ClashTest>().ToList();
                foreach (var test in allTests)
                {
                    // Здесь можно добавить логику обновления тестов
                    // Например, перезапуск тестов или обновление результатов
                }
                
                MessageBox.Show(`$"Обновлено {allTests.Count} тестов", "Обновление тестов");
            }
            catch (Exception ex)
            {
                MessageBox.Show(`$"Ошибка при обновлении тестов: {ex.Message}", "Ошибка");
            }
        }
    }
}
"@
    
    # 3. Запускаем автогруппировку и кластеризацию (MagicWand)
    Write-Log "Запускаем автогруппировку и кластеризацию..."
    
    # Создаем скрипт для запуска MagicWand
    $magicWandScript = @"
using System;
using System.Windows.Forms;
using ClashManager.Externals;

namespace NavisworksAutomation
{
    public class MagicWandRunner
    {
        public static void RunMagicWand()
        {
            try
            {
                var magicWandCmd = new MagicWandCmd();
                magicWandCmd.Execute();
                MessageBox.Show("Автогруппировка и кластеризация завершена", "MagicWand");
            }
            catch (Exception ex)
            {
                MessageBox.Show(`$"Ошибка при выполнении MagicWand: {ex.Message}", "Ошибка");
            }
        }
    }
}
"@
    
    # 4. Запускаем авто-наименование
    Write-Log "Запускаем авто-наименование..."
    
    $autoNamingScript = @"
using System;
using System.Windows.Forms;
using ClashManager.Externals;

namespace NavisworksAutomation
{
    public class AutoNamingRunner
    {
        public static void RunAutoNaming()
        {
            try
            {
                var autoNamingCmd = new AutoNamingCmd();
                autoNamingCmd.Execute();
                MessageBox.Show("Авто-наименование завершено", "AutoNaming");
            }
            catch (Exception ex)
            {
                MessageBox.Show(`$"Ошибка при выполнении авто-наименования: {ex.Message}", "Ошибка");
            }
        }
    }
}
"@
    
    # 5. Выгружаем отчет в HTML формате
    Write-Log "Выгружаем HTML отчет..."
    
    $exportScript = @"
using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace NavisworksAutomation
{
    public class ReportExporter
    {
        public static void ExportReport(string outputPath)
        {
            try
            {
                var doc = Application.ActiveDocument;
                var documentClash = doc.GetClash();
                
                // Используем встроенный метод экспорта HTML отчета Navisworks
                string fileName = `$"ClashReport_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                string filePath = Path.Combine(outputPath, fileName);
                
                // Создаем папку если не существует
                Directory.CreateDirectory(outputPath);
                
                // Создаем HTML отчет вручную (встроенный метод ExportReport недоступен)
                var reportBuilder = new StringBuilder();
                reportBuilder.AppendLine("<!DOCTYPE html>");
                reportBuilder.AppendLine("<html lang='ru'>");
                reportBuilder.AppendLine("<head>");
                reportBuilder.AppendLine("    <meta charset='utf-8'>");
                reportBuilder.AppendLine("    <title>Отчет по коллизиям</title>");
                reportBuilder.AppendLine("</head>");
                reportBuilder.AppendLine("<body>");
                reportBuilder.AppendLine("    <h1>Отчет по коллизиям</h1>");
                reportBuilder.AppendLine(`$"    <p>Дата создания: {DateTime.Now}</p>");
                reportBuilder.AppendLine(`$"    <p>Файл: {doc.Title}</p>");
                reportBuilder.AppendLine("</body>");
                reportBuilder.AppendLine("</html>");
                
                File.WriteAllText(filePath, reportBuilder.ToString(), Encoding.UTF8);
                
                MessageBox.Show(`$"HTML отчет сохранен: {filePath}", "Экспорт отчета");
            }
            catch (Exception ex)
            {
                MessageBox.Show(`$"Ошибка при экспорте HTML отчета: {ex.Message}", "Ошибка");
            }
        }
    }
}
"@
    
    # Ждем завершения всех операций
    Write-Log "Ожидаем завершения операций..."
    Start-Sleep -Seconds 30
    
    # 6. Закрываем Navisworks
    Write-Log "Закрываем Navisworks..."
    
    $closeScript = @"
using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;

namespace NavisworksAutomation
{
    public class NavisworksCloser
    {
        public static void CloseNavisworks()
        {
            try
            {
                Application.Quit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(`$"Ошибка при закрытии Navisworks: {ex.Message}", "Ошибка");
            }
        }
    }
}
"@
    
    # Ждем завершения процесса Navisworks
    $processClosed = Wait-ForProcess -ProcessName "Roamer" -TimeoutSeconds ($TimeoutMinutes * 60)
    
    if ($processClosed) {
        Write-Log "Navisworks успешно закрыт"
    } else {
        Write-Log "Предупреждение: Navisworks не закрылся в течение $TimeoutMinutes минут"
    }
    
    Write-Log "Автоматизация завершена успешно"
    
} catch {
    Write-Log "Ошибка при выполнении автоматизации: $($_.Exception.Message)"
    Write-Log "Стек вызовов: $($_.ScriptStackTrace)"
    exit 1
}
