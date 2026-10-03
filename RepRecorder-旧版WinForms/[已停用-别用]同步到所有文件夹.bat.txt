@echo off
chcp 65001 >nul
rem ============================================================
rem  同步脚本：把【本文件夹（RepRecorder-源码）】当成"主"
rem  把这里的 RepRecorder.exe / RepRecorder.cs 复制到其它文件夹
rem
rem  用法：改完代码、双击 编译.bat 编译出新的 RepRecorder.exe 之后，
rem        再双击本文件 —— 所有文件夹就都统一成这一份了。
rem
rem  约定（用户定的规矩）：
rem     · 源码和 exe 都以「RepRecorder-源码」里的为准；
rem     · 别的文件夹里的是副本，会被本脚本覆盖。
rem ============================================================
setlocal
set SRC=%~dp0
set ROOT=%SRC%..
set T1=%ROOT%\RepRecorder
set T2=%ROOT%\RepRecorder-给别人

if not exist "%SRC%RepRecorder.exe" (
  echo 本文件夹里没有 RepRecorder.exe，先双击 编译.bat 编译一次。
  pause
  exit /b 1
)

tasklist /fi "imagename eq RepRecorder.exe" | find /i "RepRecorder.exe" >nul && (
  echo 检测到 RepRecorder.exe 正在运行，请先关掉它（否则 exe 被占用复制失败）。
  pause
  exit /b 1
)

echo 从「RepRecorder-源码」同步到：
if exist "%T1%" (
  copy /Y "%SRC%RepRecorder.exe" "%T1%\RepRecorder.exe" >nul && echo    [OK] RepRecorder\
  copy /Y "%SRC%RepRecorder.cs"  "%T1%\RepRecorder.cs"  >nul && echo    [OK] RepRecorder\  （源码）
) else ( echo    [跳过] 找不到 %T1% )
if exist "%T2%" (
  copy /Y "%SRC%RepRecorder.exe" "%T2%\RepRecorder.exe" >nul && echo    [OK] RepRecorder-给别人\
) else ( echo    [跳过] 找不到 %T2% )

echo.
echo 再顺手重打两个 zip（需要 PowerShell，Windows 自带）：
powershell -NoProfile -Command ^
  "Add-Type -AssemblyName System.IO.Compression.FileSystem; " ^
  "$r='%ROOT%'; " ^
  "foreach($p in @(@($r+'\RepRecorder-给别人',$r+'\RepRecorder-发给别人.zip'),@($r+'\RepRecorder-源码',$r+'\RepRecorder-源码.zip'))){ " ^
  "  if(Test-Path -LiteralPath $p[0]){ if(Test-Path -LiteralPath $p[1]){Remove-Item -LiteralPath $p[1] -Force}; " ^
  "    [System.IO.Compression.ZipFile]::CreateFromDirectory($p[0],$p[1],[System.IO.Compression.CompressionLevel]::Optimal,$false); " ^
  "    Write-Host ('    [OK] ' + (Split-Path $p[1] -Leaf)) } }"

echo.
echo 同步完成。
pause
