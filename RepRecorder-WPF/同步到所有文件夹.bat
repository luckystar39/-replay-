@echo off
rem ============================================================
rem  同步脚本（新版 / WPF）—— 2026-10-03 阶段 7 切换之后，主目录换成了这里。
rem
rem  用法：改完代码 -> 双击 编译.bat 编出 RepRecorderWPF.exe -> 再双击本文件。
rem
rem  做的事：
rem    1. 把 RepRecorderWPF.exe 复制成 RepRecorder.exe（名字不变，用户双击的还是它），
rem       到 RepRecorder\ 和 RepRecorder-给别人\；
rem    2. 覆盖之前先把目标位置的老 exe 备份到 <目标>\备份\RepRecorder-时间戳.exe，
rem       并且立刻验证备份真的存在（交接文档第 1 节的铁律 2）；
rem    3. 重打 RepRecorder-发给别人.zip；
rem    4. 【绝对不动】RepRecorder-源码\ —— 那个目录是只读的，见交接文档铁律 1。
rem
rem  警告：别再双击 RepRecorder-源码\同步到所有文件夹.bat 了！
rem        那个旧脚本会把【旧 WinForms 版】的 exe 覆盖回来，把这次切换冲掉。
rem ============================================================
setlocal
set SRC=%~dp0
set ROOT=%SRC%..
set T1=%ROOT%\RepRecorder
set T2=%ROOT%\RepRecorder-给别人
set EXE=%SRC%RepRecorderWPF.exe

if not exist "%EXE%" (
  echo.
  echo 本文件夹里没有 RepRecorderWPF.exe，先双击 编译.bat 编译一次。
  pause
  exit /b 1
)

tasklist /fi "imagename eq RepRecorder.exe" | findstr /i "RepRecorder.exe" >nul
if not errorlevel 1 (
  echo.
  echo 检测到 RepRecorder.exe 正在运行，请先关掉它（否则 exe 被占用，复制会失败）。
  pause
  exit /b 1
)

rem 时间戳交给 PowerShell 生成 —— 不用 %date%，中文系统的 %date% 带"周六"会把解析带崩
set STAMP=unknown
for /f "delims=" %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set STAMP=%%i

call :backup "%T1%"
if errorlevel 9 goto abort
call :backup "%T2%"
if errorlevel 9 goto abort

echo.
echo 从 RepRecorder-WPF 同步到：
if exist "%T1%" (
  copy /Y "%EXE%" "%T1%\RepRecorder.exe" >nul
  if errorlevel 1 ( echo    [失败] RepRecorder\ ) else ( echo    [OK] RepRecorder\ )
) else (
  echo    [跳过] 找不到 %T1%
)
if exist "%T2%" (
  copy /Y "%EXE%" "%T2%\RepRecorder.exe" >nul
  if errorlevel 1 ( echo    [失败] RepRecorder-给别人\ ) else ( echo    [OK] RepRecorder-给别人\ )
) else (
  echo    [跳过] 找不到 %T2%
)

echo.
echo 注意：RepRecorder-源码\ 一个字节都没动（只读目录，是旧 WinForms 版的退路）。
echo       要跑旧界面：双击 启动旧界面-退路.bat，或给 exe 加 --old 参数。

echo.
echo 重打 zip：
set ZIPPS=Add-Type -AssemblyName System.IO.Compression.FileSystem; $d='%T2%'; $z='%ROOT%\RepRecorder-发给别人.zip'; if(Test-Path -LiteralPath $d){ if(Test-Path -LiteralPath $z){Remove-Item -LiteralPath $z -Force}; [System.IO.Compression.ZipFile]::CreateFromDirectory($d,$z,[System.IO.Compression.CompressionLevel]::Optimal,$false); Write-Host '    [OK] RepRecorder-发给别人.zip' }
powershell -NoProfile -Command "%ZIPPS%"

echo.
echo 同步完成。
pause
exit /b 0

:abort
echo.
echo 已中止：备份没建成功，一个文件都没覆盖。
pause
exit /b 1

:backup
if not exist "%~1\RepRecorder.exe" exit /b 0
if not exist "%~1\备份" mkdir "%~1\备份"
set BK=%~1\备份\RepRecorder-%STAMP%.exe
copy /Y "%~1\RepRecorder.exe" "%BK%" >nul
if exist "%BK%" (
  echo    [备份] %BK%
  exit /b 0
)
echo.
echo    [严重] 备份没建成功，已中止：%BK%
exit /b 9
