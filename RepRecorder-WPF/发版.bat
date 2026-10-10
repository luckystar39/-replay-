@echo off
rem 发版（2026-10-10）。用法：双击，按提示输版本号；自动化用 发版.bat --check 1.1
rem
rem 注意 本文件必须存成 GBK、且不要加 chcp 65001 —— cmd 是按字节偏移解析批处理的，
rem    带 chcp + UTF-8 会让它把半个单词当成命令（仓库里踩过一次）。
rem 注意 真正的逻辑都在 发版-生成.ps1 里；这里只做"传参 + 设两个仓库地址"。
setlocal

rem ↓↓↓ 你的 Gitee 用户名/仓库名，只填这一行。留空 = 还没建仓库，那就先跳过 Gitee 那条链接。
set GITEE=xingya30/reprecorder-update
set GITHUB=luckystar39/------------2026-06-15

set VER=%1
set MODE=
if "%1"=="--check" (set "MODE=--check" & set "VER=%2")
if "%VER%"=="" set /p VER=请输入新版本号（例如 1.1）:

set NOTES=
if not "%MODE%"=="--check" set /p NOTES=这一版的更新说明（一行，可留空）:

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0发版-生成.ps1" %VER% %MODE%

rem --check 是给验收脚本用的（cmd /c 调），不能停；正式发版才停下来让你看结果。
if not "%MODE%"=="--check" pause
