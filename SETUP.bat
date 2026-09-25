@echo off
setlocal
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
    echo   - loopMIDI is not installed. Downloading and installing silently...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri 'https://www.tobias-erichsen.de/wp-content/uploads/2020/01/loopMIDISetup_1_0_16_27.zip' -OutFile 'C:\SoftLab_ReplayBridge\loopMIDI.zip'; Expand-Archive -Path 'C:\SoftLab_ReplayBridge\loopMIDI.zip' -DestinationPath 'C:\SoftLab_ReplayBridge\loopMIDI' -Force; Start-Process -FilePath 'C:\SoftLab_ReplayBridge\loopMIDI\loopMIDISetup_1_0_16_27.exe' -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -Wait; Remove-Item 'C:\SoftLab_ReplayBridge\loopMIDI.zip' -Force; Remove-Item -Recurse -Force 'C:\SoftLab_ReplayBridge\loopMIDI'"
    set NEED_LOOPMIDI_PORT=1
) else (
    echo   - loopMIDI is already installed.
)

echo [4/6] Checking node.exe...
if not exist "C:\SoftLab_ReplayBridge\node.exe" (
    echo   - Downloading portable node.exe from nodejs.org...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri 'https://nodejs.org/dist/v22.14.0/win-x64/node.exe' -OutFile 'C:\SoftLab_ReplayBridge\node.exe'"
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