@echo off
title Budgeter - Update and build
rem Gets the newest code from GitHub and rebuilds publish\BudgetApp.exe in place,
rem so a shortcut pointing to publish\BudgetApp.exe keeps working.
cd /d "%~dp0"

git --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] Git not found. Install Git for Windows, or use the in-app updater instead.
    pause
    exit /b 1
)

echo.
echo  Getting the newest version from GitHub ...
git pull
if errorlevel 1 (
    echo  [ERROR] git pull failed. See above.
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq BudgetApp.exe" | find /I "BudgetApp.exe" >nul
if not errorlevel 1 (
    echo.
    echo  Budgeter is still running. Close it, then press a key to continue.
    pause >nul
)

call build_exe.bat
