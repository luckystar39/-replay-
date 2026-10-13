@echo off
chcp 65001 >nul
rem ============================================================
rem  双击本文件 = 启动 RepRecorder（新 WPF 界面）
rem  把 rep 文件 / 文件夹拖到本文件上 = 启动并把它们加进队列
rem
rem  2026-10-03 起，新界面已经【是默认】的了 —— 直接双击 RepRecorder.exe 就是这个。
rem  本文件留着是为了「拖 rep 上去启动」这个用法。
rem  想跑旧的 WinForms 界面：RepRecorder-旧版WinForms\ 里那份 RepRecorder.exe
rem    （2026-10-12 起，旧界面不再装在本程序里了，那一份是独立的旧版快照）
rem ============================================================
start "" "%~dp0RepRecorderWPF.exe" %*
exit
