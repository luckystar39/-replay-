@echo off
chcp 65001 >nul
rem ============================================================
rem  RepRecorder 编译脚本（把 RepRecorder.cs 编译成 RepRecorder.exe）
rem  依赖：Windows 自带的 .NET Framework 4.x 编译器（csc.exe），不用装任何东西
rem  用法：把本文件和 RepRecorder.cs 放在同一个文件夹里，双击本文件
rem ============================================================
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo 找不到 csc.exe —— 这台机器可能没有 .NET Framework 4.x
  pause
  exit /b 1
)
rem 编之前先确认程序没在跑（否则 exe 被占用会报错）
tasklist /fi "imagename eq RepRecorder.exe" | find /i "RepRecorder.exe" >nul && (
  echo 检测到 RepRecorder.exe 正在运行，请先关掉它再编译。
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /codepage:65001 /out:RepRecorder.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  RepRecorder.cs
if errorlevel 1 (
  echo.
  echo 编译失败，看上面的错误信息。
) else (
  echo.
  echo 编译成功：RepRecorder.exe
)
pause
