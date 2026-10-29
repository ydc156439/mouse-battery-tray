@echo off
setlocal
cd /d "%~dp0"
set "MOUSEBAT_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%MOUSEBAT_CSC%" set "MOUSEBAT_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%MOUSEBAT_CSC%" (
    echo Windows .NET Framework compiler is unavailable on this computer.
    pause
    exit /b 1
)
if not exist "..\release" mkdir "..\release"
"%MOUSEBAT_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"..\release\MouseBatteryTray.exe" /win32manifest:app.manifest /win32icon:app.ico /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll AssemblyInfo.cs Protocol.cs Hid.cs Dashboard.cs Program.cs
if errorlevel 1 (
    echo Build failed. See compiler output above.
    pause
    exit /b 1
)
echo EXE ready: ..\release\MouseBatteryTray.exe
pause
