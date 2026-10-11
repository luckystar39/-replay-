@echo off
rem ============================================================
rem  同步脚本（新版 / WPF）—— 2026-10-03 阶段 7 切换之后，主目录换成了这里。
rem
rem  用法：改完代码 -> 双击 编译.bat 编出 RepRecorderWPF.exe -> 再双击本文件。
rem
rem  做的事：
rem    1. 把 RepRecorderWPF.exe 复制成 RepRecorder.exe（名字不变，用户双击的还是它），
rem       到 RepRecorder-给别人\
rem    2. 覆盖之前先把目标位置的老 exe 备份到 <目标>\备份\RepRecorder-时间戳.exe，
rem       并且立刻验证备份真的存在（交接文档第 1 节的铁律 2）
rem    3. 重打 RepRecorder-发给别人.zip
rem    4. 【绝对不动】RepRecorder-旧版WinForms\ —— 那是旧版快照，是退路
rem
rem  注意：旧版那个同步脚本已经停用（在 RepRecorder-旧版WinForms\ 里，
rem        文件名带 [已停用-别用] 前缀）。它会把旧 WinForms 版的 exe 覆盖回来。
rem ============================================================
setlocal
set SRC=%~dp0
set ROOT=%SRC%..\..
set T2=%ROOT%\RepRecorder-给别人
set EXE=%SRC%..\RepRecorderWPF.exe

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

call :backup "%T2%"
if errorlevel 9 goto abort

echo.
echo 从 RepRecorder-WPF 同步到：
if exist "%T2%" (
  copy /Y "%EXE%" "%T2%\RepRecorder.exe" >nul
  rem 2026-10-08: 把目标目录里的设置和日志删掉 —— 设置文件里是【作者本人的】
  rem OBS WebSocket 密码 + 本机绝对路径。打包脚本的 zip 白名单本来就排除了它，
  rem 但磁盘上这份还在，而给使用者的说明第一句就是"把这个文件夹整个拷到整合包上一级"
  rem —— 照做的人会把密码一起带走，然后连接报"认证失败"而界面还显示"已就绪"。
  rem 删掉之后新用户第一次启动会自己生成一份干净的。
  if exist "%T2%\RepRecorder.settings.json" del /Q "%T2%\RepRecorder.settings.json"
  if exist "%T2%\RepRecorder.log" del /Q "%T2%\RepRecorder.log"
  if exist "%T2%\RepRecorder-上次.log" del /Q "%T2%\RepRecorder-上次.log"
  if errorlevel 1 ( echo    [失败] RepRecorder-给别人\ ) else ( echo    [OK] RepRecorder-给别人\ )
) else (
  echo    [跳过] 找不到 %T2%
)

echo.
echo 注意：RepRecorder-旧版WinForms\ 一个字节都没动（旧版快照，是退路）。
echo       要跑旧界面：双击 启动旧界面-退路.bat，或给 exe 加 --old 参数。

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%SRC%打包-发给别人.ps1"

echo.
echo 同步完成。
if not "%1"=="/nopause" pause
exit /b 0

:abort
echo.
echo 已中止：备份没建成功，一个文件都没覆盖。
if not "%1"=="/nopause" pause
exit /b 1

:backup
if not exist "%~1\RepRecorder.exe" exit /b 0
if not exist "%ROOT%\_exe备份" mkdir "%ROOT%\_exe备份"
set BK=%ROOT%\_exe备份\RepRecorder-%STAMP%.exe
copy /Y "%~1\RepRecorder.exe" "%BK%" >nul
if exist "%BK%" (
  echo    [备份] %BK%
  exit /b 0
)
echo.
echo    [严重] 备份没建成功，已中止：%BK%
exit /b 9
