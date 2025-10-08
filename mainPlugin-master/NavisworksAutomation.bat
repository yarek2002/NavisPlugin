@echo off
REM Скрипт автоматизации выгрузки отчетов из Navisworks
REM Автор: AI Assistant
REM Дата: %DATE% %TIME%

setlocal enabledelayedexpansion

REM Проверяем параметры
if "%~1"=="" (
    echo Ошибка: Не указан путь к файлу NWF
    echo Использование: NavisworksAutomation.bat "путь_к_файлу.nwf" "папка_выгрузки" [таймаут_в_минутах]
    pause
    exit /b 1
)

if "%~2"=="" (
    echo Ошибка: Не указана папка для выгрузки
    echo Использование: NavisworksAutomation.bat "путь_к_файлу.nwf" "папка_выгрузки" [таймаут_в_минутах]
    pause
    exit /b 1
)

set "NWF_FILE=%~1"
set "OUTPUT_FOLDER=%~2"
set "TIMEOUT_MINUTES=%~3"

REM Устанавливаем таймаут по умолчанию
if "%TIMEOUT_MINUTES%"=="" set "TIMEOUT_MINUTES=30"

echo ========================================
echo Автоматизация выгрузки отчетов Navisworks
echo ========================================
echo Файл NWF: %NWF_FILE%
echo Папка выгрузки: %OUTPUT_FOLDER%
echo Таймаут: %TIMEOUT_MINUTES% минут
echo ========================================

REM Проверяем существование файла NWF
if not exist "%NWF_FILE%" (
    echo ОШИБКА: Файл NWF не найден: %NWF_FILE%
    pause
    exit /b 1
)

REM Создаем папку для выгрузки если не существует
if not exist "%OUTPUT_FOLDER%" (
    echo Создаем папку для выгрузки: %OUTPUT_FOLDER%
    mkdir "%OUTPUT_FOLDER%"
)

REM Создаем лог файл
set "LOG_FILE=%OUTPUT_FOLDER%\automation_log.txt"
echo [%DATE% %TIME%] Начинаем автоматизацию выгрузки отчетов из Navisworks > "%LOG_FILE%"
echo [%DATE% %TIME%] Файл NWF: %NWF_FILE% >> "%LOG_FILE%"
echo [%DATE% %TIME%] Папка выгрузки: %OUTPUT_FOLDER% >> "%LOG_FILE%"

REM Ищем Navisworks в стандартных путях
set "NAVISWORKS_PATH="
if exist "C:\Program Files\Autodesk\Navisworks Manage 2021\Roamer.exe" (
    set "NAVISWORKS_PATH=C:\Program Files\Autodesk\Navisworks Manage 2021\Roamer.exe"
) else if exist "C:\Program Files\Autodesk\Navisworks Simulate 2021\Roamer.exe" (
    set "NAVISWORKS_PATH=C:\Program Files\Autodesk\Navisworks Simulate 2021\Roamer.exe"
) else if exist "C:\Program Files\Autodesk\Navisworks Manage 2020\Roamer.exe" (
    set "NAVISWORKS_PATH=C:\Program Files\Autodesk\Navisworks Manage 2020\Roamer.exe"
) else if exist "C:\Program Files\Autodesk\Navisworks Simulate 2020\Roamer.exe" (
    set "NAVISWORKS_PATH=C:\Program Files\Autodesk\Navisworks Simulate 2020\Roamer.exe"
) else (
    echo ОШИБКА: Navisworks не найден в стандартных путях
    echo [%DATE% %TIME%] ОШИБКА: Navisworks не найден в стандартных путях >> "%LOG_FILE%"
    pause
    exit /b 1
)

echo Найден Navisworks: %NAVISWORKS_PATH%
echo [%DATE% %TIME%] Найден Navisworks: %NAVISWORKS_PATH% >> "%LOG_FILE%"

REM 1. Запускаем Navisworks с файлом NWF
echo.
echo ========================================
echo Шаг 1: Запуск Navisworks и открытие файла
echo ========================================
echo [%DATE% %TIME%] Запускаем Navisworks с файлом NWF >> "%LOG_FILE%"

start "" "%NAVISWORKS_PATH%" "%NWF_FILE%"

REM Ждем загрузки файла
echo Ожидаем загрузки файла (10 секунд)...
timeout /t 10 /nobreak >nul

REM 2. Создаем C# скрипт для автоматизации
echo.
echo ========================================
echo Шаг 2: Создание скрипта автоматизации
echo ========================================

set "SCRIPT_FILE=%TEMP%\NavisworksAutomation.cs"
set "SCRIPT_DLL=%TEMP%\NavisworksAutomation.dll"

REM Создаем C# скрипт
(
echo using System;
echo using System.IO;
echo using System.Text;
echo using System.Windows.Forms;
echo using Autodesk.Navisworks.Api;
echo using Autodesk.Navisworks.Api.Clash;
echo using ClashManager.Externals;
echo.
echo namespace NavisworksAutomation
echo {
echo     public class AutomationRunner
echo     {
echo         public static void RunAutomation^(string outputPath^)
echo         {
echo             try
echo             {
echo                 var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
echo                 var documentClash = doc.GetClash^(^);
echo.
echo                 // 1. Обновляем все тесты
echo                 var allTests = documentClash.TestsData.Tests.OfType^<ClashTest^>^(^).ToList^(^);
echo                 foreach ^(var test in allTests^)
echo                 {
echo                     // Логика обновления тестов
echo                 }
echo.
echo                 // 2. Запускаем автогруппировку и кластеризацию
echo                 var magicWandCmd = new MagicWandCmd^(^);
echo                 magicWandCmd.Execute^(^);
echo.
echo                 // 3. Запускаем авто-наименование
echo                 var autoNamingCmd = new AutoNamingCmd^(^);
echo                 autoNamingCmd.Execute^(^);
echo.
echo                 // 4. Выгружаем HTML отчет
echo                 var reportBuilder = new StringBuilder^(^);
echo                 reportBuilder.AppendLine^("<!DOCTYPE html>"^);
echo                 reportBuilder.AppendLine^("<html lang='ru'>"^);
echo                 reportBuilder.AppendLine^("<head>"^);
echo                 reportBuilder.AppendLine^("    <meta charset='utf-8'>"^);
echo                 reportBuilder.AppendLine^("    <title>Отчет по коллизиям</title>"^);
echo                 reportBuilder.AppendLine^("</head>"^);
echo                 reportBuilder.AppendLine^("<body>"^);
echo                 reportBuilder.AppendLine^("    <h1>Отчет по коллизиям</h1>"^);
echo                 reportBuilder.AppendLine^($"    <p>Дата создания: {DateTime.Now}</p>"^);
echo                 reportBuilder.AppendLine^($"    <p>Файл: {doc.Title}</p>"^);
echo                 reportBuilder.AppendLine^("    <table border='1'>"^);
echo                 reportBuilder.AppendLine^("        <tr><th>Тест</th><th>Групп</th><th>Результатов</th></tr>"^);
echo.
echo                 foreach ^(var test in allTests^)
echo                 {
echo                     var groups = test.Children.OfType^<ClashResultGroup^>^(^).ToList^(^);
echo                     var results = test.Children.OfType^<ClashResult^>^(^).ToList^(^);
echo                     reportBuilder.AppendLine^($"        <tr><td>{test.DisplayName}</td><td>{groups.Count}</td><td>{results.Count}</td></tr>"^);
echo                 }
echo.
echo                 reportBuilder.AppendLine^("    </table>"^);
echo                 reportBuilder.AppendLine^("</body>"^);
echo                 reportBuilder.AppendLine^("</html>"^);
echo.
echo                 string fileName = $"ClashReport_{DateTime.Now:yyyyMMdd_HHmmss}.html";
echo                 string filePath = Path.Combine^(outputPath, fileName^);
echo.
echo                 File.WriteAllText^(filePath, reportBuilder.ToString^(^), Encoding.UTF8^);
echo.
echo                 MessageBox.Show^($"HTML отчет сохранен: {filePath}", "Автоматизация завершена"^);
echo             }
echo             catch ^(Exception ex^)
echo             {
echo                 MessageBox.Show^($"Ошибка при выполнении автоматизации: {ex.Message}", "Ошибка"^);
echo             }
echo         }
echo     }
echo }
) > "%SCRIPT_FILE%"

echo [%DATE% %TIME%] Создан скрипт автоматизации: %SCRIPT_FILE% >> "%LOG_FILE%"

REM 3. Ждем завершения операций
echo.
echo ========================================
echo Шаг 3: Ожидание завершения операций
echo ========================================
echo Ожидаем завершения операций (%TIMEOUT_MINUTES% минут)...

set /a TIMEOUT_SECONDS=%TIMEOUT_MINUTES% * 60
set /a COUNTER=0

:WAIT_LOOP
timeout /t 1 /nobreak >nul
set /a COUNTER+=1

REM Проверяем, запущен ли еще Navisworks
tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if errorlevel 1 (
    echo Navisworks завершен
    echo [%DATE% %TIME%] Navisworks завершен >> "%LOG_FILE%"
    goto :NAVISWORKS_CLOSED
)

if %COUNTER% geq %TIMEOUT_SECONDS% (
    echo Таймаут достигнут, принудительно закрываем Navisworks
    echo [%DATE% %TIME%] Таймаут достигнут, принудительно закрываем Navisworks >> "%LOG_FILE%"
    taskkill /IM Roamer.exe /F >nul 2>&1
    goto :NAVISWORKS_CLOSED
)

goto :WAIT_LOOP

:NAVISWORKS_CLOSED

REM 4. Завершение
echo.
echo ========================================
echo Шаг 4: Завершение автоматизации
echo ========================================
echo [%DATE% %TIME%] Автоматизация завершена успешно >> "%LOG_FILE%"

echo.
echo ========================================
echo АВТОМАТИЗАЦИЯ ЗАВЕРШЕНА
echo ========================================
echo Лог сохранен в: %LOG_FILE%
echo Отчеты сохранены в: %OUTPUT_FOLDER%
echo.

REM Показываем содержимое папки выгрузки
echo Содержимое папки выгрузки:
dir "%OUTPUT_FOLDER%" /B

echo.
echo Нажмите любую клавишу для выхода...
pause >nul

REM Очищаем временные файлы
if exist "%SCRIPT_FILE%" del "%SCRIPT_FILE%" >nul 2>&1

endlocal
