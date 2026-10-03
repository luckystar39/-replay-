# 这个目录里都是什么（2026-10-03~04 整理）

```
D:\【完整包】非想天则整合包2026-06-15\
├─ RepRecorder-WPF\            ← 新版（WPF 界面）**主目录，改代码改这里**
├─ RepRecorder-旧版WinForms\   ← 旧版（WinForms 界面）快照，退路
├─ RepRecorder-给别人\          ← 发给使用者的包（exe + ffmpeg.exe + 说明）
├─ 【完整包】非想天则整合包2026-06-15\  ← 游戏本体（th123 / 启动器 / Mod）
├─ Rep录制器.lnk               ← 快捷方式 → RepRecorder-WPF\RepRecorderWPF.exe
├─ RepRecorder-发给别人.zip     ← 打好的分发包
├─ _历史存档\                  ← 历史对话、前身脚本、过期文档、回滚点（见下）
└─ _exe备份\                   ← 同步脚本自动建的 exe 备份
```

## 我要改程序，从哪下手

1. 进 `RepRecorder-WPF\`
2. 改 `.cs`（**保存时选 UTF-8**）
3. 先关掉正在运行的 RepRecorder，双击 `编译.bat`
4. 双击 `同步到所有文件夹.bat` —— 分发目录和 zip 就都更新了

详细说明在 `RepRecorder-WPF\说明.md`。

## 两个文件夹的区别

| | 旧版 WinForms | 新版 WPF |
| --- | --- | --- |
| 位置 | `RepRecorder-旧版WinForms\` | `RepRecorder-WPF\` |
| 源码 | `RepRecorder.cs`（单文件，约 9000 行） | `Core.cs` + `Recorder.cs` + `Ui.cs` + `UiShell.cs` + `UiWindow.cs` + `UiDialogs.cs` |
| exe | `RepRecorder.exe` | `RepRecorderWPF.exe` |
| 为什么换 | 拖边框缩放每步全量重绘 ≈47ms，就是"卡" | 同样场景 6~12ms |
| 现在还能用吗 | 能。新版加 `--old` 参数就切回旧界面 | 是默认界面 |

## ⚠ 三条别踩的坑

1. **`RepRecorder-旧版WinForms\` 别往里写东西**（旧版快照 = 退路）。
   里面那个 `[已停用-别用]同步到所有文件夹.bat.txt` 是**旧的同步脚本**，
   它会把旧 WinForms 版的 exe 覆盖回分发目录，把切换冲掉 ——
   所以特意改掉了扩展名让它双击跑不起来。**别把它改回 `.bat`。**
   同步一律用 `RepRecorder-WPF\同步到所有文件夹.bat`。

2. **别删 `【完整包】非想天则整合包2026-06-15\`**。9.7 GB，里面是游戏本体，
   程序的 `GameDir` 设置就指向它的 `th123`。它不在 git 里，删了下载不回来。

3. `_历史存档\史书\` 里那三个 `.bak` 是**回滚点，不是垃圾，别删**
   （2026-09-26 解除会话归档 / 删插件时留的），它们现在挪进了 `_历史存档\`。

## `_历史存档\` 里是什么

2026-10-04 从根目录挪进去的，**什么都没删**。里面分四类：

| 子目录 | 内容 |
| --- | --- |
| `史书\` | 项目的完整来龙去脉 —— DSH 时期的对话导出（**很多设计决定只在这里**）+ 三个回滚点 `.bak` |
| `前身脚本\` | `RecordRepWithOBS.ps1` / `RepRenderTest.ps1` —— RepRecorder 就是从它们演化来的 |
| `DSH工具\` | 换 Claude Code 之前那套 harness 的工具，已经不用了 |
| `过期文档\` | 被取代的方案文档（含已经执行完的 WPF 移植方案） |

详细说明在 `_历史存档\README.md`。项目完整台账在 `RepRecorder-WPF\_移植进度.md`，
更早的交接文档在 `RepRecorder-旧版WinForms\交接.md`。

## `_exe备份\`

`同步到所有文件夹.bat` 每次覆盖分发目录的 exe 之前，会把旧的那份存到这里。
想回退到某个历史构建，从这里拿。
