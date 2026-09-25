@echo off
echo ========================================================
echo   Blackmagic Replay Editor Bridge - Instalace
echo ========================================================
echo.

echo [1/5] Ukoncuji bezici procesy...
taskkill /F /IM ReplayBridge.exe >nul 2>&1
taskkill /F /IM node.exe >nul 2>&1

echo [2/5] Kopiruju soubory do C:\SoftLab_ReplayBridge...
if not exist "C:\SoftLab_ReplayBridge" mkdir "C:\SoftLab_ReplayBridge"
xcopy /E /I /Y "%~dp0bin\*" "C:\SoftLab_ReplayBridge\"

echo [3/5] Kompiluju ReplayBridge.exe...
set CSC=
if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
) else if exist "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
    set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
)
if "%CSC%"=="" (
    echo   !!! CHYBA: csc.exe nenalezen, pouzivam predkompilovany exe
) else (
    "%CSC%" /nologo /platform:x86 /target:winexe /win32icon:"C:\SoftLab_ReplayBridge\icon.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"C:\SoftLab_ReplayBridge\ReplayBridge.exe" "C:\SoftLab_ReplayBridge\ReplayBridge.cs"
    if errorlevel 1 (
        echo   !!! CHYBA: Kompilace selhala!
        pause
        exit /b 1
    )
    echo   - Kompilace OK
)

echo [4/5] Konfigurace systemu...
reg import "C:\SoftLab_ReplayBridge\SoftLab_ReplayEditor.reg"
taskkill /F /IM slgpiservers.exe >nul 2>&1
cscript //nologo "C:\SoftLab_ReplayBridge\make_shortcut.vbs"

echo [5/5] Spoustim ReplayBridge...
start "" "C:\SoftLab_ReplayBridge\ReplayBridge.exe"

echo.
echo ========================================================
echo   HOTOVO! ReplayBridge bezi (ikona u hodin).
echo   Pokud sviti cervena, podivej se na soubor:
echo   C:\SoftLab_ReplayBridge\midi_debug.txt
echo ========================================================
pause