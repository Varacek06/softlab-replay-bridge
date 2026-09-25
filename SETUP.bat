@echo off
echo Instaluji Blackmagic Replay Editor Bridge pro SoftLab...
taskkill /F /IM ReplayBridge.exe >nul 2>&1
xcopy /E /I /Y "%~dp0bin\*" "C:\SoftLab_ReplayBridge\"
reg import "C:\SoftLab_ReplayBridge\SoftLab_ReplayEditor.reg"
taskkill /F /IM slgpiservers.exe >nul 2>&1
cscript //nologo "C:\SoftLab_ReplayBridge\make_shortcut.vbs"
start "" "C:\SoftLab_ReplayBridge\ReplayBridge.exe"
echo.
echo HOTOVO! ReplayBridge bezi potichu na pozadi (zelena ikonka u hodin).
pause