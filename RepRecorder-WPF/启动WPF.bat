@echo off
chcp 65001 >nul
rem ============================================================
rem  双击本文件 = 用【新的 WPF 界面】启动
rem  把 rep 文件/文件夹拖到本文件上 = 启动并把它们加进队列
rem  （不带 --wpf 的那个 RepRecorderWPF.exe 还是旧界面）
rem ============================================================
start "" "%~dp0RepRecorderWPF.exe" --wpf %*
exit
