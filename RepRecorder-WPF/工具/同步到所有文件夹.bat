@echo off
rem ============================================================
rem  同步脚本（新版 / WPF）—— 2026-10-03 阶段 7 切换之后，主目录换成了这里。
rem
rem  用法：改完代码 -> 双击 编译.bat 编出 RepRecorderWPF.exe -> 再双击本文件。
rem
rem  做的事：
rem    1. 把 RepRecorderWPF.exe 复制成 RepRecorder.exe（名字不变，用户双击的还是它），
rem       到 RepRecorder-给别人\
rem    2. 重打 RepRecorder-发给别人.zip
rem    3. 【绝对不动】RepRecorder-旧版WinForms\ —— 那是旧版快照，是退路
rem
rem  2026-10-11 用户要求：不再备份旧 exe（_exe备份\ 那一步整条去掉）。
rem     要回退到旧版本，用 _发版\ 里的完整包，或 git 里的历史提交。
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

