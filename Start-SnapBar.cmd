@echo off
where.exe pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh.exe^) was not found on PATH.
    pause
    exit /b 1
)

pwsh.exe -NoLogo -NoProfile -File "%~dp0scripts\run-dev.ps1"
set "launchExit=%errorlevel%"
if not "%launchExit%"=="0" pause
exit /b %launchExit%
