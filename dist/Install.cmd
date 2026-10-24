@echo off
setlocal
set "SRC=%~dp0SdrBrightnessTray.exe"
set "DEST=%LOCALAPPDATA%\Programs\SdrBrightnessTray"

if not exist "%SRC%" (
    echo SdrBrightnessTray.exe was not found next to this script.
    pause
    exit /b 1
)

taskkill /IM SdrBrightnessTray.exe /F >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%" >nul 2>&1
copy /Y "%SRC%" "%DEST%\SdrBrightnessTray.exe" >nul
if errorlevel 1 (
    echo Failed to copy the application files.
    pause
    exit /b 1
)

start "" "%DEST%\SdrBrightnessTray.exe" --install
echo.
echo Installed to: %DEST%
echo The app is now running in the notification area.
echo Left-click the tray icon to adjust SDR content brightness.
echo.
timeout /t 3 >nul
