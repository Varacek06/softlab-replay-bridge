@echo off
:: --- KONTROLA A VYNUCENI PRAV SPRAVCE ---
net session >nul 2>&1
if %errorLevel% == 0 (
    goto :mam_prava
) else (
    echo Zadam o prava spravce...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)
:mam_prava
:: --- KONEC KONTROLY ---
echo ========================================================
echo   Blackmagic Replay Editor Bridge - Instalace
echo ========================================================
echo.

set "INSTALL_DIR=%ProgramFiles%\SoftLab_ReplayBridge"

echo [1/5] Ukoncuji bezici procesy...
taskkill /F /IM ReplayBridge.exe >nul 2>&1
taskkill /F /IM node.exe >nul 2>&1

echo [2/5] Kopiruju soubory do %INSTALL_DIR%...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
xcopy /E /I /Y "%~dp0bin\*" "%INSTALL_DIR%\"

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
    "%CSC%" /nologo /platform:x86 /target:winexe /win32icon:"%INSTALL_DIR%\icon.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%INSTALL_DIR%\ReplayBridge.exe" "%INSTALL_DIR%\ReplayBridge.cs"
    if errorlevel 1 (
        echo   !!! CHYBA: Kompilace selhala!
        pause
        exit /b 1
    )
    echo   - Kompilace OK
)

echo [4/5] Konfigurace systemu...
reg import "%INSTALL_DIR%\SoftLab_ReplayEditor.reg"
taskkill /F /IM slgpiservers.exe >nul 2>&1
cscript //nologo "%INSTALL_DIR%\make_shortcut.vbs" "%INSTALL_DIR%"

echo [5/5] Spoustim ReplayBridge...
start "" "%INSTALL_DIR%\ReplayBridge.exe"

echo.
echo ========================================================
echo   HOTOVO! ReplayBridge bezi (ikona u hodin).
echo   Pokud sviti cervena, podivej se do slozky:
echo   %AppData%\SoftLabReplayBridge\midi_debug.txt
echo ========================================================
pause