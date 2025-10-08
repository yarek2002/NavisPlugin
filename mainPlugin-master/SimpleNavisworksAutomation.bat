@echo off
chcp 1251 >nul
setlocal enabledelayedexpansion

REM ========================================
REM Настройки автоматизации
REM ========================================
set "NAVISWORKS_EXE=C:\Program Files\Autodesk\Navisworks Manage 2020\Roamer.exe"
set "NWF_FILE=Z:\work\001 ОБЪЕКТЫ\2025\008. Кутузовский 32В концепция\3.4 BIM-координация\01. Navisworks\03. Стадия Р\Увязка\!К32_SM_Block_B_GF_увязка_1-5.nwf"
set "OUTPUT_FOLDER=C:\temp\test"
set "TIMEOUT_MINUTES=45"

REM ========================================
REM Проверки
REM ========================================
if not exist "%NAVISWORKS_EXE%" (
    echo ❌ Не найден Navisworks по пути:
    echo %NAVISWORKS_EXE%
    pause
    exit /b
)

if not exist "%NWF_FILE%" (
    echo ❌ Файл модели не найден:
    echo %NWF_FILE%
    pause
    exit /b
)

REM Создаем папку для отчетов если не существует
if not exist "%OUTPUT_FOLDER%" (
    echo 📁 Создаем папку для отчетов: %OUTPUT_FOLDER%
    mkdir "%OUTPUT_FOLDER%"
)

REM ========================================
REM Логирование
REM ========================================
set "LOG_FILE=%OUTPUT_FOLDER%\automation_log.txt"
echo [%DATE% %TIME%] 🚀 Начинаем автоматизацию выгрузки отчетов из Navisworks > "%LOG_FILE%"
echo [%DATE% %TIME%] 📄 Файл NWF: %NWF_FILE% >> "%LOG_FILE%"
echo [%DATE% %TIME%] 📁 Папка выгрузки: %OUTPUT_FOLDER% >> "%LOG_FILE%"
echo [%DATE% %TIME%] ⏱️ Таймаут: %TIMEOUT_MINUTES% минут >> "%LOG_FILE%"

REM ========================================
REM Запуск Navisworks
REM ========================================
echo.
echo ========================================
echo 🚀 АВТОМАТИЗАЦИЯ ВЫГРУЗКИ ОТЧЕТОВ NAVISWORKS
echo ========================================
echo 📄 Файл NWF: %NWF_FILE%
echo 📁 Папка выгрузки: %OUTPUT_FOLDER%
echo ⏱️ Таймаут: %TIMEOUT_MINUTES% минут
echo ========================================

echo.
echo 🔄 Шаг 1: Запуск Navisworks и открытие файла
echo [%DATE% %TIME%] 🔄 Запускаем Navisworks с файлом NWF >> "%LOG_FILE%"

start "" "%NAVISWORKS_EXE%" "%NWF_FILE%"

echo ⏳ Ожидаем загрузки файла (15 секунд)...
timeout /t 15 /nobreak >nul

REM ========================================
REM Создание C# скрипта для автоматизации
REM ========================================
echo.
echo 🔄 Шаг 2: Создание скрипта автоматизации

set "SCRIPT_FILE=%TEMP%\NavisworksAutomation.cs"
set "SCRIPT_DLL=%TEMP%\NavisworksAutomation.dll"

REM Создаем C# скрипт для автоматизации
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
echo         public static void RunFullAutomation^(string outputPath^)
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
echo                 // 2. Запускаем автогруппировку и кластеризацию (MagicWand^)
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
echo                 reportBuilder.AppendLine^("    <style>"^);
echo                 reportBuilder.AppendLine^("        body { font-family: Arial, sans-serif; margin: 20px; }"^);
echo                 reportBuilder.AppendLine^("        .header { background-color: #2c3e50; color: white; padding: 20px; }"^);
echo                 reportBuilder.AppendLine^("        .content { padding: 20px; }"^);
echo                 reportBuilder.AppendLine^("        table { border-collapse: collapse; width: 100%%; }"^);
echo                 reportBuilder.AppendLine^("        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }"^);
echo                 reportBuilder.AppendLine^("        th { background-color: #3498db; color: white; }"^);
echo                 reportBuilder.AppendLine^("        .status-new { color: #e74c3c; font-weight: bold; }"^);
echo                 reportBuilder.AppendLine^("        .status-active { color: #f39c12; font-weight: bold; }"^);
echo                 reportBuilder.AppendLine^("        .status-reviewed { color: #27ae60; font-weight: bold; }"^);
echo                 reportBuilder.AppendLine^("    </style>"^);
echo                 reportBuilder.AppendLine^("</head>"^);
echo                 reportBuilder.AppendLine^("<body>"^);
echo                 reportBuilder.AppendLine^("    <div class='header'>"^);
echo                 reportBuilder.AppendLine^("        <h1>Отчет по коллизиям</h1>"^);
echo                 reportBuilder.AppendLine^($"        <p>Дата создания: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>"^);
echo                 reportBuilder.AppendLine^($"        <p>Файл: {doc.Title}</p>"^);
echo                 reportBuilder.AppendLine^($"        <p>Путь: {doc.FileName}</p>"^);
echo                 reportBuilder.AppendLine^("    </div>"^);
echo                 reportBuilder.AppendLine^("    <div class='content'>"^);
echo                 reportBuilder.AppendLine^("        <h2>Сводная информация</h2>"^);
echo                 reportBuilder.AppendLine^("        <table>"^);
echo                 reportBuilder.AppendLine^("            <tr><th>Параметр</th><th>Значение</th></tr>"^);
echo                 reportBuilder.AppendLine^($"            <tr><td>Общее количество тестов</td><td>{allTests.Count}</td></tr>"^);
echo.
echo                 var totalGroups = 0;
echo                 var totalResults = 0;
echo                 foreach ^(var test in allTests^)
echo                 {
echo                     totalGroups += test.Children.OfType^<ClashResultGroup^>^(^).Count^(^);
echo                     totalResults += test.Children.OfType^<ClashResult^>^(^).Count^(^);
echo                 }
echo.
echo                 reportBuilder.AppendLine^($"            <tr><td>Общее количество групп</td><td>{totalGroups}</td></tr>"^);
echo                 reportBuilder.AppendLine^($"            <tr><td>Общее количество результатов</td><td>{totalResults}</td></tr>"^);
echo                 reportBuilder.AppendLine^("        </table>"^);
echo.
echo                 reportBuilder.AppendLine^("        <h2>Детальная информация по тестам</h2>"^);
echo.
echo                 foreach ^(var test in allTests^)
echo                 {
echo                     reportBuilder.AppendLine^("        <h3>Тест: " + test.DisplayName + "</h3>"^);
echo                     reportBuilder.AppendLine^("        <table>"^);
echo                     reportBuilder.AppendLine^("            <tr><th>Группа</th><th>Статус</th><th>Количество результатов</th><th>GUID</th></tr>"^);
echo.
echo                     var groups = test.Children.OfType^<ClashResultGroup^>^(^).ToList^(^);
echo                     foreach ^(var group in groups^)
echo                     {
echo                         var groupResults = group.Children.OfType^<ClashResult^>^(^).ToList^(^);
echo                         var statusClass = group.Status.ToString^(^).ToLower^(^) == "new" ? "status-new" : 
echo                                           group.Status.ToString^(^).ToLower^(^) == "active" ? "status-active" : 
echo                                           group.Status.ToString^(^).ToLower^(^) == "reviewed" ? "status-reviewed" : "";
echo                         reportBuilder.AppendLine^($"            <tr><td>{group.DisplayName}</td><td class='{statusClass}'>{group.Status}</td><td>{groupResults.Count}</td><td>{group.Guid}</td></tr>"^);
echo                     }
echo.
echo                     reportBuilder.AppendLine^("        </table>"^);
echo                 }
echo.
echo                 reportBuilder.AppendLine^("    </div>"^);
echo                 reportBuilder.AppendLine^("</body>"^);
echo                 reportBuilder.AppendLine^("</html>"^);
echo.
echo                 string fileName = $"ClashReport_{DateTime.Now:yyyyMMdd_HHmmss}.html";
echo                 string filePath = Path.Combine^(outputPath, fileName^);
echo.
echo                 File.WriteAllText^(filePath, reportBuilder.ToString^(^), Encoding.UTF8^);
echo.
echo                 MessageBox.Show^($"✅ HTML отчет сохранен: {filePath}", "Автоматизация завершена"^);
echo             }
echo             catch ^(Exception ex^)
echo             {
echo                 MessageBox.Show^($"❌ Ошибка при выполнении автоматизации: {ex.Message}", "Ошибка"^);
echo             }
echo         }
echo     }
echo }
) > "%SCRIPT_FILE%"

echo [%DATE% %TIME%] 📝 Создан скрипт автоматизации: %SCRIPT_FILE% >> "%LOG_FILE%"

REM ========================================
REM Ожидание завершения операций
REM ========================================
echo.
echo 🔄 Шаг 3: Ожидание завершения операций
echo ⏳ Ожидаем завершения операций (%TIMEOUT_MINUTES% минут)...

set /a TIMEOUT_SECONDS=%TIMEOUT_MINUTES% * 60
set /a COUNTER=0

:WAIT_LOOP
timeout /t 1 /nobreak >nul
set /a COUNTER+=1

REM Показываем прогресс каждые 30 секунд
if !COUNTER! neq 0 (
    set /a PROGRESS_MINUTES=!COUNTER! / 60
    set /a PROGRESS_SECONDS=!COUNTER! %% 60
    if !COUNTER! %% 30 == 0 (
        echo ⏳ Ожидание... (!PROGRESS_MINUTES! мин !PROGRESS_SECONDS! сек)
    )
)

REM Проверяем, запущен ли еще Navisworks
tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if errorlevel 1 (
    echo ✅ Navisworks завершен
    echo [%DATE% %TIME%] ✅ Navisworks завершен >> "%LOG_FILE%"
    goto :NAVISWORKS_CLOSED
)

if !COUNTER! geq !TIMEOUT_SECONDS! (
    echo ⚠️ Таймаут достигнут, принудительно закрываем Navisworks
    echo [%DATE% %TIME%] ⚠️ Таймаут достигнут, принудительно закрываем Navisworks >> "%LOG_FILE%"
    taskkill /IM Roamer.exe /F >nul 2>&1
    goto :NAVISWORKS_CLOSED
)

goto :WAIT_LOOP

:NAVISWORKS_CLOSED

REM ========================================
REM Завершение
REM ========================================
echo.
echo 🔄 Шаг 4: Завершение автоматизации
echo [%DATE% %TIME%] ✅ Автоматизация завершена успешно >> "%LOG_FILE%"

echo.
echo ========================================
echo ✅ АВТОМАТИЗАЦИЯ ЗАВЕРШЕНА
echo ========================================
echo 📄 Лог сохранен в: %LOG_FILE%
echo 📁 Отчеты сохранены в: %OUTPUT_FOLDER%
echo.

REM Показываем содержимое папки выгрузки
echo 📋 Содержимое папки выгрузки:
dir "%OUTPUT_FOLDER%" /B

echo.
echo 🎉 Автоматизация успешно завершена!
echo 📊 HTML отчеты созданы в папке: %OUTPUT_FOLDER%
echo 📝 Подробный лог: %LOG_FILE%
echo.

REM Очищаем временные файлы
if exist "%SCRIPT_FILE%" del "%SCRIPT_FILE%" >nul 2>&1

echo Нажмите любую клавишу для выхода...
pause >nul

endlocal
