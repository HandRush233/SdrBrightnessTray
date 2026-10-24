@echo off
setlocal
set "DEST=%LOCALAPPDATA%\Programs\SdrBrightnessTray"

taskkill /IM SdrBrightnessTray.exe /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SdrBrightnessTray /f >nul 2>&1
reg delete "HKCU\Software\SdrBrightnessTray" /f >nul 2>&1

if exist "%DEST%" rmdir /S /Q "%DEST%"
echo.
echo SdrBrightnessTray has been removed.
echo.
pause
