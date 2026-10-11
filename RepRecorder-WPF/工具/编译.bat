@echo off
chcp 65001 >nul
rem ============================================================
rem  RepRecorder (WPF) build script.
rem  Still uses the csc.exe that ships with Windows -- nothing to install.
rem  NOTE: the DPI manifest is NOT applied yet. Core.cs still carries the old
rem  WinForms UI (phase 0.1/0.2) which is laid out for a DPI-unaware process;
rem  making it DPI aware now would shrink the whole window to 1/3 on the 300%
rem  display. The manifest goes in at phase 0.3 together with the WPF window.
rem ============================================================
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo cannot find csc.exe
  if not "%1"=="/nopause" pause
  exit /b 1
)
set REF=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2
if not exist "%REF%\PresentationFramework.dll" set REF=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1
if not exist "%REF%\PresentationFramework.dll" (
  echo cannot find WPF reference assemblies under Reference Assemblies\.NETFramework
  if not "%1"=="/nopause" pause
  exit /b 1
)
tasklist /fi "imagename eq RepRecorderWPF.exe" | find /i "RepRecorderWPF.exe" >nul && (
  echo RepRecorderWPF.exe is running -- close it first.
  if not "%1"=="/nopause" pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /codepage:65001 /out:..\RepRecorderWPF.exe ^
  /r:"%REF%\PresentationFramework.dll" /r:"%REF%\PresentationCore.dll" ^
  /r:"%REF%\WindowsBase.dll" /r:"%REF%\System.Xaml.dll" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:"%REF%\System.IO.Compression.FileSystem.dll" ^
  ..\src\Ui.cs ..\src\UiShell.cs ..\src\UiWindow.cs ..\src\UiDialogs.cs ..\src\Core.cs ..\src\Recorder.cs ..\src\Update.cs ..\src\AssemblyInfo.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED -- see the errors above.
) else (
  echo.
  echo BUILD OK: RepRecorderWPF.exe
)
if not "%1"=="/nopause" pause
