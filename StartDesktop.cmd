@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT=%ROOT%WarehouseAutomatisaion.Desktop.Wpf\WarehouseAutomatisaion.Desktop.Wpf.csproj"
set "EXE=%ROOT%WarehouseAutomatisaion.Desktop.Wpf\bin\Debug\net8.0-windows\MajorWarehause.exe"

if not exist "%EXE%" (
    echo WPF executable not found. Building desktop shell...
    dotnet build "%PROJECT%"
    if errorlevel 1 (
        echo Desktop build failed.
        pause
        exit /b 1
    )
)

start "" "%EXE%"
exit /b 0
