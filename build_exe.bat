@echo off
title Budgeter - Build .exe
echo.
echo  Building standalone BudgetApp.exe ...
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] .NET SDK not found. Install the .NET 8 SDK from https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

dotnet publish BudgetApp\BudgetApp.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

if errorlevel 1 (
    echo.
    echo  [ERROR] Build failed. See output above.
    pause
    exit /b 1
)

echo.
echo  SUCCESS! Your .exe is at:  publish\BudgetApp.exe
echo.
pause
