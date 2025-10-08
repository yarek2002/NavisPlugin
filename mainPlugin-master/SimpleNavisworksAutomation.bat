@echo off
REM Простая автоматизация Navisworks через CMD
REM Автор: AI Assistant

setlocal enabledelayedexpansion

REM Проверяем параметры
if "%~1"=="" (
    echo Ошибка: Не указан путь к файлу NWF
    echo Использование: SimpleNavisworksAutomation.bat "путь_к_файлу.nwf" "папка_выгрузки"
    pause
    exit /b 1
)

if "%~2"=="" (
    echo Ошибка: Не указана папка для выгрузки
    echo Использование: SimpleNavisworksAutomation.bat "путь_к_файлу.nwf" "папка_выгрузки"
    pause
    exit /b 1
)

set "NWF_FILE=%~1"
set "OUTPUT_FOLDER=%~2"

echo ========================================
echo Простая автоматизация Navisworks
echo ========================================
echo Файл NWF: %NWF_FILE%
echo Папка выгрузки: %OUTPUT_FOLDER%
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
set "LOG_FILE=%OUTPUT_FOLDER%\simple_automation_log.txt"
echo [%DATE% %TIME%] Начинаем простую автоматизацию Navisworks > "%LOG_FILE%"
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

REM Запускаем Navisworks с файлом NWF
echo.
echo ========================================
echo Запуск Navisworks с файлом NWF
echo ========================================
echo [%DATE% %TIME%] Запускаем Navisworks с файлом NWF >> "%LOG_FILE%"

start "" "%NAVISWORKS_PATH%" "%NWF_FILE%"

REM Ждем загрузки файла
echo Ожидаем загрузки файла (15 секунд)...
timeout /t 15 /nobreak >nul

REM Создаем простой HTML отчет
echo.
echo ========================================
echo Создание простого отчета
echo ========================================

set "REPORT_FILE=%OUTPUT_FOLDER%\SimpleReport_%DATE:~-4,4%%DATE:~-10,2%%DATE:~-7,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%.html"

(
echo ^<!DOCTYPE html^>
echo ^<html lang='ru'^>
echo ^<head^>
echo     ^<meta charset='utf-8'^>
echo     ^<title^>Простой отчет по коллизиям^</title^>
echo     ^<style^>
echo         body { font-family: Arial, sans-serif; margin: 20px; }
echo         .header { background-color: #2c3e50; color: white; padding: 20px; }
echo         .content { padding: 20px; }
echo         table { border-collapse: collapse; width: 100%%; }
echo         th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
echo         th { background-color: #3498db; color: white; }
echo     ^</style^>
echo ^</head^>
echo ^<body^>
echo     ^<div class='header'^>
echo         ^<h1^>Простой отчет по коллизиям^</h1^>
echo         ^<p^>Дата создания: %DATE% %TIME%^</p^>
echo         ^<p^>Файл: %~nx1^</p^>
echo     ^</div^>
echo     ^<div class='content'^>
echo         ^<h2^>Инструкции по ручной обработке^</h2^>
echo         ^<ol^>
echo             ^<li^>Откройте Navisworks с загруженным файлом^</li^>
echo             ^<li^>Обновите все тесты коллизий^</li^>
echo             ^<li^>Запустите автогруппировку и кластеризацию (MagicWand^)</li^>
echo             ^<li^>Запустите авто-наименование^</li^>
echo             ^<li^>Экспортируйте отчет в HTML формате^</li^>
echo         ^</ol^>
echo         ^<h2^>Параметры запуска^</h2^>
echo         ^<table^>
echo             ^<tr^>^<th^>Параметр^</th^>^<th^>Значение^</th^>^</tr^>
echo             ^<tr^>^<td^>Файл NWF^</td^>^<td^>%NWF_FILE%^</td^>^</tr^>
echo             ^<tr^>^<td^>Папка выгрузки^</td^>^<td^>%OUTPUT_FOLDER%^</td^>^</tr^>
echo             ^<tr^>^<td^>Время запуска^</td^>^<td^>%DATE% %TIME%^</td^>^</tr^>
echo         ^</table^>
echo     ^</div^>
echo ^</body^>
echo ^</html^>
) > "%REPORT_FILE%"

echo [%DATE% %TIME%] Создан простой отчет: %REPORT_FILE% >> "%LOG_FILE%"

REM Завершение
echo.
echo ========================================
echo АВТОМАТИЗАЦИЯ ЗАВЕРШЕНА
echo ========================================
echo Navisworks запущен с файлом: %NWF_FILE%
echo Простой отчет создан: %REPORT_FILE%
echo Лог сохранен в: %LOG_FILE%
echo.
echo ИНСТРУКЦИИ:
echo 1. В открывшемся Navisworks выполните необходимые операции
echo 2. Используйте плагин ClashManager для автоматизации
echo 3. Экспортируйте отчет в HTML формате
echo.

echo Нажмите любую клавишу для выхода...
pause >nul

endlocal
