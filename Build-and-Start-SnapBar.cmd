@echo off
setlocal
where.exe pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh.exe^) was not found on PATH.
    pause
    exit /b 1
)

echo Building SnapBar before launch...
pwsh.exe -NoLogo -NoProfile -File "%~dp0scripts\build.ps1"
set "buildExit=%errorlevel%"
if not "%buildExit%"=="0" goto build_failed

call "%~dp0Start-SnapBar.cmd"
exit /b %errorlevel%

:build_failed
echo.
echo Build failed. SnapBar was not launched.
echo If SnapBar is running, exit it from the system tray or settings and retry.
pause
exit /b %buildExit%
