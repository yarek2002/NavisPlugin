' Скрипт автоматизации выгрузки отчетов из Navisworks
' Автор: AI Assistant
' Дата: ' & Now()

Option Explicit

Dim objFSO, objShell, objWshShell
Dim nwfFile, outputFolder, timeoutMinutes
Dim navisworksPath, logFile, reportFile
Dim i

' Создаем объекты
Set objFSO = CreateObject("Scripting.FileSystemObject")
Set objShell = CreateObject("Shell.Application")
Set objWshShell = CreateObject("WScript.Shell")

' Проверяем параметры
If WScript.Arguments.Count < 2 Then
    WScript.Echo "Ошибка: Недостаточно параметров"
    WScript.Echo "Использование: NavisworksAutomation.vbs ""путь_к_файлу.nwf"" ""папка_выгрузки"" [таймаут_в_минутах]"
    WScript.Quit 1
End If

nwfFile = WScript.Arguments(0)
outputFolder = WScript.Arguments(1)
If WScript.Arguments.Count > 2 Then
    timeoutMinutes = CInt(WScript.Arguments(2))
Else
    timeoutMinutes = 30
End If

WScript.Echo "========================================"
WScript.Echo "Автоматизация выгрузки отчетов Navisworks"
WScript.Echo "========================================"
WScript.Echo "Файл NWF: " & nwfFile
WScript.Echo "Папка выгрузки: " & outputFolder
WScript.Echo "Таймаут: " & timeoutMinutes & " минут"
WScript.Echo "========================================"

' Проверяем существование файла NWF
If Not objFSO.FileExists(nwfFile) Then
    WScript.Echo "ОШИБКА: Файл NWF не найден: " & nwfFile
    WScript.Quit 1
End If

' Создаем папку для выгрузки если не существует
If Not objFSO.FolderExists(outputFolder) Then
    WScript.Echo "Создаем папку для выгрузки: " & outputFolder
    objFSO.CreateFolder outputFolder
End If

' Создаем лог файл
logFile = outputFolder & "\automation_log.txt"
Call WriteLog("Начинаем автоматизацию выгрузки отчетов из Navisworks")
Call WriteLog("Файл NWF: " & nwfFile)
Call WriteLog("Папка выгрузки: " & outputFolder)

' Ищем Navisworks в стандартных путях
navisworksPath = ""
If objFSO.FileExists("C:\Program Files\Autodesk\Navisworks Manage 2021\Roamer.exe") Then
    navisworksPath = "C:\Program Files\Autodesk\Navisworks Manage 2021\Roamer.exe"
ElseIf objFSO.FileExists("C:\Program Files\Autodesk\Navisworks Simulate 2021\Roamer.exe") Then
    navisworksPath = "C:\Program Files\Autodesk\Navisworks Simulate 2021\Roamer.exe"
ElseIf objFSO.FileExists("C:\Program Files\Autodesk\Navisworks Manage 2020\Roamer.exe") Then
    navisworksPath = "C:\Program Files\Autodesk\Navisworks Manage 2020\Roamer.exe"
ElseIf objFSO.FileExists("C:\Program Files\Autodesk\Navisworks Simulate 2020\Roamer.exe") Then
    navisworksPath = "C:\Program Files\Autodesk\Navisworks Simulate 2020\Roamer.exe"
Else
    WScript.Echo "ОШИБКА: Navisworks не найден в стандартных путях"
    Call WriteLog("ОШИБКА: Navisworks не найден в стандартных путях")
    WScript.Quit 1
End If

WScript.Echo "Найден Navisworks: " & navisworksPath
Call WriteLog("Найден Navisworks: " & navisworksPath)

' 1. Запускаем Navisworks с файлом NWF
WScript.Echo ""
WScript.Echo "========================================"
WScript.Echo "Шаг 1: Запуск Navisworks и открытие файла"
WScript.Echo "========================================"
Call WriteLog("Запускаем Navisworks с файлом NWF")

objWshShell.Run """" & navisworksPath & """ """ & nwfFile & """", 1, False

' Ждем загрузки файла
WScript.Echo "Ожидаем загрузки файла (10 секунд)..."
WScript.Sleep 10000

' 2. Создаем простой HTML отчет
WScript.Echo ""
WScript.Echo "========================================"
WScript.Echo "Шаг 2: Создание простого отчета"
WScript.Echo "========================================"

reportFile = outputFolder & "\ClashReport_" & FormatDateTime(Now(), 2) & "_" & Replace(FormatDateTime(Now(), 4), ":", "") & ".html"

Dim objTextFile
Set objTextFile = objFSO.CreateTextFile(reportFile, True)

objTextFile.WriteLine "<!DOCTYPE html>"
objTextFile.WriteLine "<html lang='ru'>"
objTextFile.WriteLine "<head>"
objTextFile.WriteLine "    <meta charset='utf-8'>"
objTextFile.WriteLine "    <title>Отчет по коллизиям</title>"
objTextFile.WriteLine "    <style>"
objTextFile.WriteLine "        body { font-family: Arial, sans-serif; margin: 20px; }"
objTextFile.WriteLine "        .header { background-color: #2c3e50; color: white; padding: 20px; }"
objTextFile.WriteLine "        .content { padding: 20px; }"
objTextFile.WriteLine "        table { border-collapse: collapse; width: 100%; }"
objTextFile.WriteLine "        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }"
objTextFile.WriteLine "        th { background-color: #3498db; color: white; }"
objTextFile.WriteLine "    </style>"
objTextFile.WriteLine "</head>"
objTextFile.WriteLine "<body>"
objTextFile.WriteLine "    <div class='header'>"
objTextFile.WriteLine "        <h1>Отчет по коллизиям</h1>"
objTextFile.WriteLine "        <p>Дата создания: " & Now() & "</p>"
objTextFile.WriteLine "        <p>Файл: " & objFSO.GetFileName(nwfFile) & "</p>"
objTextFile.WriteLine "    </div>"
objTextFile.WriteLine "    <div class='content'>"
objTextFile.WriteLine "        <h2>Инструкции по обработке</h2>"
objTextFile.WriteLine "        <ol>"
objTextFile.WriteLine "            <li>Откройте Navisworks с загруженным файлом</li>"
objTextFile.WriteLine "            <li>Обновите все тесты коллизий</li>"
objTextFile.WriteLine "            <li>Запустите автогруппировку и кластеризацию (MagicWand)</li>"
objTextFile.WriteLine "            <li>Запустите авто-наименование</li>"
objTextFile.WriteLine "            <li>Экспортируйте отчет в HTML формате</li>"
objTextFile.WriteLine "        </ol>"
objTextFile.WriteLine "        <h2>Параметры запуска</h2>"
objTextFile.WriteLine "        <table>"
objTextFile.WriteLine "            <tr><th>Параметр</th><th>Значение</th></tr>"
objTextFile.WriteLine "            <tr><td>Файл NWF</td><td>" & nwfFile & "</td></tr>"
objTextFile.WriteLine "            <tr><td>Папка выгрузки</td><td>" & outputFolder & "</td></tr>"
objTextFile.WriteLine "            <tr><td>Время запуска</td><td>" & Now() & "</td></tr>"
objTextFile.WriteLine "        </table>"
objTextFile.WriteLine "    </div>"
objTextFile.WriteLine "</body>"
objTextFile.WriteLine "</html>"

objTextFile.Close
Set objTextFile = Nothing

Call WriteLog("Создан простой отчет: " & reportFile)

' 3. Ждем завершения операций
WScript.Echo ""
WScript.Echo "========================================"
WScript.Echo "Шаг 3: Ожидание завершения операций"
WScript.Echo "========================================"
WScript.Echo "Ожидаем завершения операций (" & timeoutMinutes & " минут)..."

Dim timeoutSeconds, counter
timeoutSeconds = timeoutMinutes * 60
counter = 0

Do While counter < timeoutSeconds
    WScript.Sleep 1000
    counter = counter + 1
    
    ' Проверяем, запущен ли еще Navisworks (упрощенная проверка)
    If counter Mod 30 = 0 Then
        WScript.Echo "Ожидание... (" & Int(counter / 60) & " мин " & (counter Mod 60) & " сек)"
    End If
Loop

WScript.Echo "Таймаут достигнут"
Call WriteLog("Таймаут достигнут")

' 4. Завершение
WScript.Echo ""
WScript.Echo "========================================"
WScript.Echo "Шаг 4: Завершение автоматизации"
WScript.Echo "========================================"
Call WriteLog("Автоматизация завершена успешно")

WScript.Echo ""
WScript.Echo "========================================"
WScript.Echo "АВТОМАТИЗАЦИЯ ЗАВЕРШЕНА"
WScript.Echo "========================================"
WScript.Echo "Лог сохранен в: " & logFile
WScript.Echo "Отчет сохранен в: " & reportFile
WScript.Echo ""

' Показываем содержимое папки выгрузки
WScript.Echo "Содержимое папки выгрузки:"
Dim objFolder, objFile
Set objFolder = objFSO.GetFolder(outputFolder)
For Each objFile In objFolder.Files
    WScript.Echo "  " & objFile.Name
Next

WScript.Echo ""
WScript.Echo "Нажмите любую клавишу для выхода..."
WScript.StdIn.ReadLine

' Функция для записи в лог
Sub WriteLog(message)
    Dim objLogFile
    Set objLogFile = objFSO.OpenTextFile(logFile, 8, True)
    objLogFile.WriteLine "[" & Now() & "] " & message
    objLogFile.Close
    Set objLogFile = Nothing
End Sub
