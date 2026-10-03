# RepRecorder：WinForms → WPF 分步移植方案

> **给执行者（Claude）：** 按本方案逐步实施，每阶段结束都要跑通该阶段的验收步骤再往下走。
> 相关技能：`superpowers:executing-plans`（本会话内逐任务实现）。

**目标：** 把 RepRecorder 的界面层从 WinForms 换成 WPF，拿到「缩放跟手不糊、真半透明、样式可写」的观感，
同时**一行不改**地保留录制/OBS/音频逻辑，并保住「单 exe、零依赖、双击编译、发给别人就能跑」这条底线。

**架构：** 并行新建 `RepRecorder-WPF/`，把现有 `RepRecorder.cs` 拆成两份：
`Core.cs`（录制逻辑，原样搬、只改 UI 调用行，可与旧版逐行 diff）+
`Ui.cs`（全新的 WPF 界面 + 调试接口）。录制逻辑一律通过一个很小的 `Ui` 门面碰界面。
旧 WinForms 版全程保留当退路，直到验收通过再切换。

**技术栈：** .NET Framework 4.8（系统自带）+ WPF（系统自带 PresentationFramework）+ `csc.exe` 直接编译，
不引入 NuGet、不引入 MSBuild、不引入 XAML（纯代码建界面），保住「单文件 + 双击编译」。

**Spec:** 本文件（方案即规格）+ `D:\WPF样机\WpfProbe.cs`（可运行的参考实现）。

**参考实现（已经在手边、能跑）：** `D:\WPF样机\WpfProbe.cs`（23040 字节的 exe，已实测：缩放 9.2ms/步、
DPI 感知文字锐利、最大化不盖任务栏、WindowChrome 拿到拖动/缩放/贴边）。
阶段 0~1 基本就是把它扩写。

---

## Global Constraints（每一步都适用，不得违反）

- **部署形态不变**：产物是单个 exe + `RepRecorder.settings.json` + 日志；不许出现需要安装的运行时、NuGet 包、外部 DLL。
- **设置文件格式不变**：沿用现有 `Settings` 类与 `SettingsVersion`（当前 7），用户的 `PanelWidth`/`BgImage`/`BgOpacity` 等必须原样读进来。
- **编译方式不变**：`编译.bat` 里仍然是系统自带的 `csc.exe`（路径见下），双击就能编。
- **不碰录制逻辑**：`ObsClient` / `GameAudio` / `Settings` / `Job` / `Log` / `Program`（含 `--cli` 分支）语义不改；
  只允许改「它怎么把结果告诉界面」这一类行。
- **旧版必须一直可用**：`RepRecorder-源码/`、`RepRecorder/`、`RepRecorder-给别人/` 在本方案全过程中不动，
  直到阶段 7 才做切换。
- **源码行尾 LF**、源码编码 UTF-8，编译加 `/codepage:65001`（现有约定，别破坏）。
- **验证脚本一律纯 ASCII**（PowerShell 5.1 读无 BOM 的 UTF-8 会当 GBK）；脚本里出现中文路径时**存成带 BOM 的 UTF-8**。
- **路径**：新工程 `D:\【完整包】非想天则整合包2026-06-15\RepRecorder-WPF\`（和 `RepRecorder-源码` 并列）。
- **DPI**：PerMonitorV2（app.manifest 声明），窗口在 300% 缩放下与现在物理尺寸一致（1180×648 DIP = 3540×1944 px）。

## Review Focus（方案里最容易翻车、但任何一个任务的单元测试都碰不到的 5 件事）

1. **并行 6 格同录时的界面刷新频率**——预计：界面不卡、状态行/进度实时更新，不掉帧不假死。（→ 任务 5.4、6.3）
2. **一次拖入上百个 rep（含文件夹递归）**——预计：列表不卡、滚动跟手、内存不爆。（→ 任务 2.5、6.3）
3. **300% 缩放下 1px 线 / 小字号 / 窗口跨屏拖动**——预计：边框线不消失、字号一致、跨屏不跳版。（→ 任务 0.3、6.2）
4. **老设置文件兼容**（字段缺失、`BgScope` 为空、`PanelCollapsed` 不存在）——预计：按默认值起，不崩、不覆盖用户数据。（→ 任务 0.4）
5. **录制中关窗**——预计：先收尾再退出，OBS 不停在后台录，快照/位图/定时器都释放。（→ 任务 5.5）

---

## 文件结构（先定死，改它要单独说）

| 文件 | 职责 | 来源 |
|---|---|---|
| `RepRecorder-WPF/Core.cs` | 除界面外的一切：Settings / Job / ObsClient / GameAudio / Log / Program / 录制流程 / 并行 / 音频路由 | **从 `RepRecorder-源码/RepRecorder.cs` 复制**，只改「碰界面」的那些行 |
| `RepRecorder-WPF/Ui.cs` | WPF 界面（窗口、顶栏、左栏卡片、队列、终端、对话框、样式表）+ `Ui` 门面 + `DebugPort` | 新写，参考 `D:\WPF样机\WpfProbe.cs` |
| `RepRecorder-WPF/app.manifest` | DPI 感知（PerMonitorV2）+ 兼容性声明 | 从样机复制 |
| `RepRecorder-WPF/编译.bat` | 用系统自带 csc 编译两个 .cs | 改自现有 `编译.bat` |
| `RepRecorder-WPF/说明.md` | 给人看的说明 | 改自现有 `说明.md` |
| `验证WPF-*.ps1` | 新的验证脚本（UIA + 调试接口） | 由现有 71 个脚本改写而来 |

**为什么拆两个文件而不是继续单文件**：`Core.cs` 要能和旧 `RepRecorder.cs` 逐行 diff，
保证「录制逻辑没被手抖改坏」这件事可被机器验证。界面是全新写的，混在一起这份 diff 就废了。

---

# 阶段 0：壳 + 可观测性（0.5~1 天）

**本阶段结束时你能做的：** 双击新 exe，看到一个和现在一模一样的空壳（背景图、左栏框、队列框、终端框、顶栏、底栏），
能拖能缩、能最大化；用一个 PowerShell 命令就能读出它的内部状态。

## 任务 0.1 建工程骨架与编译链

**Files:**
- Create: `RepRecorder-WPF/app.manifest`（复制 `D:\WPF样机\app.manifest`，**暂不启用**，见 R4）
- Create: `RepRecorder-WPF/编译.bat`
- Create: `RepRecorder-WPF/Ui.cs`

> ⚠ 执行时的修正（R4，已验证）：**`编译.bat` 不加 `/win32manifest`**。同一个 exe 里还跑着旧 WinForms 界面，
> 那套是按「DPI 不感知」布局的，整进程一变感知，在 300% 屏上会缩成 1/3。
> 改为在 `Ui.Start()` 里**运行时**调 `SetProcessDpiAwarenessContext(-4)`（PerMonitorV2），
> 只有走 WPF 那条路才设。已验证：`GetDpiForWindow` 返回 288。

- [ ] **步骤 1**：建目录，从样机复制 `app.manifest`（先留着，见上面那条修正）。
- [ ] **步骤 2**：写 `编译.bat`：

```bat
@echo off
chcp 65001 >nul
rem  RepRecorder（WPF 版）编译脚本：仍然是系统自带的 csc，不用装任何东西
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo 找不到 csc.exe & pause & exit /b 1 )
rem WPF 参考程序集（.NET Framework 4.7.2 那套，系统装 VS/SDK 时才在；没有就用 GAC 路径，见方案备注）
set REF=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2
tasklist /fi "imagename eq RepRecorderWPF.exe" | find /i "RepRecorderWPF.exe" >nul && (
  echo 检测到 RepRecorderWPF.exe 正在运行，请先关掉它再编译。 & pause & exit /b 1 )
"%CSC%" /nologo /target:winexe /codepage:65001 /win32manifest:app.manifest /out:RepRecorderWPF.exe ^
  /r:"%REF%\PresentationFramework.dll" /r:"%REF%\PresentationCore.dll" ^
  /r:"%REF%\WindowsBase.dll" /r:"%REF%\System.Xaml.dll" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  Ui.cs Core.cs
if errorlevel 1 ( echo. & echo 编译失败，看上面的错误信息。 ) else ( echo. & echo 编译成功：RepRecorderWPF.exe )
pause
```

> 备注：`System.Windows.Forms.dll` 暂时留着（Core 里还有 `MessageBox` / `Screen` 几处），
> .NET Framework 自带，留着不影响"零依赖"。等阶段 6 再决定要不要清干净。

- [ ] **步骤 3**：跑 `编译.bat`，预期输出 `编译成功：RepRecorderWPF.exe`。

## 任务 0.2 复制 Core（**已改为：保留旧界面，`--wpf` 并行长新的**）

> ⚠ 2026-10-03 改（执行时发现的，详见 `RepRecorder-WPF/_移植进度.md` 的 R5）：
> 原计划「删掉 WinForms 界面 → 让编译器把依赖全报出来」**行不通**：csc 对同一个缺失类型会去重，
> 11 个字段错误就把 8000 行的依赖信号盖住了，报错清单 ≠ 依赖表。
> 改成的做法：**Core.cs 与旧版逐字节一致**（旧路径全程可用、71 个脚本全程可验），
> WPF 界面在新文件 `Ui.cs` 里长；`Program.Main` 只加一个 `--wpf` 分支。
> 「删旧界面 + 抽 Ui 门面」整体挪到**阶段 5** 一次做完（那时阶段 1~4 已经把门面要什么摸清了）。
> 代价：中间态两套界面并存；阶段 5 那一步偏大。



**Files:**
- Create: `RepRecorder-WPF/Core.cs`（由 `RepRecorder-源码/RepRecorder.cs` 复制）
- Modify: `RepRecorder-WPF/Core.cs`（删掉 `MainForm` 类与 9 个自绘控件类、`Bg` 类；`Program.Main` 的 GUI 分支改成 `Ui.Start(args)`）

- [ ] **步骤 1**：用**二进制/精确替换**方式复制（⚠ 别用 Python 文本模式，会把 LF 变 CRLF 毁掉 diff）：

```bash
cd "d:/【完整包】非想天则整合包2026-06-15"
python -c "
import io
src = io.open(r'RepRecorder-源码/RepRecorder.cs', encoding='utf-8', newline='').read()
io.open(r'RepRecorder-WPF/Core.cs','w',encoding='utf-8',newline='').write(src)
"
tr -cd '\r' < "RepRecorder-WPF/Core.cs" | wc -c   # 必须是 0
```

- [ ] **步骤 2**：删掉这些区域（按类名整块删）：`MainForm`、`Bg`、`HudCapButton`、`BgPanel`、`BgFlowPanel`、
  `WheelRouter`、`HudScrollBar`、`HudMarquee`、`HudCombo`、`HudCheck`、`BgListView`、`BgLogView`。
  `Program.Main` 里创建窗体的那几行改成 `Ui.Start(args);`。
- [ ] **步骤 3**：编译，把编译器报的**每一个错**记到一张清单里（这就是录制逻辑对界面的全部依赖，
  预计 100~200 处，形式如 `SetStatusText(...)`、`logBox.AppendText(...)`、`list.Items[...]`、`obsState.Text`）。
  这张清单就是任务 5.1 的输入。预期：报错集中在少数几个方法名上（重复出现）。
- [ ] **步骤 4**：把清单里出现的操作归纳进 `Ui` 门面（见 0.5），**只归纳、不实现**，先让 Core 编过。

## 任务 0.3 窗口壳与视觉语言

**Files:** Modify: `RepRecorder-WPF/Ui.cs`

- [ ] **步骤 1**：把样机 `WpfProbe.cs` 的窗口骨架搬过来：`WindowChrome`、DPI 清单、调色板静态字段、
  `Card()` 卡片工厂、`FlatButtonTemplate()`、`SplitterStyle()`、`Txt()`。
- [ ] **步骤 2**：搭出空壳布局（左栏 322 / 竖分隔条 6 / 右栏；右栏 队列 `*` / 横分隔条 6 / 终端 `0.62*`；顶栏 30 / 底栏 22），
  四个框里先放占位文字。
- [ ] **步骤 3**：给**每个**交互元素和显示区设 `AutomationProperties.SetAutomationId(el, "xxx")`，
  命名表见文末附录 A。这一步不做，后面所有验证脚本都无从下手。
  ⚠ **R6（执行时实测）**：WPF 里 `Grid`/`Border`/`StackPanel` **没有 automation peer**，
  挂上去的 AutomationId 用 UIA 找不到（`TextBlock` 和 `Control` 系才有）。
  所以「验证脚本要碰得到」的元素必须是 `Control`/`TextBlock` 系，或自己加 peer ——
  实测可达的：按钮(Button)、分隔条(Thumb)、滚动区(Pane)、文本框(Edit)、文本(Text)、窗口(win)。
- [ ] **步骤 4**：跑起来截图对照现在的界面（用 `PrintWindow(hwnd, dc, 2)` 按物理尺寸抓，
  参考 `C:\Users\江博文\AppData\Local\Temp\wpf-check.ps1`）。
  **验收**：背景图铺满、1px 边框线在 300% 下不断线、字号与现在一致、最大化后矩形 == 工作区（不盖任务栏）。

## 任务 0.4 设置读写与兼容

- [ ] **步骤 1**：`Ui.Start` 里调 `Settings.Load()`，把用户现有 `RepRecorder.settings.json` 拷进来跑。
  **验收**：日志里出现和现在一样的「游戏目录 / OBS / BGM 录制」三行。
- [ ] **步骤 2**：故意删掉 json 里的 `BgScope` 和 `PanelCollapsed` 再启动。**验收**：不崩、用默认值、
  退出后文件仍是合法 json（对应 Review Focus 第 4 条）。

## 任务 0.5 调试接口（后面每一步都靠它验收）

**Files:** Modify: `RepRecorder-WPF/Ui.cs`（`DebugPort` 类）

- [ ] **步骤 1**：实现一个只在带 `--debug-port=<端口>` 启动时才起、只绑 `127.0.0.1` 的单行协议：

```
命令                              返回（单行 JSON）
status                         {"recording":false,"queue":0,"statusText":"就绪","env":"OBS: 已就绪  游戏: 已就绪","panel":322,"collapsed":false,"parallel":6}
rows                           [{"i":1,"file":"xxx.rep","state":"等待","dur":"--:--","size":"3.1MB","note":""}, ...]
log[:N]                        {"lines":["最后 N 行，默认 20"]}
press:<automationId>           {"ok":true}
set:<automationId>=<value>     {"ok":true}       // 复选框 true/false、下拉框数字、文本框字符串
```

骨架（真实实现约 80 行，用 `TcpListener` + 每连接读一行写一行，UTF-8）：

```csharp
public static class DebugPort
{
    static TcpListener srv;
    public static void TryStart(string[] args)          // 在 Ui.Start 里调
    {
        int port = 0;
        foreach (string a in args)
            if (a.StartsWith("--debug-port=")) int.TryParse(a.Substring(13), out port);
        if (port <= 0) return;
        srv = new TcpListener(IPAddress.Loopback, port);
        srv.Start();
        Thread t = new Thread(delegate () {
            while (true) {
                TcpClient c = srv.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(delegate (object o) { Serve((TcpClient)o); }, c);
            }
        });
        t.IsBackground = true; t.Start();
    }
    static void Serve(TcpClient c) { /* 读一行 → Handle → 写一行 → 关 */ }
    static string Handle(string cmd) { /* 见上面的协议表；全部走 Ui.Post 到 UI 线程上执行 */ }
}
```

- [ ] **步骤 2**：验收脚本 `C:\...\Temp\wpfstatus.ps1`（纯 ASCII）：

```powershell
param([int]$Port = 7788, [string]$Cmd = "status")
$c = New-Object System.Net.Sockets.TcpClient("127.0.0.1", $Port)
$w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($Cmd)
$r = New-Object System.IO.StreamReader($c.GetStream()); Write-Host $r.ReadLine()
$c.Close()
```

- [ ] **步骤 3**：`RepRecorderWPF.exe --debug-port=7788` 启动，跑上面的脚本两次：
  `status` 应返回 `queue=0`；`log` 应能看到启动那几行。**这就是新版的「读内部状态」手段**，
  替代旧脚本里的 `LVM_GETTOPINDEX` 之类（更稳，跨进程也不用找句柄）。
- [ ] **步骤 4（阶段 0 验收）**：提交一张对照截图：新版空壳 vs 旧版界面，缩放到同尺寸并排。

---

# 阶段 1：左栏控制面板（1.5 天）

## 任务 1.1 卡片与控件
- [ ] 「录制 / 并行 / 队列 / 声音 / 环境」五张卡片，按钮、下拉框、复选框、数值提示全部照旧版的文案与顺序。
- [ ] 按钮行按左栏宽度均分（旧版 `LayoutCardWidth`）；卡片随左栏拉伸。
- [ ] **验收**：UIA `Press` 每个按钮，`set:cbParallel=3` 后 `status` 返回 `parallel:3`。

## 任务 1.2 左栏自绘滚动条（旧版 `HudScrollBar` 的行为）
- [ ] 平时隐藏、鼠标移上去淡入、拖动/滚轮同步；不够长就不显示。
- [ ] **验收**：把窗口拖到很矮，`log`/截图确认滚动条出现且能滚到底（对应旧的 `左栏滚动条-*.ps1`）。

## 任务 1.3 活动栏与面板收起/宽度记忆
- [ ] Ctrl+B 收起、窄竖条上的 ▶ 展开、宽度记忆、拖到 <110px 自动收起（旧版 `OnPanelSplitterMoved` 那套手感）。
      （已核对源码：**全程序只有 Ctrl+B 这一个快捷键**，不用找别的。）
- [ ] **验收**：收起 → 关程序 → 重开仍是收起；展开宽度与关之前一致；`settings.json` 里 `PanelWidth`/`PanelCollapsed` 正确。
  对照旧脚本 `记忆-*.ps1`、`面板-*.ps1`、`宽度-*.ps1`。

## 任务 1.4 滚轮转发
- [ ] 鼠标停在左栏/列表/终端上滚动时，滚的是**光标底下那个**（旧版 `WheelRouter` 的语义）。
- [ ] **验收**：`set:` 造不出滚轮，改用 UIA 无解 → 直接用 `PostMessage(WM_MOUSEWHEEL)` 到窗口（旧脚本已验证可行，
  见《自动化测试的坑与可用套路》第 2 条）。对照 `滚轮-*.ps1`、`滚轮-对齐-*.ps1`。

---

# 阶段 2：队列列表（1.5~2 天）

## 任务 2.1 表格与状态点
- [ ] 六列（# / 文件 / 状态 / 用时 / 大小 / 说明）、表头样式、隔行加深、状态彩色点（等待灰 / 录制中蓝 / 完成绿 / 失败红 / 注意橙 / 已录制绿）。
- [ ] 列宽自适应：文件列优先、说明列吃掉剩余、**永远不出现横向滚动**（旧版 `FitListColumns` 的意图）。
- [ ] **验收**：`--late` 注入 60 个 rep 后，`rows` 返回 60 条且列宽不溢出；截图对照旧版。

## 任务 2.2 选择与操作
- [ ] 单选/多选/全选、框选（从行上按下拖出矩形）、空白处点击保持选择、双击、右键菜单（加入/移除/重录…照旧版清单）。
- [ ] **框选这块 WPF 能省一大截**：旧版 `HudMarquee` 是一个**独立的置顶半透明分层窗体**
  （`WS_EX_TOOLWINDOW|WS_EX_TRANSPARENT|WS_EX_NOACTIVATE`、opacity 0.42、还要自己算"只重画脏的那块"），
  WPF 里就是窗口内叠一层 `Canvas` + 一个 `Rectangle`，拖动时改 `Margin` 即可——**不要**再仿造那个独立窗体。
- [ ] **验收**：UIA `Press:btnMark` 后该批行底色变绿；`rows` 里能看出标记态；
  框选用「物理左键按下 + 窗口消息发精确坐标的 down/move/up」那套测（旧脚本 `框选-*.ps1` 已验证可行）。

## 任务 2.3 滚动
- [ ] 自绘滚动条 + 滚轮 + TopItem 语义（旧版用 `TopItem` + `LVM_GETTOPINDEX` 对齐的那套），
  末行必须能完整可见。
- [ ] **验收**：对照 `队列滚动-*.ps1`、`末行-滚到底-第20行完整可见.png` 那个用例。
  内部状态由 `rows` 提供（含首行行号），比旧版读 `LVM_GETTOPINDEX` 更好写。

## 任务 2.4 空态与拖放
- [ ] 空队列时不显示任何提示文字（用户已明确不要）；拖入时高亮「松手即加入队列 ↓」。
- [ ] **验收**：拖放模拟不了（旧结论），改走命令行注入路径验证「加入队列 N 个 rep」日志。

## 任务 2.5 大批量（Review Focus 第 2 条）
- [ ] 注入 300 个 rep（含 1 个深目录递归）。**验收**：界面不卡（`status` 在 1 秒内返回）、
  滚动到底瞬间响应、内存 < 200MB（`Get-Process` 的 `WorkingSet64`）。

---

# 阶段 3：终端区（1 天）

- [ ] 按字符宽度折行、只保留最近 2000 行原始日志、滚动条、复制全部（右键菜单）、
  折叠条（收起/展开 + 收起时右侧显示最后一条日志）。
- [ ] **验收**：灌 3000 行日志后 `status` 秒回、滚动到底部、折行宽度跟着窗口变化（对照旧版 `OnSizeChanged` 的语义：
  宽度变才重排）。对照 `终端滚动条-*.ps1`。

---

# 阶段 4：三个对话框（1 天）

- [ ] 背景设置窗（图片路径 / 不透明度 / 模糊 / 位移 X,Y / 放置方式 / 范围 content|global + 实时预览 + 确定/取消/还原）。
- [ ] 保存位置「每批问一次」与「开工前问一次」两个对话框（旧版 `AskSaveLocationDialog` / `AskSaveLocationUpFront`）。
- [ ] **验收**：改背景图不透明度 25→60 → 关对话框 → 重开还是 60（写盘正确）；
  切换 `范围=全局` → 左栏也透图。对照 `背景-*.ps1`、`记忆-*.ps1`。

---

# 阶段 5：把录制流程接上（1.5~2 天）

## 任务 5.1 `Ui` 门面实现
**Files:** Modify: `RepRecorder-WPF/Ui.cs`

- [ ] 按阶段 0.2 记下的清单，把门面填满。**门面签名以清单为准，不要预先设计过大**。起始集合：

```csharp
static class Ui
{
    public static void Start(string[] args);
    public static void Post(Action a);                 // 子线程 → UI 线程（Dispatcher.BeginInvoke）
    public static void Status(string s);               // 状态栏左侧
    public static void Env(string s, int level);       // 状态栏右侧环境：0=灰 1=绿 2=橙 3=红
                                                       // ⚠ 别传 Color：旧版传的是 System.Drawing.Color，
                                                       //   WPF 用的是 Media.Color，混用会编译不过/颜色错
    public static void Log(string s);                  // 终端追加一行（自动折行）
    public static void AddRow(Job j);
    public static void UpdateRow(Job j);
    public static void ClearRows();
    public static Job[] Rows();                        // 需要时：全部
    public static Job[] SelectedRows();
    public static void SetRecording(bool on);          // 顶栏 REC 呼吸点 + 按钮可用性
    public static void SetBusy(bool busy);             // 录制中禁用开始/清空等
    public static string AskFolder(Job job, int count);
    public static bool AskFolderUpFront(int count, out string folder);
}
```

- [ ] **验收**：`Core.cs` 零编译错误，且 `git diff` 出来的改动**只出现在原来的 UI 调用行**上。

## 任务 5.2 状态刷新与线程
- [ ] 所有跨线程更新一律走 `Ui.Post`；禁止从 worker 线程直接碰控件。
- [ ] **验收**：录 1 个 rep，界面全程不假死（同时 `status` 每次都能在 200ms 内返回）。

## 任务 5.3 单录与并行
- [ ] 单录 1 个 rep → 出 mp4；并行 2 格 → 出 2 个 mp4；日志与状态同步正确。
- [ ] **验收**：视频文件名/时长对得上，`rows` 状态从「等待 → 录制中 → 完成」，用时列有值。

## 任务 5.4 并行 6 格压力（Review Focus 第 1 条）
- [ ] 6 格同时录。**验收**：界面可交互（UIA `Press:btnStop` 有效）、状态栏不卡、录完 6 个文件齐全。

## 任务 5.5 录制中关窗（Review Focus 第 5 条）
- [ ] 关窗 → 先停录收尾 → 再退出；OBS 不留后台录制；`Process.GetCurrentProcess().HandleCount` 稳定。
- [ ] **验收**：录制中点 X，30 秒内进程退出，OBS 不再录制，且没有残留 ffmpeg/录像进程。

---

# 阶段 6：逐项校对 + 脚本改造（2~3 天）

## 任务 6.1 视觉逐项对照
- [ ] 按 `_看片/` 里已有的对照思路，出一组「新版 vs 旧版」同尺寸截图：
  空态 / 有队列 / 录制中 / 并行 / 背景透明 25% / 背景模糊 / 面板收起 / 终端折叠 / 分隔条 hover / 最大化。
- [ ] **验收**：每组并排图放到 `RepRecorder-WPF/_看片/`，差异逐条记下来（能接受 or 必须改）。

## 任务 6.2 300% DPI 细节（Review Focus 第 3 条）
- [ ] 1px 边框线是否断、小字号是否发虚、跨屏拖动是否跳版。
- [ ] **验收**：`UseLayoutRounding` / `SnapsToDevicePixels` / `TextFormattingMode.Display` 三件套按需开；
  1:1 裁剪截图确认线是干净的 1 物理像素。

## 任务 6.3 把 41 个依赖原生句柄的脚本改成 UIA + 调试接口
- [ ] 优先改高频用的：界面截图 / 列表滚动条 / 队列滚动 / 框选 / 面板宽度记忆 / 面板收缩 / 快捷键 / 组合框高亮 / 背景跟随。
- [ ] UIA 模板（纯 ASCII；**不含中文路径**，所以脚本里用 `$env:TEMP` 或把 exe 拷到 ASCII 目录再跑）：

```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = Join-Path $PSScriptRoot 'RepRecorderWPF.exe'
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | Stop-Process -Force
$p = Start-Process $exe -ArgumentList '--debug-port=7799' -PassThru
Start-Sleep -Seconds 4
$root = [System.Windows.Automation.AutomationElement]::RootElement
function ById([string]$id) {
  $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Press([string]$id) {
  $e = ById $id
  $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Dbg([string]$cmd) {
  $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', 7799)
  $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
  $r = New-Object System.IO.StreamReader($c.GetStream()); $out = $r.ReadLine(); $c.Close(); return $out
}
Press 'btnBg'; Start-Sleep -Milliseconds 500
Write-Host (Dbg 'status')
Stop-Process -Id $p.Id -Force
```

- [ ] **验收**：改完的脚本逐条跑通，输出和旧版脚本等价（内部状态从 `status`/`rows`/`log` 里读）。

---

# 阶段 7：切换与收尾（0.5 天）

- [ ] `RepRecorder-WPF/` 里补 `同步到所有文件夹.bat`（把 exe 同步到 `RepRecorder-给别人/` 并重打 zip）。
- [ ] 旧版整体改名保留：`RepRecorder-源码/` → 不动；新增 `RepRecorder-旧WinForms/` 存一份快照当退路。
- [ ] 更新 `说明.md`（编译说明改成 WPF 版；首次运行需要 .NET Framework 4.8，Win10/11 自带）。
- [ ] **验收**：在一台干净目录（模拟"别人电脑"）里跑新 exe，能开、能读设置、能录一个 rep。

---

## 风险与对策

| 风险 | 触发条件 | 对策 |
|---|---|---|
| 录制逻辑被改坏 | 搬 Core 时手抖 | Core.cs 从旧文件整块复制，改动只允许出现在 UI 调用行；每阶段 `diff` 检查 |
| 验证能力归零（41 个脚本失效）| 换了 UI 栈就没有原生子窗口 | 阶段 0 就落地 AutomationId + `--debug-port`，不要拖到阶段 6 |
| 观感来回磨 | 滚动条/行高/折行这些细节 | 以样机为基准先对齐，再微调；每轮出「新旧并排」截图 |
| WPF 冷门坑卡住某一块 | 列表虚拟化 / 日志折行 | 该块允许临时用 `WindowsFormsHost` 顶住不阻塞整体（长期不推荐） |
| 300% DPI 细节 | 1px 线、小字号 | 阶段 0 定死三件套；阶段 6.2 专门验 |
| 中途想停 | 任何阶段 | 旧版一直都在，任何阶段末尾都是「旧版可用 + 新版可跑」的状态 |

## 工期与决策点

- 编码 8~11 个工作日 + 脚本改造 1~2 天 = **合计 9~13 个工作日**；算上你手工验收与来回修，**现实 2~3 周**。
- **可以停的地方**：阶段 0 后（壳能不能看）、阶段 2 后（列表这块最重，值不值得继续）、阶段 5 后（能录了就只剩收尾）。
- 谁做什么：**我写代码 + 编译 + 自己跑自动验收**；你负责「摸手感」和最终拍板。每阶段我给你一份并排截图 + 验收脚本输出。

## 明确不做的事（避免范围蔓延）

- 不做 XAML / 不引入 MVVM 框架 / 不引入 NuGet 包 —— 保住单文件零依赖。
- 不重写录制逻辑、不改设置格式、不动音频路由。
- 不追求像素级复刻（差几个像素的空隙、差一档的灰）——以「观感一致 + 手感更好」为准。
- 不做多语言、不做主题切换、不做插件化。

---

## 附录 A：AutomationId 命名表（阶段 0.3 就要全部落地）

| 区域 | AutomationId |
|---|---|
| 窗口 | `win` |
| 顶栏 | `topBar`、`capMin`、`capMax`、`capClose`、`btnPanel`、`btnBg`、`recDot` |
| 录制卡 | `btnStart`、`btnStop` |
| 并行卡 | `cbParallel`、`lblMemHint`、`chkRound`、`chkAuto`、`chkPrepare`、`chkCloseObs` |
| 队列卡 | `btnClear`、`btnRemove`、`btnMark`、`btnRefresh`、`btnPrepare` |
| 声音卡 | `btnSound`、`btnGameAudio`、`btnBgm`、`cbMic` |
| 环境卡 | `btnSet`、`btnOpenLog` |
| 队列表 | `list`、`listScroll`，行内文本用 UIA 名字读（不需要单独 id） |
| 终端 | `logBox`、`logHeader`、`logScroll` |
| 底栏 | `statusText`、`statusEnv` |
| 对话框 | 各自窗口 id + `dlgOk`、`dlgCancel`、`bgImage`、`bgOpacity`、`bgBlur`、`bgOffX`、`bgOffY`、`bgLayout`、`bgScope` |

## 附录 B：关键参考文件

- 样机源码（能跑的参考实现）：`D:\WPF样机\WpfProbe.cs`
- 样机清单：`D:\WPF样机\app.manifest`
- 实测脚本：`C:\Users\江博文\AppData\Local\Temp\wpf-check.ps1`（清晰度/缩放耗时/最大化三合一）
- 旧版界面清单与踩坑记录：`RepRecorder/README.md`、`RepRecorder/交接.md`
- 自动化测试的坑与可用套路：见记忆 `rep-recorder-testing-limits`
