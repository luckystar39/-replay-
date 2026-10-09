// ============================================================
//  Recorder.cs -- the recording logic, lifted out of MainForm.
//
//  Why "lifted" and not "moved": Core.cs (the WinForms build) is left byte-identical so the
//  old path (no --wpf) keeps working and can always be used as the fallback. Once the WPF
//  path is verified (phase 6) we can decide about deleting MainForm.
//
//  Nothing in this file may touch a WinForms control: everything that talks to the UI goes
//  through the Ui facade. The bodies below were copied verbatim from MainForm, so the
//  remaining compiler errors ARE the to-do list.
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RepRecorder
{
    // ---- "影子控件"：逻辑里本来写的是 WinForms 控件的属性赋值（btnSound.Text = "…"）。
    //      下面这几个代理把赋值转成对 WPF 界面的操作，这样逻辑那边的 60 来处一字不用改，
    //      也不会因为手改而漏掉某一处。名字沿用旧的，纯粹为了少动逻辑。
    sealed class UiButton
    {
        readonly string id;
        readonly Flat flat;
        public UiButton(string automationId) { id = automationId; flat = new Flat(automationId); }
        public string Id { get { return id; } }
        public sealed class Flat
        {
            readonly string id;
            public Flat(string automationId) { id = automationId; }
            public System.Drawing.Color BorderColor { set { Ui.SetButtonBorderColor(id, value); } }
        }
        public string Text { get { return Ui.GetButtonText(id); } set { Ui.SetButtonText(id, value); } }
        public bool Enabled { get { return true; } set { Ui.SetButtonEnabled(id, value); } }
        public System.Drawing.Color ForeColor { set { Ui.SetButtonForeColor(id, value); } }
        public System.Drawing.Color BackColor { set { Ui.SetButtonBackColor(id, value); } }
        public Flat FlatAppearance { get { return flat; } }
        public double Width { set { Ui.SetButtonWidth(id, value); } }
        public void Refresh() { }
    }

    sealed class UiLabel
    {
        readonly string id;
        public UiLabel(string automationId) { id = automationId; }
        public string Text { get { return ""; } set { Ui.SetLabelText(id, value); } }
    }

    public class Recorder
    {

        // ---- 这三处旧版写在 MainForm 里（弹窗和策略混在一起）。现在策略留在这边，弹窗交给 Ui。
        //      AskSaveLocation* 的两个对话框在 Ui.AskDialog 里，返回 Choice：0 留在原地 / 1 另存 / 2 第三个按钮。
        string AskSaveLocationDialog(Job job, int count)
        {
            string result = "keep";
            try
            {
                string where = job == null || string.IsNullOrEmpty(job.Output) ? "" : Path.GetDirectoryName(job.Output);
                string info;
                if (count > 1)
                    info = "这一批 " + count + " 个视频都录好了（例如：" + Path.GetFileName(job.Output) + "，"
                         + (job.Size / 1048576.0).ToString("0.0") + " MB，" + FmtDur(job.VideoSeconds) + "）\r\n"
                         + "现在位置：" + where;
                else
                    info = "已录好：" + Path.GetFileName(job.Output) + "\r\n"
                         + (job.Size / 1048576.0).ToString("0.0") + " MB，时长 " + FmtDur(job.VideoSeconds) + "\r\n"
                         + "现在位置：" + where;
                info += "\r\n\r\n" + (count > 1 ? "这 " + count + " 个视频要放到哪里？" : "你想把这个视频放到哪里？");
                AskResult res = Ui.ShowAskDialog(false, info, cfg == null ? "" : cfg.LastSaveFolder,
                                                    "本队列后面录好的视频也都这样处理（不再询问）");
                if (res == null) return result;
                if (res.Checked) queueSaveDecided = true;
                if (res.Choice == 1 && res.Picked != null)
                {
                    cfg.Save();
                    if (res.Checked) queueSaveFolder = res.Picked;
                    result = res.Picked;
                }
                else if (res.Choice == 2)
                {
                    cfg.AskSaveLocation = false;
                    cfg.Save();
                    Log.Write("已关闭“每次询问保存位置”，可在设置里重新打开");
                }
            }
            catch (Exception ex) { Log.Write("询问保存位置失败：" + ex.Message); }
            return result;
        }

        string AskSaveLocationDialog(Job job) { return AskSaveLocationDialog(job, 1); }

        bool AskSaveLocationUpFront(int count)
        {
            queueSaveDecided = false;
            queueSaveFolder = null;
            bool go = true;
            try
            {
                string info = "这次要录 " + count + " 个 rep" + (count > 6 ? "（会按「同时录制数量」自动分批）" : "") + "。\r\n"
                            + "先选好视频放在哪里，选完就开始录。\r\n\r\n"
                            + "上次用的位置：" + (cfg == null || string.IsNullOrEmpty(cfg.LastSaveFolder) ? "（还没选过）" : cfg.LastSaveFolder);
                // 没有「以后不再询问」那个勾了 —— 用户要求每次开录都问一遍
                AskResult res = Ui.ShowAskDialog(true, info, cfg == null ? "" : cfg.LastSaveFolder, null);
                if (res == null || res.Choice == 2) go = false;
                else
                {
                    if (res.Choice == 1 && res.Picked != null)
                    {
                        queueSaveFolder = res.Picked;
                        // ⚠ 记住这次选的文件夹（2026-10-08 补）。移植时把旧 WinForms
                        //   PickSaveFolder 里那句 cfg.LastSaveFolder = fb.SelectedPath 弄丢了 ——
                        //   全工程再没有第二处写它，于是"上次用的位置"永远显示"（还没选过）"，
                        //   而且 FindExistingOutput 靠它去找已录的文件：另存到别处之后就找不回来，
                        //   那些 rep 下次刷新会变回「等待」，被重录一遍。
                        cfg.LastSaveFolder = res.Picked;
                    }
                    queueSaveDecided = true;
                    cfg.Save();
                }
            }
            catch (Exception ex)
            {
                Log.Write("询问保存位置失败（照常开始录制）：" + ex.Message);
                queueSaveDecided = true;
            }
            if (!go) Log.Write("你在询问里点了「取消（先不录）」——这次不录制");
            else if (!string.IsNullOrEmpty(queueSaveFolder)) Log.Write("本次录好的视频会放到：" + queueSaveFolder);
            else Log.Write("本次录好的视频就放在各自 rep 旁边");
            return go;
        }

        // 把"本批"（钉住的优先）标绿
        void HighlightBatchRows()
        {
            List<Job> batch = pinnedBatch.Count > 0 ? pinnedBatch : selectedBatch;
            Ui.HighlightBatch(batch);
        }

        // 底栏右边的环境状态（旧版是直接改 obsState 那个 Label）
        void RefreshEnvLabel()
        {
            string obsExe = (!string.IsNullOrEmpty(cfg.ObsExe) && File.Exists(cfg.ObsExe)) ? cfg.ObsExe : Settings.FindObsExe();
            string game = (!string.IsNullOrEmpty(cfg.GameDir) && File.Exists(Path.Combine(cfg.GameDir, "th123.exe"))) ? cfg.GameDir : Settings.FindGameDir();
            string s = (obsExe == null) ? "OBS：未安装（winget install --id OBSProject.OBSStudio -e）" : "OBS：已就绪";
            s += (game == null) ? "    游戏：未找到 th123.exe" : "    游戏：已就绪";
            // 2026-10-08：左下角这一行以前**完全不提 ffmpeg** —— 并行录制每一格都会因为缺它而
            // 「切分失败」，而这里是用户唯一会看一眼的地方，写着「已就绪」等于骗人。
            string ffm = null;
            try { ffm = FindFfmpeg(); } catch { }
            if (ffm == null) s += "    ffmpeg：未找到（并行录制需要，单录不用）";
            Ui.Env(s, (obsExe == null || game == null || ffm == null) ? 3 : 0);
        }

        static string MuteFlagPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.已静音待恢复"); }
        }

        bool? ReadBgmMuted()
        {
            try
            {
                foreach (string p in BgmIniPaths())
                {
                    string t = File.ReadAllText(p);
                    System.Text.RegularExpressions.Match mm =
                        System.Text.RegularExpressions.Regex.Match(t, @"(?im)^\s*MuteMusic\s*=\s*(\d+)\s*$");
                    if (mm.Success) return mm.Groups[1].Value.Trim() != "0";
                }
            }
            catch { }
            return null;
        }

        // ---- 旧代码里那些颜色常量（对应 MainForm 的 Hud* 静态字段）----
        static Color HudOff { get { return MainForm.HudOff; } }
        static Color HudOn { get { return MainForm.HudOn; } }
        static Color HudDim { get { return MainForm.HudDim; } }
        static Color HudLine { get { return MainForm.HudLine; } }
        static Color HudText { get { return MainForm.HudText; } }
        static Color HudRed { get { return MainForm.HudRed; } }
        static Color HudOrange { get { return MainForm.HudOrange; } }
        static Color HudAccent2 { get { return MainForm.HudAccent2; } }

        // ⚠ 旧版是 MainForm 的构造函数里 cfg = Settings.Load()。搬字段的时候把这一句漏了，
        //   结果逻辑这边的 cfg 一直是 null（启动当场空引用）。现在由界面那边建好同一个实例传进来 ——
        //   必须是【同一个】实例：界面改了并行数、逻辑也在读 cfg。
        public Recorder(Settings settings) { cfg = settings; }

        // ================================================================
        //  给 WPF 界面的公开入口（旧版这些是挂在按钮的 Click 上的私有方法；
        //  现在按钮在界面层，这里开一层很薄的公开壳，逻辑一行没动）
        // ================================================================
        public void UiStart() { OnStart(null, null); }
        public void UiStop() { OnStop(null, null); }
        public void UiClear() { OnClear(null, null); }
        public void UiRemove() { OnRemove(null, null); }
        public void UiMarkBatch() { OnMarkBatch(null, null); }
        public void UiRefreshQueue() { OnRefreshQueue(null, null); }
        public void UiCheck() { OnCheck(null, null); }
        public void UiPrepareObs() { OnPrepare(null, null); }
        public void UiOpenSettings() { OnSettings(null, null); }
        public void UiOpenLog() { OnOpenLog(null, null); }
        public void UiToggleSound() { OnSoundToggle(null, null); }
        public void UiToggleGameAudio() { OnGameAudioToggle(null, null); }
        public void UiToggleBgm() { OnBgmToggle(null, null); }
        public void UiAddPaths(string[] paths) { if (paths != null && paths.Length > 0) AddPaths(paths); }
        public void UiRefreshSoundButtons() { RefreshSoundButtons(); }

        // 旧版是挂在 list.ItemSelectionChanged 上的那句「列表里选中几个 rep，「同时录制数量」就跟着变（用户要求）」。
        // 移植时这根线漏接了（WPF 的 ListBox 没有原生句柄，没人提醒），靠 验证WPF-10 抓出来 —— 补在 HookRecorder 里。
        // 设置窗里改了音频相关的项 → 立刻生效并刷新那几个按钮（Ui.ShowSettings 里调）
        public void UiApplyAudioSettings()
        {
            try { ApplyBgmSetting(true); } catch { }
            try { ApplyGameAudioSetting(); } catch { }
            try { RefreshSoundButtons(); } catch { }
            try { RefreshGameAudioButton(); } catch { }
            try { RefreshBgmButton(); } catch { }
        }

        public void UiOnSelectionChanged()
        {
            try
            {
                if (suppressSelSync) return;
                if (Ui.SelectedJobCount > 0) pinnedBatch.Clear();   // 用户重新选了 → 之前的绿色标记作废
                SyncParallelCountToSelection();
            }
            catch { }
        }

        // 旧版 MainForm.OnFormClosing 的移植（2026-10-05 补）。
        // ⚠ 移植时整块漏了 —— `closing` 这个字段搬过来了却没人用，编译器都报了 CS0414
        //   （"已赋值但从未使用"），当时的收尾没注意到那句话。
        //   后果：录到一半点右上角 ✕，进程直接退，OBS 在后台**一直录下去**；
        //   之后所有 SetVideoSettings / StartRecord 都会失败（交接文档 §十 记过这条）。
        // 返回 true = 现在可以关；false = 已经拦下来去做收尾，窗口先别关（收尾完自己再关一次）。
        public bool UiOnClosing()
        {
            try { cancel = true; } catch { }        // 让正在跑的那一轮尽快收手
            if (!closing && worker != null && worker.IsAlive)
            {
                closing = true;
                Ui.Status("正在停止录制并收尾…");
                Log.Write("关闭窗口：先停止录制并收尾…");
                Thread t = new Thread(delegate()
                {
                    try { worker.Join(20000); } catch { }
                    Ui.CloseMain();                 // 收尾完了再关（那时候 closing 已是 true，放行）
                });
                t.IsBackground = true;
                t.Start();
                return false;
            }
            try { cfg.Save(); } catch { }
            return true;
        }

        // 启动那一段（旧版写在 MainForm 的 Shown 里，顺序照搬）
        public void UiOnShown(string[] args)
        {
            try
            {
                cfg.Save();
                RefreshEnvLabel();
                RefreshSoundButtons();
                RefreshGameAudioButton();
                Ui.SetBusy(false);
                Ui.Status("就绪");
                RestoreSoundIfLeftMuted();
                Log.RotateOnStart();
                Log.Write("==== Rep → MP4 录制器启动 ====");
                Log.Write("界面：WPF 版（旧界面仍可用：加 --old 启动）");   // 日志轮转之后写，这一行才留得住
                Log.Write("游戏目录：" + (string.IsNullOrEmpty(cfg.GameDir) ? "<未找到>" : cfg.GameDir));
                Log.Write("OBS     ：" + (string.IsNullOrEmpty(cfg.ObsExe) ? "<未找到>" : cfg.ObsExe));
                SyncBgmFromIni();
                if (args != null && args.Length > 0)
                {
                    // 只入队，不自动开录 —— 2026-10-05 用户要求「拖入后自动开始」整条去掉，
                    // 一律由用户自己点「开始录制」。
                    AddPaths(args);
                }
                // 首次启动自检（2026-10-08）：新用户拿到包，双击之后必须当场知道缺什么。
                // 只在【有问题时】弹，而且一台机器只弹一次（EnvChecked）—— 环境正常的人永远看不到它，
                // 想看随时点「检查环境」，那个永远会弹。
                if (!cfg.EnvChecked)
                {
                    EnvReport rep0 = EnvReport.Build(cfg);
                    if (!rep0.AllOk)
                    {
                        Ui.ShowEnvDialog(rep0);
                        RefreshEnvLabel();   // 用户可能顺手在设置里把游戏目录指对了
                    }
                    cfg.EnvChecked = true;
                    cfg.Save();
                }
            }
            catch (Exception ex) { Log.Write("启动初始化出错：" + ex); }
        }

        // 影子控件（对应旧版 MainForm 里的那几个字段；名字一模一样，逻辑那边不用改）
        readonly UiButton btnStart = new UiButton("btnStart");
        readonly UiButton btnStop = new UiButton("btnStop");
        readonly UiButton btnSound = new UiButton("btnSound");
        readonly UiButton btnGameAudio = new UiButton("btnGameAudio");
        readonly UiButton btnBgm = new UiButton("btnBgm");
        readonly UiLabel lblParHint = new UiLabel("lblParHint");
        readonly Settings cfg;
        readonly List<Job> jobs = new List<Job>();
        readonly object jobsLock = new object();
        Thread worker;
        volatile bool cancel;
        volatile bool titleWatch = false;   // 并行录制期间盯着窗口标题，防止游戏改回去导致三格抢同一窗口
        ObsClient obs = new ObsClient();
        string captureSource = null;    // 采集源名字
        string captureScene = null;     // 用来截图自检的场景名
        bool captureSourceIsOurs = false;
        string originalScene = null;
        string originalRecordDir = null;
        bool closing = false;
        int currentMethod = -1;   // 当前用的捕获方式
        int blackStreak = 0;      // 连续检测到黑屏的次数
        bool gameMutedByUs = false;      // 录制期间是否由我们静音了游戏声音
        bool weMutedSystem = false;      // 系统静音是不是我们按的（录完要自动恢复）
        double jobMaxGameLevel = 0;      // 本次录制中游戏音频的最大电平（音频自检用）
        string queueSaveFolder = null;   // 本队列统一保存到的文件夹
        bool queueSaveDecided = false;   // 本队列是否已经问过/决定过
        const string RecSceneName = "RepRecorder 录制场景";
        const string RecSourceName = "RepRecorder 游戏窗口";
        const string AudioSourceName = "RepRecorder 游戏声音";

        // ---- moved from MainForm ----
        string FindExistingOutput(Job job)
        {
            try
            {
                if (!string.IsNullOrEmpty(job.Output) && File.Exists(job.Output)) return job.Output;
                string[] exts = new string[] { ".mp4", ".mkv", ".flv", ".mov" };
                foreach (string e in exts)
                {
                    string p = Path.ChangeExtension(job.Rep, e);
                    if (File.Exists(p)) return p;
                }
                if (!string.IsNullOrEmpty(cfg.LastSaveFolder) && Directory.Exists(cfg.LastSaveFolder))
                {
                    string baseName = Path.GetFileNameWithoutExtension(job.Rep);
                    foreach (string e in exts)
                    {
                        string p = Path.Combine(cfg.LastSaveFolder, baseName + e);
                        if (File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            return null;
        }
        // ---- moved from MainForm ----
        IntPtr WaitForMainWindow(Process p, int seconds)
        {
            DateTime end = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < end)
            {
                Thread.Sleep(400);
                try
                {
                    p.Refresh();
                    if (p.HasExited) return IntPtr.Zero;
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                }
                catch { return IntPtr.Zero; }
            }
            return IntPtr.Zero;
        }
        // ---- moved from MainForm ----
        void MoveWindowClientTo(IntPtr h, int x, int y)
        {
            RECT wr, cr; POINT pt;
            if (!GetWindowRect(h, out wr) || !GetClientRect(h, out cr)) return;
            pt.X = 0; pt.Y = 0;
            if (!ClientToScreen(h, ref pt)) return;
            int dx = pt.X - wr.Left;
            int dy = pt.Y - wr.Top;
            SetWindowPos(h, IntPtr.Zero, x - dx, y - dy, wr.Right - wr.Left, wr.Bottom - wr.Top, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        // ---- moved from MainForm ----
        bool ForceAllWindowTitles(List<IntPtr> handles, List<string> wants, int waitSeconds)
        {
            try
            {
                int n = handles.Count;
                string[] last = new string[n];
                int[] stable = new int[n];
                DateTime end = DateTime.Now.AddSeconds(waitSeconds);
                while (DateTime.Now < end)
                {
                    bool allStable = true;
                    for (int i = 0; i < n; i++)
                    {
                        if (handles[i] == IntPtr.Zero) continue;
                        StringBuilder sb = new StringBuilder(512);
                        GetWindowText(handles[i], sb, 512);
                        string cur = sb.ToString();
                        if (cur == last[i]) stable[i]++;
                        else { stable[i] = 0; last[i] = cur; }
                        if (stable[i] < 3) allStable = false;
                    }
                    if (allStable) break;
                    Thread.Sleep(400);
                }
                for (int round = 0; round < 15; round++)
                {
                    for (int i = 0; i < n; i++)
                        if (handles[i] != IntPtr.Zero) SetWindowText(handles[i], wants[i]);
                    Thread.Sleep(300);
                    bool all = true;
                    for (int i = 0; i < n; i++)
                    {
                        if (handles[i] == IntPtr.Zero) continue;
                        StringBuilder sb = new StringBuilder(512);
                        GetWindowText(handles[i], sb, 512);
                        if (sb.ToString() != wants[i]) { all = false; break; }
                    }
                    if (all && round >= 2) return true;
                }
                return false;
            }
            catch { return false; }
        }
        // ---- moved from MainForm ----
        static string MakeWindowString(string title, string cls)
        {
            return (title == null ? "" : title) + ":" + (cls == null ? "" : cls) + ":th123.exe";
        }
        // ---- moved from MainForm ----
        void EnsureInput(string name, string kind, Dictionary<string, object> settings)
        {
            List<object> inputs = ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]);
            foreach (object o in inputs)
            {
                Dictionary<string, object> m = (Dictionary<string, object>)o;
                if (Convert.ToString(m["inputName"]) == name) return;
            }
            Dictionary<string, object> req = new Dictionary<string, object>();
            req["sceneName"] = RecSceneName;
            req["inputName"] = name;
            req["inputKind"] = kind;
            req["inputSettings"] = settings;
            req["sceneItemEnabled"] = true;
            obs.Request("CreateInput", req);
            Log.Write("并行：已创建源 " + name);
        }
        // ---- moved from MainForm ----
        static Dictionary<string, object> AudioInputSettings(string window)
        {
            Dictionary<string, object> a = new Dictionary<string, object>();
            a["window"] = window;
            a["priority"] = 1;
            return a;
        }
        // ---- moved from MainForm ----
        static Dictionary<string, object> VideoInputSettings(string window)
        {
            Dictionary<string, object> v = new Dictionary<string, object>();
            v["window"] = window;
            v["method"] = 2;          // WGC
            v["client_area"] = true;
            v["cursor"] = false;
            v["priority"] = 1;
            return v;
        }
        // ---- moved from MainForm ----
        void ApplyCellWindow(Dictionary<string, string> applied, string vName, string aName, string window)
        {
            string old;
            if (applied.TryGetValue(vName, out old) && old == window) return;
            SetInputSettingsRaw(vName, VideoInputSettings(window));
            SetInputSettingsRaw(aName, AudioInputSettings(window));
            applied[vName] = window;
            applied[aName] = window;
            // 走到这里说明窗口串真的变了（= OBS 会重建采集设备，画面会闪一帧黑）；
            // 窗口串没变时上面已经 return，不会闪 —— 这就是"第 9~10 秒一帧黑"的由来与修法。
            Log.Write("并行：重设采集源绑定 " + vName + " → " + window);
        }
        // ---- moved from MainForm ----
        void SetInputSettingsRaw(string inputName, Dictionary<string, object> settings)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["inputName"] = inputName;
            r["inputSettings"] = settings;
            obs.Request("SetInputSettings", r);
        }
        // ---- moved from MainForm ----
        void SetTransformRaw(string scene, int itemId, int x, int y, int cw, int ch)
        {
            if (itemId < 0) return;
            Dictionary<string, object> t = new Dictionary<string, object>();
            t["positionX"] = (double)x;
            t["positionY"] = (double)y;
            t["scaleX"] = 1.0;
            t["scaleY"] = 1.0;
            // 同样用边界框：源还没解析出尺寸时也能正确铺满格子
            t["boundsType"] = cw > 0 && ch > 0 ? "OBS_BOUNDS_SCALE_INNER" : "OBS_BOUNDS_NONE";
            t["boundsAlignment"] = 5;
            if (cw > 0) t["boundsWidth"] = (double)cw;
            if (ch > 0) t["boundsHeight"] = (double)ch;
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["sceneName"] = scene;
            r["sceneItemId"] = itemId;
            r["sceneItemTransform"] = t;
            obs.Request("SetSceneItemTransform", r);
        }
        // ---- moved from MainForm ----
        void SetSceneItemEnabled(string scene, int itemId, bool enabled)
        {
            if (itemId < 0) return;
            try
            {
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["sceneName"] = scene;
                r["sceneItemId"] = itemId;
                r["sceneItemEnabled"] = enabled;
                obs.Request("SetSceneItemEnabled", r);
            }
            catch (Exception ex) { Log.Write("并行：切换元素显示状态失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void SetTransformFitCell(string scene, int itemId, int x, int y, int cw, int ch, int idx)
        {
            if (itemId < 0) { Log.Write("并行：第 " + idx + " 格元素没找到（itemId<0），位置没设上"); return; }
            try
            {
                Dictionary<string, object> t = new Dictionary<string, object>();
                t["positionX"] = (double)x;
                t["positionY"] = (double)y;
                t["scaleX"] = 1.0;
                t["scaleY"] = 1.0;
                t["boundsType"] = "OBS_BOUNDS_SCALE_INNER";   // 等比缩放到正好装进边界框
                t["boundsAlignment"] = 5;                     // 对齐到左上
                t["boundsWidth"] = (double)cw;
                t["boundsHeight"] = (double)ch;
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["sceneName"] = scene;
                req["sceneItemId"] = itemId;
                req["sceneItemTransform"] = t;
                obs.Request("SetSceneItemTransform", req);
                // 回读一次，记下 OBS 报的源尺寸（排查用；不影响摆放）
                double sw = 0, sh = 0;
                try
                {
                    Dictionary<string, object> q2 = new Dictionary<string, object>();
                    q2["sceneName"] = scene;
                    q2["sceneItemId"] = itemId;
                    Dictionary<string, object> r2 = obs.Request("GetSceneItemTransform", q2);
                    if (r2.ContainsKey("sceneItemTransform"))
                    {
                        Dictionary<string, object> t2 = (Dictionary<string, object>)r2["sceneItemTransform"];
                        if (t2.ContainsKey("sourceWidth")) sw = Convert.ToDouble(t2["sourceWidth"]);
                        if (t2.ContainsKey("sourceHeight")) sh = Convert.ToDouble(t2["sourceHeight"]);
                    }
                }
                catch { }
                Log.Write("并行：第 " + idx + " 格摆到 (" + x + "," + y + ")，缩放到 " + cw + "×" + ch);
            }
            catch (Exception ex) { Log.Write("并行：第 " + idx + " 格设置位置失败 " + ex.Message); }
        }
        // ---- moved from MainForm ----
        void SetAudioTracksRaw(string inputName, int trackNo)
        {
            Dictionary<string, object> tracks = new Dictionary<string, object>();
            for (int k = 1; k <= 6; k++) tracks[k.ToString()] = (k == trackNo);
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["inputName"] = inputName;
            r["inputAudioTracks"] = tracks;
            obs.Request("SetInputAudioTracks", r);
        }
        // ---- moved from MainForm ----
        void SetInputMuteRaw(string inputName, bool muted)
        {
            try
            {
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["inputName"] = inputName;
                s["inputMuted"] = muted;
                obs.Request("SetInputMute", s);
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void SetProfileParamRaw(string cat, string name, string val)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["parameterCategory"] = cat;
            r["parameterName"] = name;
            r["parameterValue"] = val;
            obs.Request("SetProfileParameter", r);
        }
        // ---- moved from MainForm ----
        // internal（不是 private）：EnvReport 要问它 ffmpeg 在不在，好把结论一起报给用户。
        internal static string FindFfmpeg()
        {
            List<string> cands = new List<string>();
            try { cands.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")); } catch { }
            try
            {
                cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       @"Microsoft\WinGet\Links\ffmpeg.exe"));
            }
            catch { }
            try
            {
                string pkg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          @"Microsoft\WinGet\Packages");
                if (Directory.Exists(pkg))
                    foreach (string d in Directory.GetDirectories(pkg, "Gyan.FFmpeg*"))
                        foreach (string b in Directory.GetDirectories(d, "ffmpeg*"))
                            cands.Add(Path.Combine(b, @"bin\ffmpeg.exe"));
            }
            catch { }
            foreach (string c in cands) { try { if (File.Exists(c)) return c; } catch { } }
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (path != null)
                    foreach (string d in path.Split(';'))
                    {
                        if (d.Trim().Length == 0) continue;
                        string p = Path.Combine(d.Trim(), "ffmpeg.exe");
                        if (File.Exists(p)) return p;
                    }
            }
            catch { }
            return null;
        }
        // ---- moved from MainForm ----
        bool SplitOne(string grid, Job job, int idx, int cw, int ch, int x, int y, double startSec, double maxSeconds, out string err)
        {
            err = "";
            string ff = FindFfmpeg();
            if (ff == null) { err = "找不到 ffmpeg"; return false; }
            string target = Path.ChangeExtension(job.Rep, ".mp4");
            string enc = string.IsNullOrEmpty(cfg.ParallelSplitEncoder) ? "h264_nvenc" : cfg.ParallelSplitEncoder;
            // 编码器 + 各自合适的参数，并准备好"换编码器重试"的梯子：
            // 别人的电脑不一定有 NVIDIA 显卡（h264_nvenc 用不了）→ 自动退到 libx264 软件编码，保证一定成功。
            // 注意：nvenc 的 -rc vbr / -cq 参数 libx264 不认，所以每档要带自己的参数。
            List<string> encArgs = new List<string>();
            if (enc == "libx264")
            {
                encArgs.Add(" -c:v libx264 -preset medium -crf " + cfg.ParallelSplitCQ + " -pix_fmt yuv420p");
            }
            else
            {
                encArgs.Add(" -c:v " + enc + " -preset p5 -tune hq -rc vbr -cq " + cfg.ParallelSplitCQ + " -b:v 0 -pix_fmt yuv420p");
                encArgs.Add(" -c:v libx264 -preset medium -crf " + cfg.ParallelSplitCQ + " -pix_fmt yuv420p");   // 没有 N 卡时的兜底
            }
            if (startSec < 0) startSec = 0;
            double dur = maxSeconds - startSec;   // 这格真正要的时长
            string seek = (startSec > 0.2)
                ? " -ss " + startSec.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : "";
            string baseArgs = "-hide_banner -y" + seek + " -i \"" + grid + "\" -map 0:v:0";
            // 往里裁一圈再缩放：并行时相邻格的采集源是"亚像素"缩放摆的（边界框），格与格之间会漏
            // 1~3 像素的邻居画面（亮）或画布黑边（暗）进来；整边裁就会把那条线带进成品。
            // 用户实测：成品最右 2~3 像素亮度 43 → 77 的断崖。往里裁 6 个成品像素后断崖消失（最大跳变 15 → 3.7）。
            int insetOut = cfg.ParallelEdgeInset;
            if (insetOut < 0) insetOut = 0;
            int outW = cfg.CanvasWidth > 0 ? cfg.CanvasWidth : 1440;
            int inX = outW > 0 ? (int)Math.Round((double)insetOut * cw / outW) : 0;
            int inY = cw > 0 ? (int)Math.Round((double)inX * ch / cw) : 0;
            if (inX * 2 >= cw - 16) inX = 0;      // 裁太多就别裁了（防呆）
            if (inY * 2 >= ch - 16) inY = 0;
            int cw2 = cw - inX * 2, ch2 = ch - inY * 2;
            if (inX > 0)
                Log.Write("并行切分：每格往里裁 " + inX + " 像素（格子坐标）≈ 成品 " + insetOut + " 像素，" +
                          "裁后 " + cw2 + "×" + ch2 + " 再缩放到 " + outW + "×" + (cfg.CanvasHeight > 0 ? cfg.CanvasHeight : 1080));
            string scale = "";
            try
            {
                if (cfg.CanvasWidth > cw || cfg.CanvasHeight > ch)
                    // 像素点采样（neighbor）而不是 lanczos：游戏是 640×480 的像素画，
                    // 用 lanczos 放大到 1440×1080 会整体发糊（用户实测：和别人的录像一比"清晰程度差很多"）。
                    // neighbor 保留硬边像素块，和游戏本身/别人录出来的观感一致。
                    scale = ",scale=" + cfg.CanvasWidth + ":" + cfg.CanvasHeight + ":flags=lanczos";   // 回到 9/27 22:55 那版的放大方式（用户指定）
            }
            catch { }
            // 结尾淡出：并行切分本来就要重编码，加这个不额外花钱，避免"最后一帧硬切"的观感
            string fadeV = "", fadeA = "";
            if (dur > 1.5)
            {
                string st = (dur - 0.6).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                fadeV = ",fade=t=out:st=" + st + ":d=0.6";
                fadeA = " -af afade=t=out:st=" + st + ":d=0.6";
            }
            string vfCore = " -filter:v crop=" + cw2 + ":" + ch2 + ":" + (x + inX) + ":" + (y + inY) + scale + fadeV;
            string trim = (dur > 10)
                ? " -t " + dur.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                : "";
            // ⚠ afade 必须走 -af（以前拼成 ",afade=..." 直接跟在输出参数后面，ffmpeg 解析失败 →
            //    前两次尝试都失败、退化成第三次的 -an（无声），于是并行成品全都没声音。已修）
            string aa = " -c:a aac -b:a 192k" + fadeA + trim;
            List<string> attempts = new List<string>();
            foreach (string e in encArgs)
            {
                attempts.Add(baseArgs + " -map 0:a:" + idx + vfCore + e + aa);
                attempts.Add(baseArgs + vfCore + e + aa);              // 音轨不够时退化成不带音频
            }
            attempts.Add(baseArgs + vfCore + encArgs[0] + " -an" + trim);   // 最后兜底：无声
            int attemptNo = 0;
            foreach (string a in attempts)
            {
                attemptNo++;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(ff, a + " \"" + target + "\"");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WorkingDirectory = Path.GetDirectoryName(ff);
                    Process p = Process.Start(psi);
                    p.WaitForExit();
                    if (p.ExitCode == 0 && File.Exists(target) && new FileInfo(target).Length > 4096)
                    {
                        if (attemptNo > 1)
                            Log.Write("并行切分：第 " + attemptNo + " 种参数才成功（"
                                      + (attemptNo > 2 ? "多半没 NVIDIA 显卡，已换 libx264 软编" : "退化成不带音频") + "）");
                        return true;
                    }
                    err = "ffmpeg 退出码 " + p.ExitCode;
                    if (p.ExitCode != 0 && attemptNo == 1 && encArgs.Count > 1)
                        Log.Write("并行切分：" + enc + " 失败（多半没对应显卡），改用 libx264 软编重试…");
                }
                catch (Exception ex) { err = ex.Message; }
            }
            return false;
        }
        // ---- moved from MainForm ----
        bool SourceLooksStatic(string sourceName)
        {
            try
            {
                byte[] a = ShotOf(sourceName);
                Thread.Sleep(800);
                byte[] b = ShotOf(sourceName);
                if (a == null || b == null) return false;
                if (a.Length != b.Length) return false;
                int limit = a.Length / 100 + 16;
                int diff = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] != b[i]) { diff++; if (diff > limit) return false; }
                }
                return true;
            }
            catch { return false; }
        }
        // ---- moved from MainForm ----
        void CaptureOriginalRecordDir()
        {
            if (originalRecordDir != null) return;
            try { originalRecordDir = Convert.ToString(obs.Request("GetRecordDirectory", null)["recordDirectory"]); }
            catch { }
        }
        // ---- moved from MainForm ----
        void RunParallel()
        {
            int want = cfg.ParallelCount;
            if (want < 1) want = 1;
            if (want > 6) want = 6;   // 上限 6：OBS 只有 6 条音轨，再多每格就没声音了

            // 「录制选中的」：先按选中的列表跑，每批 want 个
            List<Job> sel = selectedBatch;
            selectedBatch = null;
            if (sel != null)
            {
                if (sel.Count == 0) return;
                Log.Write("==== 选中批次开始：共 " + sel.Count + " 个，每批 " + want + " 个 ====");
                forceRecord = true;
                try
                {
                    for (int off = 0; off < sel.Count; off += want)
                    {
                        if (cancel) { Log.Write("已停止：剩下的选中项不再录"); break; }
                        int take = Math.Min(want, sel.Count - off);
                        Log.Write("---- 第 " + (off / want + 1) + " 批（" + take + " 个）----");
                        obsStuckRetried = false;
                        RunParallelBatch(sel.GetRange(off, take));
                    }
                }
                finally { forceRecord = false; }
                Log.Write("==== 选中批次全部结束 ====");
                return;
            }

            List<Job> batch = new List<Job>();
            lock (jobsLock)
            {
                foreach (Job j in jobs)
                {
                    if (j.Status == "等待") { batch.Add(j); if (batch.Count >= want) break; }
                }
            }
            if (batch.Count == 0)
            {
                Log.Write("没有待录制的 rep（都录过了？点「刷新」看看）");
                return;
            }
            obsStuckRetried = false;
            RunParallelBatch(batch);
        }
        // 某个窗口所在显示器的 DPI 缩放（1.0 = 100%）。用来把 GetClientRect 的物理像素
        // 换算成逻辑像素 —— 见 RunParallelBatch 里量窗口尺寸那段。
        //
        // ⚠ 这里**不能**用 GetDpiForWindow。2026-10-05 用一个"假装是游戏"的 DPI 不感知探针
        //   实测过：那个窗口自己看到的是 640×480、GetDpiForWindow 报 96；
        //   从感知进程去问同一个窗口，GetDpiForWindow 仍然报 96 —— 拿它去除等于没除，
        //   画布还是 1920×1440，掉帧照旧。
        //   GetDpiForMonitor 返回的是显示器真实 DPI（300% 屏 = 288），这才对。
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);

        static double WindowDpiScale(IntPtr hwnd)
        {
            try
            {
                IntPtr mon = MonitorFromWindow(hwnd, 2);   // MONITOR_DEFAULTTONEAREST
                uint dx, dy;
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0, out dx, out dy) == 0
                    && dx >= 48 && dx <= 480)
                    return dx / 96.0;
            }
            catch { }   // 老系统没有 shcore.dll 就是这个 catch
            return 1.0;
        }

        // 某个"cols 列 × rows 行"的排法下，每格最多能占 OBS 画布上限的几成（1.0 = 不用缩）。
        // 见 RunParallelBatch 里挑排法的那段。
        static double GridScaleToFit(int cols, int rows, int cw, int ch, int maxCanvas)
        {
            double s = 1.0;
            if ((long)cols * cw > maxCanvas) s = Math.Min(s, (double)maxCanvas / ((double)cols * cw));
            if ((long)rows * ch > maxCanvas) s = Math.Min(s, (double)maxCanvas / ((double)rows * ch));
            return s;
        }

        // ---- moved from MainForm ----
        void RunParallelBatch(List<Job> batch)
        {
            // 哪几格自愈完还是黑的 —— 切分完要把它们标成「注意」而不是「完成」。
            // 黑格子照样能切出一个 mp4（内容是整段全黑），以前无条件写「完成」，用户会以为录好了
            // （2026-10-08 查实，2026-10-09 用户拍板改成「注意」）。
            // ⚠ 必须声明在方法最外层：用它的那处在切分线程里，比自检那段深 4 层花括号，
            //   声明在里层的话编译器直接报「当前上下文中不存在名称 blackCells」。
            bool[] blackCells = new bool[8];
            if (batch.Count == 0) return;
            // 在上一批的收尾里按了「停止」→ 这一批根本别开。
            // （批与批之间本来就有检查，这里是再兜一道，保证"停止"之后绝不会再冒出下一批）
            if (cancel) { Log.Write("已停止：这一批不再开始"); return; }
            // 降级成单录时，也要把采集源准备好并切到录制场景（单录流程自己不做这些）
            if (batch.Count == 1)
            {
                Log.Write("这一批只有 1 个，改成普通单录");
                if (cfg.AutoPrepareObs) PrepareObs(false);
                if (cfg.AutoPrepareObs && cfg.UseDedicatedScene) SwitchScene(true);
                CaptureOriginalRecordDir();
                // ⚠ 单录这条路以前漏了 ApplyAudioRouting：默认「只录游戏声音」时 OBS 桌面音频没被静音，
                //   系统里的其他声音会混进录像。这里补上，录完在 finally 里恢复（原本走 RunQueue 的
                //   那段恢复代码因为 parallelRequested 恒为 true 早就不执行了）。
                try { ApplyAudioRouting(true); } catch { }
                try { ProcessJob(batch[0]); }
                finally { try { ApplyAudioRouting(false); } catch { } }
                return;
            }
            if (!cfg.AutoPrepareObs) { Log.Write("并行录制需要打开「自动准备采集源并绑定游戏窗口」，已中止"); return; }

            // 队列里的「用时」要靠 Started/Ended 相减：并行这条路径以前从没设过 Started，
            // 结果列表里显示成 (现在 - 0001年) 那种天文数字（63926114748s）
            DateTime batchStart = DateTime.Now;
            for (int i = 0; i < batch.Count; i++) batch[i].Started = batchStart;
            Ui.ScrollToBatch(batch);   // 新的一批开始了，把列表挪过去看一眼（之后整批都不再动它）

            // OBS 最多 6 条音轨：超过 6 格时每格的声音没法分开，整批录成无声，避免出现"画面是这局、声音是那局"
            bool cellAudio = cfg.RecordGameAudio && batch.Count <= 6;
            if (batch.Count > 6 && cfg.RecordGameAudio)
                Log.Write("⚠ 同时录 " + batch.Count + " 个超过 6 条音轨上限，这一批会是无声视频");

            Log.Write("==== 并行录制开始：同时录 " + batch.Count + " 个 ====");
            List<Process> games = new List<Process>();
            List<Process> games2 = new List<Process>();   // 带 rep 重启后的那些实例
            List<string> vNames = new List<string>();
            List<string> aNames = new List<string>();
            List<int> cellItemIds = new List<int>();      // 每格画面元素在场景里的 itemId（绑完窗口再定位置）
            Dictionary<string, string> appliedWin = new Dictionary<string, string>();   // 已经设过的窗口串（一样就跳过重设，避免闪黑帧）
            int stdItemId = -1;                           // 单文件用的采集源元素（并行期间要藏起来）
            bool stdWasEnabled = true;
            DateTime recordStart = DateTime.MinValue;     // 开录时刻（用来算每格实际录了多久）
            double[] cellSec = null;
            double[] cellHead = null;
            double[] cellLastSeen = null;
            string gridFile = null;
            bool parMutedByUs = false;                    // 这一批是不是我们静音了系统输出（收尾要恢复）
            int CW = 0, CH = 0;
            int cols = 1, rows = 1;   // 画布网格（格子多了要折行，见下面）
            int origW = 0, origH = 0, origFps = 0;
            string origTracks = null;

            try
            {
                PrepareObs(false);
                // ★ 每一批开始前，把上一批可能残留的 P1~P6 采集源彻底删掉、音轨全部归零。
                //   轮录会连着跑很多批；上一批的源只要没删干净，它就会带着旧的「窗口绑定 + 音轨」
                //   活到这一批里 —— 表现就是"声音/画面跟别的 rep 串了"（用户实测）。
                for (int ci = 1; ci <= 6; ci++)
                {
                    string[] nms = new string[] { "RepRecorder P" + ci + " 画面", "RepRecorder P" + ci + " 声音" };
                    foreach (string nm in nms)
                    {
                        try { SetAudioTracksRaw(nm, 0); } catch { }   // 先摘掉音轨，残留源就算还在也没声音
                        try
                        {
                            Dictionary<string, object> rr = new Dictionary<string, object>();
                            rr["inputName"] = nm;
                            obs.Request("RemoveInput", rr);
                        }
                        catch { }
                    }
                }
                Log.Write("并行：已清理上一批可能残留的 P1~P6 采集源并把音轨归零");
                try
                {
                    Dictionary<string, object> v = obs.Request("GetVideoSettings", null);
                    origW = Convert.ToInt32(v["baseWidth"]);
                    origH = Convert.ToInt32(v["baseHeight"]);
                    if (v.ContainsKey("fpsNumerator") && v["fpsNumerator"] != null) origFps = Convert.ToInt32(v["fpsNumerator"]);
                }
                catch { }
                try
                {
                    Dictionary<string, object> q = new Dictionary<string, object>();
                    q["parameterCategory"] = "SimpleOutput";
                    q["parameterName"] = "RecTracks";
                    Dictionary<string, object> r = obs.Request("GetProfileParameter", q);
                    origTracks = r.ContainsKey("parameterValue") ? Convert.ToString(r["parameterValue"]) : "1";
                }
                catch { }
                try
                {
                    Dictionary<string, object> rd = new Dictionary<string, object>();
                    rd["recordDirectory"] = Path.GetDirectoryName(batch[0].Rep);
                    obs.Request("SetRecordDirectory", rd);
                }
                catch { }
                ApplyAudioRouting(true);   // 静音 OBS 桌面音频，避免把系统声音混进来
                CaptureOriginalRecordDir();

                // ★ 把「单文件录制」用的那套采集源在并行期间藏起来 + 静音。
                //   不这么做会出两个问题（实测）：
                //   1) 它的声音默认落在第 1 条音轨上 —— 会混进第 1 格的成品里，听起来像"两局的声音一起放"；
                //   2) 它的画面元素铺满整块画布（在格子底下）—— 某局先结束后，格子里没画面了，
                //      它就从空出来的地方透出来，看起来像"把另一局的画面接到了这个视频后面"。
                try
                {
                    stdItemId = CleanupDuplicateItems(RecSceneName, RecSourceName);
                    if (stdItemId >= 0)
                    {
                        try
                        {
                            Dictionary<string, object> q0 = new Dictionary<string, object>();
                            q0["sceneName"] = RecSceneName;
                            q0["sceneItemId"] = stdItemId;
                            stdWasEnabled = Convert.ToBoolean(obs.Request("GetSceneItemEnabled", q0)["sceneItemEnabled"]);
                        }
                        catch { }
                        SetSceneItemEnabled(RecSceneName, stdItemId, false);
                    }
                    SetInputMuteRaw(AudioSourceName, true);
                    Log.Write("并行：已把单文件用的采集源「" + RecSourceName + "」隐藏、并把「" + AudioSourceName + "」静音"
                              + "（否则画面/声音会漏进格子）");
                }
                catch (Exception ex) { Log.Write("并行：隐藏单文件采集源失败（可忽略）：" + ex.Message); }
                cellSec = new double[batch.Count];
                cellHead = new double[batch.Count];     // 这一格"采集到画面"的时刻（剪掉开头的黑屏）
                cellLastSeen = new double[batch.Count]; // 这一格"最后一次看到画面"的时刻（剪掉结尾的黑屏）

                // 准备阶段中途按了「停止」→ 别再往下开了（否则游戏照开、录像照起）
                if (cancel)
                {
                    Log.Write("已停止：这一批在准备阶段被中止，不会启动游戏、不会开录");
                    return;
                }

                // ===== 只开一次游戏 =====
                // 以前是"先空参数开 N 个实例停在标题画面 → 摆好源 → 全部杀掉 → 开录 → 再带 rep 重启"，
                // 等于把游戏开两遍（用户实测：这两个动作重复了两次，纯属浪费时间）。
                // 现在改成：带 rep 启动 → 等窗口出现（此时游戏还在读盘/黑屏）→ 量出窗口尺寸、
                // 摆好画布 → 开录 → 建源绑定。开播前那几秒黑屏由切分时按"采集到画面的时刻"剪掉。
                List<IntPtr> handles2 = new List<IntPtr>();
                List<string> wants = new List<string>();
                for (int i = 0; i < batch.Count; i++)
                {
                    Ui.SetJob(batch[i], "启动游戏", "并行 " + (i + 1) + "/" + batch.Count);
                    Process np = null;
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(cfg.GameDir, "th123.exe"),
                                                                    "\"" + batch[i].Rep + "\"");
                        psi.WorkingDirectory = cfg.GameDir;
                        np = Process.Start(psi);
                        Log.Write("并行：第 " + (i + 1) + " 个实例已启动 → " + Path.GetFileName(batch[i].Rep));
                    }
                    catch (Exception ex) { Log.Write("并行：第 " + (i + 1) + " 个实例启动失败 " + ex.Message); }
                    games2.Add(np);
                    wants.Add("th123 #" + (i + 1));
                    Thread.Sleep(400);
                }
                for (int i = 0; i < games2.Count; i++)
                {
                    if (games2[i] == null) { handles2.Add(IntPtr.Zero); continue; }
                    IntPtr h2 = WaitForMainWindow(games2[i], 90);
                    handles2.Add(h2);
                    if (h2 == IntPtr.Zero)
                    {
                        Log.Write("并行：第 " + (i + 1) + " 个实例的窗口没出现（这一格会是空的）");
                        // ⚠ 这一格【也会是黑的】，而且黑屏自检根本抓不到它 —— 自检看的是
                        //   `games2[i] 活着 && 采集源画面全黑`，而窗口没出现时采集源压根没绑上、
                        //   截图直接失败，IsBlank 拿不到东西 → 返回 false → 一格都不报。
                        //   2026-10-09 用假 th123.exe 实测出来的：日志明明写着"这一格会是空的"，
                        //   最后那两格却还是被标成「完成」。用户最早报的"游戏没开、那个 rep 不见了"
                        //   就是这种情况。所以这里必须**直接**把这一格记成坏格。
                        blackCells[i] = true;
                    }
                }
                // 窗口尺寸（每格按游戏客户区大小排，保持 4:3 不变形）
                for (int i = 0; i < handles2.Count; i++)
                {
                    if (handles2[i] == IntPtr.Zero) continue;
                    RECT cr;
                    if (GetClientRect(handles2[i], out cr) && cr.Right > 0 && cr.Bottom > 0 && CW == 0)
                    {
                        // ⚠ 必须换算成**逻辑尺寸**再用。
                        //   非想天则是 DPI 不感知的老程序，Windows 会把它的 640×480 画面**位图拉伸**
                        //   到物理尺寸再显示（300% 屏上就是 1920×1440）。按物理尺寸采等于把"已经糊掉的
                        //   放大图"再存一遍：像素多 9 倍、一点真细节都没有，还把编码器直接压垮。
                        //   2026-10-05 实测：画布 3640×4095 时 OBS 报「编码丢帧 98.1%」，
                        //   而老版本（同一段逻辑、但进程 DPI 不感知，量到的就是 640×480）
                        //   画布 1920×960，丢帧 0.2%。
                        //   —— 也就是说：老版本一直是对的，是 WPF 版设了 DPI 感知之后这里才量错的。
                        double sc = WindowDpiScale(handles2[i]);
                        CW = (int)Math.Round(cr.Right / sc);
                        CH = (int)Math.Round(cr.Bottom / sc);
                        if (CW > 1920 || CH > 1440) { CW = 640; CH = 480; }   // 异常大就按游戏原生尺寸
                        // 每格不需要比"切分后的输出尺寸"更大 —— 切分那一步反正会缩到
                        // CanvasWidth×CanvasHeight。这同时也是一道保险：万一上面 DPI 那一步
                        // 没测准（或者哪天换个游戏窗口特别大），画布也不会失控到把编码器压垮。
                        int capW = cfg.CanvasWidth > 0 ? cfg.CanvasWidth : 1440;
                        int capH = cfg.CanvasHeight > 0 ? cfg.CanvasHeight : 1080;
                        if (CW > capW || CH > capH)
                        {
                            double k = Math.Min((double)capW / CW, (double)capH / CH);
                            int nw = Math.Max(2, (int)Math.Floor(CW * k));
                            int nh = Math.Max(2, (int)Math.Floor(CH * k));
                            Log.Write("并行：游戏窗口 " + CW + "×" + CH + " 比输出尺寸 " + capW + "×" + capH
                                      + " 还大，每格按 " + nw + "×" + nh + " 采（切分后反正要缩到这个尺寸）");
                            CW = nw; CH = nh;
                        }
                    }
                }
                if (CW <= 0 || CH <= 0) { CW = 640; CH = 480; }

                // 网格排布：让中继画布尽量方正（6 格 = 3×2 = 1920×960，而不是 3840×480 那种 8:1 的长条，
                // 后者在 OBS 预览里看着就像"画面没摆好"）。
                // ⚠ OBS 画布上限 4096 —— 光靠"换列数"是躲不掉的。2026-10-05 用户的
                //   并行+轮录一次全废（20 个 rep，前 18 个每批都报
                //   「SetVideoSettings 失败：402 baseWidth 超过 4096」）根因就在这段：
                //   6 格 × 1920×1440 无论怎么排都超（3×2 = 5760×2880、2×3 = 3840×4320），
                //   而旧代码只会写一句警告然后照样往下走 → 整批报废。
                //   （旧版 WinForms 是 DPI 不感知的，读到的是 640×480 逻辑像素，
                //    6 格 = 3×2 = 1920×960 正好放得下，所以"老版本是好的"。详见 MakeDpiAware。）
                //   现在：装不下就把**每格等比缩小**（保持长宽比，不然游戏会被拉变形），
                //   并且在所有列数里挑"每格能留得最大"的那种排法。
                //   上面改成逻辑尺寸之后，这里基本不会再触发（6 格 640×480 = 1920×960），
                //   留着当安全网：万一哪天游戏窗口真的很大，宁可缩格子也不能让整批报废。
                {
                    int maxCanvas = 4096;
                    cols = (int)Math.Ceiling(Math.Sqrt(batch.Count * (double)CH / CW));
                    if (cols < 1) cols = 1;
                    rows = (batch.Count + cols - 1) / cols;
                    double sc = GridScaleToFit(cols, rows, CW, CH, maxCanvas);
                    if (sc < 1.0)
                    {
                        double best = -1; int bc = cols, br = rows;
                        for (int c = 1; c <= batch.Count; c++)
                        {
                            int r = (batch.Count + c - 1) / c;
                            double t = GridScaleToFit(c, r, CW, CH, maxCanvas);
                            bool better = t > best + 1e-9
                                       || (Math.Abs(t - best) <= 1e-9 && Math.Abs(c - r) < Math.Abs(bc - br));
                            if (better) { best = t; bc = c; br = r; }
                        }
                        cols = bc; rows = br; sc = best;
                    }
                    if (sc < 1.0)
                    {
                        int nw = Math.Max(2, (int)Math.Floor(CW * sc));
                        int nh = Math.Max(2, (int)Math.Floor(CH * sc));
                        Log.Write("并行：中继画布 " + (cols * CW) + "×" + (rows * CH) + " 放不进 OBS 上限 "
                                  + maxCanvas + "，每格缩到 " + nw + "×" + nh);
                        CW = nw; CH = nh;
                    }
                    if (rows * CH > maxCanvas || cols * CW > maxCanvas)
                        Log.Write("⚠ 并行：画布 " + (cols * CW) + "×" + (rows * CH) + " 仍超过 OBS 上限 " + maxCanvas +
                                  "，可能会失败（把「同时录制数量」调小一点）");
                    Dictionary<string, object> nv = new Dictionary<string, object>();
                    nv["baseWidth"] = cols * CW;
                    nv["baseHeight"] = rows * CH;
                    nv["outputWidth"] = cols * CW;
                    nv["outputHeight"] = rows * CH;
                    nv["fpsNumerator"] = 60;
                    nv["fpsDenominator"] = 1;
                    obs.Request("SetVideoSettings", nv);
                    Log.Write("并行：每格画面 " + CW + "×" + CH + "，" + cols + " 列 × " + rows + " 行，中继画布 " +
                              (cols * CW) + "×" + (rows * CH));
                }

                // 标题必须在我们开录前就改成唯一值：OBS 的采集源按标题匹配窗口，
                // 而且游戏自己在启动约 2 秒后会把标题改回中文版 —— 改晚了采集源就抓不到窗口（整段黑）。
                bool titleOk = ForceAllWindowTitles(handles2, wants, 20);
                Log.Write("并行：窗口标题改成唯一值 " + (titleOk ? "成功" : "失败（可能有多格录到同一个游戏）"));
                for (int i = 0; i < handles2.Count; i++)
                    if (handles2[i] != IntPtr.Zero) MoveWindowClientTo(handles2[i], (i % 5) * 80, (i / 5) * 60);

                // 先把每格的采集源建出来、音轨分好（都不依赖窗口，放在开录前更稳：录制中再新增源会让编码抖一下）。
                // 绑定窗口放到开录之后（那时才知道游戏窗口，而且黑屏由切分时剪掉）。
                for (int i = 0; i < batch.Count; i++)
                {
                    string vName = "RepRecorder P" + (i + 1) + " 画面";
                    string aName = "RepRecorder P" + (i + 1) + " 声音";
                    try
                    {
                        EnsureInput(vName, "window_capture", VideoInputSettings(""));
                        EnsureInput(aName, "wasapi_process_output_capture", AudioInputSettings(""));
                        vNames.Add(vName); aNames.Add(aName);
                        int itemId = CleanupDuplicateItems(RecSceneName, vName);
                        cellItemIds.Add(itemId);
                        SetTransformRaw(RecSceneName, itemId, (i % cols) * CW, (i / cols) * CH, CW, CH);
                        CleanupDuplicateItems(RecSceneName, aName);
                        SetAudioTracksRaw(aName, batch.Count <= 6 ? i + 1 : 0);
                        SetInputMuteRaw(aName, !cellAudio);
                    }
                    catch (Exception ex) { Log.Write("并行：第 " + (i + 1) + " 格建源失败 " + ex.Message); }
                }
                SetProfileParamRaw("SimpleOutput", "RecTracks", ((1 << Math.Min(batch.Count, 6)) - 1).ToString());
                try
                {
                    foreach (string n in Program.FindMicTargets(obs))
                    {
                        // ⚠ 静音之前【必须】先把原来的状态记进 micOriginalMute。
                        //   这里图省事直接 SetInputMute，等于绕过了 ApplyMicMode —— 而
                        //   micOriginalMute 只在 ApplyMicMode(true) 里填。并行这条路从来不调它，
                        //   于是收尾时 ApplyMicMode(false) 遍历的是一张空表，**一个都没恢复**：
                        //   并行录完，用户 OBS 里的麦克风就一直是哑的（2026-10-08 查实；
                        //   OBS 会把静音状态存进场景集合，重启 OBS 也还是哑的）。
                        try
                        {
                            Dictionary<string, object> q = new Dictionary<string, object>();
                            q["inputName"] = n;
                            bool was = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]);
                            if (!micOriginalMute.ContainsKey(n)) micOriginalMute[n] = was;
                        }
                        catch { }
                        Dictionary<string, object> s = new Dictionary<string, object>();
                        s["inputName"] = n; s["inputMuted"] = true;
                        obs.Request("SetInputMute", s);
                    }
                }
                catch { }

                // 开录前先确认 OBS 没有"卡住的录像"：并行这条路以前完全不做这一步，
                // 一旦 OBS 处于录制/半停止状态，StartRecord 返回成功但实际什么都没录，
                // 结果等 3 分钟、StopRecord 报 501、拿不到文件 —— 用户实测就是这样白录一批。
                StopObsRecordingIfAny();

                recordStart = DateTime.Now;
                obs.Request("StartRecord", null);
                // 再确认一次"真的在录"，不在就重试一次，还不在就直接报错（绝不白等）
                bool reallyRecording = false;
                for (int attempt = 0; attempt < 2 && !reallyRecording; attempt++)
                {
                    Thread.Sleep(1500);
                    try
                    {
                        Dictionary<string, object> rs = obs.Request("GetRecordStatus", null);
                        reallyRecording = rs.ContainsKey("outputActive") && rs["outputActive"] != null &&
                                          Convert.ToBoolean(rs["outputActive"]);
                    }
                    catch { }
                    if (!reallyRecording && attempt == 0)
                    {
                        Log.Write("并行：StartRecord 之后 OBS 说没在录，重试一次…");
                        try { obs.Request("StartRecord", null); } catch (Exception ex) { Log.Write("并行：重试 StartRecord 失败：" + ex.Message); }
                    }
                }
                if (!reallyRecording)
                {
                    Log.Write("并行：❌ OBS 没真正开录（outputActive=false）。可能：有残留录像 / OBS 卡住 / 录制目录不可写");
                    // 自动救一次：杀掉卡住的 OBS → 重新拉起 → 重建场景/采集源 → 把这一批完整重跑一遍。
                    // （用户实测就是这样白录一批的；能自动救回来最好，救不回来也明确报错，不再白等。）
                    if (!obsStuckRetried)
                    {
                        obsStuckRetried = true;
                        Log.Write("并行：正在自动重启 OBS 并把这批重跑一次（第 1 次也是唯一一次自动重试）…");
                        try
                        {
                            foreach (Process g in games2) { try { if (g != null && !g.HasExited) g.Kill(); } catch { } }
                            KillGame();
                            try { obs.Dispose(); } catch { }
                            foreach (Process o in Process.GetProcessesByName("obs64")) { try { o.Kill(); } catch { } }
                            Thread.Sleep(4000);
                            if (EnsureObs())
                            {
                                if (cfg.AutoPrepareObs) PrepareObs(false);
                                if (cfg.AutoPrepareObs && cfg.UseDedicatedScene) SwitchScene(true);
                                Log.Write("并行：OBS 已重启并重新准备好，开始重跑这一批…");
                                RunParallelBatch(batch);
                                return;
                            }
                            Log.Write("并行：重启 OBS 失败");
                        }
                        catch (Exception ex) { Log.Write("并行：自动重启 OBS 出错：" + ex.Message); }
                    }
                    foreach (Job j in batch) Ui.SetJob(j, "失败", "OBS 没有真正开始录像（已中止，避免白等）");
                    return;
                }
                Log.Write("并行录制：已开始录像（" + batch.Count + " 格，已确认 OBS 真的在录）");

                // 把每格绑到各自的游戏窗口，并按源的真实尺寸摆进格子
                List<string> boundStrings = new List<string>();
                for (int i = 0; i < batch.Count && i < vNames.Count; i++)
                {
                    try
                    {
                        string ws = null;
                        if (handles2[i] != IntPtr.Zero)
                        {
                            StringBuilder tb = new StringBuilder(512);
                            GetWindowText(handles2[i], tb, 512);
                            string tt = tb.ToString();
                            // ⚠ 标题读空/被游戏改回去时，宁可用我们自己强制设的 "th123 #N"。
                            // 空标题拼出来的窗口串是 ":th123_110a:th123.exe" → OBS 匹配不到窗口 → 那一格全黑
                            //（用户实测：6 格里左边第一个全黑，日志就是 "→ :th123_110a:th123.exe"）。
                            if (tt.Length == 0 || tt.IndexOf("th123 #", StringComparison.Ordinal) < 0)
                            {
                                tt = "th123 #" + (i + 1);
                                Log.Write("并行：第 " + (i + 1) + " 格窗口标题读不到/不对，改用强制标题「" + tt + "」绑定");
                            }
                            ws = MakeWindowString(tt, "th123_110a");
                        }
                        if (ws != null)
                        {
                            boundStrings.Add(ws);
                            ApplyCellWindow(appliedWin, vNames[i], aNames[i], ws);
                        }
                        SetTransformFitCell(RecSceneName, (i < cellItemIds.Count ? cellItemIds[i] : -1),
                                            (i % cols) * CW, (i / cols) * CH, CW, CH, i + 1);
                        if (batch.Count <= 6) SetAudioTracksRaw(aNames[i], i + 1);
                        SetInputMuteRaw(aNames[i], !cellAudio);
                        Log.Write("并行：第 " + (i + 1) + " 格绑定窗口串 = " + (ws == null ? "<窗口没出现>" : ws));
                    }
                    catch (Exception ex) { Log.Write("并行：第 " + (i + 1) + " 格绑定失败 " + ex.Message); }
                }
                int uniq = new HashSet<string>(boundStrings).Count;
                if (uniq < boundStrings.Count)
                    Log.Write("⚠ 并行：有 " + (boundStrings.Count - uniq + 1) + " 个格子绑到了同一个窗口（"
                              + string.Join(" | ", boundStrings.ToArray()) + "）—— 录出来会是同一场对局");
                Log.Write("并行：建源并绑定完成（" + boundStrings.Count + " 格，" + uniq + " 个不同窗口）");
                // 诊断：把每格音源的「静音 + 音轨」读回来打一行。万一以后又"声音串味"，
                // 这行日志能直接看出是哪一格混进了别的音轨（用户反馈过这个问题，当时没日志可查）。
                for (int i = 0; i < aNames.Count; i++)
                {
                    try
                    {
                        Dictionary<string, object> q2 = new Dictionary<string, object>();
                        q2["inputName"] = aNames[i];
                        bool mm = false;
                        try { mm = Convert.ToBoolean(obs.Request("GetInputMute", q2)["inputMuted"]); } catch { }
                        string tr = "";
                        try
                        {
                            Dictionary<string, object> trs = obs.Request("GetInputAudioTracks", q2);
                            if (trs.ContainsKey("inputAudioTracks"))
                            {
                                Dictionary<string, object> dd = trs["inputAudioTracks"] as Dictionary<string, object>;
                                if (dd != null)
                                    for (int k = 1; k <= 6; k++)
                                        tr += (dd.ContainsKey(k.ToString()) && Convert.ToBoolean(dd[k.ToString()]) ? "1" : "0");
                            }
                        }
                        catch { }
                        Log.Write("并行：第 " + (i + 1) + " 格音源状态 —— 静音=" + (mm ? "是" : "否") +
                                  "  音轨=" + (tr == "" ? "?" : tr) + "（应只有第 " + (i + 1) + " 位是 1）");
                    }
                    catch { }
                }
                Log.Write("并行：游戏声音进录像 = " + (cellAudio
                    ? "开（每格一条音轨）"
                    : (cfg.RecordGameAudio ? "关（超过 6 格，音轨不够分）" : "关（音源已静音）")));
                if (!cellAudio)
                {
                    // 超过 6 格时把标准采集源也静音，否则它会把其中一局的声音漏进所有格子
                    SetInputMuteRaw(AudioSourceName, true);
                    Log.Write("并行：已把「" + AudioSourceName + "」也静音，保证这批确实是无声的");
                }

                // 录制期间盯着窗口标题：游戏加载完/开播时会把标题改回去，一改采集源就抓不到窗口。
                // 1 秒一次改回来（这条看守越及时，开头那段黑屏越短、也就越不会切到对局中间）。
                titleWatch = true;
                Thread wt = new Thread(delegate()
                {
                    while (titleWatch && !cancel)
                    {
                        for (int i = 0; i < handles2.Count; i++)
                        {
                            if (handles2[i] == IntPtr.Zero) continue;
                            try
                            {
                                StringBuilder cb = new StringBuilder(512);
                                GetWindowText(handles2[i], cb, 512);
                                string wantTitle = "th123 #" + (i + 1);
                                if (cb.ToString() != wantTitle) SetWindowText(handles2[i], wantTitle);
                            }
                            catch { }
                        }
                        Thread.Sleep(1000);
                    }
                });
                wt.IsBackground = true;
                wt.Start();

                // 等游戏真的开播、标题稳定之后再把绑定刷一次：
                // OBS 的「应用程序音频采集」按窗口标题解析进程，而且只在设置变更时解析一次，
                // 解析那一刻若有两个同名窗口就会锁到同一个进程（两格声音完全一样）。
                Thread.Sleep(6000);
                for (int i = 0; i < games2.Count && i < vNames.Count; i++)
                {
                    if (games2[i] == null || handles2[i] == IntPtr.Zero) continue;
                    try
                    {
                        StringBuilder rb = new StringBuilder(512);
                        GetWindowText(handles2[i], rb, 512);
                        string tt3 = rb.ToString();
                        // 同上：标题空掉/不对就用强制标题，绝不拿空标题去绑（否则那格全黑）
                        if (tt3.Length == 0 || tt3.IndexOf("th123 #", StringComparison.Ordinal) < 0)
                            tt3 = "th123 #" + (i + 1);
                        string ws3 = MakeWindowString(tt3, "th123_110a");
                        ApplyCellWindow(appliedWin, vNames[i], aNames[i], ws3);
                        if (batch.Count <= 6) SetAudioTracksRaw(aNames[i], i + 1);
                        SetInputMuteRaw(aNames[i], !cellAudio);
                    }
                    catch { }
                }
                Log.Write("并行：开播后已再刷新一次采集绑定（音频按各格自己的窗口重新解析）");
                for (int i = 0; i < vNames.Count && i < games2.Count; i++)
                {
                    try { if (games2[i] == null || games2[i].HasExited) continue; } catch { continue; }
                    if (SourceLooksStatic(vNames[i]))
                        Log.Write("并行：第 " + (i + 1) + " 格画面看起来还是静止的（可能在读盘/开场，不影响整场录制）");
                }
                // 每格"是不是黑屏"自检 + 自愈：发现黑的就重新强制标题、重新绑一次采集源。
                // （用户实测：6 格里第一格整场全黑，就是窗口标题空掉、OBS 匹配不到窗口造成的。）
                for (int round = 0; round < 6; round++)
                {
                    Thread.Sleep(3000);
                    bool anyBlack = false;
                    for (int i = 0; i < vNames.Count && i < games2.Count; i++)
                    {
                        try
                        {
                            if (games2[i] == null || games2[i].HasExited) continue;
                            if (!IsBlank(ShotOf(vNames[i]))) continue;
                            anyBlack = true;
                            string fixT = "th123 #" + (i + 1);
                            if (handles2[i] != IntPtr.Zero) { try { SetWindowText(handles2[i], fixT); } catch { } }
                            appliedWin.Remove(vNames[i]);   // 强制重设一次（绕过"没变就跳过"）
                            ApplyCellWindow(appliedWin, vNames[i], aNames[i], MakeWindowString(fixT, "th123_110a"));
                            if (batch.Count <= 6) SetAudioTracksRaw(aNames[i], i + 1);
                            Log.Write("并行：第 " + (i + 1) + " 格画面是黑的 → 已重新绑定窗口（第 " + (round + 1) + " 次检查）");
                        }
                        catch { }
                    }
                    if (!anyBlack) break;
                }
                for (int i = 0; i < vNames.Count && i < games2.Count; i++)
                {
                    try
                    {
                        if (games2[i] == null || games2[i].HasExited) continue;
                        if (IsBlank(ShotOf(vNames[i])))
                        {
                            Log.Write("并行：⚠ 第 " + (i + 1) + " 格一直黑屏，很可能没录上 —— 建议单独重录");
                            blackCells[i] = true;
                        }
                    }
                    catch { }
                }

                DateTime t0 = DateTime.Now;
                bool timeout = false;
                while (!cancel)
                {
                    Thread.Sleep(1000);
                    bool allExited = true;
                    for (int i = 0; i < games2.Count; i++)
                    {
                        try
                        {
                            if (games2[i] == null || games2[i].HasExited)
                            {
                                // 记下这一格是什么时候结束的：切分时按它裁剪，
                                // 免得某局先结束后，视频后面跟着一大段黑屏/别人的画面
                                if (cellSec != null && i < cellSec.Length && cellSec[i] <= 0 && recordStart != DateTime.MinValue)
                                {
                                    cellSec[i] = (DateTime.Now - recordStart).TotalSeconds;
                                    if (cellSec[i] > 10)
                                        Log.Write("并行：第 " + (i + 1) + " 格对局结束（录了 " + cellSec[i].ToString("0") + " 秒），"
                                                  + "切分时按这个长度裁掉后面多余的部分");
                                }
                                continue;
                            }
                            allExited = false;
                        }
                        catch { }
                    }
                    // 各格"画面到底有没有出现"：切分时按第一帧真画面剪掉开头的黑、按最后一帧剪掉结尾的黑。
                    if (recordStart != DateTime.MinValue && cellHead != null && vNames.Count > 0)
                    {
                        for (int i = 0; i < games2.Count && i < vNames.Count; i++)
                        {
                            try
                            {
                                if (games2[i] == null || games2[i].HasExited) continue;
                                double sec = (DateTime.Now - recordStart).TotalSeconds;
                                bool blank = IsBlank(ShotOf(vNames[i]));
                                if (!blank)
                                {
                                    if (cellHead[i] <= 0) cellHead[i] = sec;
                                    cellLastSeen[i] = sec;
                                }
                            }
                            catch { }
                        }
                    }
                    if (allExited) break;
                    // 需求：并行录制也做「窗口被最小化 → 自动还原」（和单录一样的保护）。
                    // 最小化后 Windows 不再为那个窗口做桌面合成，那一格就会拍成黑屏。
                    if (cfg.KeepGameCapturable)
                    {
                        for (int i = 0; i < games2.Count; i++)
                        {
                            try
                            {
                                if (games2[i] == null || games2[i].HasExited) continue;
                                games2[i].Refresh();
                                IntPtr wh = games2[i].MainWindowHandle;
                                if (wh == IntPtr.Zero || !IsIconic(wh)) continue;
                                ShowWindow(wh, SW_RESTORE);
                                minimizedFixCount++;
                                Log.Write("并行：⚠ 第 " + (i + 1) + " 格窗口被最小化（会录成黑屏）—— 已还原（第 "
                                          + minimizedFixCount + " 次）");
                            }
                            catch { }
                        }
                    }
                    if ((DateTime.Now - t0).TotalMinutes > cfg.MaxMinutes) { timeout = true; break; }
                    TimeSpan el = DateTime.Now - t0;
                    string txt = "并行中 " + ((int)el.TotalMinutes) + ":" + el.Seconds.ToString("00");
                    foreach (Job j in batch) Ui.SetJob(j, "录制中", txt);
                }
                titleWatch = false;
                if (timeout)
                {
                    Log.Write("并行录制超过 " + cfg.MaxMinutes + " 分钟上限，强制结束游戏");
                    foreach (Process g in games2) { try { if (g != null && !g.HasExited) g.Kill(); } catch { } }
                }
                Thread.Sleep(2000);
                try
                {
                    Dictionary<string, object> resp = obs.Request("StopRecord", null);
                    if (resp.ContainsKey("outputPath")) gridFile = Convert.ToString(resp["outputPath"]);
                }
                catch (Exception ex) { Log.Write("并行：停止录像失败 " + ex.Message); }
                // 兜底：StopRecord 失败（例如 OBS 报 501）时，自己去录像目录里找刚录出来的那个文件。
                // 以前这里直接放弃 → 用户看到的就是"录完 6 个什么都没拿到、直接报错"。
                if (string.IsNullOrEmpty(gridFile) || !File.Exists(gridFile))
                {
                    try
                    {
                        string dir = null;
                        try { dir = Convert.ToString(obs.Request("GetRecordDirectory", null)["recordDirectory"]); } catch { }
                        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetDirectoryName(batch[0].Rep);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            FileInfo newest = null;
                            foreach (string f in Directory.GetFiles(dir))
                            {
                                string ext = Path.GetExtension(f).ToLower();
                                if (ext != ".mp4" && ext != ".mkv" && ext != ".flv" && ext != ".mov" && ext != ".ts") continue;
                                FileInfo fi = new FileInfo(f);
                                if (recordStart != DateTime.MinValue && fi.LastWriteTime < recordStart.AddSeconds(-30)) continue;
                                if (fi.Length < 1024 * 1024) continue;   // 太小的不是这次录的
                                if (newest == null || fi.LastWriteTime > newest.LastWriteTime) newest = fi;
                            }
                            if (newest != null)
                            {
                                gridFile = newest.FullName;
                                Log.Write("并行：StopRecord 没给文件，但在录像目录里找到了刚录的：" + newest.Name);
                            }
                            else Log.Write("并行：录像目录里找不到刚录的文件：" + dir + "（多半是 OBS 没在录）");
                        }
                    }
                    catch (Exception ex2) { Log.Write("并行：找合并文件失败：" + ex2.Message); }
                }
                Log.Write("并行录制结束，合并文件：" + gridFile);
            }
            catch (Exception ex) { Log.Write("并行录制出错：" + ex.Message); }
            finally
            {
                // ⚠ 异常收尾兜底：正常路径要到 L7138/L7147 才停「标题看守」和 StopRecord；
                //   中途一旦抛异常，看守线程会一直给（可能已被系统回收复用的）窗口改名，
                //   而且 OBS 会一直录下去、文件越写越大。这里统一兜住。
                try { titleWatch = false; } catch { }
                try
                {
                    if (obs != null && obs.IsConnected)
                    {
                        Dictionary<string, object> rs = obs.Request("GetRecordStatus", null);
                        if (rs.ContainsKey("outputActive") && Convert.ToBoolean(rs["outputActive"]))
                        {
                            obs.Request("StopRecord", null);
                            Log.Write("并行：异常收尾时发现 OBS 仍在录像，已停止");
                        }
                    }
                }
                catch { }
                try { if (origW > 0) { Dictionary<string,object> ov = new Dictionary<string,object>();
                        ov["baseWidth"] = origW; ov["baseHeight"] = origH; ov["outputWidth"] = origW; ov["outputHeight"] = origH;
                        ov["fpsNumerator"] = origFps > 0 ? origFps : 60; ov["fpsDenominator"] = 1;
                        obs.Request("SetVideoSettings", ov);
                        Log.Write("并行：已还原 OBS 画布 " + origW + "×" + origH); } } catch { }
                try { SetProfileParamRaw("SimpleOutput", "RecTracks", string.IsNullOrEmpty(origTracks) ? "1" : origTracks); } catch { }
                foreach (string s in vNames) { try { obs.Request("RemoveInput", new Dictionary<string,object>{{"inputName",s}}); } catch { } }
                foreach (string s in aNames) { try { obs.Request("RemoveInput", new Dictionary<string,object>{{"inputName",s}}); } catch { } }
                try { ApplyAudioRouting(false); } catch { }
                // 把这一批开录前静音掉的麦克风恢复回去（静音在下面建源那段里做的）。
                // 不补这一句，并行录完麦克风就永久哑了 —— 见那段里的注释。
                try { RestoreInputMutes(micOriginalMute, "并行：已把麦克风恢复成录制前的状态"); } catch { }
                try { ApplyGameAudioSetting(); } catch { }   // 把标准采集源的静音状态按设置还原
                if (parMutedByUs)
                {
                    string mm;
                    if (GameAudio.SetSystemMute(false, out mm)) { parMutedByUs = false; Log.Write("并行：已恢复系统声音（" + mm + "）"); MarkSystemMuted(false); }
                }
                try { if (stdItemId >= 0) SetSceneItemEnabled(RecSceneName, stdItemId, stdWasEnabled); } catch { }   // 还原单文件采集源的显示状态
                try { if (cfg.UseDedicatedScene && obs.IsConnected) SwitchScene(false); } catch { }
                try { RestoreRecordDirectory(); } catch { }
                foreach (Process g in games) { try { if (!g.HasExited) g.Kill(); } catch { } }
                foreach (Process g in games2) { try { if (g != null && !g.HasExited) g.Kill(); } catch { } }
            }

            // 7) 切分
            if (gridFile != null && File.Exists(gridFile))
            {
                int okCount = 0;
                // 1) 先把每格的"开头/结尾该剪到第几秒"算好（这一步要抓 OBS 截图，串行做）
                double[] heads = new double[batch.Count];
                double[] ends = new double[batch.Count];
                for (int i = 0; i < batch.Count; i++)
                {
                    double head = 0;
                    try
                    {
                        double h = AnalyzeCellHead(gridFile, (i % cols) * CW, (i / cols) * CH, CW, CH);
                        if (h > 0.2) head = h - 0.15;
                        Log.Write("并行切分（第 " + (i + 1) + " 格）：画面从第 " + h.ToString("0.0") +
                                  " 秒开始 → 开头剪掉 " + head.ToString("0.0") + " 秒");
                    }
                    catch { }
                    double end = (cellSec != null && i < cellSec.Length) ? cellSec[i] : 0;
                    if (cellLastSeen != null && i < cellLastSeen.Length && cellLastSeen[i] > 0)
                    {
                        double seen = cellLastSeen[i] + 0.25;
                        if (end <= 0 || seen < end) end = seen;
                    }
                    if (head > 0 && end > 0)
                        Log.Write("并行切分（第 " + (i + 1) + " 格）：保留 " + head.ToString("0.0") + " ~ " +
                                  end.ToString("0.0") + " 秒（共 " + (end - head).ToString("0.0") + " 秒）");
                    if (end > 0 && end - head < 5) end = 0;
                    heads[i] = head;
                    ends[i] = end;
                }
                // 2) 并发切分：同一时间跑 SplitParallel 个 ffmpeg（默认 3），切分总时间大约除以这个数。
                //    以前是一格一格串行切，6 格要等 5~6 遍。
                int conc = cfg.SplitParallel;
                if (conc < 1) conc = 1;
                if (conc > 6) conc = 6;
                if (conc > batch.Count) conc = batch.Count;
                if (batch.Count > 1) Log.Write("并行切分：同时跑 " + conc + " 个 ffmpeg（共 " + batch.Count + " 格）");
                object splitGate = new object();
                int nextCell = 0;
                List<Thread> splitThreads = new List<Thread>();
                for (int w = 0; w < conc; w++)
                {
                    Thread st = new Thread(delegate()
                    {
                        while (true)
                        {
                            int i;
                            lock (splitGate) { i = nextCell; nextCell++; }
                            if (i >= batch.Count) return;
                            string err;
                            if (SplitOne(gridFile, batch[i], i, CW, CH, (i % cols) * CW, (i / cols) * CH,
                                         heads[i], ends[i], out err))
                            {
                                batch[i].Output = Path.ChangeExtension(batch[i].Rep, ".mp4");
                                long outBytes = 0;
                                try { outBytes = new FileInfo(batch[i].Output).Length; } catch { }
                                batch[i].Size = outBytes;
                                batch[i].VideoSeconds = Mp4Duration(batch[i].Output);
                                batch[i].Ended = DateTime.Now;
                                // 整场全黑的那几格标「注意」：文件是有的、也能播，但很可能没录上。
                                // 标「完成」等于骗人 —— 用户得自己点开看才知道是黑的。
                                Ui.SetJob(batch[i], blackCells[i] ? "注意" : "完成",
                                          (outBytes / 1048576.0).ToString("0.0") + " MB（并行切分）"
                                          + (blackCells[i] ? "  ⚠ 这一格整场全黑，建议单独重录" : ""));
                                Log.Write("并行切分完成（第 " + (i + 1) + " 格）：" + Path.GetFileName(batch[i].Output) +
                                          "  " + (outBytes / 1048576.0).ToString("0.0") + " MB  视频时长 " + FmtDur(batch[i].VideoSeconds));
                                lock (splitGate) okCount++;
                            }
                            else
                            {
                                Ui.SetJob(batch[i], "失败", "切分失败");
                                Log.Write("并行切分失败（第 " + (i + 1) + " 格）：" + err + "；合并文件保留在 " + gridFile);
                            }
                        }
                    });
                    st.IsBackground = true;
                    st.Start();
                    splitThreads.Add(st);
                }
                foreach (Thread st in splitThreads) { try { st.Join(); } catch { } }
                if (okCount == batch.Count)
                {
                    try { File.Delete(gridFile); Log.Write("并行：已删除中间合并文件（切分全部成功）"); } catch { }
                }
                else Log.Write("并行：有切分失败，中间合并文件保留在 " + gridFile + "（可用 ffmpeg 手动裁）");

                // 录完问用户要把视频放哪 —— 并行这条路以前从来不问（用户实测就是在等这个弹窗）
                if (okCount > 0)
                {
                    if (!queueSaveDecided)
                    {
                        string choice = AskSaveLocationDialog(batch[0], okCount);
                        if (!string.IsNullOrEmpty(choice) && choice != "keep")
                            foreach (Job j in batch)
                            {
                                if (j.Output == null || !File.Exists(j.Output)) continue;
                                string moved = MoveOutputTo(j, choice);
                                if (moved != null) { j.Output = moved; j.Size = new FileInfo(moved).Length; Ui.SetJob(j, "完成", "已放到 " + Path.GetDirectoryName(moved)); }
                            }
                    }
                    else if (!string.IsNullOrEmpty(queueSaveFolder))
                    {
                        foreach (Job j in batch)
                        {
                            if (j.Output == null || !File.Exists(j.Output)) continue;
                            string moved = MoveOutputTo(j, queueSaveFolder);
                            if (moved != null) { j.Output = moved; j.Size = new FileInfo(moved).Length; Ui.SetJob(j, "完成", "已放到 " + queueSaveFolder); }
                        }
                    }
                }
            }
            else
            {
                Log.Write("并行录制：没有拿到合并视频文件，无法切分");
                foreach (Job j in batch) Ui.SetJob(j, "失败", "并行录制没拿到文件");
            }
            Log.Write("==== 并行录制结束 ====");
        }
        // ---- moved from MainForm ----
        void StartRecording()
        {
            // ⚠ 游戏目录不对就别开录（2026-10-08 查实）：以前这里什么都不拦，
            //   结果每个游戏实例启动失败、录出一整批黑视频，**而且任务还标成「完成」** ——
            //   用户以为录好了。宁可不录，也要当场说清楚。
            if (cfg == null || string.IsNullOrEmpty(cfg.GameDir) || !File.Exists(Path.Combine(cfg.GameDir, "th123.exe")))
            {
                Log.Write("游戏目录不对（" + (cfg == null || string.IsNullOrEmpty(cfg.GameDir) ? "空" : cfg.GameDir) +
                          "），先配好再录");
                Ui.Status("游戏目录不对 —— 先点「检查环境」");
                try { Ui.ShowEnvDialog(EnvReport.Build(cfg)); RefreshEnvLabel(); } catch { }
                return;
            }
            // 并行（同时录 >=2 个）每一格都要调 ffmpeg 去切分；缺了它整批都会「切分失败」。
            // 别让用户等到录完才发现 —— 开录前就弹窗，让他当场补。
            if (cfg.ParallelCount >= 2 && FindFfmpeg() == null)
            {
                Log.Write("没有 ffmpeg —— 并行录制需要它，先把环境补齐");
                Ui.Status("缺 ffmpeg，并行录制需要它");
                try { Ui.ShowEnvDialog(EnvReport.Build(cfg)); RefreshEnvLabel(); } catch { }
                if (FindFfmpeg() == null) return;
            }
            List<Job> sel = new List<Job>();
            bool manualSel = false;
            // ★ 轮录优先：只要勾了「轮录」，就【从队列第一个 rep 开始数】，按每批 N 个一路录完，
            //   完全忽略当前的蓝色选中 / 绿色标记 —— 否则"选中下面 6 个、上面留 2 个"就会从第 3 个开始算
            //   （用户实测的 bug）。不勾轮录时，选中/标记仍然是"只录这几个"。
            bool round = cfg.RoundRecord;
            lock (jobsLock)
            {
                if (round)
                {
                    foreach (Job j in jobs) if (j.Status == "等待") sel.Add(j);   // 队列顺序 = 列表顺序
                }
                else
                {
                    foreach (Job it in Ui.SelectedJobs())
                    {
                        Job j = it;
                        if (j != null && !sel.Contains(j)) sel.Add(j);
                    }
                    if (sel.Count == 0 && pinnedBatch != null && pinnedBatch.Count > 0)
                        foreach (Job pj in pinnedBatch) if (!sel.Contains(pj)) sel.Add(pj);
                }
                manualSel = !round && sel.Count > 0;
                if (round && sel.Count == 0)
                {
                    Log.Write("轮录：没有待录的 rep（都录过了？点「刷新」或选中几行要重录的）");
                    return;
                }
                if (!round && sel.Count == 0 && jobs.Count == 0) { Log.Write("队列是空的，先把 rep 拖进来"); return; }
                if (round) pinnedBatch.Clear();   // 轮录不看标记，顺手清干净，免得界面上还绿着
            }
            cfg.FillDefaults();
            int per = Math.Max(1, Math.Min(6, cfg.ParallelCount));

            int pending = 0;
            lock (jobsLock) foreach (Job j in jobs) if (j.Status == "等待") pending++;

            if (round)
            {
                Log.Write("==== 轮录开始：共 " + sel.Count + " 个，每批 " + per + " 个"
                          + (per == 1 ? "（依次单录）" : "") + "，从队列最前面数 ====");
            }

            // 需求 0：保存位置提前问，选好了再开录。
            // 2026-10-05 用户要求：**不论单录 / 并行 / 并行+轮录，一律先问**，
            // 不再受设置里那个「AskSaveLocation」开关和「以后不再询问」的影响
            // （以前点过一次「以后不再询问」就再也不问了，用户找不到地方改回来）。
            {
                int n = manualSel ? sel.Count : (round ? sel.Count : pending);
                if (!AskSaveLocationUpFront(n)) return;
            }

            if (sel.Count > 0)
            {
                selectedBatch = sel;
                if (manualSel) forceRecord = true;    // 明确选中 → 忽略「已有录像就跳过」（一律重录）
                if (manualSel)
                    Log.Write("开始录制：你选中了 " + sel.Count + " 个，同时录 " + per + " 个" +
                              (sel.Count > per ? "，分 " + ((sel.Count + per - 1) / per) + " 批" : "") + "（选中的一律重录）");
            }
            else
            {
                selectedBatch = null;
                if (pending == 0) { Log.Write("没有待录制的 rep（都录过了？点「刷新」重新检查，或先在列表里选中要重录的）"); return; }
                Log.Write("开始录制：待录 " + pending + " 个，同时录 " + per + " 个"
                          + (pending > per ? "（只录这一批，想全录完请勾「轮录」）" : "")
                          + (pending == 1 ? "（只有 1 个，走单录）" : ""));
            }
            parallelRequested = true;
            Ui.Post(delegate { btnStart.Enabled = false; btnStop.Enabled = true; });
            StartQueue();
        }
        // ---- moved from MainForm ----
        static double Mp4Duration(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    long len = fs.Length;
                    byte[] b8 = new byte[8];
                    long pos = 0;
                    while (pos + 8 <= len)
                    {
                        fs.Position = pos;
                        if (fs.Read(b8, 0, 8) != 8) break;
                        long size = ((long)b8[0] << 24) | ((long)b8[1] << 16) | ((long)b8[2] << 8) | b8[3];
                        string type = Encoding.ASCII.GetString(b8, 4, 4);
                        long hdr = 8;
                        if (size == 1)
                        {
                            byte[] e = new byte[8];
                            if (fs.Read(e, 0, 8) != 8) break;
                            size = 0; for (int k = 0; k < 8; k++) size = (size << 8) | e[k];
                            hdr = 16;
                        }
                        else if (size == 0) size = len - pos;
                        if (size < hdr) break;
                        if (type == "moov")
                        {
                            long end = Math.Min(pos + size, len);
                            long p = pos + hdr;
                            while (p + 8 <= end)
                            {
                                fs.Position = p;
                                if (fs.Read(b8, 0, 8) != 8) break;
                                long sz = ((long)b8[0] << 24) | ((long)b8[1] << 16) | ((long)b8[2] << 8) | b8[3];
                                string t2 = Encoding.ASCII.GetString(b8, 4, 4);
                                long h2 = 8;
                                if (sz == 1)
                                {
                                    byte[] e = new byte[8];
                                    if (fs.Read(e, 0, 8) != 8) break;
                                    sz = 0; for (int k = 0; k < 8; k++) sz = (sz << 8) | e[k];
                                    h2 = 16;
                                }
                                else if (sz == 0) sz = end - p;
                                if (t2 == "mvhd")
                                {
                                    byte[] body = new byte[32];
                                    fs.Position = p + h2;
                                    int got = fs.Read(body, 0, 32);
                                    if (got < 20) return 0;
                                    if (body[0] == 1)
                                    {
                                        long ts = ((long)body[20] << 24) | ((long)body[21] << 16) | ((long)body[22] << 8) | body[23];
                                        long dur = 0; for (int k = 24; k < 32; k++) dur = (dur << 8) | body[k];
                                        return ts > 0 ? (double)dur / ts : 0;
                                    }
                                    long ts0 = ((long)body[12] << 24) | ((long)body[13] << 16) | ((long)body[14] << 8) | body[15];
                                    long dur0 = ((long)body[16] << 24) | ((long)body[17] << 16) | ((long)body[18] << 8) | body[19];
                                    return ts0 > 0 ? (double)dur0 / ts0 : 0;
                                }
                                if (sz <= 0) break;
                                p += sz;
                            }
                            return 0;
                        }
                        if (size <= 0) break;
                        pos += size;
                    }
                }
            }
            catch { }
            return 0;
        }
        // ---- moved from MainForm ----
        public static string FmtDur(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            long t = (long)Math.Round(seconds);
            long h = t / 3600, m = (t % 3600) / 60, s = t % 60;
            if (h > 0) return h + "小时" + m.ToString("00") + "分" + s.ToString("00") + "秒";
            if (m > 0) return m + "分" + s.ToString("00") + "秒";
            return s + "秒";
        }
        // ---- moved from MainForm ----
        bool EnsureObs()
        {
            string obsExe = Settings.FindObsExe();
            if (!string.IsNullOrEmpty(cfg.ObsExe) && File.Exists(cfg.ObsExe)) obsExe = cfg.ObsExe;
            if (string.IsNullOrEmpty(obsExe))
            {
                Log.Write("找不到 obs64.exe —— 先安装 OBS：  winget install --id OBSProject.OBSStudio -e");
                return false;
            }
            cfg.ObsExe = obsExe;

            if (Process.GetProcessesByName("obs64").Length == 0)
            {
                string wsMsg;
                Settings.EnsureWebSocketEnabled(out wsMsg);
                Log.Write(wsMsg);
                Log.Write("OBS 未运行，正在启动：" + obsExe);
                ProcessStartInfo psi = new ProcessStartInfo(obsExe, "--minimize-to-tray");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(obsExe);
                Process.Start(psi);
                Thread.Sleep(3000);
            }

            DateTime deadline = DateTime.Now.AddSeconds(60);
            Exception last = null;
            while (DateTime.Now < deadline)
            {
                if (cancel) return false;
                try
                {
                    obs.MetersEnabled = true;   // 订阅音频电平事件（用于录制时自检有没有声音）
                    obs.Connect(cfg.Port, cfg.Password);
                    return true;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(2000);
                }
            }
            Log.Write("连接 obs-websocket 失败：" + (last == null ? "超时" : last.Message));
            Log.Write("请检查：OBS → 工具 → WebSocket 服务器设置 → 启用（端口 " + cfg.Port + "）");
            return false;
        }
        // ---- moved from MainForm ----
        void PrepareObs(bool verbose)
        {
            try
            {
                StopObsRecordingIfAny();   // 上次被强杀留下的"没停的录像"要先停掉，否则后面全报 500

                if (cfg.AutoCanvas)
                {
                    Dictionary<string, object> v = new Dictionary<string, object>();
                    v["baseWidth"] = cfg.CanvasWidth;
                    v["baseHeight"] = cfg.CanvasHeight;
                    v["outputWidth"] = cfg.CanvasWidth;
                    v["outputHeight"] = cfg.CanvasHeight;
                    v["fpsNumerator"] = cfg.CanvasFps;
                    v["fpsDenominator"] = 1;
                    obs.Request("SetVideoSettings", v);
                    Log.Write("OBS 画布/输出已设为 " + cfg.CanvasWidth + "×" + cfg.CanvasHeight + "@" + cfg.CanvasFps);
                }

                // 记下原来的节目场景，录完要还回去
                try
                {
                    originalScene = Convert.ToString(obs.Request("GetSceneList", null)["currentProgramSceneName"]);
                }
                catch { originalScene = null; }

                List<object> inputs = ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]);

                if (!cfg.UseDedicatedScene)
                {
                    // 旧行为：用当前场景里已有的采集源（可能是显示器采集，会录到整个桌面）
                    string existing = null;
                    foreach (object o in inputs)
                    {
                        Dictionary<string, object> m = (Dictionary<string, object>)o;
                        string kind = Convert.ToString(m["unversionedInputKind"]);
                        if (kind == "game_capture" || kind == "window_capture" ||
                            kind == "monitor_capture" || kind == "screen_capture")
                        {
                            existing = Convert.ToString(m["inputName"]);
                            break;
                        }
                    }
                    if (existing != null)
                    {
                        captureSource = existing;
                        captureSourceIsOurs = false;
                        try { captureScene = Convert.ToString(obs.Request("GetSceneList", null)["currentProgramSceneName"]); }
                        catch { captureScene = null; }
                        Log.Write("使用当前场景里已有的采集源：" + existing);
                        return;
                    }
                }

                // 专用录制场景 + 只放一个绑定游戏窗口的窗口采集源
                bool sceneExists = false;
                try
                {
                    List<object> scenes = ObsClient.ToList(obs.Request("GetSceneList", null)["scenes"]);
                    foreach (object s in scenes)
                    {
                        Dictionary<string, object> sm = (Dictionary<string, object>)s;
                        if (Convert.ToString(sm["sceneName"]) == RecSceneName) { sceneExists = true; break; }
                    }
                }
                catch { }
                if (!sceneExists)
                {
                    Dictionary<string, object> cr = new Dictionary<string, object>();
                    cr["sceneName"] = RecSceneName;
                    obs.Request("CreateScene", cr);
                    Log.Write("已创建录制专用场景：" + RecSceneName);
                }

                bool sourceExists = false;
                foreach (object o in inputs)
                {
                    Dictionary<string, object> m = (Dictionary<string, object>)o;
                    if (Convert.ToString(m["inputName"]) == RecSourceName) { sourceExists = true; break; }
                }

                if (!sourceExists)
                {
                    Dictionary<string, object> settings = new Dictionary<string, object>();
                    settings["method"] = 0;
                    settings["client_area"] = true;
                    settings["cursor"] = false;
                    Dictionary<string, object> req = new Dictionary<string, object>();
                    req["sceneName"] = RecSceneName;
                    req["inputName"] = RecSourceName;
                    req["inputKind"] = "window_capture";
                    req["inputSettings"] = settings;
                    req["sceneItemEnabled"] = true;
                    obs.Request("CreateInput", req);
                    Log.Write("已创建窗口采集源：" + RecSourceName);
                }

                // 同一个源可以被同一个场景添加多次（OBS 允许），
                // 所以这里必须"只保留一个元素、把多余的删掉"，否则画面上会叠出好几个游戏画面。
                int keepItemId = -1;
                int removed = 0;
                try
                {
                    Dictionary<string, object> q = new Dictionary<string, object>();
                    q["sceneName"] = RecSceneName;
                    List<object> items = ObsClient.ToList(obs.Request("GetSceneItemList", q)["sceneItems"]);
                    foreach (object o in items)
                    {
                        Dictionary<string, object> m = (Dictionary<string, object>)o;
                        if (Convert.ToString(m["sourceName"]) != RecSourceName) continue;
                        int id = Convert.ToInt32(m["sceneItemId"]);
                        if (keepItemId < 0) { keepItemId = id; continue; }
                        try
                        {
                            Dictionary<string, object> d = new Dictionary<string, object>();
                            d["sceneName"] = RecSceneName;
                            d["sceneItemId"] = id;
                            obs.Request("RemoveSceneItem", d);
                            removed++;
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { Log.Write("读取场景元素失败：" + ex.Message); }
                if (removed > 0) Log.Write("清理了 " + removed + " 个重复的采集源元素（之前重复添加导致的画面叠加）");

                // 场景元素可能是「隐藏」状态：并行录制期间会把单文件采集源临时隐藏，如果那次程序被强制结束
                // （任务管理器结束/崩溃/断电），隐藏状态就留在 OBS 里了 —— 之后所有单录都会录成全黑
                // （音频却正常，因为声音是另一个源）。这里强制显示一次。
                if (keepItemId >= 0)
                {
                    try
                    {
                        Dictionary<string, object> q0 = new Dictionary<string, object>();
                        q0["sceneName"] = RecSceneName;
                        q0["sceneItemId"] = keepItemId;
                        if (!Convert.ToBoolean(obs.Request("GetSceneItemEnabled", q0)["sceneItemEnabled"]))
                        {
                            SetSceneItemEnabled(RecSceneName, keepItemId, true);
                            Log.Write("画面采集源原本处于隐藏状态（多半是上次并行录制被强制结束留下的），已重新显示");
                        }
                    }
                    catch { }
                }

                if (keepItemId < 0)
                {
                    try
                    {
                        Dictionary<string, object> req = new Dictionary<string, object>();
                        req["sceneName"] = RecSceneName;
                        req["sourceName"] = RecSourceName;
                        req["sceneItemEnabled"] = true;
                        Dictionary<string, object> resp = obs.Request("CreateSceneItem", req);
                        if (resp.ContainsKey("sceneItemId")) keepItemId = Convert.ToInt32(resp["sceneItemId"]);
                    }
                    catch (Exception ex) { Log.Write("添加采集源到场景失败：" + ex.Message); }
                }

                // 只要游戏声音：再加一个「应用程序音频采集(th123.exe)」，录制时会把 OBS 桌面音频静音避免重复
                if (cfg.GameAudioOnly)
                {
                    bool audioExists = false;
                    foreach (object o in inputs)
                    {
                        Dictionary<string, object> m = (Dictionary<string, object>)o;
                        if (Convert.ToString(m["inputName"]) == AudioSourceName) { audioExists = true; break; }
                    }
                    if (!audioExists)
                    {
                        Dictionary<string, object> aset = new Dictionary<string, object>();
                        aset["window"] = "";
                        Dictionary<string, object> areq = new Dictionary<string, object>();
                        areq["sceneName"] = RecSceneName;
                        areq["inputName"] = AudioSourceName;
                        areq["inputKind"] = "wasapi_process_output_capture";
                        areq["inputSettings"] = aset;
                        areq["sceneItemEnabled"] = true;
                        obs.Request("CreateInput", areq);
                        Log.Write("已创建应用程序音频采集源（只录游戏声音）：" + AudioSourceName);
                    }
                    else
                    {
                        // 已存在：只确保它在场景里，绝不重复添加（否则源列表/混音器会出现一堆同名项）
                        try
                        {
                            Dictionary<string, object> req = new Dictionary<string, object>();
                            req["sceneName"] = RecSceneName;
                            req["sourceName"] = AudioSourceName;
                            req["sceneItemEnabled"] = true;
                            obs.Request("CreateSceneItem", req);
                        }
                        catch { }
                    }
                    // 清理历史遗留的重复元素（同名音频源多个 scene item）
                    CleanupDuplicateItems(RecSceneName, AudioSourceName);
                    ApplyGameAudioSetting();   // 按「录打击音效」开关决定这个源要不要静音
                }

                captureSource = RecSourceName;
                captureSourceIsOurs = true;
                captureScene = RecSceneName;
                FitSourceToCanvas(RecSceneName, keepItemId);
            }
            catch (Exception ex)
            {
                Log.Write("准备 OBS 时出错：" + ex.Message);
            }
        }
        // ---- moved from MainForm ----
        void SwitchScene(bool toOurs)
        {
            try
            {
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["sceneName"] = toOurs ? RecSceneName : (originalScene == null ? RecSceneName : originalScene);
                obs.Request("SetCurrentProgramScene", r);
                if (!toOurs) Log.Write("已切回原来的场景：" + originalScene);
            }
            catch (Exception ex) { Log.Write("切换场景失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void RestoreRecordDirectory()
        {
            if (originalRecordDir == null || !obs.IsConnected) return;
            if (string.IsNullOrEmpty(originalRecordDir)) return;
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["recordDirectory"] = originalRecordDir;
            obs.Request("SetRecordDirectory", r);
            Log.Write("已还原 OBS 录像目录：" + originalRecordDir);
        }
        // ---- moved from MainForm ----
        void RestoreInputMutes(Dictionary<string, bool> saved, string logMessage)
        {
            foreach (KeyValuePair<string, bool> kv in saved)
            {
                try
                {
                    Dictionary<string, object> s = new Dictionary<string, object>();
                    s["inputName"] = kv.Key;
                    s["inputMuted"] = kv.Value;
                    obs.Request("SetInputMute", s);
                }
                catch { }
            }
            if (saved.Count > 0) Log.Write(logMessage);
            saved.Clear();
        }
        // ---- moved from MainForm ----
        void ApplyMicMode(bool beforeRecording)
        {
            try
            {
                if (beforeRecording)
                {
                    // 用【有效】模式，不是 cfg.MicMode：并行（数量≥2）时一律当 gameOnly，
                    // 哪怕设置里存着「游戏+麦克风」—— 界面那个下拉在并行时也是锁成
                    // 「只录游戏声音」的（ShellWindow.ApplyMicLock），两边必须同一套规矩。
                    bool wantMuted = (cfg.EffectiveMicMode != "gameAndMic");
                    List<string> targets = Program.FindMicTargets(obs);
                    if (targets.Count == 0)
                    {
                        Log.Write("没找到麦克风设备，跳过");
                        return;
                    }
                    foreach (string name in targets)
                    {
                        try
                        {
                            Dictionary<string, object> q = new Dictionary<string, object>();
                            q["inputName"] = name;
                            bool was = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]);
                            if (!micOriginalMute.ContainsKey(name)) micOriginalMute[name] = was;
                            if (was == wantMuted)
                            {
                                Log.Write("麦克风 " + name + "：" + (was ? "已静音" : "已开启") + "（符合当前录音模式，不用改）");
                                continue;
                            }
                            Dictionary<string, object> s = new Dictionary<string, object>();
                            s["inputName"] = name;
                            s["inputMuted"] = wantMuted;
                            obs.Request("SetInputMute", s);
                            Log.Write(wantMuted
                                ? ("已静音麦克风（录制期间只留游戏声音）：" + name)
                                : ("已开启麦克风（录制期间会录进解说）：" + name));
                        }
                        catch (Exception ex) { Log.Write("设置麦克风 " + name + " 失败：" + ex.Message); }
                    }
                }
                else
                {
                    RestoreInputMutes(micOriginalMute, "已把麦克风恢复成录制前的状态");
                }
            }
            catch (Exception ex) { Log.Write("处理麦克风失败（可忽略）：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void ApplyAudioRouting(bool beforeRecording)
        {
            // 两种情况都要临时静音 OBS 的桌面音频：
            //   1) GameAudioOnly：只录游戏声音，避免把系统里的其他声音混进来；
            //   2) 「录打击音效」被关掉：录像必须是真的无声，桌面音频也不能漏进去。
            if (!cfg.GameAudioOnly && cfg.RecordGameAudio) return;
            try
            {
                if (beforeRecording)
                {
                    List<string> desks = Program.FindDesktopTargets(obs);
                    foreach (string name in desks)
                    {
                        try
                        {
                            Dictionary<string, object> q = new Dictionary<string, object>();
                            q["inputName"] = name;
                            bool was = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]);
                            if (!deskOriginalMute.ContainsKey(name)) deskOriginalMute[name] = was;
                            if (was) continue;
                            Dictionary<string, object> s = new Dictionary<string, object>();
                            s["inputName"] = name;
                            s["inputMuted"] = true;
                            obs.Request("SetInputMute", s);
                            Log.Write("已静音 OBS 桌面音频：" + name);
                        }
                        catch (Exception ex) { Log.Write("静音桌面音频 " + name + " 失败：" + ex.Message); }
                    }
                }
                else
                {
                    RestoreInputMutes(deskOriginalMute, "已恢复 OBS 桌面音频");
                }
            }
            catch (Exception ex) { Log.Write("处理音频路由失败（可忽略）：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void KeepGameWindowCapturable(Process game)
        {
            if (!cfg.KeepGameCapturable || game == null) return;
            try
            {
                game.Refresh();
                if (game.HasExited) return;
                IntPtr h = game.MainWindowHandle;
                if (h == IntPtr.Zero) return;
                if (!IsIconic(h)) return;

                ShowWindow(h, SW_RESTORE);
                minimizedFixCount++;
                Log.Write("⚠ 游戏窗口被最小化（会录成黑屏）—— 已还原（第 " + minimizedFixCount + " 次）。录制期间请【盖住】它，别最小化");
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void LogAudioLevels(int round)
        {
            if (!obs.MetersFresh)
            {
                Log.Write("音频电平第" + round + "次：还没收到电平事件（可能 OBS 没在推，或没订阅）");
                return;
            }
            string s = "";
            foreach (string n in obs.LevelNames())
            {
                if (n != AudioSourceName && n.IndexOf("桌面音频") < 0 && n.IndexOf("麦克风") < 0) continue;
                double v = obs.GetLevel(n);
                bool muted = false;
                try
                {
                    Dictionary<string, object> q = new Dictionary<string, object>();
                    q["inputName"] = n;
                    muted = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]);
                }
                catch { }
                s += (s.Length > 0 ? "   |   " : "") + n + " = " + v.ToString("0.000") +
                     (muted ? "（源已静音→不进录像）" : (v > 0.001 ? "（有声音，会进录像）" : "（无信号）"));
            }
            Log.Write("音频电平第" + round + "次：" + (s.Length == 0 ? "<没有相关的音频源>" : s));
        }
        // ---- moved from MainForm ----
        int CleanupDuplicateItems(string scene, string source)
        {
            int keep = -1;
            int removed = 0;
            try
            {
                Dictionary<string, object> q = new Dictionary<string, object>();
                q["sceneName"] = scene;
                List<object> items = ObsClient.ToList(obs.Request("GetSceneItemList", q)["sceneItems"]);
                foreach (object o in items)
                {
                    Dictionary<string, object> m = (Dictionary<string, object>)o;
                    if (Convert.ToString(m["sourceName"]) != source) continue;
                    int id = Convert.ToInt32(m["sceneItemId"]);
                    if (keep < 0) { keep = id; continue; }
                    try
                    {
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["sceneName"] = scene;
                        d["sceneItemId"] = id;
                        obs.Request("RemoveSceneItem", d);
                        removed++;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log.Write("读取场景元素失败：" + ex.Message); }
            if (removed > 0) Log.Write("清理了 " + removed + " 个重复的「" + source + "」元素");
            return keep;
        }
        // ---- moved from MainForm ----
        void FitSourceToCanvas(string scene, int itemId)
        {
            if (itemId < 0) return;
            try
            {
                // 用 OBS 当前真实画布尺寸（不是设置里写的值），让 4:3 画面居中留黑边
                int cw = cfg.CanvasWidth;
                int ch = cfg.CanvasHeight;
                try
                {
                    Dictionary<string, object> v = obs.Request("GetVideoSettings", null);
                    if (v.ContainsKey("baseWidth") && v["baseWidth"] != null) cw = Convert.ToInt32(v["baseWidth"]);
                    if (v.ContainsKey("baseHeight") && v["baseHeight"] != null) ch = Convert.ToInt32(v["baseHeight"]);
                }
                catch { }

                // 单录这里用"正好等于画布"的边界框（等比缩放、居中），并先把源最外圈裁掉几像素：
                // 实测 WGC 抓 640×480 游戏窗口时最左/最右 2~3 像素带一条亮边（整段列平均：最左列 82.6、
                // 第 3 列起 45.6）；用 OBS 的源裁剪等比裁掉这一圈后，缩放铺满画布，画面不变形、也不用重编码。
                int edge = cfg.SingleEdgeInset;
                if (edge < 0) edge = 0;
                if (edge * 2 > 60) edge = 0;      // 防呆
                Dictionary<string, object> t = new Dictionary<string, object>();
                t["positionX"] = 0.0;
                t["positionY"] = 0.0;
                t["boundsType"] = "OBS_BOUNDS_SCALE_INNER";
                t["boundsAlignment"] = 0;
                t["boundsWidth"] = (double)cw;
                t["boundsHeight"] = (double)ch;
                if (edge > 0)
                {
                    t["cropLeft"] = edge;
                    t["cropRight"] = edge;
                    t["cropTop"] = edge;
                    t["cropBottom"] = edge;
                }
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["sceneName"] = scene;
                r["sceneItemId"] = itemId;
                r["sceneItemTransform"] = t;
                obs.Request("SetSceneItemTransform", r);
                Log.Write("采集源已按画布 " + cw + "×" + ch + " 居中适配" +
                          (edge > 0 ? "（并裁掉源最外圈 " + edge + " 像素，去掉边缘那条亮边）" : ""));
            }
            catch (Exception ex) { Log.Write("缩放采集源失败（可忽略）：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        bool BindGameWindow(int pid)
        {
            if (captureSource == null) return false;
            string title = null, cls = null;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p == (uint)pid && IsWindowVisible(h))
                {
                    StringBuilder sb = new StringBuilder(512);
                    GetWindowText(h, sb, 512);
                    if (sb.Length > 0)
                    {
                        StringBuilder cb = new StringBuilder(256);
                        GetClassName(h, cb, 256);
                        title = sb.ToString();
                        cls = cb.ToString();
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            if (title == null) return false;

            string windowValue = MakeWindowString(title, cls);
            return ApplyWindowBinding(windowValue);
        }
        // ---- moved from MainForm ----
        bool ApplyWindowBinding(string windowValue)
        {
            if (captureSource == null) return false;
            try
            {
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["window"] = windowValue;
                s["method"] = 0;
                s["client_area"] = true;
                s["cursor"] = false;
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["inputName"] = captureSource;
                r["inputSettings"] = s;
                obs.Request("SetInputSettings", r);
                Log.Write("已把采集源绑定到窗口：" + windowValue);

                // 音频源（应用程序音频采集）也要绑到同一个窗口，否则抓不到游戏声音
                if (cfg.GameAudioOnly)
                {
                    try
                    {
                        Dictionary<string, object> a = new Dictionary<string, object>();
                        a["window"] = windowValue;
                        Dictionary<string, object> ar = new Dictionary<string, object>();
                        ar["inputName"] = AudioSourceName;
                        ar["inputSettings"] = a;
                        obs.Request("SetInputSettings", ar);
                        Log.Write("已把音频采集源绑定到同一个窗口");
                    }
                    catch (Exception ex) { Log.Write("绑定音频采集源失败：" + ex.Message); }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("绑定窗口失败：" + ex.Message);
                return false;
            }
        }
        // ---- moved from MainForm ----
        bool BindSingleGameWindow(Process game, out IntPtr hwnd)
        {
            hwnd = WaitForMainWindow(game, 30);
            if (hwnd == IntPtr.Zero) { Log.Write("没等到游戏窗口，仍按进程标题绑定"); return BindGameWindow(game.Id); }
            try { SetWindowText(hwnd, SingleWinTitle); } catch { }
            StringBuilder cb = new StringBuilder(256);
            GetClassName(hwnd, cb, 256);
            string cls = cb.ToString();
            if (cls == "") cls = "th123_110a";
            return ApplyWindowBinding(MakeWindowString(SingleWinTitle, cls));
        }
        // ---- moved from MainForm ----
        void KeepSingleTitleFixed(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                StringBuilder sb = new StringBuilder(512);
                GetWindowText(hwnd, sb, 512);
                if (sb.ToString() != SingleWinTitle)
                {
                    SetWindowText(hwnd, SingleWinTitle);
                    Log.Write("游戏把窗口标题改掉了，已改回「" + SingleWinTitle + "」（否则采集源会抓不到窗口变全黑）");
                }
            }
            catch { }
        }
        // ---- moved from MainForm ----
        static string MethodName(int m)
        {
            if (m == 1) return "BitBlt";
            if (m == 2) return "Windows 图形捕获(WGC)";
            return "自动";
        }
        // ---- moved from MainForm ----
        void ApplyMethod(int m)
        {
            Dictionary<string, object> s = new Dictionary<string, object>();
            s["method"] = m;
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["inputName"] = captureSource;
            r["inputSettings"] = s;
            obs.Request("SetInputSettings", r);
            Thread.Sleep(m == 2 ? 1200 : 800);
        }
        // ---- moved from MainForm ----
        bool EnsureNotBlank()
        {
            if (captureSource == null) return true;
            int[] methods = new int[] { 2, 0, 1 };
            foreach (int m in methods)
            {
                try
                {
                    ApplyMethod(m);
                    byte[] shot = Shot();
                    try { File.WriteAllBytes(LastFramePath, shot); } catch { }
                    if (!IsBlank(shot))
                    {
                        currentMethod = m;
                        Log.Write("采集画面正常（捕获方式 " + MethodName(m) + "，源 " + captureSource + "）；已存一份截图：" + LastFramePath);
                        return true;
                    }
                    Log.Write("采集画面全黑（捕获方式 " + MethodName(m) + "），换一种再试…");
                }
                catch (Exception ex)
                {
                    Log.Write("检测采集画面失败（可忽略）：" + ex.Message);
                    return true;
                }
            }
            Log.Write("采集画面还是黑的，录像可能是黑屏 —— 请在 OBS 里换捕获方式；截图：" + LastFramePath);
            return false;
        }
        // ---- moved from MainForm ----
        void StopObsRecordingIfAny()
        {
            try
            {
                Dictionary<string, object> st = obs.Request("GetRecordStatus", null);
                if (st != null && st.ContainsKey("outputActive") && Convert.ToBoolean(st["outputActive"]))
                {
                    obs.Request("StopRecord", null);
                    Log.Write("发现 OBS 里还留着一段没停的录像（上次程序被强制结束留下的），已停止它");
                    Thread.Sleep(2000);   // StopRecord 是异步的，等它收尾完，否则后面改设置会报 500
                }
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void EnsureStandardSourceEnabled()
        {
            try
            {
                int id = CleanupDuplicateItems(RecSceneName, RecSourceName);
                if (id < 0) return;
                Dictionary<string, object> q = new Dictionary<string, object>();
                q["sceneName"] = RecSceneName;
                q["sceneItemId"] = id;
                if (!Convert.ToBoolean(obs.Request("GetSceneItemEnabled", q)["sceneItemEnabled"]))
                {
                    SetSceneItemEnabled(RecSceneName, id, true);
                    Log.Write("采集源上次被隐藏了（并行被强杀留下），已恢复显示");
                }
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void CheckCaptureHealth()
        {
            try
            {
                byte[] shot = Shot();
                if (!IsBlank(shot)) { blackStreak = 0; return; }
                blackStreak++;
                Log.Write("录制中检测到画面变黑（第 " + blackStreak + " 次，当前捕获方式 " + MethodName(currentMethod) +
                          "）—— 通常是游戏窗口被别的窗口挡住了");
                if (blackStreak < 2) return;
                int[] order = new int[] { 2, 0, 1 };
                foreach (int m in order)
                {
                    if (m == currentMethod) continue;
                    ApplyMethod(m);
                    if (!IsBlank(Shot()))
                    {
                        currentMethod = m;
                        blackStreak = 0;
                        Log.Write("画面已恢复（捕获方式切换为 " + MethodName(m) + "）");
                        return;
                    }
                }
                Log.Write("画面仍然是黑的：这段录像可能是黑屏。录制期间请不要让其他窗口挡住游戏窗口。");
                blackStreak = 0;
            }
            catch { }
        }
        // ---- moved from MainForm ----
        byte[] Shot()
        {
            if (captureScene != null && captureScene != captureSource)
            {
                try { return ShotOf(captureScene); }
                catch { }
            }
            return ShotOf(captureSource);
        }
        // ---- moved from MainForm ----
        byte[] ShotOf(string sourceName)
        {
            Dictionary<string, object> q = new Dictionary<string, object>();
            q["sourceName"] = sourceName;
            q["imageFormat"] = "jpg";
            q["imageWidth"] = 320;
            q["imageHeight"] = 240;
            q["imageCompressionQuality"] = 60;
            string data = Convert.ToString(obs.Request("GetSourceScreenshot", q)["imageData"]);
            int k = data.IndexOf("base64,");
            if (k >= 0) data = data.Substring(k + 7);
            return Convert.FromBase64String(data);
        }
        // ---- moved from MainForm ----
        bool IsBlank(byte[] jpg)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(jpg))
                using (Bitmap bmp = new Bitmap(ms))
                {
                    double max = 0;
                    for (int y = 0; y < bmp.Height; y += 8)
                        for (int x = 0; x < bmp.Width; x += 8)
                        {
                            Color c = bmp.GetPixel(x, y);
                            double l = (c.R + c.G + c.B) / 3.0;
                            if (l > max) max = l;
                        }
                    return max < 20;
                }
            }
            catch { return false; }
        }
        // ---- moved from MainForm ----
        void ProcessJob(Job job)
        {
            job.Started = DateTime.Now;
            try
            {
                if (cfg.SkipExisting && !forceRecord && !job.Force)
                {
                    string a = Path.ChangeExtension(job.Rep, ".mp4");
                    string b = Path.ChangeExtension(job.Rep, ".mkv");
                    if (File.Exists(a) || File.Exists(b))
                    {
                        Ui.SetJob(job, "跳过", "已有录像文件（可在设置里关掉“跳过已录制”）");
                        Log.Write("跳过（已有录像）：" + Path.GetFileName(job.Rep));
                        return;
                    }
                }
                if ((forceRecord || job.Force) && cfg.SkipExisting)
                    Log.Write("你明确选了重录它 → 即使已有录像也重录：" + Path.GetFileName(job.Rep));
                Ui.SetJob(job, "准备", "");
                try { ApplyMicMode(true); } catch { }   // 每个文件开录前都按当前模式再确认一次麦克风状态
                KillGame();
                try
                {
                    Dictionary<string, object> r = new Dictionary<string, object>();
                    r["recordDirectory"] = Path.GetDirectoryName(job.Rep);
                    obs.Request("SetRecordDirectory", r);
                    Log.Write("OBS 录像目录已设为：" + Path.GetDirectoryName(job.Rep));
                }
                catch (Exception ex) { Log.Write("设置 OBS 录像目录失败（会录到 OBS 默认目录再移动）：" + ex.Message); }



                Ui.SetJob(job, "启动游戏", "");
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(cfg.GameDir, "th123.exe"), "\"" + job.Rep + "\"");
                psi.WorkingDirectory = cfg.GameDir;
                Process game = Process.Start(psi);

                bool shown = false;
                DateTime wDeadline = DateTime.Now.AddSeconds(60);
                while (DateTime.Now < wDeadline && !cancel)
                {
                    Thread.Sleep(500);
                    try
                    {
                        game.Refresh();
                        if (game.HasExited) break;
                        if (game.MainWindowHandle != IntPtr.Zero) { shown = true; break; }
                    }
                    catch { break; }
                }
                if (!shown && !cancel) Log.Write("警告：没等到游戏窗口，仍继续尝试");

                try { if (game.HasExited) { Ui.SetJob(job, "失败", "游戏启动后立刻退出"); return; } }
                catch { }

                // 兜底：单录前确认画面采集源是显示的（并行录制会临时隐藏它；上次被强杀就会一直隐藏 → 全黑）
                EnsureStandardSourceEnabled();

                IntPtr singleWin = IntPtr.Zero;
                if (captureSource != null && captureSourceIsOurs) BindSingleGameWindow(game, out singleWin);
                // 清晰度：把 OBS 画布/输出设成【游戏窗口的原生尺寸】（640×480），让 OBS 完全不缩放，
                // 录完再用 ffmpeg 以 neighbor（点采样）放大到目标尺寸 —— 否则 OBS 用双线性把 640×480 拉到
                // 1440×1080，整体发糊（用户实测：和别人的录像一比"清晰程度差很多"）。
                for (int i = 0; i < cfg.PreRollSeconds && !cancel; i++) Thread.Sleep(1000);
                // 说明：单录这条路径开头的黑屏只有 0.3 秒（实测样本），不需要额外等画面。
                // 曾经在这里加过"等游戏画出画面再开录"，结果反而卡死：这台机器上录制开始前
                // GetSourceScreenshot 一直返回全黑（节目输出还没切到录制场景），等不到就白等 45 秒。
                // 5~8 秒的黑头是并行路径特有的（开录后才重启游戏实例），那边已在切分时按各格
                // "画面出现的时刻" 用 -ss 精确剪掉。
                if (captureSource != null) EnsureNotBlank();

                obs.Request("StartRecord", null);
                Ui.SetJob(job, "录制中", "0:00");
                jobMaxGameLevel = 0;
                try { ApplyGameAudioSetting(); } catch { }   // 再确认一次"录不录打击音效"
                Log.Write("开始录像：" + Path.GetFileName(job.Rep));

                bool timeout = false;
                DateTime t0 = DateTime.Now;
                int ticks = 0;
                blackStreak = 0;
                double lastVisibleSec = 0;   // 最后一次"画面还在"的时刻（相对开录），用来剪掉结尾的黑
                while (!cancel)
                {
                    Thread.Sleep(500);
                    bool exited = false;
                    try { exited = game.HasExited; } catch { exited = true; }
                    if (exited) break;
                    if ((DateTime.Now - t0).TotalMinutes > cfg.MaxMinutes) { timeout = true; break; }
                    ticks++;
                    if (captureSource != null && ticks % 10 == 0) CheckCaptureHealth();
                    // 每秒记一次"画面还在不在"：游戏退出时会先黑一下，收尾时按这个把结尾的黑剪掉
                    if (captureSource != null && ticks % 2 == 0)
                    {
                        try { if (!IsBlank(ShotOf(captureSource))) lastVisibleSec = (DateTime.Now - t0).TotalSeconds; }
                        catch { }
                    }
                    if (ticks % 4 == 0) KeepGameWindowCapturable(game);   // 防止被最小化导致录不到画面
                    if (ticks % 4 == 0) KeepSingleTitleFixed(singleWin);  // 标题被游戏改回去就改回来（否则采集源断）
                    if (ticks % 60 == 0) LogAudioLevels(ticks / 60);
                    if (cfg.GameAudioOnly)
                    {
                        double lv = obs.GetLevel(AudioSourceName);
                        if (lv > jobMaxGameLevel) jobMaxGameLevel = lv;
                    }
                    TimeSpan el = DateTime.Now - t0;
                    Ui.SetJob(job, "录制中", string.Format("{0}:{1:00}", (int)el.TotalMinutes, el.Seconds));
                }

                if (timeout)
                {
                    Log.Write("超过 " + cfg.MaxMinutes + " 分钟上限，强制结束");
                    KillGame();
                }
                if (gameMutedByUs)
                {
                    string m;
                    if (GameAudio.SetSystemMute(false, out m)) { Log.Write("已恢复系统声音（" + m + "）"); MarkSystemMuted(false); }
                    gameMutedByUs = false;
                }
                double recTotalSec = 0;
                // 画面已经黑了就别再等收尾秒数了，否则结尾又多一段纯黑
                bool picGone = false;
                try { picGone = captureSource != null && IsBlank(ShotOf(captureSource)); } catch { }
                if (picGone && cfg.PostRollSeconds > 0)
                    Log.Write("游戏已退出，不再等 " + cfg.PostRollSeconds + " 秒收尾");
                for (int i = 0; i < cfg.PostRollSeconds && !picGone; i++) Thread.Sleep(1000);
                recTotalSec = (DateTime.Now - t0).TotalSeconds;   // 必须在收尾等待之后算，否则漏掉这段黑

                string recPath = null;
                try
                {
                    Dictionary<string, object> resp = obs.Request("StopRecord", null);
                    if (resp.ContainsKey("outputPath")) recPath = Convert.ToString(resp["outputPath"]);
                }
                catch (Exception ex) { Log.Write("停止录像出错：" + ex.Message); }

                if (string.IsNullOrEmpty(recPath) || !File.Exists(recPath))
                {
                    try
                    {
                        string dir = Convert.ToString(obs.Request("GetRecordDirectory", null)["recordDirectory"]);
                        if (Directory.Exists(dir))
                        {
                            DateTime since = DateTime.Now.AddMinutes(-3);
                            foreach (string f in Directory.GetFiles(dir))
                            {
                                string ext = Path.GetExtension(f).ToLower();
                                if ((ext == ".mp4" || ext == ".mkv" || ext == ".flv" || ext == ".mov") &&
                                    File.GetLastWriteTime(f) > since)
                                {
                                    recPath = f;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(recPath) || !File.Exists(recPath))
                {
                    Ui.SetJob(job, "失败", "拿不到录像文件");
                    Log.Write("失败：拿不到录像文件 —— " + Path.GetFileName(job.Rep));
                    return;
                }

                string target = Path.ChangeExtension(job.Rep, Path.GetExtension(recPath));
                if (!string.Equals(Path.GetFullPath(recPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(recPath, target);
                            break;
                        }
                        catch { Thread.Sleep(500); }
                    }
                }
                job.Output = target;
                TrimTrailingBlack(job, recTotalSec, lastVisibleSec);   // 剪掉结尾那段黑屏
                if (File.Exists(target)) job.Size = new FileInfo(target).Length;
                job.VideoSeconds = Mp4Duration(target);                // 列表「用时」显示视频真实时长
                job.Ended = DateTime.Now;

                string note = "";
                if (target.ToLower().EndsWith(".mp4") && !HasMoov(target)) note = "缺少 moov 索引，可能不可播";
                else if (!target.ToLower().EndsWith(".mp4")) note = "格式 " + Path.GetExtension(target) + "，需要转封装";

                // 音频自检：既看电平，也看源有没有被静音（OBS 的电平表在源被静音时照样跳）
                if (!cfg.RecordGameAudio)
                    Log.Write("音频自检：你关掉了「录打击音效」，这段视频按预期是无声的");
                else if (cfg.GameAudioOnly)
                {
                    bool srcMuted = false;
                    try
                    {
                        Dictionary<string, object> q = new Dictionary<string, object>();
                        q["inputName"] = AudioSourceName;
                        srcMuted = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]);
                    }
                    catch { }

                    if (srcMuted)
                    {
                        Log.Write("⚠ 音频自检：「" + AudioSourceName + "」源处于静音状态，这段录像里没有游戏声音");
                        if (note == "") note = "音频源被静音，录像无声";
                    }
                    else if (!obs.MetersFresh)
                        Log.Write("音频自检：没有收到电平数据，无法判断（不影响录制）");
                    else if (jobMaxGameLevel < 0.002)
                    {
                        Log.Write("⚠ 音频自检：整段几乎没声音（最大电平 " + jobMaxGameLevel.ToString("0.000")
                                  + "），检查 OBS 里「" + AudioSourceName + "」有没有绑上游戏窗口");
                        if (note == "") note = "录成了静音？(音频自检)";
                    }
                    else
                        Log.Write("音频自检：游戏声音正常（最大电平 " + jobMaxGameLevel.ToString("0.000") + "）");
                }

                Ui.SetJob(job, note == "" ? "完成" : "注意", note);
                Log.Write((note == "" ? "完成：" : "完成（" + note + "）：") + Path.GetFileName(target) +
                          "  " + (job.Size / 1048576.0).ToString("0.0") + " MB  视频时长 " + FmtDur(job.VideoSeconds) +
                          "（任务耗时 " + FmtDur((job.Ended - job.Started).TotalSeconds) + "）");
                if (note.StartsWith("格式")) Log.Write("转封装：ffmpeg -i \"" + target + "\" -c copy \"" + Path.ChangeExtension(target, ".mp4") + "\"");

                // 需求：录完问用户要把视频放哪
                string saveNote = "";
                if (!queueSaveDecided)
                {
                    string choice = AskSaveLocationDialog(job);
                    if (!string.IsNullOrEmpty(choice) && choice != "keep")
                    {
                        string moved = MoveOutputTo(job, choice);
                        if (moved != null) { job.Output = moved; saveNote = "已放到 " + Path.GetDirectoryName(moved); }
                    }
                }
                else if (!string.IsNullOrEmpty(queueSaveFolder))
                {
                    string moved = MoveOutputTo(job, queueSaveFolder);
                    if (moved != null) { job.Output = moved; saveNote = "已放到 " + queueSaveFolder; }
                }
                if (saveNote != "")
                {
                    if (File.Exists(job.Output)) job.Size = new FileInfo(job.Output).Length;
                    Log.Write("视频位置：" + job.Output);
                    Ui.SetJob(job, note == "" ? "完成" : "注意", saveNote);
                }
            }
            catch (Exception ex)
            {
                Ui.SetJob(job, "失败", ex.Message);
                Log.Write("任务失败：" + Path.GetFileName(job.Rep) + " —— " + ex.Message);
                try { if (obs.IsConnected) obs.Request("StopRecord", null); } catch { }
                KillGame();
            }
        }
        // ---- moved from MainForm ----
        void TrimTrailingBlack(Job job, double totalSec, double lastVisibleSec)
        {
            string tmp = null;
            try
            {
                string path = job.Output;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                if (!path.ToLower().EndsWith(".mp4")) return;
                if (lastVisibleSec <= 1 || totalSec <= lastVisibleSec) return;
                double keep = lastVisibleSec + 0.25;
                double cut = totalSec - keep;
                if (cut < 0.4) return;                     // 黑的不多，不值得动文件
                string ff = FindFfmpeg();
                if (ff == null) return;
                tmp = path + ".trim.mp4";
                if (File.Exists(tmp)) File.Delete(tmp);
                string args = "-hide_banner -y -i \"" + path + "\" -c copy -t " +
                              keep.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                              " -movflags +faststart \"" + tmp + "\"";
                ProcessStartInfo psi = new ProcessStartInfo(ff, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(ff);
                Process p = Process.Start(psi);
                p.WaitForExit();
                if (p.ExitCode != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length < 4096 || !HasMoov(tmp))
                {
                    Log.Write("结尾黑屏修剪没成功（ffmpeg 退出码 " + p.ExitCode + "），保留原文件");
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    return;
                }
                try { File.Replace(tmp, path, null); }
                catch
                {
                    File.Delete(path);
                    File.Move(tmp, path);
                }
                Log.Write("已剪掉结尾 " + FmtDur(cut) + " 的黑屏（" + FmtDur(totalSec) + " → " + FmtDur(keep) + "）");
            }
            catch (Exception ex)
            {
                Log.Write("结尾黑屏修剪出错（已忽略）：" + ex.Message);
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
        // ---- moved from MainForm ----
        string MoveOutputTo(Job job, string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(job.Output) || !File.Exists(job.Output)) return null;
                if (!Directory.Exists(folder))
                {
                    Log.Write("目标文件夹不存在：" + folder);
                    return null;
                }
                string target = Path.Combine(folder, Path.GetFileName(job.Output));
                if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(job.Output), StringComparison.OrdinalIgnoreCase))
                    return job.Output;
                string baseName = Path.GetFileNameWithoutExtension(job.Output);
                string ext = Path.GetExtension(job.Output);
                for (int i = 2; File.Exists(target) && i < 500; i++)
                    target = Path.Combine(folder, baseName + " (" + i + ")" + ext);
                File.Move(job.Output, target);
                return target;
            }
            catch (Exception ex)
            {
                Log.Write("移动视频失败：" + ex.Message);
                return null;
            }
        }
        // ---- moved from MainForm ----
        static bool HasMoov(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    int n = (int)Math.Min(1048576, fs.Length);
                    byte[] head = new byte[n];
                    fs.Read(head, 0, n);
                    fs.Seek(Math.Max(0, fs.Length - 1048576), SeekOrigin.Begin);
                    int n2 = (int)Math.Min(1048576, fs.Length);
                    byte[] tail = new byte[n2];
                    fs.Read(tail, 0, n2);
                    string s = Encoding.ASCII.GetString(head) + Encoding.ASCII.GetString(tail);
                    return s.Contains("moov");
                }
            }
            catch { return false; }
        }
        // ---- moved from MainForm ----
        static void KillGame()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("th123"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void RunQueue()
        {
            try
            {
                Ui.Status("准备 OBS…");
                if (!EnsureObs())
                {
                    MarkPendingFailed("无法使用 OBS");
                    return;
                }
                if (parallelRequested)
                {
                    parallelRequested = false;
                    RunParallel();
                    return;
                }
                if (cfg.AutoPrepareObs) PrepareObs(false);
                if (cfg.AutoPrepareObs && cfg.UseDedicatedScene) SwitchScene(true);
                try
                {
                    if (originalRecordDir == null) CaptureOriginalRecordDir();
                }
                catch { }
                ApplyMicMode(true);
                ApplyAudioRouting(true);

                while (!cancel)
                {
                    Job job = null;
                    lock (jobsLock)
                    {
                        foreach (Job j in jobs) if (j.Status == "等待") { job = j; break; }
                    }
                    if (job == null) break;
                    ProcessJob(job);
                }
            }
            catch (Exception ex)
            {
                Log.Write("队列异常终止：" + ex.Message);
            }
            finally
            {
                // 录完恢复系统声音：你在录制时按过【静音】的话，这里自动帮你打开
                if (weMutedSystem)
                {
                    try
                    {
                        string m;
                        if (GameAudio.SetSystemMute(false, out m))
                        {
                            weMutedSystem = false;
                            Log.Write("录制结束：已自动恢复系统声音（录制时你按过静音）");
                        }
                    }
                    catch { }
                }
                try { ApplyMicMode(false); } catch { }
                try { ApplyAudioRouting(false); } catch { }
                try { if (cfg.AutoPrepareObs && cfg.UseDedicatedScene && obs.IsConnected) SwitchScene(false); } catch { }
                try { RestoreRecordDirectory(); } catch { }
                try { obs.Dispose(); } catch { }
                Ui.Post(delegate
                {
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                });
                if (cancel) Ui.Status("已停止");
                else Ui.Status("队列结束");
                Log.Write("==== 队列结束 ====");
                // 默认不关 OBS；只有勾了界面上的「录完关闭 OBS」才会关
                if (!cancel && cfg.CloseObsAfter)
                {
                    try
                    {
                        foreach (Process p in Process.GetProcessesByName("obs64"))
                            p.CloseMainWindow();
                        Log.Write("已关闭 OBS（你勾选了「录完关闭 OBS」）");
                    }
                    catch { }
                }
            }
        }
        // ---- moved from MainForm ----
        void StartQueue()
        {
            if (worker != null && worker.IsAlive) { Log.Write("队列已在运行，新加入的会排在后面"); return; }
            cancel = false;
            worker = new Thread(RunQueue);
            worker.IsBackground = true;
            worker.Start();
        }
        // ---- moved from MainForm ----
        void MarkPendingFailed(string reason)
        {
            Ui.Post(delegate
            {
                lock (jobsLock)
                {
                    foreach (Job j in jobs)
                        if (j.Status == "等待") Ui.SetJob(j, "失败", reason);
                }
            });
        }
        // ---- moved from MainForm ----
        void AddPaths(string[] paths)
        {
            List<string> reps = new List<string>();
            foreach (string p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                        reps.AddRange(Directory.GetFiles(p, "*.rep", SearchOption.AllDirectories));
                    else if (File.Exists(p) && p.ToLower().EndsWith(".rep"))
                        reps.Add(p);
                }
                catch (Exception ex) { Log.Write("读取 " + p + " 失败：" + ex.Message); }
            }
            reps.Sort(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            List<Job> already = new List<Job>();
            Ui.Post(delegate
            {
                // ⚠ 必须用 BeginUpdate/EndUpdate 把"加条目"整个包起来。
                //   这是 owner-draw 的 ListView：每 Items.Add 一次它就自己重画一次，而重画的时候
                //   后面的条目还没排好版 —— 结果就是"一部分行画出来了、后面的行只有序号是空的，
                //   再点一下才补上"（用户报的"放进来一开始不显示，点一下才出来"）。
                //   顺带也解决了性能问题：放几百个 rep 时不再逐条重画。
                try
                {
                lock (jobsLock)
                {
                    foreach (string r in reps)
                    {
                        bool dup = false;
                        foreach (Job j in jobs) if (string.Equals(j.Rep, r, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                        if (dup) continue;
                        Job job = new Job(r);
                        // 已经有录好的视频 → 先标成「已录制」，然后统一问一次用户"要不要重录"
                        string have = FindExistingOutput(job);
                        if (have != null)
                        {
                            job.Output = have;
                            try { job.Size = new FileInfo(have).Length; } catch { }
                            job.Status = "已录制";
                            job.Detail = "已有视频，点开始会跳过";
                            already.Add(job);
                        }
                        job.Item = new ListViewItem(new string[] {
                            (jobs.Count + 1).ToString(), Path.GetFileName(r), job.Status, "",
                            job.Size > 0 ? (job.Size / 1048576.0).ToString("0.0") + " MB" : "", job.Detail });
                        job.Item.Tag = job;
                        if (job.Status == "已录制") job.Item.ForeColor = Color.FromArgb(0, 120, 0);
                        jobs.Add(job);
                        Ui.AddRow(job);
                        added++;
                    }
                }
                }
                finally
                {
                }
            });
            Ui.SyncScroll();     // 条目数变了，自绘滚动条要跟着更新
            if (added > 0) Log.Write("加入队列 " + added + " 个 rep");
            else Log.Write("没有可加入的 rep（重复或不是 .rep）");
            // 需求：检测到"已经录制过"的文件 → 问一次是否重录
            if (already.Count > 0) AskReRecord(already);
            // 少一步操作：数量自动跟着"放进来几个"走，并把这一批要录的高亮出来
            AfterQueueChanged(added);
        }
        // ---- moved from MainForm ----
        void AfterQueueChanged(int added)
        {
            try
            {
                if (added > 0)
                {
                    int want = Math.Min(6, added);
                    if (cfg.ParallelCount != want)
                    {
                        cfg.ParallelCount = want;
                        cfg.Save();
                        if (Ui.ParallelCount != want) Ui.SetParallel(want);
                        Log.Write("放进来 " + added + " 个 rep → 同时录制数量设为 " + want + (added > 6 ? "（上限 6）" : ""));
                    }
                }
                HighlightBatchRows();
                try { Ui.Status("就绪 · 本批 " + Math.Max(1, Math.Min(6, cfg.ParallelCount)) + " 个（列表里浅绿高亮的就是）"); } catch { }
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void AskReRecord(List<Job> already)
        {
            try
            {
                string sample = already.Count == 1
                    ? Path.GetFileName(already[0].Rep)
                    : (Path.GetFileName(already[0].Rep) + " 等 " + already.Count + " 个");
                string msg = "检测到 " + already.Count + " 个 rep 已经录制过了（旁边/上次的文件夹里已有同名视频）：\r\n\r\n" +
                             sample + "\r\n\r\n" +
                             "要重新录制吗？\r\n" +
                             "  是 = 这些文件重录（会覆盖原来的视频）\r\n" +
                             "  否 = 跳过它们，只录没有录过的";
                Log.Write("检测到 " + already.Count + " 个 rep 已经录制过，正在询问是否重录……");
                System.Windows.MessageBoxResult dr = System.Windows.MessageBox.Show(msg, "这些 rep 已经录制过了", System.Windows.MessageBoxButton.YesNo,
                                                  System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No);
                if (dr == System.Windows.MessageBoxResult.Yes)
                {
                    foreach (Job j in already)
                    {
                        j.Force = true;
                        Ui.SetJob(j, "等待", "你选了重录（会覆盖原视频）");
                    }
                    Log.Write("你选择重录：" + already.Count + " 个（状态已改成「等待」，会覆盖原来的视频）");
                }
                else
                {
                    foreach (Job j in already) Ui.SetJob(j, "已录制", "已有视频（你选择跳过）");
                    Log.Write("你选择跳过：" + already.Count + " 个已录制过的 rep");
                }
            }
            catch (Exception ex) { Log.Write("询问重录失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void SyncParallelCountToSelection()
        {
            try
            {
                int n = Ui.SelectedJobCount;
                bool fromPin = false;
                if (n <= 0 && pinnedBatch != null && pinnedBatch.Count > 0) { n = pinnedBatch.Count; fromPin = true; }
                if (n <= 0) { if (lblParHint != null) lblParHint.Text = ""; return; }
                int want = Math.Min(6, n);
                if (Ui.ParallelCount != want)
                    Ui.SetParallel(want);     // 走 SelectedIndexChanged 里的保存/日志
                if (lblParHint != null)
                    lblParHint.Text = n > 6
                        ? "（你" + (fromPin ? "标记" : "选中") + "了 " + n + " 个，上限 6 → 会分 " + ((n + 5) / 6) + " 批）"
                        : "（= 你" + (fromPin ? "标记" : "选中") + "的 " + n + " 个）";
                HighlightBatchRows();
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void RestoreSoundIfLeftMuted()
        {
            try
            {
                if (!File.Exists(MuteFlagPath)) return;
                bool muted;
                string msg;
                if (!GameAudio.GetSystemMute(out muted, out msg)) return;
                if (muted)
                {
                    string m2;
                    if (GameAudio.SetSystemMute(false, out m2))
                        Log.Write("上次静音后没恢复（多半被强杀），已自动把声音打开");
                    else
                        Log.Write("想恢复上次的静音但失败了：" + m2);
                }
                File.Delete(MuteFlagPath);
                RefreshSoundButtons();
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void RefreshSoundButtons()
        {
            if (btnSound == null) return;
            bool muted;
            string msg;
            if (!GameAudio.GetSystemMute(out muted, out msg))
            {
                btnSound.Text = "🔊 声音：未知";
                btnSound.Width = 148;
                btnSound.BackColor = HudOff;
                btnSound.ForeColor = HudDim;
                btnSound.FlatAppearance.BorderColor = HudLine;
                return;
            }
            if (muted)
            {
                btnSound.Text = "🔊 声音：静音 ✕（录完自动恢复）";
                btnSound.Width = 260;
                Ui.ToggleLook(btnSound, false);
            }
            else
            {
                btnSound.Text = "🔊 声音：开 ✓";
                btnSound.Width = 148;
                Ui.ToggleLook(btnSound, true);
            }
        }
        // ---- moved from MainForm ----
        void ApplyGameAudioSetting()
        {
            if (obs == null || !obs.IsConnected) return;
            try
            {
                Dictionary<string, object> q = new Dictionary<string, object>();
                q["inputName"] = AudioSourceName;
                bool muted = false;
                try { muted = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]); }
                catch { }
                bool want = !cfg.RecordGameAudio;
                if (muted != want)
                {
                    Dictionary<string, object> s = new Dictionary<string, object>();
                    s["inputName"] = AudioSourceName;
                    s["inputMuted"] = want;
                    obs.Request("SetInputMute", s);
                }
                Log.Write("游戏声音录制：" + (cfg.RecordGameAudio ? "开（会录进视频）" : "关（视频无声）") +
                          "；OBS 音频源「" + AudioSourceName + "」现在" + (want ? "已静音" : "未静音"));
                ApplyRecordVolume(AudioSourceName);
            }
            catch (Exception ex) { Log.Write("设置游戏声音录制失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void ApplyRecordVolume(string inputName)
        {
            if (obs == null || !obs.IsConnected) return;
            try
            {
                double mul = cfg.RecordVolume;
                if (mul <= 0) mul = 1.0;
                if (mul > 20) mul = 20;
                if (Math.Abs(mul - 1.0) < 0.001) return;   // 1.0 就是原样，不用动
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["inputName"] = inputName;
                s["inputVolumeMul"] = mul;
                obs.Request("SetInputVolume", s);
                Log.Write("录像音量：" + inputName + " 设为 " + mul.ToString("0.##") + " 倍");
            }
            catch (Exception ex) { Log.Write("设置录像音量失败（忽略）：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void RefreshGameAudioButton()
        {
            if (btnGameAudio == null) return;
            if (cfg.RecordGameAudio)
            {
                btnGameAudio.Text = "🔊 录打击音效：开 ✓";
                Ui.ToggleLook(btnGameAudio, true);
            }
            else
            {
                btnGameAudio.Text = "🔊 录打击音效：关 ✕";
                Ui.ToggleLook(btnGameAudio, false);
            }
            // 「录打击音效」关掉 = 录出来完全无声 → 顺手把「录BGM」锁成关（连游戏里也不放 BGM）
            if (btnBgm != null)
            {
                btnBgm.Enabled = cfg.RecordGameAudio;
                if (!cfg.RecordGameAudio)
                {
                    btnBgm.Text = "录BGM：关 ✕（已锁）";
                    btnBgm.BackColor = HudOff;
                    btnBgm.ForeColor = HudDim;
                }
                else RefreshBgmButton();
            }
        }
        // ---- moved from MainForm ----
        List<string> BgmIniPaths()
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(cfg.GameDir)) return list;
            foreach (string sub in BgmIniSubPaths)
            {
                string p = Path.Combine(cfg.GameDir, sub);
                if (File.Exists(p)) list.Add(p);
            }
            return list;
        }
        // ---- moved from MainForm ----
        void ApplyBgmSetting(bool verbose)
        {
            int want = cfg.RecordBgm ? 0 : 1;
            List<string> paths = BgmIniPaths();
            if (paths.Count == 0)
            {
                if (verbose) Log.Write("找不到 ReplayDnD.ini，BGM 开关没生效（检查「设置…」里的游戏目录）");
                return;
            }
            foreach (string p in paths)
            {
                try
                {
                    string t = File.ReadAllText(p);
                    string nt;
                    if (System.Text.RegularExpressions.Regex.IsMatch(t, @"(?im)^\s*MuteMusic\s*=\s*\d+\s*$"))
                        nt = System.Text.RegularExpressions.Regex.Replace(t, @"(?im)^\s*MuteMusic\s*=\s*\d+\s*$", "MuteMusic=" + want);
                    else
                        nt = t.TrimEnd() + Environment.NewLine + "MuteMusic=" + want + Environment.NewLine;
                    if (nt != t) File.WriteAllText(p, nt);
                    if (verbose)
                        Log.Write("BGM：" + Path.GetFileName(Path.GetDirectoryName(p)) + @" → MuteMusic=" + want +
                                  (want == 0 ? "（播放 rep 时会放 BGM）" : "（不放 BGM，只有音效）"));
                }
                catch (Exception ex) { Log.Write("写 ReplayDnD.ini 失败（" + p + "）：" + ex.Message); }
            }
        }
        // ---- moved from MainForm ----
        void RefreshBgmButton()
        {
            if (btnBgm == null) return;
            if (cfg.RecordBgm)
            {
                btnBgm.Text = "录BGM：开 ✓";
                Ui.ToggleLook(btnBgm, true);
            }
            else
            {
                btnBgm.Text = "录BGM：关 ✕";
                Ui.ToggleLook(btnBgm, false);
            }
        }
        // ---- moved from MainForm ----
        void SyncBgmFromIni()
        {
            bool? muted = ReadBgmMuted();
            if (muted.HasValue)
            {
                cfg.RecordBgm = !muted.Value;
                // 「录打击音效」关着的话，BGM 一律锁成关（连游戏里也不放），保证"完全无声视频"
                if (!cfg.RecordGameAudio && cfg.RecordBgm)
                {
                    cfg.RecordBgm = false;
                    ApplyBgmSetting(true);
                    Log.Write("录打击音效关了 → BGM 一并关掉（MuteMusic=1）");
                }
                RefreshBgmButton();
                Log.Write("BGM：" + (cfg.RecordBgm ? "开（MuteMusic=0，带 BGM）" : "关（MuteMusic=1，只有音效）"));
            }
            else
            {
                RefreshBgmButton();
                Log.Write("找不到 ReplayDnD.ini，BGM 开关读不到（检查「设置…」里的游戏目录）");
            }
        }
        // ---- moved from MainForm ----
        void SetSystemMuteUi(bool mute)
        {
            string msg;
            bool ok = GameAudio.SetSystemMute(mute, out msg);
            Log.Write((mute ? "【静音】" : "【恢复声音】") + "：" + msg + (ok ? "" : "  失败"));
            if (ok)
            {
                weMutedSystem = mute;   // 记住这个静音是我们按的，录制结束自动恢复
                MarkSystemMuted(mute);
                if (mute)
                    Log.Write("现在你自己听不到任何系统声音；录像里仍然有游戏原声。录制结束后会【自动恢复】声音");
                else
                    Log.Write("系统声音已恢复");
            }
            RefreshSoundButtons();
        }
        // ---- moved from MainForm ----
        static void MarkSystemMuted(bool muted)
        {
            try
            {
                if (muted) File.WriteAllText(MuteFlagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Encoding.UTF8);
                else if (File.Exists(MuteFlagPath)) File.Delete(MuteFlagPath);
            }
            catch { }
        }
        // ---- moved from MainForm ----
        void OnSoundToggle(object sender, EventArgs e)
        {
            bool muted;
            string msg;
            if (!GameAudio.GetSystemMute(out muted, out msg))
            {
                Log.Write("读取系统静音状态失败：" + msg);
                return;
            }
            SetSystemMuteUi(!muted);   // 已静音 → 恢复；未静音 → 静音
        }
        // ---- moved from MainForm ----
        void OnGameAudioToggle(object sender, EventArgs e)
        {
            cfg.RecordGameAudio = !cfg.RecordGameAudio;
            // 关闭「录打击音效」→ 直接锁住「录BGM」并强制关掉它（MuteMusic=1），做到"完全无声视频"
            if (!cfg.RecordGameAudio)
            {
                if (cfg.RecordBgm)
                {
                    cfg.RecordBgm = false;
                    ApplyBgmSetting(true);
                    Log.Write("录打击音效关了 → 录BGM 一并关掉（保证完全无声）");
                }
                else ApplyBgmSetting(false);
            }
            cfg.Save();
            Log.Write(cfg.RecordGameAudio
                ? "录打击音效：开（视频含游戏声音）"
                : "录打击音效：关（视频完全无声，录BGM 被锁）");
            if (obs != null && obs.IsConnected)
            {
                Thread t = new Thread(delegate()
                {
                    try { ApplyGameAudioSetting(); } catch { }
                    // 并行录制正在跑的话，把每一格的音源也一起切掉/打开（不然按钮要等下一次录制才生效）
                    try
                    {
                        for (int i = 1; i <= 4; i++)
                            SetInputMuteRaw("RepRecorder P" + i + " 声音", !cfg.RecordGameAudio);
                    }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
            }
            RefreshGameAudioButton();
        }
        // ---- moved from MainForm ----
        void OnBgmToggle(object sender, EventArgs e)
        {
            cfg.RecordBgm = !cfg.RecordBgm;
            cfg.Save();
            ApplyBgmSetting(true);
            RefreshBgmButton();
            Log.Write(cfg.RecordBgm
                ? "录BGM：开（MuteMusic 改成 0，带背景音乐）"
                : "录BGM：关（MuteMusic 改成 1，只有音效）");
            if (cfg.RecordBgm)
                Log.Write("提醒：BGM 有版权风险（B 站等平台会检测），用不用你自己判断。");
        }
        // ---- moved from MainForm ----
        void OnStart(object sender, EventArgs e)
        {
            StartRecording();
        }
        // ---- moved from MainForm ----
        void OnStop(object sender, EventArgs e)
        {
            // 队列/批次根本没在跑的时候点「停止」什么都不该做。
            // 以前无条件走停止流程：空队列点一下也会写「已请求停止……」并把状态卡在「正在停止…」，
            // 看起来像把界面弄坏了（用户实测）。
            bool running = worker != null && worker.IsAlive;
            if (!running)
            {
                Log.Write("现在没有在录，不需要停止" + (Ui.RowCount == 0 ? "（队列也是空的）" : ""));
                cancel = false;
                Ui.Status("就绪");
                Ui.Post(delegate { btnStop.Enabled = false; btnStart.Enabled = true; });
                return;
            }
            cancel = true;
            Log.Write("已请求停止……");
            Ui.Status("正在停止…");
            Ui.Post(delegate { btnStop.Enabled = false; });   // 已经在停了，别让它被连点
        }
        // ---- moved from MainForm ----
        void OnClear(object sender, EventArgs e)
        {
            if (worker != null && worker.IsAlive) { Log.Write("队列运行中，先停止再清空"); return; }
            lock (jobsLock) { jobs.Clear(); }
            // ⚠ 「钉住的本批」（pinnedBatch）存的是 Job 对象，队列清空后它们已经不在 jobs 里了。
            //   不同步清掉的话，下次放进新 rep 时 HighlightBatchRows 会拿这批**已经不存在的 job**
            //   去比对（pinnedBatch 非空就只认它），结果一行都不高亮 —— 用户实测：清空队列后
            //   再拖进来，状态栏写着「本批 6 个」，列表里却一条绿的都没有。
            pinnedBatch.Clear();
            Ui.ClearRows();
            Ui.SyncScroll();
            Log.Write("队列已清空");
        }
        // ---- moved from MainForm ----
        void OnRemove(object sender, EventArgs e)        {
            if (worker != null && worker.IsAlive) { Log.Write("队列运行中，不能移除"); return; }
            List<Job> del = new List<Job>();
            foreach (Job it in Ui.SelectedJobs()) del.Add(it);
            // ⚠ 没有蓝色选中时，删「本批」那批绿色的 —— 跟「开始录制」一套逻辑。
            //   用户实测：钉子钉成绿色之后再点「移除选中」什么都不删（因为 green 不是 ListView 的选中态）。
            if (del.Count == 0 && pinnedBatch != null && pinnedBatch.Count > 0) del.AddRange(pinnedBatch);
            if (del.Count == 0)
            {
                Log.Write("没得移除：先在列表里选中几行（蓝色），或者用「标记本批」把它们标成绿色再移除");
                return;
            }
            lock (jobsLock)
            {
                foreach (Job j in del) { jobs.Remove(j); Ui.RemoveRow(j); }
                Ui.RenumberRows();
            }
            foreach (Job dj in del) pinnedBatch.Remove(dj);
            HighlightBatchRows();   // 移除后本批高亮要跟着挪
            Ui.SyncScroll();
            Log.Write("已从队列移除 " + del.Count + " 个 rep");
        }
        // ---- moved from MainForm ----
        void OnMarkBatch(object sender, EventArgs e)
        {
            try
            {
                List<Job> sel = new List<Job>();
                foreach (Job it in Ui.SelectedJobs())
                {
                    Job j = it;
                    if (j != null && !sel.Contains(j)) sel.Add(j);
                }
                if (sel.Count == 0)
                {
                    Log.Write("还没选：先在列表里选中几行（蓝色），再点「标记本批」");
                    return;
                }
                pinnedBatch.Clear();
                foreach (Job j in sel) pinnedBatch.Add(j);
                suppressSelSync = true;                     // 取消蓝色选中时别让"数量跟随"连锁乱跳
                try { Ui.ClearSelection(); }
                catch { }
                finally { suppressSelSync = false; }
                HighlightBatchRows();
                SyncParallelCountToSelection();
                Log.Write("已把选中的 " + sel.Count + " 个标记为「本批」（绿色），数量也跟着改成 " +
                          Math.Min(6, sel.Count) + " 个");
            }
            catch (Exception ex) { Log.Write("标记本批失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void OnRefreshQueue(object sender, EventArgs e)
        {
            int done = 0, waiting = 0, skip = 0;
            lock (jobsLock)
            {
                foreach (Job j in jobs)
                {
                    if (j.Status == "录制中" || j.Status == "准备" || j.Status == "启动游戏" || j.Status == "收尾")
                    {
                        skip++;
                        continue;   // 正在录的不动
                    }
                    string have = FindExistingOutput(j);
                    if (have != null)
                    {
                        j.Output = have;
                        try { j.Size = new FileInfo(have).Length; } catch { j.Size = 0; }
                        Ui.SetJob(j, "已录制", "已有视频，点开始会跳过");
                        done++;
                    }
                    else
                    {
                        j.Output = "";
                        j.Size = 0;
                        // ⚠ 也要清掉"用时"：VideoSeconds 是上一次录出来那条视频的真实时长，
                        //   视频删了它还留着，列表就会一直显示一个不存在的时长（用户实测的 bug）。
                        j.VideoSeconds = 0;
                        j.Started = DateTime.MinValue;
                        j.Ended = DateTime.MinValue;
                        Ui.SetJob(j, "等待", "还没有视频，会被录制");
                        waiting++;
                    }
                }
            }
            Log.Write("已刷新队列：有视频 " + done + " 个，待录制 " + waiting + " 个" +
                      (skip > 0 ? "，正在处理中 " + skip + " 个（未改动）" : ""));
        }
        // ---- moved from MainForm ----
        void OnOpenLog(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(Log.FilePath)) Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
                else Log.Write("日志文件还没生成");
            }
            catch { }
        }
        // ---- moved from MainForm ----
        // 「检查环境」：把五项报给用户看。
        // 2026-10-08 之前这里【只写日志】—— 用户点完一个窗都不弹，得自己开终端去翻，
        // 而左下角那张环境卡又只报两项。现在先弹窗，日志照写（脚本和排查都还要看它）。
        void OnCheck(object sender, EventArgs e)
        {
            cfg.FillDefaults();
            cfg.Save();
            RefreshEnvLabel();
            Log.Write("---- 环境检查 ----");
            Log.Write("游戏目录：" + (string.IsNullOrEmpty(cfg.GameDir) ? "未找到！" : cfg.GameDir));
            Log.Write("OBS     ：" + (string.IsNullOrEmpty(cfg.ObsExe) ? "未安装！" : cfg.ObsExe));
            Log.Write("WebSocket：127.0.0.1:" + cfg.Port + "（密码" + (string.IsNullOrEmpty(cfg.Password) ? "为空" : "已获取") + "）");
            // ffmpeg：并行录制必需（放在程序旁边最省事），别人电脑上最常缺的就是它
            try
            {
                string ff = FindFfmpeg();
                Log.Write("ffmpeg  ：" + (ff == null
                    ? "找不到！并行录制会失败 —— 把 ffmpeg.exe 放到程序同目录"
                    : ff));
            }
            catch { }
            // 换台电脑最容易漏的一步：整合包里的 ReplayDnD 没启用 → 游戏会开在标题画面，录出来根本不是对局。
            // 这里只【读】ModLoaderSettings.json 判断一下，绝不写它（写它会让 mod loader 崩，见 README 第十六节）。
            try
            {
                if (!string.IsNullOrEmpty(cfg.GameDir))
                {
                    string mls = Path.Combine(cfg.GameDir, "ModLoaderSettings.json");
                    if (File.Exists(mls))
                    {
                        // 用 JSON 解析（键名里是反斜杠，正则容易写错）
                        Dictionary<string, object> root = (Dictionary<string, object>)
                            new JavaScriptSerializer().DeserializeObject(File.ReadAllText(mls, Encoding.UTF8));
                        bool on = false, found = false;
                        object modsObj = (root != null && root.ContainsKey("modules")) ? root["modules"] : null;
                        Dictionary<string, object> mods = modsObj as Dictionary<string, object>;
                        if (mods != null)
                        {
                            foreach (KeyValuePair<string, object> kv in mods)
                            {
                                if (kv.Key.IndexOf("ReplayDnD", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                found = true;
                                Dictionary<string, object> v = kv.Value as Dictionary<string, object>;
                                if (v != null && v.ContainsKey("enabled") && v["enabled"] != null && Convert.ToBoolean(v["enabled"]))
                                    on = true;
                            }
                        }
                        Log.Write("ReplayDnD mod：" + (on ? "已启用 ✓" : (found ? "没启用 ⚠" : "整合包里没有 ⚠")));
                        if (!on)
                            Log.Write("  ⚠ 请在 mod 配置菜单里启用 ReplayDnD，否则录出来的不是对局");
                    }
                    else Log.Write("ReplayDnD mod：找不到 " + mls + "（游戏目录可能不对）");
                }
            }
            catch (Exception ex) { Log.Write("检查 ReplayDnD 失败（不影响使用）：" + ex.Message); }
            try
            {
                if (!obs.IsConnected) obs.Connect(cfg.Port, cfg.Password);
                Log.Write("OBS 连接：成功");
                Log.Write("当前场景：" + Convert.ToString(obs.Request("GetSceneList", null)["currentProgramSceneName"]));
                List<object> inputs = ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]);
                if (inputs.Count == 0) Log.Write("场景里还没有任何源（可用「准备 OBS」自动创建）");
                foreach (object o in inputs)
                {
                    Dictionary<string, object> m = (Dictionary<string, object>)o;
                    Log.Write("  源：" + Convert.ToString(m["inputName"]) + "  (" + Convert.ToString(m["unversionedInputKind"]) + ")");
                }
            }
            catch (Exception ex) { Log.Write("OBS 连接：失败 —— " + ex.Message); }

            // 最后把结论弹出来给用户看（日志上面已经写全了）。
            try
            {
                Ui.ShowEnvDialog(EnvReport.Build(cfg));
                RefreshEnvLabel();
            }
            catch (Exception ex) { Log.Write("环境窗弹出失败（不影响使用）：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        void OnPrepare(object sender, EventArgs e)
        {
            try
            {
                // ⚠ 录制中不许「准备 OBS」（2026-10-08 查实）：PrepareObs 第一句就是
                //   StopObsRecordingIfAny()，而且 AutoCanvas 会顺手把画布改成 1440×1080 ——
                //   正在跑的那一批当场被掐断、画布也被改错，而 worker 完全不知道：
                //   它继续等游戏退出，最后 StopRecord 报 501、退回去捡那个残缺文件，
                //   然后每一格都标成「完成」。界面上只有 开始/停止 两个键在录制时被锁，
                //   所以这个键点得到 —— 这里补上同一道闸。
                if (worker != null && worker.IsAlive)
                {
                    Log.Write("正在录制，先点「停止」再准备 OBS");
                    return;
                }
                if (!EnsureObs()) return;
                PrepareObs(true);
                Log.Write("OBS 准备完毕");
            }
            catch (Exception ex) { Log.Write("准备 OBS 失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        // 设置窗口整个搬去界面层了（旧版是个 PropertyGrid 对话框，WPF 里没有现成控件，
        // 用反射列字段自己搭了一个，见 Ui.ShowSettings）
        void OnSettings(object sender, EventArgs e)
        {
            Ui.ShowSettings();
        }
        // ---- moved from MainForm ----
        static void StopRecordingCli()
        {
            try
            {
                Settings cfg = Settings.Load();
                using (ObsClient obs = new ObsClient())
                {
                    obs.Connect(cfg.Port, cfg.Password);
                    Dictionary<string, object> st = obs.Request("GetRecordStatus", null);
                    bool active = st.ContainsKey("outputActive") && st["outputActive"] != null &&
                                  Convert.ToBoolean(st["outputActive"]);
                    if (!active)
                    {
                        Log.Write("--stop-record：OBS 当前没有在录像");
                        return;
                    }
                    Dictionary<string, object> resp = obs.Request("StopRecord", null);
                    string path = (resp.ContainsKey("outputPath") && resp["outputPath"] != null)
                                  ? Convert.ToString(resp["outputPath"]) : "";
                    Log.Write("--stop-record：已停止录像" + (path.Length > 0 ? "，文件：" + path : ""));
                }
            }
            catch (Exception ex)
            {
                Log.Write("--stop-record 失败：" + ex.Message);
            }
        }
        // ---- moved from MainForm ----
        static void SetRecordDirCli(string dir)
        {
            try
            {
                Settings cfg = Settings.Load();
                using (ObsClient obs = new ObsClient())
                {
                    obs.Connect(cfg.Port, cfg.Password);
                    Dictionary<string, object> r = new Dictionary<string, object>();
                    r["recordDirectory"] = dir;
                    obs.Request("SetRecordDirectory", r);
                    Log.Write("--set-record-dir：OBS 录像目录已设为 " + dir);
                }
            }
            catch (Exception ex)
            {
                Log.Write("--set-record-dir 失败：" + ex.Message);
            }
        }
        // ---- moved from MainForm ----
        static void MicCli(string mode)      {
            try
            {
                Settings cfg = Settings.Load();
                using (ObsClient obs = new ObsClient())
                {
                    obs.Connect(cfg.Port, cfg.Password);
                    List<string> all = Program.FindGlobalAudioNames(false);
                    Log.Write("--mic " + mode + "：JSON 里的全局音频设备 = " +
                              (all.Count == 0 ? "<无>" : string.Join(" / ", all.ToArray())));
                    List<string> targets = Program.FindMicTargets(obs);
                    Log.Write("--mic " + mode + "：麦克风候选 = " +
                              (targets.Count == 0 ? "<无>" : string.Join(" / ", targets.ToArray())));
                    if (targets.Count == 0) return;
                    bool want = (mode == "on");
                    foreach (string n in targets)
                    {
                        try
                        {
                            Dictionary<string, object> q = new Dictionary<string, object>();
                            q["inputName"] = n;
                            bool was = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]);
                            Log.Write("  " + n + "：当前" + (was ? "已静音" : "未静音"));
                            if (mode == "test") continue;
                            if (was != want)
                            {
                                Dictionary<string, object> s = new Dictionary<string, object>();
                                s["inputName"] = n;
                                s["inputMuted"] = want;
                                obs.Request("SetInputMute", s);
                            }
                            bool now = Convert.ToBoolean(obs.Request("GetInputMute", q)["inputMuted"]);
                            Log.Write("  " + n + "：写回后" + (now ? "已静音" : "未静音") + (now == want ? "  OK" : "  没生效"));
                        }
                        catch (Exception ex)
                        {
                            Log.Write("  " + n + "：失败 —— " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("--mic 失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        static void GameSoundCli(string mode)        {
            try
            {
                bool mute = (mode == "off" || mode == "mute" || mode == "静音");
                Process[] ps = Process.GetProcessesByName("th123");
                if (ps.Length == 0) { Log.Write("--game-sound：游戏没在运行"); return; }
                foreach (Process p in ps)
                {
                    string msg;
                    bool ok = GameAudio.SetProcessMute(p.Id, mute, out msg);
                    Log.Write("--game-sound：" + (mute ? "静音" : "打开") + " 游戏声音 PID " + p.Id + " → " + msg + (ok ? "  OK" : "  失败"));
                }
            }
            catch (Exception ex) { Log.Write("--game-sound 失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        static void AudioMeterCli(string secArg)
        {
            int sec = 15;
            int.TryParse(secArg, out sec);
            if (sec <= 0) sec = 15;
            try
            {
                Settings cfg = Settings.Load();
                using (ObsClient obs = new ObsClient())
                {
                    obs.MetersEnabled = true;
                    obs.Connect(cfg.Port, cfg.Password);
                    DateTime end = DateTime.Now.AddSeconds(sec);
                    int round = 0;
                    while (DateTime.Now < end)
                    {
                        Thread.Sleep(1000);
                        round++;
                        if (!obs.MetersFresh)
                        {
                            Log.Write("--audio-meter #" + round + "：还没收到电平事件");
                            continue;
                        }
                        string s = "";
                        foreach (string n in obs.LevelNames())
                            s += (s.Length > 0 ? "   |   " : "") + n + " = " + obs.GetLevel(n).ToString("0.000");
                        Log.Write("--audio-meter #" + round + "：" + (s.Length == 0 ? "<没有活动的音频源>" : s));
                    }
                }
            }
            catch (Exception ex) { Log.Write("--audio-meter 失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        static void SysMuteCli(string mode)
        {
            try
            {
                string m;
                if (mode == "on")
                    Log.Write("--sys-mute on：" + (GameAudio.SetSystemMute(true, out m) ? "OK " : "失败 ") + m);
                else if (mode == "off")
                    Log.Write("--sys-mute off：" + (GameAudio.SetSystemMute(false, out m) ? "OK " : "失败 ") + m);
                else
                {
                    bool cur;
                    GameAudio.GetSystemMute(out cur, out m);
                    Log.Write("--sys-mute test：默认输出设备" + (cur ? "已静音" : "未静音") + "（" + m + "）");
                }
            }
            catch (Exception ex) { Log.Write("--sys-mute 失败：" + ex.Message); }
        }
        // ---- moved from MainForm ----
        static void ObsReqCli(string type)
        {
            try
            {
                Settings cfg = Settings.Load();
                using (ObsClient obs = new ObsClient())
                {
                    obs.Connect(cfg.Port, cfg.Password);
                    Dictionary<string, object> resp = obs.Request(type, null);
                    Log.Write("--obs-req " + type + " → " + new JavaScriptSerializer().Serialize(resp));
                }
            }
            catch (Exception ex) { Log.Write("--obs-req " + type + " 失败：" + ex.Message); }
        }

        // ---- Win32 declarations + state, copied from MainForm ----
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr hWnd, string text);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        struct RECT { public int Left, Top, Right, Bottom; }
        const int SW_RESTORE = 9;
        const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        const int CAPBTN_W = 46;   // 窗口按钮宽度（三个并排贴在右上角）
                    const int arm = 12;                         // 角标每条臂的长度
        const int MaxRaw = 2000;                            // 只留最近这么多原始行，免得越跑越慢

        // ---- extra logic members ----
        List<Job> pinnedBatch = new List<Job>();   // 点空白处"钉"下来的本批（绿色标记），当作手动选中用

        List<Job> selectedBatch = null;   // 「录制选中的」交给队列线程的那批 rep

        volatile bool forceRecord = false; // 「录制选中的」期间忽略「已有录像就跳过」

        const string SingleWinTitle = "th123 录制中";

        bool obsStuckRetried = false;      // 这一批是否已经因为「OBS 没真正开录」自动重启过 OBS（只重试一次）

        int minimizedFixCount = 0;

        bool parallelRequested = false;

        bool suppressSelSync = false;      // 恢复列表选择期间抑制"数量跟随选择"（否则会连锁乱跳）

        static readonly string LastFramePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.lastframe.jpg");

        readonly Dictionary<string, bool> micOriginalMute = new Dictionary<string, bool>();

        readonly Dictionary<string, bool> deskOriginalMute = new Dictionary<string, bool>();

        static readonly string[] BgmIniSubPaths = new string[] { @"modules\ReplayDnD\ReplayDnD.ini", @"modules\ReplayDnD-old\ReplayDnD.ini" };

        double AnalyzeCellHead(string grid, int x, int y, int cw, int ch)
        {
            string ff = FindFfmpeg();
            if (ff == null) return 0;
            string tmpDir = Path.Combine(Path.GetTempPath(), "RepRecorder_scan_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmpDir);
                string vf = "crop=" + cw + ":" + ch + ":" + x + ":" + y + ",scale=160:120,fps=5";
                ProcessStartInfo psi = new ProcessStartInfo(ff,
                    "-hide_banner -loglevel error -y -t 45 -i \"" + grid + "\" -vf \"" + vf + "\" -q:v 6 \"" +
                    Path.Combine(tmpDir, "h%04d.jpg") + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(ff);
                Process p = Process.Start(psi);
                p.WaitForExit();
                string[] files = Directory.GetFiles(tmpDir, "h*.jpg");
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        if (!IsBlank(File.ReadAllBytes(files[i])))
                            return i / 5.0;      // 第 i 帧（5 帧/秒）
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
            }
            return 0;
        }

        // MISSING: ReadBgmMuted

        // MISSING: MuteFlagPath

    }
}
