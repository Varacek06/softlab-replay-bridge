@echo off
setlocal

:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting Administrator privileges...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo ========================================================
echo   Blackmagic Replay Editor Bridge Setup for SoftLab
echo ========================================================
echo.

echo [1/6] Terminating running processes...
taskkill /F /IM ReplayBridge.exe >nul 2>&1
taskkill /F /IM loopMIDI.exe >nul 2>&1

echo [2/6] Copying files to C:\SoftLab_ReplayBridge...
if not exist "C:\SoftLab_ReplayBridge" mkdir "C:\SoftLab_ReplayBridge"
xcopy /E /I /Y "%~dp0bin\*" "C:\SoftLab_ReplayBridge\"

echo [3/6] Checking loopMIDI...
if not exist "C:\Program Files (x86)\Tobias Erichsen\loopMIDI\loopMIDI.exe" (
    echo   - loopMIDI is not installed!
    echo   - Opening download page in your web browser...
    start https://www.tobias-erichsen.de/software/loopmidi.html
    echo.
    echo   ********************************************************
    echo   * PLEASE INSTALL LOOPMIDI NOW                          *
    echo   * 1. Download loopMIDI from the webpage that opened.   *
    echo   * 2. Run the installer and finish the installation.    *
    echo   * 3. Come back here and press any key to continue.     *
    echo   ********************************************************
    pause
    set NEED_LOOPMIDI_PORT=1
) else (
    echo   - loopMIDI is already installed.
)

echo [4/6] Checking node.exe...
if not exist "C:\SoftLab_ReplayBridge\node.exe" (
    echo   - Downloading portable node.exe from nodejs.org...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference = 'SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing -Uri 'https://nodejs.org/dist/v22.14.0/win-x64/node.exe' -OutFile 'C:\SoftLab_ReplayBridge\node.exe'"
)

echo [5/6] Compiling ReplayBridge.exe using csc.exe...
set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
%CSC% /nologo /target:winexe /win32icon:"C:\SoftLab_ReplayBridge\icon.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"C:\SoftLab_ReplayBridge\ReplayBridge.exe" "C:\SoftLab_ReplayBridge\ReplayBridge.cs"

echo [6/6] Configuring SoftLab and starting services...
reg import "C:\SoftLab_ReplayBridge\SoftLab_ReplayEditor.reg"
taskkill /F /IM slgpiservers.exe >nul 2>&1
cscript //nologo "C:\SoftLab_ReplayBridge\make_shortcut.vbs"

start "" "C:\SoftLab_ReplayBridge\ReplayBridge.exe"
if exist "C:\Program Files (x86)\Tobias Erichsen\loopMIDI\loopMIDI.exe" (
    start "" "C:\Program Files (x86)\Tobias Erichsen\loopMIDI\loopMIDI.exe"
)

echo.
echo ========================================================
echo   DONE! ReplayBridge is running (check the system tray).
if "%NEED_LOOPMIDI_PORT%"=="1" (
    echo.
    echo   !!! IMPORTANT NOTE !!!
    echo   The loopMIDI window has just opened.
    echo   Please type "loopMIDI Port" (without quotes) into the bottom field and click "+".
    echo   Then you can close loopMIDI (it will run in the background).
)
echo ========================================================
pause