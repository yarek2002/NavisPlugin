@echo off
chcp 1251 >nul
setlocal

set "NAVISWORKS_EXE=C:\Program Files\Autodesk\Navisworks Manage 2020\Roamer.exe"
set "NWF_FILE=Z:\work\001 ОБЪЕКТЫ\2025\008. Кутузовский 32В концепция\3.4 BIM-координация\01. Navisworks\03. Стадия Р\Увязка\!К32_SM_Block_B_GF_увязка_1-5.nwf"

if not exist "%NAVISWORKS_EXE%" (
    echo ? Не найден Navisworks по пути:
    echo %NAVISWORKS_EXE%
    pause
    exit /b
)

if not exist "%NWF_FILE%" (
    echo ? Файл модели не найден:
    echo %NWF_FILE%
    pause
    exit /b
)

echo ? Открываю Navisworks с файлом:
echo %NWF_FILE%
echo.

start "" "%NAVISWORKS_EXE%" "%NWF_FILE%"
pause
