@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# compiler not found.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /out:"%~dp0OpenFront.exe" /platform:x64 ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  /reference:"%~dp0Microsoft.Web.WebView2.Core.dll" ^
  /reference:"%~dp0Microsoft.Web.WebView2.WinForms.dll" ^
  "%~dp0OpenFrontApp.cs"
if errorlevel 1 exit /b 1
echo Built %~dp0OpenFront.exe
