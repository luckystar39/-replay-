// RepRecorder —— 把非想天则 .rep 拖进来，自动用 OBS 依次录成 mp4
// 依赖：.NET Framework 4.x（Windows 自带）、OBS Studio（obs-websocket 5.x，需启用）
// 编译：csc.exe /nologo /target:winexe /codepage:65001 /out:RepRecorder.exe
//         /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll RepRecorder.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RepRecorder
{
    // ============================================================ 设置
    public class Settings
    {
        [Category("路径"), Description("obs64.exe 的完整路径。留空自动搜索。")]
        public string ObsExe { get; set; }
        [Category("路径"), Description("th123 文件夹（里面有 th123.exe）。留空自动搜索。")]
        public string GameDir { get; set; }
        [Category("OBS 连接"), Description("obs-websocket 端口，默认 4455。")]
        public int Port { get; set; }
        [Category("OBS 连接"), Description("obs-websocket 密码。留空则自动从 OBS 配置文件读取。")]
        public string Password { get; set; }
        [Category("并行录制"), Description("一次同时开几个游戏实例来录（1~6，默认 3）。1 = 相当于普通单录。\r\n" +
            "上限锁在 6 是因为 OBS 最多只有 6 条音轨 —— 超过 6 格每格的声音就没法分开，会录成没声音的视频。\r\n" +
            "内存参考：每实例约 550 MB，6 个约 3.3 GB。")]
        public int ParallelCount { get; set; }
        [Category("并行录制"), Description("切分并行视频时，同时跑几个 ffmpeg（默认 3）。\r\n" +
            "并行录完是一整张合并视频，要按格裁成 N 个文件，这一步是重编码，最花时间。\r\n" +
            "同时跑多个能快好几倍（切 6 格从 3 分钟降到 1 分钟左右）；\r\n" +
            "但 N 卡的 NVENC 同时编码路数有限（家用卡一般 3~8 路），设太大反而会回退到很慢的软件编码。")]
        public int SplitParallel { get; set; }
        [Category("并行录制"), Description("切分并行视频用的编码器：h264_nvenc（默认，兼容性最好）/ hevc_nvenc / av1_nvenc / libx264。\r\n" +
            "没有 N 卡会自动改用 libx264，不用手动改。")]
        public string ParallelSplitEncoder { get; set; }
        [Category("并行录制"), Description("切分时的质量 CQ（越小越清晰、文件越大），默认 19。")]
        public int ParallelSplitCQ { get; set; }   // 越大体积越小（19→23 约省一半）
        [Category("录制"), Description("录制中如果检测到游戏窗口被最小化，自动还原并挪到屏幕外（最小化会导致采集不到画面），录制继续。")]
        public bool KeepGameCapturable { get; set; }
        [Category("录制"), Description("单个 rep 的录制上限（分钟），超时强制停止。")]
        public int MaxMinutes { get; set; }
        [Category("录制"), Description("游戏窗口出现后、开始录像前等待的秒数。")]
        public int PreRollSeconds { get; set; }
        [Category("录制"), Description("游戏退出后、停止录像前等待的秒数。")]
        public int PostRollSeconds { get; set; }
        [Category("行为"), Description("把 rep 拖进窗口后立刻自动开始录制。")]
        public bool AutoStartOnDrop { get; set; }
        [Category("行为"), Description("自动检查/创建 OBS 采集源，并把游戏窗口绑定给它。")]
        public bool AutoPrepareObs { get; set; }
        [Category("画布"), Description("「准备 OBS」和每次开录时，是否把 OBS 的画布/输出分辨率与帧率设成下面三个值。\r\n" +
            "非想天则原生是 4:3 的 640×480：用 1440×1080 这种 4:3 画布时画面正好铺满、左右没有黑边；\r\n" +
            "关掉则不动 OBS 设置（若 OBS 画布是 1920×1080 的 16:9，4:3 画面会居中、左右各留黑边）。\r\n" +
            "（原来主界面上的那个复选框就是这个开关，已合并到这里）")]
        public bool AutoCanvas { get; set; }
        [Category("画布"), Description("画布/输出宽度。")]
        public int CanvasWidth { get; set; }
        [Category("画布"), Description("画布/输出高度。")]
        public int CanvasHeight { get; set; }
        [Category("画布"), Description("帧率。")]
        public int CanvasFps { get; set; }
        [Category("行为"), Description("全部录完后关闭 OBS。")]
        public bool CloseObsAfter { get; set; }
        [Category("行为"), Description("使用专用的「RepRecorder 录制场景」：只录游戏窗口，不会把整个桌面录进去。关掉则使用你当前场景里已有的采集源。")]
        public bool UseDedicatedScene { get; set; }
        [Category("录音"), Description("录进视频的音频里是否包含游戏声音。关掉 → 录出来的视频是无声的（适合你之后自己配 BGM）。")]
        public bool RecordGameAudio { get; set; }
        [Category("录音"), Description("gameOnly = 只录游戏声音（录制时自动静音麦克风）；gameAndMic = 游戏声音 + 麦克风（边录边解说）。")]
        public string MicMode { get; set; }
        [Category("录音"), Description("只把游戏声音录进视频：新增「应用程序音频采集(th123.exe)」，并临时静音 OBS 的桌面音频，避免录进其他系统声音或重复。")]
        public bool GameAudioOnly { get; set; }
        [Category("录音"), Description("录进视频的音量倍数（增益），默认 1.0。\r\n" +
            "0.5 = 一半音量；2.0 = 两倍（+6dB）；上限 20。\r\n" +
            "为什么需要：任务栏那个「电脑音量」调的是输出设备，OBS 抓的是进程音频流，改它【不会】影响录像音量；\r\n" +
            "所以想改录像的响度就用这里，或者用 Windows「音量合成器」里 th123 那一条（那条会影响录像）。")]
        public double RecordVolume { get; set; }
        [Category("行为"), Description("轮录：点一次「开始录制」就把队列里所有待录的 rep 一批接一批全部录完。\r\n" +
            "每批 = 「同时录制数量」（上限 6）；最后不足一批的按实际剩余数量录；每批 1 个时自动走单录、依次录。\r\n" +
            "不勾 = 和以前一样，点一次只录一批。")]
        public bool RoundRecord { get; set; }
        [Category("录音"), Description("是否把游戏背景音乐（BGM）也录进视频。\r\n" +
            "开 = 会自动把 ReplayDnD.ini 里的 MuteMusic 改成 0（播放 rep 时放 BGM）；关 = 改回 1。\r\n" +
            "界面上有「录BGM」按钮可以直接切。注意 BGM 有版权风险。")]
        public bool RecordBgm { get; set; }
        [Category("录音"), Description("单录时把游戏画面最外圈裁掉几像素（源坐标，默认 4）。\r\n" +
            "实测 WGC 抓 640×480 游戏窗口时，最左/最右 2~3 像素会带一条亮边\r\n" +
            "（整段视频列平均：最左列 82.6，第 3 列起 45.6）。等比裁掉这一圈后再缩放铺满，画面不变形，只损失约 0.6% 边缘。\r\n" +
            "设成 0 = 不裁。")]
        public int SingleEdgeInset { get; set; }
        [Category("录音"), Description("并行切分时，每一格往里裁掉多少像素（按成品视频的像素算，默认 6）。\r\n" +
            "为什么需要：并行时相邻格的采集源是亚像素缩放摆的，格边会漏 1~3 像素的邻居画面或黑边进来，\r\n" +
            "成品最外一圈就会出现一条亮线/暗线（用户实测：右边缘最外 2~3 像素亮度从 43 跳到 77）。\r\n" +
            "往里裁一圈就干净了；6 像素 ≈ 0.4%，肉眼看不出来。设成 0 = 不裁（最外圈可能带线）。")]
        public int ParallelEdgeInset { get; set; }
        [Category("行为"), Description("已经有同名 mp4 的 rep 直接跳过（批量重拖时省时间）。")]
        public bool SkipExisting { get; set; }
        [Category("行为"), Description("每录完一个视频就弹窗问你：要放在哪里。")]
        public bool AskSaveLocation { get; set; }
        [Category("行为"), Description("“另存到文件夹…”上次用过的文件夹。")]
        public string LastSaveFolder { get; set; }
        [Category("界面"), Description("左侧控制面板的宽度（像素）。拖分隔条或收起/展开时会自动记住，下次启动还原。")]
        public int PanelWidth { get; set; }
        [Category("界面"), Description("上次退出时左侧控制面板是不是收起状态。下次启动按这个还原。")]
        public bool PanelCollapsed { get; set; }
        [Category("界面"), Description("背景图片路径。留空 = 不用背景图。")]
        public string BgImage { get; set; }
        [Category("界面"), Description("背景图的不透明度（0~100）。越大图越清楚，越小越只剩底色。默认 60。")]
        public int BgOpacity { get; set; }
        [Category("界面"), Description("背景图的摆放方式：fill=等比填满并裁掉多余；fit=等比完整显示（可能留边）；\r\nstretch=拉伸铺满（会变形）；tile=原图平铺；center=原始大小居中。")]
        public string BgLayout { get; set; }
        [Category("界面"), Description("背景图的模糊程度（0~60）。模糊一点能让上面的文字更好读。默认 0。")]
        public int BgBlur { get; set; }
        [Category("界面"), Description("背景图的水平偏移（-100~100，按窗口宽度的百分比算）。0 = 居中，负=往左，正=往右。")]
        public int BgOffsetX { get; set; }
        [Category("界面"), Description("背景图的垂直偏移（-100~100，按窗口高度的百分比算）。0 = 居中，负=往上，正=往下。")]
        public int BgOffsetY { get; set; }
        [Category("界面"), Description("背景图的范围：content=只铺右侧内容区（队列+终端，默认）；global=全局铺满整个窗口（控制面板也透出背景图）。")]
        public string BgScope { get; set; }
        [Category("界面"), Description("WPF 界面的面板底色强度（0~100）。0 = 面板完全透明、背景图零遮挡；\r\n越大越暗、压在背景图上的文字越好读。默认 25（图片仍一眼看清，文字有保障）。")]
        public int PanelScrim { get; set; }
        [Category("界面"), Description("【已停用】整窗不透明度。窗口现在固定 100% 不透明（让文字永远实心）；\r\n想调背景的浓淡请用「背景…」对话框里的「图片不透明度」。")]
        public int WindowOpacity { get; set; }
        [Browsable(false)]
        public int SettingsVersion { get; set; }   // 老设置文件里没有这一项（=0），用它判断要不要补默认值

        public static string SettingsPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.settings.json"); }
        }

        public Settings()
        {
            ObsExe = "";
            GameDir = "";
            Port = 4455;
            Password = "";
            MaxMinutes = 12;
            PreRollSeconds = 3;
            PostRollSeconds = 2;
            AutoStartOnDrop = false;   // 默认：拖进来只入队，等用户点「开始录制」
            AutoPrepareObs = true;
            // 默认开启：把 OBS 的画布/输出设成 1440×1080（正好 4:3，和游戏一致）。
            // ⚠ 以前是 false → 新用户第一次用时程序不设画布，就沿用 OBS 默认的 1920×1080（16:9），
            //   4:3 的游戏会被左右加黑边（用户实测过这种"两边有轮廓"）。老设置文件里显式写了 false 的仍然尊重
            //   （那通常是用户自己想去用 OBS 的画布设置），这里只改"新用户/新文件"的默认值。
            AutoCanvas = true;
            CanvasWidth = 1440;
            CanvasHeight = 1080;
            CanvasFps = 60;
            CloseObsAfter = false;
            UseDedicatedScene = true;
            MicMode = "gameOnly";
            RecordGameAudio = true;
            GameAudioOnly = true;
            RoundRecord = false;
            RecordBgm = false;   // 默认不录 BGM（整合包默认 MuteMusic=1）
            ParallelEdgeInset = 6;    // 并行切分时每格往里裁 6 个成品像素，避免边缘那条亮线/暗线
            SingleEdgeInset = 4;      // 单录时把源最外圈裁 4 像素，去掉 WGC 带来的亮边
            RecordVolume = 1.0;       // 录像音量：1.0 = 原样（任务栏音量不影响录像，这里才是"提前设录像音量"的地方）
            SettingsVersion = 4;
            KeepGameCapturable = true;
            ParallelCount = 3;
            ParallelSplitEncoder = "h264_nvenc";
            SplitParallel = 3;        // 切分时同时跑 3 个 ffmpeg（快 ~3 倍；N 卡同时编码路数够用）
            ParallelSplitCQ = 23;   // 用户反馈视频太大 → 默认从 19 调到 23（体积约省一半，画质几乎无感）
            SkipExisting = true;
            AskSaveLocation = true;
            LastSaveFolder = "";
            PanelWidth = 322;          // 左侧控制面板默认宽度
            PanelCollapsed = false;    // 默认展开
            BgImage = "";
            BgOpacity = 60;
            BgLayout = "fill";
            BgBlur = 0;
            BgOffsetX = 0;
            BgOffsetY = 0;
            BgScope = "content";       // 默认只铺右侧内容区；"global" = 全局铺满整个窗口
            PanelScrim = 25;           // 面板底色：淡淡一层，图片仍一眼看清，文字有保障
            WindowOpacity = 85;
            SettingsVersion = 2;
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string txt = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    Settings loaded = new JavaScriptSerializer().Deserialize<Settings>(txt);
                    if (loaded != null) s = loaded;
                }
            }
            catch (Exception ex) { Log.Write("读取设置失败：" + ex.Message); }
            // 同时录制数量硬性夹在 1~6：OBS 只有 6 条音轨，超过 6 格就没法给每格单独放声音，
            // 会出现"录出来没声音"的视频（老设置文件里如果存着 7~10，这里也一并夹回来）
            if (s.ParallelCount < 1) s.ParallelCount = 1;
            if (s.ParallelCount > 6) s.ParallelCount = 6;
            // 老设置文件里没有 BgScope → 反序列化出来是 null，给默认"只铺内容区"
            if (string.IsNullOrEmpty(s.BgScope)) s.BgScope = "content";
            // 老设置文件（没有 SettingsVersion）里没有"显示计分板 / 边缘裁切"这些项，反序列化出来会是 false/0，
            // 那样老用户就看不到计分板、边缘也不会裁（等于修复没生效）。所以给老文件补上默认值。
            if (s.SettingsVersion < 2)
            {
                s.SettingsVersion = 2;
            }
            if (s.SettingsVersion < 3)
            {
                if (s.SingleEdgeInset <= 0) s.SingleEdgeInset = 4;     // 去掉单录最外圈那条亮边
                if (s.ParallelEdgeInset <= 0) s.ParallelEdgeInset = 6; // 去掉并行每格边缘的邻居/黑边残影
                s.SettingsVersion = 3;
            }
            if (s.SettingsVersion < 4)
            {
                if (s.RecordVolume <= 0) s.RecordVolume = 1.0;   // 老设置文件里没有"录像音量" → 给个默认 1.0
                if (s.SplitParallel <= 0) s.SplitParallel = 3;   // 老设置文件里没有"并发切分数" → 给个默认 3
                s.SettingsVersion = 4;
            }
            if (s.SettingsVersion < 5)
            {
                // 老设置文件里没有"面板宽度" → 给个默认值，别让面板窄成 0
                if (s.PanelWidth < 160) s.PanelWidth = 322;
                s.SettingsVersion = 5;
            }
            if (s.SettingsVersion < 6)
            {
                // 老设置文件里没有"背景图"这套 → 补默认值
                if (s.BgImage == null) s.BgImage = "";
                if (string.IsNullOrEmpty(s.BgLayout)) s.BgLayout = "fill";
                if (s.BgOpacity <= 0) s.BgOpacity = 60;
                if (s.BgBlur < 0) s.BgBlur = 0;
                if (s.WindowOpacity <= 0) s.WindowOpacity = 85;
                s.SettingsVersion = 6;
            }
            if (s.SettingsVersion < 7)
            {
                // 老设置文件里没有"背景图位移" → 默认居中
                s.SettingsVersion = 7;
            }
            if (s.SettingsVersion < 8)
            {
                // 老设置文件里没有"面板底色" → 给默认值。
                // 这一段只在升级时跑一次，所以之后用户主动把滑块拉到 0（零遮挡）会被正常保留。
                if (s.PanelScrim <= 0) s.PanelScrim = 25;
                s.SettingsVersion = 8;
            }
            s.FillDefaults();
            return s;
        }

        public void Save()
        {
            try
            {
                string txt = new JavaScriptSerializer().Serialize(this);
                File.WriteAllText(SettingsPath, txt, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("保存设置失败：" + ex.Message); }
        }

        public void FillDefaults()
        {
            if (string.IsNullOrEmpty(GameDir) || !File.Exists(Path.Combine(GameDir, "th123.exe")))
            {
                string found = FindGameDir();
                if (found != null) GameDir = found;
            }
            if (string.IsNullOrEmpty(ObsExe) || !File.Exists(ObsExe))
            {
                string found = FindObsExe();
                if (found != null) ObsExe = found;
            }
            // 从 OBS 配置文件里偷密码/端口
            try
            {
                string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                          @"obs-studio\plugin_config\obs-websocket\config.json");
                if (File.Exists(cfg))
                {
                    Dictionary<string, object> d = (Dictionary<string, object>)
                        new JavaScriptSerializer().DeserializeObject(File.ReadAllText(cfg, Encoding.UTF8));
                    if (d != null)
                    {
                        if (d.ContainsKey("server_port") && d["server_port"] != null)
                            Port = Convert.ToInt32(d["server_port"]);
                        if (string.IsNullOrEmpty(Password) && d.ContainsKey("server_password") && d["server_password"] != null)
                            Password = Convert.ToString(d["server_password"]);
                    }
                }
            }
            catch { }
            if (Port <= 0) Port = 4455;
        }

        public static string FindGameDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            List<string> roots = new List<string>();
            roots.Add(baseDir);
            try
            {
                DirectoryInfo up = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (up != null) roots.Add(up.FullName);
            }
            catch { }
            foreach (string root in roots)
            {
                if (File.Exists(Path.Combine(Path.Combine(root, "th123"), "th123.exe")))
                    return Path.Combine(root, "th123");
                try
                {
                    foreach (string sub in Directory.GetDirectories(root))
                        if (File.Exists(Path.Combine(Path.Combine(sub, "th123"), "th123.exe")))
                            return Path.Combine(sub, "th123");
                }
                catch { }
            }
            return null;
        }

        public static string FindObsExe()
        {
            List<string> cands = new List<string>();

            // 1) 注册表卸载项（OBS 可能装在非默认目录，比如 D:\OBS\obs-studio）
            try
            {
                string[] subKeys = new string[] {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                foreach (string sk in subKeys)
                {
                    using (Microsoft.Win32.RegistryKey rk = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(sk))
                    {
                        if (rk == null) continue;
                        foreach (string name in rk.GetSubKeyNames())
                        {
                            using (Microsoft.Win32.RegistryKey app = rk.OpenSubKey(name))
                            {
                                if (app == null) continue;
                                string disp = Convert.ToString(app.GetValue("DisplayName"));
                                if (disp == null || disp.IndexOf("OBS", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                string icon = Convert.ToString(app.GetValue("DisplayIcon"));
                                if (!string.IsNullOrEmpty(icon))
                                {
                                    int comma = icon.IndexOf(',');
                                    if (comma > 0) icon = icon.Substring(0, comma);
                                    cands.Add(icon.Trim().Trim('"'));
                                }
                                string loc = Convert.ToString(app.GetValue("InstallLocation"));
                                if (!string.IsNullOrEmpty(loc))
                                    cands.Add(Path.Combine(loc.Trim().Trim('"'), @"bin\64bit\obs64.exe"));
                            }
                        }
                    }
                }
            }
            catch { }

            // 2) 常见安装位置
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"obs-studio\bin\64bit\obs64.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"obs-studio\bin\64bit\obs64.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\obs-studio\bin\64bit\obs64.exe"));

            // 3) 各固定磁盘根目录下名字带 obs 的文件夹
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    foreach (string sub in Directory.GetDirectories(d.RootDirectory.FullName))
                    {
                        if (Path.GetFileName(sub).ToLower().IndexOf("obs") < 0) continue;
                        cands.Add(Path.Combine(sub, @"bin\64bit\obs64.exe"));
                        cands.Add(Path.Combine(sub, @"obs-studio\bin\64bit\obs64.exe"));
                    }
                }
            }
            catch { }

            foreach (string c in cands)
            {
                try { if (File.Exists(c)) return c; }
                catch { }
            }
            return null;
        }

        public static string ObsWebSocketConfigPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    @"obs-studio\plugin_config\obs-websocket\config.json");
            }
        }

        // OBS 的 WebSocket 服务器默认是关的；OBS 没在跑的时候帮它打开
        public static bool EnsureWebSocketEnabled(out string message)
        {
            message = "";
            try
            {
                string file = ObsWebSocketConfigPath;
                if (!File.Exists(file)) { message = "找不到 OBS 的 WebSocket 配置文件（先启动一次 OBS）"; return false; }
                string txt = File.ReadAllText(file, Encoding.UTF8);
                Dictionary<string, object> d = (Dictionary<string, object>)
                    new JavaScriptSerializer().DeserializeObject(txt);
                if (d == null) { message = "WebSocket 配置文件读不出来"; return false; }
                bool enabled = d.ContainsKey("server_enabled") && d["server_enabled"] != null &&
                               Convert.ToBoolean(d["server_enabled"]);
                if (enabled) { message = "WebSocket 服务器已启用"; return true; }
                if (Process.GetProcessesByName("obs64").Length > 0)
                {
                    message = "OBS 正在运行，但 WebSocket 服务器是关闭的：请关掉 OBS 后重试本程序，或手动在 工具 → WebSocket 服务器设置 里启用";
                    return false;
                }
                d["server_enabled"] = true;
                File.WriteAllText(file, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
                message = "已自动帮你启用 OBS 的 WebSocket 服务器";
                return true;
            }
            catch (Exception ex)
            {
                message = "自动启用 WebSocket 失败：" + ex.Message;
                return false;
            }
        }
    }

    // ============================================================ 日志
    public static class Log
    {
        static readonly object gate = new object();
        public static string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.log");
        public static event Action<string> OnLine;

        // 启动时把上一次的日志挪成 RepRecorder-上次.log：
        // 以前只写同一个文件，下一次运行就把上一次的记录盖掉了 —— 出过错却查不到（用户那次轮录报错就是这么丢的）。
        public static void RotateOnStart()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder-上次.log");
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                File.Move(FilePath, old);
            }
            catch { }   // 挪不动也无所谓，继续追加写
        }

        public static void Write(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (gate)
            {
                try { File.AppendAllText(FilePath, line + Environment.NewLine, new UTF8Encoding(false)); }
                catch { }
            }
            Action<string> h = OnLine;
            if (h != null)
            {
                try { h(line); } catch { }
            }
        }
    }

    // ============================================================ 任务
    public class Job
    {
        public string Rep;
        public string Status;
        public string Detail;
        public string Output;
        public long Size;
        public DateTime Started;
        public DateTime Ended;
        public double VideoSeconds;   // 成品视频真实时长（从 mp4 的 mvhd 读出来；列表「用时」显示它）
        public bool Force;            // 用户在"已经录过了，要重录吗"的询问里选了「要重录」
        public ListViewItem Item;

        public Job(string rep)
        {
            Rep = rep;
            Status = "等待";
            Detail = "";
            Output = "";
        }
    }

    // ============================================================ OBS WebSocket 客户端
    public class ObsClient : IDisposable
    {
        ClientWebSocket ws;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        int reqId;
        Thread reader;
        volatile bool running;
        readonly object sendLock = new object();
        readonly object gate = new object();
        readonly Dictionary<string, Dictionary<string, object>> pending = new Dictionary<string, Dictionary<string, object>>();
        readonly Dictionary<string, ManualResetEvent> signals = new Dictionary<string, ManualResetEvent>();

        // 音频电平（源名 → 最大电平 0~1），来自 InputVolumeMeters 事件
        readonly Dictionary<string, double> levels = new Dictionary<string, double>();
        DateTime lastMeterAt = DateTime.MinValue;

        // 订阅 InputVolumeMeters 高频事件（1<<16），必须在 Connect 之前设置
        public bool MetersEnabled = false;

        public double GetLevel(string inputName)
        {
            lock (levels)
            {
                double v;
                return levels.TryGetValue(inputName, out v) ? v : 0.0;
            }
        }

        public List<string> LevelNames()
        {
            lock (levels) return new List<string>(levels.Keys);
        }

        public bool MetersFresh
        {
            get { return (DateTime.Now - lastMeterAt).TotalSeconds < 3; }
        }

        static string B64Sha256(string s)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        void Send(object o)
        {
            string txt = json.Serialize(o);
            byte[] bytes = Encoding.UTF8.GetBytes(txt);
            lock (sendLock)
            {
                ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                             CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        Dictionary<string, object> Receive()
        {
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[65536];
            while (true)
            {
                ArraySegment<byte> seg = new ArraySegment<byte>(buf);
                WebSocketReceiveResult res = ws.ReceiveAsync(seg, CancellationToken.None).GetAwaiter().GetResult();
                if (res.MessageType == WebSocketMessageType.Close) throw new Exception("OBS 关闭了 WebSocket 连接");
                if (res.Count > 0) ms.Write(buf, 0, res.Count);
                if (res.EndOfMessage) break;
            }
            string txt = Encoding.UTF8.GetString(ms.ToArray());
            if (txt.Length == 0) return null;
            return (Dictionary<string, object>)json.DeserializeObject(txt);
        }

        public bool IsConnected
        {
            get { return ws != null && ws.State == WebSocketState.Open; }
        }

        public void Connect(int port, string password)
        {
            Dispose();
            ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port), CancellationToken.None).GetAwaiter().GetResult();

            Dictionary<string, object> hello = Receive();
            if (hello == null || Convert.ToInt32(hello["op"]) != 0) throw new Exception("OBS 未返回 Hello 握手包");
            Dictionary<string, object> d = (Dictionary<string, object>)hello["d"];
            Dictionary<string, object> identify = new Dictionary<string, object>();
            identify["rpcVersion"] = 1;
            // 65536 = EventSubscription::InputVolumeMeters（高频事件，默认不订阅）
            identify["eventSubscriptions"] = MetersEnabled ? 65536 : 0;
            if (d.ContainsKey("authentication") && d["authentication"] != null)
            {
                Dictionary<string, object> auth = (Dictionary<string, object>)d["authentication"];
                if (string.IsNullOrEmpty(password)) throw new Exception("OBS 要求 WebSocket 密码，但设置里是空的");
                string secret = B64Sha256(password + Convert.ToString(auth["salt"]));
                identify["authentication"] = B64Sha256(secret + Convert.ToString(auth["challenge"]));
            }
            Send(new Dictionary<string, object> { { "op", 1 }, { "d", identify } });
            Dictionary<string, object> ok = Receive();
            if (ok == null || Convert.ToInt32(ok["op"]) != 2) throw new Exception("obs-websocket 认证失败（密码不对？）");
            Log.Write("已连接 OBS（obs-websocket " + Convert.ToString(d["obsWebSocketVersion"]) +
                      (MetersEnabled ? "，已订阅音频电平" : "") + "）");

            running = true;
            reader = new Thread(ReaderLoop);
            reader.IsBackground = true;
            reader.Start();
        }

        // 后台接收：请求响应交给等在 Request 里的线程，事件在这里分发
        void ReaderLoop()
        {
            while (running)
            {
                Dictionary<string, object> msg;
                try { msg = Receive(); }
                catch { break; }
                if (msg == null) continue;
                int op;
                try { op = Convert.ToInt32(msg["op"]); }
                catch { continue; }

                if (op == 7)      // RequestResponse
                {
                    Dictionary<string, object> d = msg["d"] as Dictionary<string, object>;
                    if (d == null || !d.ContainsKey("requestId")) continue;
                    string id = Convert.ToString(d["requestId"]);
                    ManualResetEvent ev = null;
                    lock (gate)
                    {
                        if (signals.ContainsKey(id))
                        {
                            pending[id] = d;
                            ev = signals[id];
                        }
                    }
                    if (ev != null) { try { ev.Set(); } catch { } }
                }
                else if (op == 5) // Event
                {
                    Dictionary<string, object> d = msg["d"] as Dictionary<string, object>;
                    if (d != null) HandleEvent(d);
                }
            }
        }

        void HandleEvent(Dictionary<string, object> d)
        {
            string type = Convert.ToString(d.ContainsKey("eventType") ? d["eventType"] : "");
            if (type != "InputVolumeMeters") return;
            Dictionary<string, object> ed = d.ContainsKey("eventData") ? d["eventData"] as Dictionary<string, object> : null;
            if (ed == null) return;
            List<object> inputs = ToList(ed.ContainsKey("inputs") ? ed["inputs"] : null);
            lock (levels)
            {
                levels.Clear();
                foreach (object o in inputs)
                {
                    Dictionary<string, object> m = o as Dictionary<string, object>;
                    if (m == null) continue;
                    string name = Convert.ToString(m["inputName"]);
                    double max = 0;
                    if (m.ContainsKey("inputLevelsMul") && m["inputLevelsMul"] != null)
                    {
                        foreach (object ch in ToList(m["inputLevelsMul"]))
                        {
                            foreach (object v in ToList(ch))
                            {
                                if (v == null) continue;
                                double x = Convert.ToDouble(v);
                                if (x > max) max = x;
                            }
                        }
                    }
                    levels[name] = max;
                }
            }
            lastMeterAt = DateTime.Now;
        }

        public Dictionary<string, object> Request(string type, Dictionary<string, object> data)
        {
            if (!IsConnected) throw new Exception("OBS 未连接");
            string id;
            lock (gate) { reqId++; id = "r" + reqId; }
            Dictionary<string, object> inner = new Dictionary<string, object>();
            inner["requestType"] = type;
            inner["requestId"] = id;
            if (data != null) inner["requestData"] = data;

            ManualResetEvent ev = new ManualResetEvent(false);
            lock (gate) signals[id] = ev;
            try
            {
                Send(new Dictionary<string, object> { { "op", 6 }, { "d", inner } });
                if (!ev.WaitOne(20000)) throw new Exception("OBS 请求 " + type + " 超时（20 秒）");
                Dictionary<string, object> d;
                lock (gate) { d = pending.ContainsKey(id) ? pending[id] : null; }
                if (d == null) throw new Exception("OBS 请求 " + type + " 没有拿到响应");
                Dictionary<string, object> st = (Dictionary<string, object>)d["requestStatus"];
                if (!Convert.ToBoolean(st["result"]))
                    throw new Exception("OBS 请求 " + type + " 失败：" + Convert.ToString(st["code"]) + " " +
                                        (st.ContainsKey("comment") ? Convert.ToString(st["comment"]) : ""));
                if (d.ContainsKey("responseData") && d["responseData"] != null)
                    return (Dictionary<string, object>)d["responseData"];
                return new Dictionary<string, object>();
            }
            finally
            {
                lock (gate)
                {
                    signals.Remove(id);
                    pending.Remove(id);
                }
                try { ev.Close(); } catch { }
            }
        }

        // JavaScriptSerializer 会把 JSON 数组反序列化成 Object[]，统一转成 List<object>
        public static List<object> ToList(object o)
        {
            List<object> r = new List<object>();
            System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
            if (e != null)
                foreach (object x in e) r.Add(x);
            return r;
        }

        public void Dispose()
        {
            if (ws == null) return;
            running = false;
            try
            {
                if (ws.State == WebSocketState.Open)
                    ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }
            try { ws.Dispose(); } catch { }   // 让阻塞中的 Receive 抛错退出
            ws = null;
            try { if (reader != null && reader.IsAlive) reader.Join(1500); } catch { }
            reader = null;
            lock (gate)
            {
                foreach (KeyValuePair<string, ManualResetEvent> kv in signals) { try { kv.Value.Set(); } catch { } }
            }
            lock (levels) { levels.Clear(); }
        }
    }

    // ============================================================ 主窗口
    public class MainForm : Form
    {
        // --- win32 ---
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        // ⚠ 2026-09-26 实测：合成 WM_DROPFILES 把 rep 投给游戏窗口 **完全无效**（游戏一直停在标题画面，
        //   既不开始播也不退出）—— 这版 ReplayDnD / ReplayDnD-old 走的应该是 OLE 拖放，只有真人鼠标拖才有用。
        //   所以并行录制改成了「把 rep 当命令行参数重启实例」（见 RunParallel 第 6 步）。
        //   当年为此写的那套 SendMessage + DROPFILES + DropFileOnWindow 已经删掉；这条结论留着，别再试一遍。
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr hWnd, string text);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        const int SW_RESTORE = 9;
        const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

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
        readonly Dictionary<string, bool> micOriginalMute = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> deskOriginalMute = new Dictionary<string, bool>();

        BgListView list;   // 用 BgListView 类型：空态提示要访问 EmptyHint（见 BgListView.WndProc）
        BgLogView logBox;
        Label hint;
        Label obsState;
        Panel hudTop;              // 顶部状态条
        SplitContainer split;      // 上下分栏（列表 / 终端），做成字段才能按 3:5 比例摆
        SplitContainer mainSplit;  // 左右分栏（控制面板 | 内容区），拖分隔条可调宽
        Button btnPanel;           // 顶栏上的「收起 / 展开面板」
        HudCapButton btnMin;       // 顶栏右边：最小化
        HudCapButton btnMax;       // 顶栏右边：最大化 / 还原
        HudCapButton btnClose;     // 顶栏右边：关闭
        const int CAPBTN_W = 46;   // 窗口按钮宽度（三个并排贴在右上角）
        Panel activityBar;         // 收起之后左边那条窄竖条（学 VS Code 的活动栏）
        Button btnExpand;          // 竖条上的「▶」展开按钮
        static Font barFont;       // 竖排文字用的字体
        static Image bgSource;     // 背景原图（缓存，避免每次重建都读盘）
        static string bgSourcePath = "";
        System.Windows.Forms.Timer bgTimer;   // 窗口缩放时给重建做防抖
        Button btnBg;              // 顶栏上的「背景…」
        BgFlowPanel top;           // 左栏那一整列控件（内容）
        BgPanel leftView;          // 左栏的视口（裁剪 top）
        HudScrollBar leftScroll;   // 左栏的自绘滚动条
        HudScrollBar listScroll;   // 队列列表的自绘滚动条
        bool syncingLeft = false;
        bool syncingList = false;
        bool panelStateReady = false;   // InitPanelLayout 跑完之前不往设置里写盘
        int panelWidth = 322;      // 收起前记住的面板宽度，展开时还原
        // ⚠ 初值必须是 true：构造期间控件停靠/缩放也会触发 SplitterMoved，
        //   不屏蔽的话会把 panelWidth 记成布局过程中的中间值（实测记成了 ~390）。
        //   InitPanelLayout() 跑完后才放开。
        bool suppressSplitterEvent = true;
        bool splitterDragging = false;   // 分隔条是不是真的被鼠标拖着
        bool isResizing = false;         // 正在拖窗口边框/标题栏（WM_ENTERSIZEMOVE ~ WM_EXITSIZEMOVE）
        // ---- 拖动中的「整窗快照预览」（2026-10-03，见 WndProc 里那段实测数据）----
        //   按下缩放那一刻把整窗拍成一张位图；拖动期间顶层子控件临时藏起来（隐藏的控件不参与布局，
        //   也不重绘），窗体自己把这张图按新尺寸铺满 —— 每步只剩一次 blit。
        //   实测同一台机器：边拖边完整重绘 48ms/步（≈20fps），一张图铺满整窗 13ms/步。
        Bitmap dragSnap;                 // 快照位图（拍在"按下那一刻"，松手就扔）
        bool snapArmed = false;          // 快照已拍好，等第一次真的变尺寸再启用
        bool snapActive = false;         // 正在用快照预览（子控件已藏）
        int lastSnapSizeTick = 0;        // 上一次变尺寸的时刻（保险丝用：久没变 + 左键松开 = 拖动已结束）
        readonly List<Control> snapShow = new List<Control>();   // 藏之前哪些是可见的，收工时原样放回
        bool splitterHover = false;      // 光标是不是压在分隔条上（压在才把它画出来）
        Label lblParHint;
        Button btnStart, btnStop, btnClear, btnRemove, btnMark, btnCheck, btnPrepare, btnSet, btnOpenLog;
        Button btnSound;
        Button btnGameAudio;
        Button btnBgm;
        CheckBox chkAuto, chkPrepare, chkCloseObs, chkRound;
        ComboBox cbMic;
        Label lblMic;
        Button btnRefresh;
        ComboBox cbParallel;
        ToolTip tip;
        System.Windows.Forms.Timer soundTimer;
        StatusStrip status;
        ToolStripStatusLabel statusLabel;
        ToolStripStatusLabel statusEnv;     // OBS/游戏环境状态（底部状态栏右侧，替代顶栏绿字）
        Label lblMemHint;           // 「并行」组里的内存估算小字
        Panel logHeader;            // 终端区顶部的折叠条
        bool logCollapsed = false;
        string lastLogLine = "终端 STDOUT";
        // 自适应布局：左栏被拖宽时，卡片跟着拉伸、按钮行均分行宽（见 LayoutCardWidth）
        List<FlowLayoutPanel> hudCards = new List<FlowLayoutPanel>();   // 需要随左栏宽度拉伸的卡片
        List<Panel> hudTitles = new List<Panel>();                      // 卡片标题条（分隔线要跟着拉宽）
        List<FlowLayoutPanel> hudRows = new List<FlowLayoutPanel>();    // 需要均分按钮宽度的按钮行（队列/环境）
        List<Control> hudFull = new List<Control>();                    // 需要拉伸到整行宽的大按钮
        Panel recIndicator;         // 顶栏左侧的 REC 状态点（自绘）
        Label recLabel;             // "REC" 字样
        System.Windows.Forms.Timer recTimer;   // 录制中呼吸闪烁

        WheelRouter wheelRouter;           // 滚轮分流（装在消息泵上，见构造函数里那段说明）

        static string monoName = null;     // 自动挑出来的等宽字体名（挑不到就 Consolas）
        bool splitRatioLocked = false;     // 防止 ApplySplitRatio 改 SplitterDistance 时又触发 Layout 递归进来
        static Font hudFrameFont;          // "| 队列 / QUEUE |" 这种边框标题用的字体

        public MainForm(string[] args)
        {
            // ⚠ 这行必须是构造函数的第一句。
            //   实测（埋点打的 CreateParams）：句柄是在构造过程中就被创建的，而且**第一次创建时
            //   FormBorderStyle 还是默认的 Sizable** —— 也就是窗口先带着 WS_CAPTION 生出来，
            //   之后再靠 UpdateStyles/SetWindowLong 把标题栏样式抠掉。多数情况下看不出来，
            //   但在某些渲染路径下框架不会重算非客户区，系统标题栏就留在那儿了。
            //   放到最前面，第一次创建就是无边框的。
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;              // 窗体自身双缓冲：拖分割线/缩放时背景图不闪（用户实测的闪烁）

            cfg = Settings.Load();
            Text = "Rep → MP4 录制器（OBS）";
            Font = Ui(9F);                                  // 中文控件走雅黑（等宽字体没有中文字形）
            // 无边框：系统标题栏在深色模式下渲成 #202020，压在 #3C3E45 的界面上就是那条突兀的黑带。
            // 直接不要它，顶栏和最小化/最大化/关闭全部自绘（学 Folia）。
            // ⚠ FormBorderStyle.None 会连窗口样式里的 WS_THICKFRAME 一起拿掉 —— 那样就没了拖边缩放、
            //   Aero Snap、双击最大化、从最大化拖下来还原。所以下面 CreateParams 里单独把它加回来。
            MinimumSize = new Size(760, 520);   // 先定最小尺寸，下面的"按屏幕收口"要用到它
            // ⚠ 默认尺寸不能超过屏幕工作区：小屏（1280×720）上窗口会被顶出去，底部的状态栏和左栏
            //   最后一张卡片整个落到屏幕外，用户根本看不到「就绪」那一条（实测：740 高的窗口压在
            //   720 高的屏幕上）。这里按工作区收口，保证窗口一定整块可见。
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(
                Math.Max(MinimumSize.Width, Math.Min(1180, workArea.Width - 24)),
                Math.Max(MinimumSize.Height, Math.Min(780, workArea.Height - 24)));
            BackColor = HudBg;                              // 蓝黑主题：窗体底色
            ForeColor = HudText;
            ApplyWindowOpacity();                           // 整窗不透明度（设置里可调，默认 85 = 15% 透明）
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            StartPosition = FormStartPosition.CenterScreen;

            // ---- 蓝黑 HUD 风格：左边控制面板 / 右边 rep 列表 / 下面终端 ----
            top = new BgFlowPanel();              // 左边一整列：所有按钮和选项
            // ⚠ 不用 Dock，也不用 AutoScroll：位置要由自绘滚动条来控制（top.Top = -值）。
            //   停靠过的控件位置由布局引擎接管，手动设 Top 不生效。
            top.AutoSize = true;                  // 高度跟着内容走
            top.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            top.Location = new Point(0, 0);
            top.Width = 324;                      // 卡片 316 + 自绘滚动条 10 的余量
            top.FlowDirection = FlowDirection.TopDown;
            top.WrapContents = false;
            top.Padding = new Padding(6, 8, 6, 8);

            btnStart = MakeButton("▶  开始录制", 300, OnStart, HudOn);
            btnStart.Height = 36;
            btnStart.Font = Ui(10F);
            btnStart.Font = new Font(btnStart.Font, FontStyle.Bold);
            btnStop = MakeButton("■  停止", 300, OnStop, HudOff);
            btnStop.Height = 28;
            // ⚠ 按钮禁用时，WinForms 会把文字和背景混成一片暗灰（ControlPaint.DrawStringDisabled），
            //   在浅色按钮上几乎看不见，看上去就像个"空按钮"。所以禁用状态自己补画一遍文字。
            btnStop.Paint += delegate(object s, PaintEventArgs pe)
            {
                if (btnStop.Enabled) return;    // 启用时让按钮自己画
                TextRenderer.DrawText(pe.Graphics, btnStop.Text, btnStop.Font, btnStop.ClientRectangle, HudDim,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPrefix);
            };
            btnClear = MakeButton("清空队列", 146, OnClear, Color.Empty);
            btnRemove = MakeButton("移除选中", 146, OnRemove, Color.Empty);
            // 把当前蓝色选中的几行钉成"本批"（绿色）。以前只有"点列表下方空白处"这一条路，
            // 队列塞满时根本没有空白处可点（用户实测），所以补一个一直能用的入口。
            btnMark = MakeButton("标记本批", 146, OnMarkBatch, Color.Empty);
            btnRefresh = MakeButton("刷新", 146, OnRefreshQueue, Color.Empty);
            btnCheck = MakeButton("检查环境", 146, OnCheck, Color.Empty);
            btnPrepare = MakeButton("准备 OBS", 146, OnPrepare, Color.Empty);
            btnSet = MakeButton("设置…", 146, OnSettings, Color.Empty);
            btnOpenLog = MakeButton("日志", 146, OnOpenLog, Color.Empty);
            btnSound = MakeButton("🔊 声音：开 ✓", 148, OnSoundToggle, Color.Empty);
            btnSound.FlatStyle = FlatStyle.Flat;
            // 说明：旧的 lblSoundNote 已被卡片里的 lblAudioNote 取代且从未加入容器，这里删掉；
            //   它的 tooltip 文案移到 lblAudioNote 上，不丢说明。
            btnGameAudio = MakeButton("🔊 录打击音效：开 ✓", 192, OnGameAudioToggle, Color.Empty);
            btnGameAudio.FlatStyle = FlatStyle.Flat;
            btnBgm = MakeButton("录BGM：关 ✕", 116, OnBgmToggle, Color.Empty);
            btnBgm.FlatStyle = FlatStyle.Flat;

            tip = new ToolTip();
            tip.SetToolTip(btnSound, "点一下切换【整个电脑静音 / 恢复声音】：静音时你自己（以及整个电脑）都听不到声音，\r\n" +
                                     "但录出来的视频仍然有完整的游戏原声（OBS 抓的是游戏进程的音频流，端点静音掐不断它）。录完会自动恢复。");
            tip.SetToolTip(btnGameAudio, "这个按钮只管一件事：【游戏声音（打击音效 / BGM）要不要录进视频】。\r\n" +
                                         "  开 = 游戏声音照实录进视频；\r\n" +
                                         "  关 = 录出来的视频完全没有声音，并且会自动把「录BGM」锁成关（连游戏里的 BGM 也不放）。\r\n" +
                                         "你自己耳朵听不听得到，由左边的「声音」按钮决定。");
            tip.SetToolTip(btnBgm, "这个按钮只管一件事：【播放 rep 时游戏要不要放背景音乐】。\r\n" +
                                      "游戏是靠 ReplayDnD.ini 里的 MuteMusic 决定的，这个按钮只改那一行：\r\n" +
                                      "  开 = MuteMusic 改成 0（有 BGM）；关 = 改回 1（只有音效）。\r\n" +
                                      "BGM 有/没有都会照实录进视频（除非你把「录打击音效」关掉，那样整段都没声音；那样 BGM 也会被锁成关）。\r\n" +
                                      "改完从下一个文件开始生效（游戏是启动时读这个 ini 的）。注意 BGM 有版权风险。");
            tip.SetToolTip(btnPrepare, "启动并连接 OBS：自动创建/检查采集源、把游戏窗口绑上去（勾了画布选项还会设分辨率）");
            tip.SetToolTip(btnRefresh, "重新检查队列里每个 rep 有没有已录好的视频；视频被你删掉的会变回「等待」，可以重新录");
            tip.SetToolTip(btnRefresh, "重新检查队列里每个 rep 有没有已录好的视频；视频被你删掉的会变回「等待」，可以重新录");

            chkAuto = new HudCheck();
            chkAuto.Text = "拖入后自动开始";
            chkAuto.AutoSize = true;
            chkAuto.Margin = new Padding(6, 8, 12, 0);
            chkAuto.Checked = cfg.AutoStartOnDrop;
            chkAuto.CheckedChanged += delegate { cfg.AutoStartOnDrop = chkAuto.Checked; cfg.Save(); };

            chkPrepare = new HudCheck();
            chkPrepare.Text = "自动准备采集源并绑定游戏窗口";
            chkPrepare.AutoSize = true;
            chkPrepare.Margin = new Padding(6, 8, 12, 0);
            chkPrepare.Checked = cfg.AutoPrepareObs;
            chkPrepare.CheckedChanged += delegate { cfg.AutoPrepareObs = chkPrepare.Checked; cfg.Save(); };

            // 画布选项已移到「设置…」里（分类「画布」→ AutoCanvas），主界面不再重复放一个复选框
            chkCloseObs = new HudCheck();
            chkCloseObs.Text = "录完关闭 OBS";
            chkCloseObs.AutoSize = true;
            chkCloseObs.Margin = new Padding(6, 8, 12, 0);
            chkCloseObs.Checked = cfg.CloseObsAfter;
            chkCloseObs.CheckedChanged += delegate { cfg.CloseObsAfter = chkCloseObs.Checked; cfg.Save(); };

            // 轮录：点一次「开始录制」就把队列里所有待录的 rep 一批接一批全部录完（不用守着电脑）
            chkRound = new HudCheck();
            chkRound.Text = "轮录（录完一批自动接着录下一批）";
            chkRound.AutoSize = true;
            chkRound.Margin = new Padding(6, 8, 12, 0);
            chkRound.Checked = cfg.RoundRecord;
            chkRound.CheckedChanged += delegate
            {
                cfg.RoundRecord = chkRound.Checked;
                cfg.Save();
                Log.Write(chkRound.Checked
                    ? "【轮录：开】点「开始录制」会把队列里所有待录的 rep 一批接一批全部录完（每批 = 上面「同时录制数量」个，最多 6 个；最后不足一批的按实际数量录；每批 1 个时走单录依次录）"
                    : "【轮录：关】点「开始录制」只录一批（和以前一样）");
            };

            obsState = new Label();
            obsState.AutoSize = true;
            obsState.ForeColor = Color.DimGray;
            obsState.Margin = new Padding(12, 9, 0, 0);

            // 录音模式：只录游戏声音 / 游戏+麦克风（解说）
            lblMic = new Label();
            lblMic.Text = "录音模式：";
            lblMic.AutoSize = true;
            lblMic.Margin = new Padding(6, 10, 2, 0);
            cbMic = new HudCombo();
            cbMic.Width = 180;
            cbMic.Margin = new Padding(0, 5, 0, 0);
            cbMic.Items.Add("只录游戏声音");
            cbMic.Items.Add("游戏声音 + 麦克风");
            cbMic.SelectedIndex = (cfg.MicMode == "gameAndMic") ? 1 : 0;
            cbMic.SelectedIndexChanged += delegate
            {
                cfg.MicMode = (cbMic.SelectedIndex == 1) ? "gameAndMic" : "gameOnly";
                cfg.Save();
                Log.Write("录音模式已切换为：" + cbMic.Text +
                          (cfg.MicMode == "gameAndMic" ? "（录制时会保持/开启麦克风）" : "（录制时会静音麦克风）"));
                // 队列正在跑（或正在录）时立刻生效，否则这次录制会沿用旧模式
                if (worker != null && worker.IsAlive && obs.IsConnected)
                {
                    Thread t = new Thread(delegate()
                    {
                        try { ApplyMicMode(true); } catch { }
                    });
                    t.IsBackground = true;
                    t.Start();
                }
                ClearComboHighlight(cbMic);
            };

            hint = new Label();
            hint.AutoSize = true;
            hint.Font = new Font(Font, FontStyle.Bold);   // 中文提示 → 跟着窗体的雅黑
            hint.ForeColor = HudAccent;
            hint.Text = "";    // 默认不显示（只在拖文件进来时提示"松手即加入队列"）
            hint.Margin = new Padding(6, 8, 16, 0);
            // 说明：hint 之前被删掉了父容器挂载（旧 row3），现已在左栏末尾补上 top.Controls.Add(hint)

            // tooltip 要等控件建好之后再挂（早挂会 SetToolTip(null) 直接崩）
            tip.SetToolTip(btnStart, "点一下就开始录：\r\n" +
                                     "· 列表里选中了 rep → 只录选中的那些（不管之前有没有录过，一律重录）\r\n" +
                                     "· 没选中 → 录队列里最靠前的待录 rep\r\n" +
                                     "· 要录多个 → 按右边的「同时录制数量」并行，超过就自动分批；只有 1 个就按普通单录走\r\n" +
                                     "（原来的「并行录制」「录制选中的」两个按钮已经并进这个按钮了）");

            // ============ 左栏分组卡片：录制 / 并行 / 队列 / 声音 / 环境 ============
            // ⚠ 卡片必须用 FlowLayoutPanel（TopDown）不能用普通 Panel：
            //   普通 Panel 里标题和内容都默认落在 (0,0) 会叠在一起，内容第一行被标题盖掉（实测）。
            int cardW = 316;                    // 内容按钮 300 + 左右内边距 8+8
            Color cardBack = HudBg;             // 设计图里卡片和窗口底色同色，靠分隔线分区

            // 通用：组标题（图标 + 标题 + 分隔线）。icon 用 "▍" 就是原来的竖条；
            // 录制/并行/声音 按参考图配了专属小图标（◉ / ≡ / 喇叭）
            Func<string, string, Panel> MakeCardTitle = delegate(string icon, string t)
            {
                Panel p = new Panel();
                p.Size = new Size(cardW, 22);
                p.BackColor = Color.Transparent;
                Label lb = new Label();
                lb.Text = icon + " " + t;
                lb.ForeColor = HudText;
                lb.Font = Ui(9F);
                lb.AutoSize = true;
                lb.Location = new Point(2, 2);
                p.Controls.Add(lb);
                hudTitles.Add(p);               // 拉伸时分隔线跟着卡片走（见 LayoutCardWidth）
                p.Paint += delegate(object s, PaintEventArgs pe)
                {
                    using (Pen pen = new Pen(HudLine))
                        pe.Graphics.DrawLine(pen, 0, 20, p.Width - 1, 20);
                };
                return p;
            };

            // 通用：卡片容器（FlowLayoutPanel 自动把标题/内容从上往下排，不会重叠）
            Func<FlowLayoutPanel> MakeCard = delegate()
            {
                FlowLayoutPanel c = new FlowLayoutPanel();
                c.AutoSize = true;
                c.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                c.FlowDirection = FlowDirection.TopDown;
                c.WrapContents = false;
                c.MinimumSize = new Size(cardW, 0);   // 宽度至少 cardW，窄内容也保持整宽
                hudCards.Add(c);                // 左栏拖宽时这张卡片跟着拉伸（见 LayoutCardWidth）
                c.BackColor = cardBack;
                c.Tag = "card";                 // ApplyHudTheme 看到这个标记就跳过，不改它的底色
                c.Margin = new Padding(0, 0, 0, 12);
                c.Padding = new Padding(4, 6, 4, 8);
                return c;
            };

            // 通用：卡片内内容面板（流式布局）
            Func<FlowLayoutPanel> MakeCardContent = delegate()
            {
                FlowLayoutPanel f = new FlowLayoutPanel();
                f.AutoSize = true;
                f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                f.FlowDirection = FlowDirection.TopDown;
                f.WrapContents = false;
                f.BackColor = Color.Transparent;
                f.Margin = new Padding(0);
                f.Padding = new Padding(4, 2, 4, 0);
                return f;
            };

            // ---- ① 录制 ----
            Panel cardRecord = MakeCard();
            cardRecord.Controls.Add(MakeCardTitle("◉", "录 制"));
            FlowLayoutPanel contentRecord = MakeCardContent();
            contentRecord.Controls.Add(btnStart);
            contentRecord.Controls.Add(btnStop);
            cardRecord.Controls.Add(contentRecord);
            top.Controls.Add(cardRecord);

            // ---- ② 并行 ----
            Panel cardParallel = MakeCard();
            cardParallel.Controls.Add(MakeCardTitle("≡", "并 行"));
            FlowLayoutPanel contentParallel = MakeCardContent();
            // 同时录制数量：1~6 的下拉列表
            Label lblPar = new Label();
            lblPar.Text = "同时录制数量：";
            lblPar.AutoSize = true;
            lblPar.Margin = new Padding(0, 4, 2, 0);
            lblPar.ForeColor = HudText;
            lblPar.Font = new Font(Font, FontStyle.Bold);
            cbParallel = new HudCombo();
            cbParallel.Width = 96;
            cbParallel.Margin = new Padding(0, 0, 0, 0);
            ((HudCombo)cbParallel).Face = HudLine;    // 设计图里这个下拉框是钢蓝底
            cbParallel.Font = new Font(Font, FontStyle.Bold);
            for (int n = 1; n <= 6; n++) cbParallel.Items.Add(n.ToString());
            int pn = cfg.ParallelCount;
            if (pn < 1) pn = 1;
            if (pn > 6) pn = 6;
            cbParallel.SelectedIndex = pn - 1;
            cbParallel.SelectedIndexChanged += delegate
            {
                cfg.ParallelCount = cbParallel.SelectedIndex + 1;
                cfg.Save();
                Log.Write("同时录制数量改为 " + cfg.ParallelCount + " 个（内存约 "
                          + (cfg.ParallelCount * 550 / 1024.0).ToString("0.0") + " GB）");
                UpdateMemHint();
                ClearComboHighlight(cbParallel);
            };
            tip.SetToolTip(cbParallel, "一次同时开几个游戏实例（1~6）。\r\n" +
                                       "1 = 相当于普通单录；3 个约 1.6 GB 内存；6 个约 3.3 GB。\r\n" +
                                       "上限锁在 6：OBS 只有 6 条音轨，再多就没法给每格单独放声音，会录出没声音的视频。");
            FlowLayoutPanel parRow = new FlowLayoutPanel();
            parRow.AutoSize = true;
            parRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            parRow.FlowDirection = FlowDirection.LeftToRight;
            parRow.WrapContents = false;
            parRow.BackColor = Color.Transparent;
            parRow.Margin = new Padding(0);
            parRow.Controls.Add(lblPar);
            parRow.Controls.Add(cbParallel);
            contentParallel.Controls.Add(parRow);
            lblMemHint = new Label();
            lblMemHint.AutoSize = true;
            lblMemHint.ForeColor = HudDim;
            lblMemHint.Margin = new Padding(0, 2, 0, 4);
            UpdateMemHint();
            contentParallel.Controls.Add(lblMemHint);
            contentParallel.Controls.Add(chkRound);
            cardParallel.Controls.Add(contentParallel);
            top.Controls.Add(cardParallel);

            // ---- ③ 队列 ----
            Panel cardQueue = MakeCard();
            cardQueue.Controls.Add(MakeCardTitle("▍", "队 列"));
            FlowLayoutPanel contentQueue = MakeCardContent();
            FlowLayoutPanel queueBtns = new FlowLayoutPanel();
            queueBtns.AutoSize = true;
            queueBtns.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            queueBtns.FlowDirection = FlowDirection.LeftToRight;
            queueBtns.WrapContents = false;         // 不换行：拉伸时 4 枚按钮均分一行宽（见 LayoutCardWidth）
            queueBtns.BackColor = Color.Transparent;
            queueBtns.Margin = new Padding(0);
            queueBtns.Controls.Add(btnClear);
            queueBtns.Controls.Add(btnRemove);
            queueBtns.Controls.Add(btnMark);
            queueBtns.Controls.Add(btnRefresh);
            hudRows.Add(queueBtns);
            contentQueue.Controls.Add(queueBtns);
            cardQueue.Controls.Add(contentQueue);
            top.Controls.Add(cardQueue);

            // ---- ④ 声音 ----
            Panel cardSound = MakeCard();
            cardSound.Controls.Add(MakeCardTitle("🔊", "声 音"));
            FlowLayoutPanel contentSound = MakeCardContent();
            // 「声音」按钮独占一行，说明文字放下一行（同排会超出卡片宽度被截断）
            FlowLayoutPanel soundRow1 = new FlowLayoutPanel();
            soundRow1.AutoSize = true;
            soundRow1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            soundRow1.FlowDirection = FlowDirection.LeftToRight;
            soundRow1.WrapContents = false;
            soundRow1.BackColor = Color.Transparent;
            soundRow1.Margin = new Padding(0);
            soundRow1.Controls.Add(btnSound);
            contentSound.Controls.Add(soundRow1);
            Label lblAudioNote = new Label();
            lblAudioNote.AutoSize = true;
            lblAudioNote.ForeColor = HudDim;
            lblAudioNote.Margin = new Padding(0, 2, 0, 4);
            lblAudioNote.Text = "本机静音，录像里照样有声音";
            // 原 lblSoundNote 的 tooltip 文案移到这里（那个标签已删）
            tip.SetToolTip(lblAudioNote, "说明：这个按钮管的是【你自己听不听得到】——它静音的是整台电脑的默认输出设备；\r\n" +
                                         "录像里照样有游戏声音（录不录声音由右边的「录打击音效」决定）。");
            contentSound.Controls.Add(lblAudioNote);
            contentSound.Controls.Add(btnGameAudio);
            contentSound.Controls.Add(btnBgm);
            FlowLayoutPanel micRow = new FlowLayoutPanel();
            micRow.AutoSize = true;
            micRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            micRow.FlowDirection = FlowDirection.LeftToRight;
            micRow.WrapContents = false;
            micRow.BackColor = Color.Transparent;
            micRow.Margin = new Padding(0);
            micRow.Controls.Add(lblMic);
            micRow.Controls.Add(cbMic);
            contentSound.Controls.Add(micRow);
            cardSound.Controls.Add(contentSound);
            top.Controls.Add(cardSound);

            // ---- ⑤ 环境 ----
            Panel cardEnv = MakeCard();
            cardEnv.Controls.Add(MakeCardTitle("▍", "环 境"));
            FlowLayoutPanel contentEnv = MakeCardContent();
            FlowLayoutPanel envBtns = new FlowLayoutPanel();
            envBtns.AutoSize = true;
            envBtns.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            envBtns.FlowDirection = FlowDirection.LeftToRight;
            envBtns.WrapContents = false;           // 不换行：拉伸时 4 枚按钮均分一行宽
            envBtns.BackColor = Color.Transparent;
            envBtns.Margin = new Padding(0);
            envBtns.Controls.Add(btnPrepare);
            envBtns.Controls.Add(btnCheck);
            envBtns.Controls.Add(btnSet);
            envBtns.Controls.Add(btnOpenLog);
            hudRows.Add(envBtns);
            contentEnv.Controls.Add(envBtns);
            contentEnv.Controls.Add(chkAuto);
            contentEnv.Controls.Add(chkPrepare);      // ⚠ 重构时漏加：不补回来「自动准备采集源」就再也点不到了
            contentEnv.Controls.Add(chkCloseObs);
            cardEnv.Controls.Add(contentEnv);
            top.Controls.Add(cardEnv);
            // ⚠ 拖拽提示标签：重构后旧 row3 被删、hint 忘了重新挂到父容器，拖文件时"松手即加入队列 ↓"
            //   永远不显示。补回左栏末尾（和 lblParHint 一起）。
            top.Controls.Add(hint);

            // 自适应拉伸登记：左栏拖宽时这些大按钮铺满整行宽。
            // ⚠ 三个声音开关（声音/录打击音效/录BGM）不放进来：它们跟着拉伸会一会儿宽一会儿窄（用户实测），
            //   保持固定宽度 124/168/116 更稳。
            hudFull.Add(btnStart);
            hudFull.Add(btnStop);

            // 选中几个 rep 就自动把数量改成几个（省得每次去点下拉框），并在旁边说明
            lblParHint = new Label();
            lblParHint.AutoSize = true;
            lblParHint.ForeColor = HudDim;
            lblParHint.Margin = new Padding(4, 10, 0, 0);
            top.Controls.Add(lblParHint);

            // ⚠ 左栏里的卡片 / 各行容器都是普通 FlowLayoutPanel，默认【没开双缓冲】。
            //   它们一重排（改「同时录制数量」时 lblMemHint 文字变化、选中几行让 lblParHint 变化、
            //   滚轮上下滑左栏）就会"先擦背景再画内容"，擦和画之间那一帧露出来就是整片闪
            //   （用户报的"滚轮滑控制面板会闪 / 点队列区域会闪"）。
            //   这里统一给左栏整棵树打开缓冲（和 SplitContainer 用的是同一招），行为一点没改。
            foreach (Control bc in AllControls(top)) BufferSplitter(bc);
            BufferSplitter(top);

            // obsState 已挪到底部状态栏右侧（只当数据源）；hint 见上面，已挂回左栏末尾

            // ---- 列表 + 日志（各自装进带边框的 HUD 面板里）----
            list = new BgListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.HideSelection = false;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = HudBg;
            list.ForeColor = HudText;
            list.Font = Mono(9F);                  // 列表用等宽：文件名 / 数字对齐
            list.OwnerDraw = true;                 // 自己画：表头/行都跟着蓝黑风格
            list.DrawColumnHeader += OnHudDrawColumnHeader;
            list.DrawItem += OnHudDrawItem;
            list.DrawSubItem += OnHudDrawSubItem;
            list.Columns.Add("#", 44);
            list.Columns.Add("文件", 290);
            list.Columns.Add("状态", 84);
            list.Columns.Add("用时", 78);
            list.Columns.Add("大小", 82);
            list.Columns.Add("说明", 200);
            list.DoubleClick += OnListDoubleClick;
            // 空队列提示：⚠ 不能在 list.Paint 里画 —— ListView 原生 WM_PAINT 会在之后把这里画的东西盖掉
            //   （实测：空队列截图里提示文字完全不显示）。改由 BgListView 在自己的 WM_PAINT 末尾画，
            //   这里只把文案交给它，见 BgListView.WndProc。
            // 2026-10-03：用户不要队列里这行字了（"把 .rep 文件拖到这里…"压在背景图上很碍眼）。
            //   机制留着没删：哪天想加回来，把下面这行注释放开就行，BgListView 那边不用动。
            // list.EmptyHint = "把 .rep 文件拖到这里（可多选/可拖文件夹）";

            // 终端：自定义控件（TextBox 的背景不支持透明，透不出背景图）
            logBox = new BgLogView();
            logBox.Dock = DockStyle.Fill;
            logBox.BackColor = HudBg;              // 没有背景图时的底色
            logBox.Font = Mono(9F);                // 终端也用等宽

            Panel frameQueue = MakeFrame("队列 / QUEUE");
            frameQueue.Dock = DockStyle.Fill;
            // 原生滚动条藏起来，改用自绘的（颜色才能跟主题走）。
            // ⚠ 但**不能**用 Scrollable=false：那是给控件打 LVS_NOSCROLL，会让列表**彻底不能滚**，
            //   连 TopItem / LVM_SCROLL 都变成空操作（实测：60 个条目时滚轮、拖自绘滚动条全无反应，
            //   top index 一直 0）。正确做法是保留滚动能力，再用 ShowScrollBar 把原生那条藏掉
            //   （见 HideNativeListScrollbar，comctl32 会自己再把它显出来，所以每次同步滚动条时都要再藏一遍）。
            list.Scrollable = true;
            listScroll = new HudScrollBar();
            listScroll.Dock = DockStyle.Right;
            listScroll.Width = 10;
            listScroll.ValueChanged += delegate
            {
                if (syncingList) return;
                try
                {
                    if (list.Items.Count == 0) return;
                    int i = Math.Min(list.Items.Count - 1, Math.Max(0, listScroll.Value));
                    list.TopItem = list.Items[i];   // 把这一行顶到最上面
                    HideNativeListScrollbar();      // 滚动过程中 comctl32 会想把原生那条显出来
                }
                catch { }
            };
            frameQueue.Controls.Add(list);
            frameQueue.Controls.Add(listScroll);   // 后加的排在更外层 → 先靠右
            list.Resize += delegate { SyncListScroll(); };
            Panel frameLog = MakeFrame("终端 / STDOUT");
            frameLog.Dock = DockStyle.Fill;
            // 终端区：和「队列」同款边框。折叠条放在框内标题下面一条，点它收起/展开
            logHeader = new Panel();
            logHeader.Dock = DockStyle.Top;
            logHeader.Height = 22;
            logHeader.BackColor = HudBg;
            logHeader.Cursor = Cursors.Hand;
            logHeader.Paint += delegate(object s, PaintEventArgs pe)
            {
                pe.Graphics.Clear(HudBg);
                using (Pen p = new Pen(HudLine))
                    pe.Graphics.DrawLine(p, 0, logHeader.Height - 1, logHeader.Width, logHeader.Height - 1);
                string arrow = logCollapsed ? "▶ 展开" : "▼ 收起";
                TextRenderer.DrawText(pe.Graphics, arrow, Ui(8F), new Point(6, 3), HudDim);
                if (logCollapsed && !string.IsNullOrEmpty(lastLogLine))
                {
                    Size sz = TextRenderer.MeasureText(lastLogLine, Ui(8F));
                    int x = logHeader.Width - sz.Width - 12;
                    if (x > 90)
                        TextRenderer.DrawText(pe.Graphics, lastLogLine, Ui(8F), new Point(x, 3), HudDim);
                }
            };
            logHeader.Click += delegate { ToggleLogCollapse(); };
            frameLog.Controls.Add(logBox);         // 先加 Fill，再加 Top：Top 的折叠条先停靠，日志填剩下的
            frameLog.Controls.Add(logHeader);

            split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;   // 上面列表、下面终端
            split.Panel1.Padding = new Padding(6, 6, 6, 3);
            split.Panel2.Padding = new Padding(6, 3, 6, 6);
            split.Panel1.Controls.Add(frameQueue);
            split.Panel2.Controls.Add(frameLog);
            split.BackColor = HudBg;
            split.SplitterWidth = 6;                      // 上下比例由 ApplySplitRatio() 按 3:5 摆
            // splitterDragging：拖分隔条期间为 true（分隔条高亮 + 让 OnPanelSplitterMoved 认出"是手拖的"）。
            // 以前这里还顺手 FreezePaint(true/false)，2026-10-03 整套冻结已废弃，见 WndProc 顶上那段。
            split.SplitterMoving += delegate { splitterDragging = true; };
            split.SplitterMoved += delegate { splitterDragging = false; };
            BufferSplitter(split);
            // 队列和终端之间的缝（分割条 6px + 上下面板各 3px Padding）不能露实色，
            // 否则背景图会被这条横杠拦腰截断（用户实测）。让缝也透出背景图：
            // 分割条、Panel1 底边、Panel2 顶边各补画一遍图 —— 大部分区域反正被子面板盖住，只有缝露出来。
            split.Paint += delegate(object s, PaintEventArgs pe) { Bg.Paint(split, pe.Graphics); };
            split.Panel1.Paint += delegate(object s, PaintEventArgs pe) { Bg.Paint(split.Panel1, pe.Graphics); };
            split.Panel2.Paint += delegate(object s, PaintEventArgs pe) { Bg.Paint(split.Panel2, pe.Graphics); };
            BufferSplitter(split.Panel1);
            BufferSplitter(split.Panel2);

            status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("就绪");
            statusLabel.Spring = true;               // 占满左侧，把环境状态顶到右边
            statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            status.Items.Add(statusLabel);
            // OBS/游戏环境状态：从顶栏挪到状态栏右侧（用户要求去掉顶栏右边的绿字）
            statusEnv = new ToolStripStatusLabel("");
            statusEnv.ForeColor = HudDim;
            status.Items.Add(statusEnv);
            status.BackColor = HudBg;
            status.ForeColor = HudDim;
            statusLabel.ForeColor = HudDim;
            status.Font = Mono(9F);
            status.SizingGrip = false;
            obsState.TextChanged += delegate
            {
                statusEnv.Text = obsState.Text;          // 环境文字照搬到底栏
                statusEnv.ForeColor = obsState.ForeColor;
            };
            obsState.Visible = false;                // 顶栏不再显示，只当数据源用

            // 顶部条：应用名 + 环境状态（像截图里那条状态栏）
            hudTop = new BgPanel();
            hudTop.Dock = DockStyle.Top;
            hudTop.Height = 30;
            hudTop.BackColor = HudPanel;
            // 录制状态指示（REC 呼吸点）：放在「背景…」按钮右边，不挤标题
            recIndicator = new Panel();
            recIndicator.SetBounds(520, 9, 12, 12);
            recIndicator.BackColor = Color.Transparent;
            recIndicator.Paint += delegate(object s, PaintEventArgs pe)
            {
                bool recording = worker != null && worker.IsAlive;
                Color c = recording ? Color.FromArgb(255, 80, 80) : HudDim;
                using (SolidBrush b = new SolidBrush(c))
                    pe.Graphics.FillEllipse(b, 0, 0, 11, 11);
            };
            recTimer = new System.Windows.Forms.Timer();
            recTimer.Interval = 600;
            recTimer.Tick += delegate
            {
                bool recording = worker != null && worker.IsAlive;
                recIndicator.Visible = recording;
                recLabel.Visible = recording;
                recIndicator.Invalidate();
                // 快照预览的保险丝：左键已经松开、而且 800ms 没再变过尺寸，却还挂着快照 ——
                // 说明收尾那条 WM_EXITSIZEMOVE 丢了，赶紧把子控件放回来，绝不能停在"一张图"上。
                // ⚠ WndProc 里那条 `!isResizing && snapActive` 的自检对付不了这种情况：
                //   消息丢了的话 isResizing 一直是 true，它永远不触发。
                // ⚠ 两个条件都要：只看鼠标键的话，那种"程序性连续改尺寸"（没有鼠标按键的场合）
                //   会在拖动中途被误判成结束，快照当场被收掉。
                if (snapActive && (Control.MouseButtons & MouseButtons.Left) == 0
                    && unchecked(Environment.TickCount - lastSnapSizeTick) > 800)
                {
                    isResizing = false;
                    EndSnap();
                    try { OnContentGeometryChanged(); } catch { }
                }
            };
            recLabel = new Label();
            recLabel.Text = "REC";
            recLabel.ForeColor = Color.FromArgb(255, 80, 80);
            recLabel.Font = Tech(9F, FontStyle.Bold);
            recLabel.AutoSize = true;
            recLabel.Location = new Point(536, 8);
            recLabel.BackColor = Color.Transparent;
            recLabel.Visible = false;
            recIndicator.Visible = false;
            hudTop.Controls.Add(recIndicator);
            hudTop.Controls.Add(recLabel);
            recTimer.Start();
            Label hudTitle = new Label();
            hudTitle.Text = "REP → MP4  录制器   (OBS)";
            hudTitle.AutoSize = true;
            hudTitle.ForeColor = HudText;                 // 参考图里标题是浅灰，亮蓝留给"强调"
            hudTitle.Font = Tech(11F, FontStyle.Bold);
            // x=22：和下面「控制面板 / CONTROL」标题的 | 起笔对齐（frame 左沿 6 + 框内 16）
            hudTitle.Location = new Point(22, 6);
            hudTop.Controls.Add(hudTitle);
            // 顶栏那两个按钮：收起/展开面板（学 VS Code 侧边栏）、背景设置。
            // ⚠ x=310 起：标题 Tech 11F 加粗大约 250px 宽（22+250≈272），按钮放它后面，别压住标题
            btnPanel = new Button();
            btnPanel.Text = "◀  收起面板";
            btnPanel.SetBounds(310, 3, 112, 24);
            btnPanel.FlatStyle = FlatStyle.Flat;
            btnPanel.BackColor = HudOn;
            btnPanel.ForeColor = HudText;
            btnPanel.FlatAppearance.BorderColor = HudLine;
            btnPanel.Click += delegate { SetPanelCollapsed(!mainSplit.Panel1Collapsed); };
            hudTop.Controls.Add(btnPanel);

            btnBg = new Button();
            btnBg.Text = "背景…";
            btnBg.SetBounds(428, 3, 80, 24);
            btnBg.FlatStyle = FlatStyle.Flat;
            btnBg.BackColor = HudOn;
            btnBg.ForeColor = HudText;
            btnBg.FlatAppearance.BorderColor = HudLine;
            btnBg.Click += delegate { OnBackgroundSettings(); };
            hudTop.Controls.Add(btnBg);
            // 环境状态（OBS/游戏）已挪到底部状态栏右侧，顶栏不再放绿字
            hudTop.Paint += delegate(object s, PaintEventArgs pe)
            {
                pe.Graphics.Clear(HudPanel);
                using (Pen hudPen1 = new Pen(HudLine))
                    pe.Graphics.DrawLine(hudPen1, 0, hudTop.Height - 1, hudTop.Width, hudTop.Height - 1);
                // 右边一小段刻度装饰线（参考图顶栏那个细节）。⚠ 要让开右边的窗口按钮，
                // 否则那条线会从按钮底下穿过去。
                using (Pen hudPen2 = new Pen(HudLineDim))
                    for (int hx = hudTop.Width - 240; hx < hudTop.Width - CAPBTN_W * 3 - 12; hx += 10)
                        pe.Graphics.DrawLine(hudPen2, hx, 10, hx, 20);
            };

            // 顶栏右边的窗口按钮。无边框窗口没有系统标题栏，这三个得自己画。
            btnMin = new HudCapButton(HudCapButton.KindMin);
            btnMax = new HudCapButton(HudCapButton.KindMax);
            btnClose = new HudCapButton(HudCapButton.KindClose);
            btnMin.Click += delegate { try { WindowState = FormWindowState.Minimized; } catch { } };
            btnMax.Click += delegate
            {
                try
                {
                    WindowState = (WindowState == FormWindowState.Maximized)
                        ? FormWindowState.Normal : FormWindowState.Maximized;
                }
                catch { }
            };
            btnClose.Click += delegate { try { Close(); } catch { } };
            // ⚠ 加的顺序决定 z 序：后加的在上层，所以要 关闭 → 最大化 → 最小化 这样倒着加，
            //   最终呈现才是「最小化 | 最大化 | 关闭」从左到右。
            hudTop.Controls.Add(btnClose);
            hudTop.Controls.Add(btnMax);
            hudTop.Controls.Add(btnMin);
            hudTop.Resize += delegate { LayoutCaptionButtons(); };
            Resize += delegate { SyncCaptionButtons(); };   // 最大化/还原 → 换中间那个按钮的图标

            // 左侧控制面板也装进带边框的面板（| 控制面板 / CONTROL |）
            Panel frameCtrl = MakeFrame("控制面板 / CONTROL");
            frameCtrl.Dock = DockStyle.Fill;
            frameCtrl.Margin = new Padding(0);

            // 左栏 = 视口(leftView) + 自绘滚动条(leftScroll)；内容 top 在视口里上下滑
            leftView = new BgPanel();
            leftView.Dock = DockStyle.Fill;
            leftView.Controls.Add(top);
            leftScroll = new HudScrollBar();
            leftScroll.Dock = DockStyle.Right;
            leftScroll.Width = 10;
            // 左栏这条平时不画出来（内容一直比视口高，常驻一条挺碍眼）；移上去/滚它/拖它才出现。
            leftScroll.AutoHide = true;
            // ⚠ 这里必须填【底色】不是面板色：这条 10px 的列紧挨着的是左栏内容区，
            //   那里是 HudBg(#3C3E45)。填成 HudPanel(#4A4C54) 会比周围亮一档，
            //   反倒露出一条"滚动条的轮廓"（用户第二次报的就是这个）。
            leftScroll.IdleBack = HudBg;
            leftScroll.ValueChanged += delegate { SyncLeftScroll(false); };
            frameCtrl.Controls.Add(leftView);
            frameCtrl.Controls.Add(leftScroll);   // 后加的排在更外层 → 先靠右
            leftView.Resize += delegate { SyncLeftScroll(false); LayoutCardWidth(); };
            top.SizeChanged += delegate { SyncLeftScroll(false); };

            // 左右分栏：左控制面板 / 右内容区。拖中间那条能调宽，拖得够窄就自动收起（学 VS Code）
            mainSplit = new SplitContainer();
            mainSplit.Dock = DockStyle.Fill;
            mainSplit.Orientation = Orientation.Vertical;
            mainSplit.Panel1.Controls.Add(frameCtrl);
            mainSplit.Panel2.Controls.Add(split);
            // 分隔条平时【看不见】：涂成底色，跟左右两边的留白连成一条暗缝（学 VS Code / Folia）。
            // 原来固定涂 HudLineDim，而左栏外框也是 HudLineDim、两者挨着 → 连成一根又长又直的亮条，
            // 蓝色主题下特别显眼（用户报的"笔直的竖条，不好看"）。
            // 拖拽功能一点没少：宽度反而从 4 加到 6，更好抓；光标压上去 / 正在拖时才亮一条细蓝线（见下面 Paint）。
            mainSplit.BackColor = HudBg;
            mainSplit.SplitterWidth = 6;
            // 上下左右留白和右边两栏的外沿一致：左栏是通到底的，上沿对齐队列（6）、
            // 下沿对齐终端（6）。⚠ 底部不能照抄队列的 3 —— 那是队列在右栏上半格的参数，
            // 抄过来左栏底框会比终端底框低 3px（用户配图报的）。
            mainSplit.Panel1.Padding = new Padding(6, 6, 6, 6);
            // 改窗口大小时让左栏保持原宽、由右栏吸收（VS Code 侧边栏就是这个行为），
            // 顺便避免 FixedPanel.None 下缩放把 SplitterDistance 一起缩放掉。
            mainSplit.FixedPanel = FixedPanel.Panel1;
            // ⚠ Panel1MinSize / Panel2MinSize 不能在这里设：此刻控件还没有真实尺寸，
            //   SplitContainer 会校验 SplitterDistance 落不落在两者之间，直接抛异常。放到 Shown 里初始化。
            mainSplit.SplitterMoved += delegate { OnPanelSplitterMoved(); };
            mainSplit.SplitterMoving += delegate { splitterDragging = true; };
            BufferSplitter(mainSplit);
            // 全局背景模式：左栏四周的 Padding 缝和左右分割条也要透图（内容区模式画不出来，无害）
            mainSplit.Panel1.Paint += delegate(object s, PaintEventArgs pe) { Bg.Paint(mainSplit.Panel1, pe.Graphics); };
            BufferSplitter(mainSplit.Panel1);
            // 分隔条自己画：平时只铺底色（暗缝），光标压上去或正在拖的时候中间亮一条 2px 强调蓝。
            // 这样"看不看得见"和"能不能拖"分开了 —— 平时干净，要拖的时候它自己会提示你在这儿。
            mainSplit.Paint += delegate(object s, PaintEventArgs pe)
            {
                try
                {
                    int sw = mainSplit.SplitterWidth;
                    int x = mainSplit.Panel1.Width;
                    if (sw <= 0 || x <= 0) return;
                    using (SolidBrush b = new SolidBrush(HudBg))
                        pe.Graphics.FillRectangle(b, x, 0, sw, mainSplit.Height);
                    // 全局背景模式：这条竖缝也透出图，别在整幅背景图上留一道实色竖杠
                    if (cfg.BgScope == "global" && Bg.Cache != null)
                        Bg.PaintRect(mainSplit, pe.Graphics, new Rectangle(x, 0, sw, mainSplit.Height));
                    if (splitterHover || splitterDragging)
                        using (SolidBrush b2 = new SolidBrush(HudAccent))
                            pe.Graphics.FillRectangle(b2, x + (sw - 2) / 2, 0, 2, mainSplit.Height);
                }
                catch { }
            };
            // 光标压没压在分隔条上。分隔条那块地方不属于 Panel1/Panel2，鼠标消息直接落在
            // SplitContainer 自己身上，所以这两个事件能收到。
            mainSplit.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int x = mainSplit.Panel1.Width;
                bool on = (x > 0 && e.X >= x && e.X < x + mainSplit.SplitterWidth);
                if (on != splitterHover) { splitterHover = on; mainSplit.Invalidate(); }
            };
            mainSplit.MouseLeave += delegate
            {
                if (splitterHover) { splitterHover = false; mainSplit.Invalidate(); }
            };
            mainSplit.SizeChanged += delegate { OnContentGeometryChanged(); };   // 内容区变了，背景图要重排
            // ⚠ 只挂 mainSplit.SizeChanged 是不够的 —— 这正是"图片不跟随分割线"的根因：
            //   拖分隔条时 SplitContainer 自己的尺寸【不变】，变的只有 Panel2。收起/展开同理。
            //   挂 Panel2 的 SizeChanged 才能覆盖所有让内容区变形的路径。
            mainSplit.Panel2.SizeChanged += delegate { OnContentGeometryChanged(); };

            // Ctrl+B：收起 / 展开左边的控制面板
            KeyPreview = true;                       // 焦点在子控件上时也先给窗体处理
            KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.Control && ke.KeyCode == Keys.B)
                {
                    SetPanelCollapsed(!mainSplit.Panel1Collapsed);
                    ke.Handled = true;
                    ke.SuppressKeyPress = true;
                }
            };

            // 收起之后左边留一条窄竖条（学 VS Code 的活动栏），点一下就把面板拉回来
            btnExpand = new Button();
            btnExpand.Text = "▶";
            btnExpand.SetBounds(4, 8, 28, 26);
            btnExpand.FlatStyle = FlatStyle.Flat;
            btnExpand.Click += delegate { SetPanelCollapsed(false); };
            tip.SetToolTip(btnExpand, "展开左边的控制面板");

            activityBar = new BgPanel();
            activityBar.Dock = DockStyle.Left;
            activityBar.Width = 36;
            activityBar.Visible = false;            // 只在收起时出现
            activityBar.Controls.Add(btnExpand);
            activityBar.Paint += delegate(object s, PaintEventArgs pe)
            {
                pe.Graphics.Clear(HudPanel);
                using (Pen pen = new Pen(HudLine))
                    pe.Graphics.DrawLine(pen, activityBar.Width - 1, 0, activityBar.Width - 1, activityBar.Height);
                // 竖排文字：TextRenderer 不吃 Graphics 的旋转变换，这里必须用 DrawString
                if (barFont == null) barFont = Ui(9F);
                Graphics g = pe.Graphics;
                System.Drawing.Drawing2D.GraphicsState st = g.Save();
                g.TranslateTransform(0, activityBar.Height);
                g.RotateTransform(-90F);
                using (SolidBrush br = new SolidBrush(HudDim))
                    g.DrawString("控制面板 / CONTROL", barFont, br, 10F, 10F);
                g.Restore(st);
            };

            // ⚠ 加的顺序决定停靠层次：后加的排在更外层。所以要 mainSplit → activityBar → status → hudTop，
            //   结果才是「顶栏 / 底栏 / 左侧活动栏 / 中间内容」。
            Controls.Add(mainSplit);
            Controls.Add(activityBar);
            Controls.Add(status);
            Controls.Add(hudTop);

            // 整体套上蓝黑主题 + 窗口标题栏也变深色
            ApplyHudTheme(this);
            EnableDarkTitleBar();
            // 主题之后再钉一次尺寸与强调色（FlowLayoutPanel 里下拉框容易被挤小/被主题改掉）
            try
            {
                cbParallel.Width = 96;
                cbParallel.BackColor = HudSel;
                cbParallel.ForeColor = Color.White;
                cbMic.Width = 210;
                lblParHint.ForeColor = HudDim;
                if (hint != null) hint.ForeColor = HudAccent;
            }
            catch { }

            // 上下分栏固定 3:5（列表 3 / 终端 2），窗口一改尺寸就重算
            split.Resize += delegate { ApplySplitRatio(); };
            Layout += delegate { ApplySplitRatio(); };
            Shown += delegate
            {
                // 三处滚动条现在都是自绘的，不再需要给系统控件换主题了
                SyncLeftScroll(false);
                SyncListScroll();
                SyncCaptionButtons();    // 顶栏宽度此刻才定下来，窗口按钮要现在才摆得对
                // ⚠ 背景图【不能】在这里建：此刻 InitPanelLayout 还没跑，SplitterDistance 还是
                //   默认的 50，算出来的内容区是错的 —— 实测这张图建在 X=54、宽 1126，
                //   而面板归位后正确值是 X=380、宽 800。图上画的东西全错位（用户报的
                //   "图片不跟随分割线"根因之一）。交给 InitPanelLayout 末尾统一建。
                BeginInvoke((MethodInvoker)delegate
                {
                    ApplySplitRatio();
                    InitPanelLayout();
                    LayoutCardWidth();       // 布局定下来后按实际视口宽铺一遍按钮
                    // 顶栏两个按钮紧跟标题右缘（标题实际宽度要等布局出来才知道，写死会留大空隙）
                    btnPanel.Left = hudTitle.Right + 16;
                    btnBg.Left = btnPanel.Right + 6;
                    recIndicator.Left = btnBg.Right + 14;
                    recLabel.Left = recIndicator.Right + 4;
                    InstallChromeHooks();    // 布局定下来才知道哪些子窗口压在了四条边/顶栏上
                });
            };

            Log.OnLine += OnLogLine;
            FormClosing += OnFormClosing;
            // 列表里选中几个 rep，「同时录制数量」就跟着变（用户要求）
            list.ItemSelectionChanged += delegate
            {
                if (suppressSelSync) return;
                if (list.SelectedItems.Count > 0) pinnedBatch.Clear();   // 用户重新选了 → 之前的绿色标记作废
                SyncParallelCountToSelection();
            };
            // 点列表下方空白处时，ListView 默认会【清空选择】—— 用户明确要求"选中的文件要保持选中"。
            // ⚠ 实测：清空发生在【鼠标抬起】那一步，所以在 MouseDown 里恢复会被随后的 Up 覆盖；
            //   这里 Down/Up 都挂，并且用 BeginInvoke 推迟到本次消息处理完之后再恢复。
            // ⚠ 恢复时必须先 suppressSelSync：逐条取消/重选会各触发一次 ItemSelectionChanged，
            //   把"数量跟随选择"打得一路乱跳（用户实测日志里那串 5→4→3→2→1 就是这么来的）。
            list.MouseDown += delegate(object s, MouseEventArgs me) { KeepSelectionOnEmptyClick(me); };
            list.MouseUp += delegate(object s, MouseEventArgs me) { KeepSelectionOnEmptyClick(me); };
            // 框选：在**行上**按住左键拖也能框（详见 MarqueeDown/Move/Up 那段注释）
            list.MouseDown += delegate(object s, MouseEventArgs me) { MarqueeDown(me); };
            list.MouseMove += delegate(object s, MouseEventArgs me) { MarqueeMove(me); };
            list.MouseUp += delegate(object s, MouseEventArgs me) { MarqueeUp(me); };
            // 鼠标滚轮滚过「同时录制数量」时不要改它 —— WinForms 默认会一格格改，
            // 用户实测：滚动列表时顺手把数量从 6 滚成了 1（日志里那一串 5→4→3→2→1）。
            cbParallel.MouseWheel += delegate(object s, MouseEventArgs me)
            {
                HandledMouseEventArgs h = me as HandledMouseEventArgs;
                if (h != null) h.Handled = true;
            };

            // 改窗口大小时重建背景图。防抖 200ms —— 拖边框时每帧重建一张上千像素的位图太慢
            bgTimer = new System.Windows.Forms.Timer();
            bgTimer.Interval = 200;
            bgTimer.Tick += delegate { bgTimer.Stop(); RebuildBackground(); };
            SizeChanged += delegate { OnContentGeometryChanged(); };

            // 滚轮分流：挂在【消息泵】那一层，不再靠窗体自己的 MouseWheel 事件。
            // 为什么换掉原来那段"窗体收到滚轮再按光标位置转发"（实测过，不是猜的 —— 写了个独立探针量）：
            //   · WinForms 只把 WM_MOUSEWHEEL 交给【有焦点】的那个控件；
            //   · 光标停在队列列表上时，消息只到 ListView，【不会冒泡】到窗体
            //     —— 原来挂在窗体上的那段转发代码根本不会执行（这就是"滚轮滚不动队列"的根因）；
            //   · 三处自绘滚动条都是普通 Control（不是可选中的控件），永远拿不到焦点，
            //     光标停在滚动条上时连 WM_MOUSEWHEEL 都到不了它自己身上。
            // IMessageFilter 看得到所有发给本线程窗口的消息，按光标位置分流才不会漏。
            wheelRouter = new WheelRouter(this);
            Application.AddMessageFilter(wheelRouter);

            // 每 2 秒同步一次"系统声音是否静音"，让按钮颜色/文字始终反映真实状态
            soundTimer = new System.Windows.Forms.Timer();
            soundTimer.Interval = 2000;
            soundTimer.Tick += delegate { RefreshSoundButtons(); };
            soundTimer.Start();

            Shown += delegate
            {
                cfg.Save();
                RefreshEnvLabel();
                RefreshSoundButtons();
                RefreshGameAudioButton();
                // 启动时「停止」必须是灰的：没在录制就不该能点（用户实测过空队列点停止把状态卡住）
                btnStop.Enabled = false;
                btnStart.Enabled = true;
                SetStatusText("就绪");
                RestoreSoundIfLeftMuted();   // 上次若是"静音录制"中被强杀，这里把声音恢复回来
                Log.RotateOnStart();   // 先保住上一次的日志（出错时才有记录可查）
                Log.Write("==== Rep → MP4 录制器启动 ====");
                Log.Write("游戏目录：" + (string.IsNullOrEmpty(cfg.GameDir) ? "<未找到>" : cfg.GameDir));
                Log.Write("OBS     ：" + (string.IsNullOrEmpty(cfg.ObsExe) ? "<未找到>" : cfg.ObsExe));
                SyncBgmFromIni();
                if (args != null && args.Length > 0)
                {
                    AddPaths(args);
                    if (cfg.AutoStartOnDrop) StartRecording();
                }
            };
        }

        // 在合并录像里"逐帧"找这一格什么时候开始有画面（精确到 0.2 秒）。
        // ⚠ 为什么不在录制时靠 OBS 截图判断：实测录制刚开头那十几秒，GetSourceScreenshot 一直返回黑帧
        //   （源还没真正开始出图），拿它当"画面出现时刻"会把对局开头十几秒剪掉 —— 用户实测：每局最开始都没了。
        //   改成录完之后切出这一格的画面、按亮度逐帧找：找不到就返回 0（= 不剪，宁可多留黑也不误删画面）。
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

        // 读 mp4 的真实时长：直接解析 moov→mvhd（不需要 ffprobe，也不用抓子进程输出，稳且快）。
        // 列表里的「用时」就显示这个值 —— 用户要看的是这条视频多长，不是任务跑了多久。
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

        // 把秒数说成人话：45秒 / 2分13秒 / 1小时02分03秒
        static string FmtDur(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            long t = (long)Math.Round(seconds);
            long h = t / 3600, m = (t % 3600) / 60, s = t % 60;
            if (h > 0) return h + "小时" + m.ToString("00") + "分" + s.ToString("00") + "秒";
            if (m > 0) return m + "分" + s.ToString("00") + "秒";
            return s + "秒";
        }

        // ============================================================ 设计图那套深蓝灰（2026-10 按设计图取色重做）
        // 设计图（图二）整体是一块**统一的深蓝灰**：窗口/卡片/按钮/队列/终端全是 #272E3A 上下，
        // 结构全靠细的蓝灰描边撑，亮色几乎不用。所以：
        //   · 底色 #272E3A；顶栏/状态栏亮一档 #2E3644；输入框再亮一档 #3A4557
        //   · 边框 #475979（设计图里那圈蓝灰线）；暗线 #38425A
        //   · 正文 #E6EBF4、次要 #93A0B4 —— 分区标题/表头/提示文字在设计图里都是浅色，不再是紫/蓝
        //   · 按钮（开始录制/开关）在设计图里是"暗底 + 描边 + 白字"，不再用亮紫整块填色
        //   · 强调蓝只留给勾选框/状态点；薄荷绿压暗，只用于"成功"状态字
        // ⚠ 底色一动，HudDim/HudLineDim 的取值就得跟着重挑，别只改 HudBg。
        internal static readonly Color HudBg = Color.FromArgb(39, 46, 58);       // #272E3A  底色（设计图主色：窗口/卡片/队列/终端统一）
        internal static readonly Color HudPanel = Color.FromArgb(46, 54, 68);    // #2E3644  顶栏（比底色亮一点）
        internal static readonly Color HudPanel2 = Color.FromArgb(58, 69, 87);   // #3A4557  下拉框/输入框底
        internal static readonly Color HudLine = Color.FromArgb(71, 89, 121);    // #475979  边框（设计图那圈蓝灰线）
        internal static readonly Color HudLineDim = Color.FromArgb(56, 66, 90);  // #38425A  暗线 / 刻度线 / 面板外框
        internal static readonly Color HudCorner = Color.FromArgb(90, 110, 140); // #5A6E8C  面板四个角的角标（比外框亮一档）
        internal static readonly Color HudText = Color.FromArgb(230, 235, 244);  // #E6EBF4  正文
        internal static readonly Color HudDim = Color.FromArgb(147, 160, 180);   // #93A0B4  次要文字
        // 强调色：勾选框 / 状态点 / 滚动条拖动中。设计图里亮色用得极少，别到处撒。
        internal static readonly Color HudAccent = Color.FromArgb(79, 116, 216);      // #4F74D8  强调蓝（勾选框那处）
        internal static readonly Color HudAccent2 = Color.FromArgb(55, 200, 160);     // #37C8A0  薄荷绿：成功状态字（压暗版）
        internal static readonly Color HudSel = Color.FromArgb(58, 74, 107);     // #3A4A6B  选中行
        internal static readonly Color HudOn = Color.FromArgb(46, 55, 70);       // #2E3746  按钮「开/主操作」底（设计图：暗底描边，不再亮紫）
        internal static readonly Color HudOff = Color.FromArgb(42, 49, 64);      // #2A3140  开关按钮「关」
        internal static readonly Color HudBatch = Color.FromArgb(46, 74, 62);    // #2E4A3E  列表里"本批"那几行的绿底
        internal static readonly Color HudBatchBar = Color.FromArgb(55, 200, 160);    // 和 HudAccent2 同一个薄荷绿
        internal static readonly Color HudHead = Color.FromArgb(51, 59, 74);     // #333B4A  列表表头底（设计图表头行）
        internal static readonly Color HudLog = Color.FromArgb(169, 194, 232);   // #A9C2E8  终端文字（设计图浅蓝灰）
        internal static readonly Color HudRed = Color.FromArgb(0xFF, 0x67, 0x67);     // #ff6767  失败
        internal static readonly Color HudOrange = Color.FromArgb(0xFF, 0xCA, 0x85);  // #ffca85  注意

        // ---- 字体：中文控件走雅黑，终端/表头/边框标题走等宽
        // （等宽字体没有中文字形，硬套上去中文会回退成雅黑、和西文混排时字重基线都对不齐）
        static string MonoName()
        {
            if (monoName != null) return monoName;
            string[] want = new string[] { "Sarasa Mono SC", "Sarasa Term SC", "Cascadia Mono", "Cascadia Code", "JetBrains Mono", "Consolas" };
            foreach (string n in want)
            {
                try
                {
                    using (Font f = new Font(n, 9F))
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) { monoName = n; break; }
                }
                catch { }
            }
            if (monoName == null) monoName = "Consolas";
            return monoName;
        }

        static Font Mono(float size) { return new Font(MonoName(), size); }
        static Font Mono(float size, FontStyle st) { return new Font(MonoName(), size, st); }
        static Font Ui(float size) { return new Font("Microsoft YaHei UI", size); }

        // 标题（顶栏大标题 / 面板框标题）用的"科技感"字体。
        // ⚠ 参考图里那套是**点阵字体**，这台机器上没装、也没网下载，所以只能挑最接近的窄体工业字：
        //   Bahnschrift SemiCondensed（Win10 自带，窄、正）→ Bahnschrift → OCR A Extended（仪表盘刻字感）
        //   → 都没有就退回等宽字体。中文没有字形会回退雅黑，所以这个字体只用在标题上。
        static string techName;
        static string TechName()
        {
            if (techName != null) return techName;
            string[] want = new string[] { "Bahnschrift SemiCondensed", "Bahnschrift Condensed", "Bahnschrift", "OCR A Extended" };
            foreach (string n in want)
            {
                try
                {
                    using (Font f = new Font(n, 10F))
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) { techName = n; break; }
                }
                catch { }
            }
            if (techName == null) techName = MonoName();
            return techName;
        }
        static Font Tech(float size) { return new Font(TechName(), size); }
        static Font Tech(float size, FontStyle st) { return new Font(TechName(), size, st); }

        // 带边框和标题的面板：参考图里 | 队列 / QUEUE | 那种框
        Panel MakeFrame(string title)
        {
            if (hudFrameFont == null) hudFrameFont = Tech(10.5F, FontStyle.Bold);
            Panel p = new BgPanel();
            p.Tag = "hudframe";                 // ApplyHudTheme 看到这个标记就跳过，不改它的底色
            p.BackColor = HudBg;                // 设计图：框内和窗口同色，只留描边
            p.Padding = new Padding(2, 24, 2, 2);
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Rectangle rc = p.ClientRectangle;
                int r = rc.Width - 1, b = rc.Height - 1;
                // 边框学参考图：整圈线压到几乎看不见（只当轮廓），真正看得出来的是四个角的角标
                using (Pen pen = new Pen(HudLineDim))
                    g.DrawRectangle(pen, 0, 0, r, b);
                using (Pen penC = new Pen(HudCorner))
                {
                    const int arm = 12;                         // 角标每条臂的长度
                    g.DrawLine(penC, 0, 0, arm, 0); g.DrawLine(penC, 0, 0, 0, arm);
                    g.DrawLine(penC, r - arm, 0, r, 0); g.DrawLine(penC, r, 0, r, arm);
                    g.DrawLine(penC, 0, b, arm, b); g.DrawLine(penC, 0, b, 0, b - arm);
                    g.DrawLine(penC, r - arm, b, r, b); g.DrawLine(penC, r, b, r, b - arm);
                }
                // 标题：浅灰字 + 底下一块"擦掉框线"的底色，后面拖一条细线到右边
                // ⚠ 左边要留出角标的 12px，不然标题底色会把左上角那笔擦掉
                string text = "| " + title + " |";
                Size sz = TextRenderer.MeasureText(g, text, hudFrameFont);
                using (SolidBrush br = new SolidBrush(HudBg))
                    g.FillRectangle(br, 14, 0, sz.Width + 4, 20);
                TextRenderer.DrawText(g, text, hudFrameFont, new Point(16, 3), HudText);
                int x = 16 + sz.Width + 8;
                if (x < r - 14)
                    using (Pen pen2 = new Pen(HudLine))
                        g.DrawLine(pen2, x, 10, r - 12, 10);
            };
            return p;
        }

        // 开关按钮（声音 / 录打击音效 / 录BGM）统一的"开/关"外观
        static void HudSetToggle(Button b, bool on)
        {
            if (b == null) return;
            b.BackColor = on ? HudOn : HudOff;
            b.ForeColor = on ? HudText : HudDim;
            b.FlatAppearance.BorderColor = HudLine;
            b.FlatStyle = FlatStyle.Flat;
        }

        // 下拉框现在整只自绘，见文件末尾的 HudCombo。这里只留字体，其余样式在 HudCombo 里。

        // 有背景图时把不透明色压成半透明，让图透出来；没图时原样返回（保证观感不变）
        static Color Alpha(Color c, int a)
        {
            if (Bg.Cache == null) return c;
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        // 把"语义色"（绿=开 / 红=停 / 灰=关）换成蓝黑主题里的对应色
        static Color HudMapBack(Color c)
        {
            if (c == Color.FromArgb(0, 120, 60)) return HudOn;
            if (c == Color.FromArgb(160, 60, 60)) return HudOff;
            if (c == Color.FromArgb(190, 60, 60)) return HudOff;
            if (c == Color.FromArgb(120, 120, 120)) return HudOff;
            if (c == Color.FromArgb(180, 180, 180)) return HudPanel2;
            if (c == Color.FromArgb(255, 236, 179)) return HudPanel2;
            return c;
        }

        // 递归给所有控件套上蓝黑主题（按钮/复选框/下拉框/标签/面板）
        void ApplyHudTheme(Control root)
        {
            try
            {
                foreach (Control c in root.Controls)
                {
                    Button b = c as Button;
                    if (b != null)
                    {
                        // 比色时要先把 alpha 剥掉：否则第二次 ApplyHudTheme 会把半透明色误判成"有语义色"
                        Color bc0 = b.BackColor;
                        bool colored = bc0 != SystemColors.Control && bc0 != Color.Empty && bc0 != Color.Transparent &&
                                       Color.FromArgb(bc0.R, bc0.G, bc0.B) != HudPanel;
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = HudLine;
                        b.FlatAppearance.BorderSize = 1;
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 74, 107);
                        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(48, 62, 90);
                        if (colored) b.BackColor = Alpha(HudMapBack(Color.FromArgb(bc0.R, bc0.G, bc0.B)), 200);
                        else b.BackColor = Alpha(HudPanel, 195);   // 够透能看见图，又不会淡到看不清字
                        // 灰字（「录BGM」被锁时）保留，别被刷掉
                        if (b.ForeColor != Color.White && b.ForeColor != Color.DimGray && b.ForeColor != HudDim)
                            b.ForeColor = HudText;
                        b.Font = Ui(9F);
                    }
                    CheckBox cb = c as CheckBox;
                    if (cb != null)
                    {
                        cb.ForeColor = HudText;
                        cb.BackColor = Color.Transparent;
                        cb.FlatStyle = FlatStyle.Flat;
                        cb.Font = Ui(9F);
                    }
                    ComboBox cmb = c as ComboBox;
                    if (cmb != null) cmb.Font = Ui(9F);   // 其余样式由 HudCombo 自己负责
                    Label lb = c as Label;
                    if (lb != null)
                    {
                        if (lb.BackColor != Color.Transparent) lb.BackColor = Color.Transparent;
                        if (lb.ForeColor != HudAccent && lb.ForeColor != HudAccent2)
                        {
                            bool dim = lb.ForeColor == Color.DimGray;
                            lb.ForeColor = dim ? HudDim : HudText;
                        }
                    }
                    FlowLayoutPanel flp = c as FlowLayoutPanel;
                    // 卡片（Tag="card"）：有背景图才整块透明让图透出；没图时保持实色 HudBg
                    if (flp != null)
                        flp.BackColor = (flp.Tag as string == "card" && Bg.Cache == null) ? HudBg : Color.Transparent;
                    Panel pn = c as Panel;
                    // MakeFrame 造出来的面板自己要留 HudPanel 底 + 蓝色边框，别被刷成 HudBg
                    bool isFrame = pn != null && pn.Tag is string && (string)pn.Tag == "hudframe";
                    bool isCard = pn != null && pn.Tag is string && (string)pn.Tag == "card";
                    // 普通 Panel（卡片标题条 / 终端收起横条等）：有背景图时整块透明让图透出，
                    // 不再是一条条实色"黑带"；没图时保持 HudBg，不用背景的用户观感不变。
                    if (pn != null && pn != hudTop && !isFrame && !isCard)
                        pn.BackColor = (Bg.Cache != null) ? Color.Transparent : HudBg;
                    ApplyHudTheme(c);
                }
            }
            catch { }
        }

        // 无边框但仍然是"可缩放窗口"。
        // 只把 WS_THICKFRAME 加回来，【不加 WS_CAPTION】—— 有了 WS_CAPTION 系统就又去画标题栏了，
        // 白改。有 WS_THICKFRAME 才有：拖边缘缩放、贴边分屏（Aero Snap）、双击顶栏最大化、
        // 从最大化状态往下拖自动还原。这几样自己实现又脆又难测，交给系统更稳。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // ⚠ 这里要【保留】WS_CAPTION，不能清掉。
                //   DWM 只把"带框窗口"当正常窗口看 —— 最小化/最大化的过渡动画、投影、圆角
                //   都靠这个样式。清掉它（或者用 FormBorderStyle.None）窗口就变成"无框"的，
                //   动画就没了（实测：老版本过渡帧 5 个，清掉 WS_CAPTION 的版本只有 2 个）。
                //   标题栏之所以看不见，是靠 WM_NCCALCSIZE 把客户区撑满整个窗口实现的 ——
                //   非客户区是零，系统根本没地方画标题栏。VS Code 那类自定义标题栏也是这么做的。
                cp.Style |= 0x00C00000;    // WS_CAPTION = WS_BORDER | WS_DLGFRAME
                cp.Style |= 0x00040000;    // WS_THICKFRAME   可缩放边框
                // ⚠ WS_EX_COMPOSITED 两轮实测都不行：既压不住缩放闪，还让每次细小重绘（鼠标 hover）
                //   都整窗走离屏合成，半透明按钮/下拉框反而"鼠标划过就闪"。防闪全靠 WndProc 里的三层冻结。
                cp.Style |= 0x00080000;    // WS_SYSMENU      顶栏右键出系统菜单
                cp.Style |= 0x00020000;    // WS_MINIMIZEBOX
                cp.Style |= 0x00010000;    // WS_MAXIMIZEBOX
                return cp;
            }
        }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
        const uint MONITOR_DEFAULTTONEAREST = 2;
        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO
        {
            public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct NCCALCSIZE_PARAMS
        {
            public RECT rgrc0, rgrc1, rgrc2;
            public IntPtr lppos;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
        }

        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_NCCALCSIZE = 0x0083;
        const int WM_NCHITTEST = 0x0084;
        const int HTCLIENT = 1, HTCAPTION = 2;
        const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                  HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        const int GRIP = 6;        // 缩放热区宽度（客户区坐标；这台机器 300% 缩放，物理上是 18px）

        protected override void WndProc(ref Message m)
        {
            // ⚠⚠ 2026-10-03：「整棵树 WM_SETREDRAW=0」那套"三层冻结"已彻底废弃，别再捡回来。
            //   实测（对窗体发 WM_SETREDRAW(0) 之后查状态，三次采样）：
            //     正常   WS_VISIBLE=True   IsWindowVisible=True   WindowFromPoint(窗口中心)=自己
            //     冻结中 WS_VISIBLE=False  IsWindowVisible=False  WindowFromPoint(窗口中心)=别的窗口
            //     解冻后 WS_VISIBLE=True   IsWindowVisible=True   WindowFromPoint(窗口中心)=自己
            //   也就是 WM_SETREDRAW(FALSE) 走到 DefWindowProc 时会把 WS_VISIBLE 抹掉（官方文档 Note 里也写了），
            //   于是窗口在系统眼里"不存在"了 —— 正好对上用户报的两条现场：
            //     ① 任务栏只给"可见窗口"显示按钮 → 拖窗口期间按钮直接消失，松手才回来；
            //     ② 窗口不参与命中测试 → 点哪儿都穿到后面的窗口上，看着就是"整个软件卡死了"。
            //   更要命的是它【会漏】：SplitterMoving 一旦没等到配对的 SplitterMoved（拖到一半松手在外面、
            //   点那条竖线时最容易），冻结就永久留在那儿 —— 窗口从此再也点不动（用户实测的复现方式）。
            //   LockWindowUpdate 那条路早先也试过，压不住子控件自己往屏幕画，同样不考虑。
            //   现在改成：拖动期间【照常实时重绘】，背景图靠 Bg.PaintRect 的"拉伸预览"实时变形，
            //   整份代码里不再有任何"禁重绘"状态，也就没有漏的地方。
            //
            // ⚠⚠ 2026-10-03 第二版：光"照常重绘"还是卡。实测（同一台机器、全屏大小、每步一次缩放）：
            //     空壳窗体 + 背景图                13 ms/步
            //     RepRecorder（现在这样，全量重绘） 48 ms/步   ← 约 20fps，用户说的"卡"
            //     RepRecorder（把绘制全关掉）       18 ms/步
            //     加 WS_EX_COMPOSITED              47 ms/步（没用）
            //   也就是说这 48ms 里我们自己画的部分只有 ~30ms，剩下的全是"每步把 74 个子窗口
            //   重新布局 + 重新画 + 逐个提交给合成器"。WinForms 里"边拖边完整重绘"的天花板就在这儿。
            //   所以拖动期间改成放"整窗快照"：顶层子控件临时藏掉（隐藏的控件既不参与布局也不重绘），
            //   窗体自己把按下那一刻拍下的整窗画面按新尺寸铺满 —— 每步只剩一次 blit（≈13ms）。
            //   视觉上就是 macOS 那种缩放：整幅画面跟着鼠标实时缩放，文字略糊，松手瞬间恢复锐利。
            //   对照实验里 DPI 感知/合成层都试过了，都不如这条（数据见 _看片 附的说明）。
            if (m.Msg == 0x0231)             // WM_ENTERSIZEMOVE
            {
                isResizing = true;
                ArmSnap();                   // 先拍一张（这个时候窗口还没变过大小，拍下来才是完整的）
            }
            else if (m.Msg == 0x0232)        // WM_EXITSIZEMOVE
            {
                isResizing = false;
                EndSnap();                   // 恢复正常绘制（子控件放回来）
                OnContentGeometryChanged();  // 刷新裁剪区 + 排一次重建（拖动中间不重建，见 ScheduleBgRebuild）
            }
            // 保险丝：拖动早就结束了（WM_EXITSIZEMOVE 万一没送到），快照还挂着 → 立刻收工。
            // 这一步只是把 Visible 放回去，放两次也没事，不会像以前 WM_SETREDRAW 那样把窗口搞成点不动。
            if (!isResizing && snapActive) EndSnap();
            // 真的开始变尺寸了才启用快照：只是拖标题栏挪位置的话（尺寸没变）完全用不上，什么都别动。
            if (m.Msg == 0x0005 && isResizing)                                           // WM_SIZE
            {
                lastSnapSizeTick = Environment.TickCount;
                if (snapArmed && !snapActive) BeginSnap();
                // ⚠ 这一句不能省：子控件藏起来之后没人再触发重绘（WinForms 默认不在改变大小时
                //   整窗失效，ResizeRedraw 也没开），画面会停在旧尺寸那一帧上、看着像卡住。
                //   每步主动失效一次，正好就是"一张图按新尺寸铺满"的那一次 blit。
                if (snapActive) Invalidate();
            }

            // 只在拖窗口期间吞掉窗体擦背景：系统每帧用底色刷新区、和子控件重画叠在一起就是闪烁。
            // ⚠ 不能无条件吞：hover 重绘也走这条消息，全局吞掉会造成"鼠标划过就闪"（用户实测）。
            if (m.Msg == 0x0014 && isResizing) { m.Result = (IntPtr)1; return; }   // WM_ERASEBKGND
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // 客户区 = 整个窗口 —— 系统标题栏就没地方画了（WS_CAPTION 虽然留着，
                // 但非客户区是零，一条 1px 的边框都留不下）。
                // ⚠ 不处理这段的话，窗口顶部会留一条 7px 的 #202020 深色带（实测真实屏幕和
                //   PrintWindow 都有），正是用户要去掉的那条东西。
                // 代价：系统自带的缩放热区没了，改由下面的 WM_NCHITTEST 自己报。
                //
                // 例外：最大化的时候要减掉那一圈边框。
                //   保留了 WS_CAPTION 之后，系统给最大化窗口的矩形会比工作区大一圈
                //   （实测 1296x688 vs 工作区 1280x672，四边各多 8px，那圈是给"拖下来还原"用的）。
                //   客户区不跟着缩的话，内容会被屏幕四边各切掉 8px。窗口本身大一点无所谓 ——
                //   它整个在屏幕外，看不见。
                if (IsZoomed(Handle))
                {
                    NCCALCSIZE_PARAMS ncp = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                    int fx = GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
                    int fy = GetSystemMetrics(SM_CYSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
                    ncp.rgrc0.Left += fx;
                    ncp.rgrc0.Top += fy;
                    ncp.rgrc0.Right -= fx;
                    ncp.rgrc0.Bottom -= fy;
                    Marshal.StructureToPtr(ncp, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_GETMINMAXINFO)
            {
                // ⚠ 必须先调 base：WinForms 在这里把 MinimumSize / MaximumSize 写进 ptMinTrackSize/
                //   ptMaxTrackSize。拦下来不调 base 的话最小尺寸限制就没了（还能拖到比 760x520 更小）。
                base.WndProc(ref m);
                // 无边框窗口最大化时默认会连任务栏一起盖住（无边框窗口的通病）。
                // 把"最大化"的尺寸改写成显示器【工作区】，行为就和普通窗口一模一样。
                try
                {
                    IntPtr mon = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(mon, ref mi))
                    {
                        MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                        mmi.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                        mmi.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                        mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                        mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                        Marshal.StructureToPtr(mmi, m.LParam, false);
                        m.Result = IntPtr.Zero;
                    }
                }
                catch { }
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                // ⚠ lParam 是【屏幕坐标】，x 在低 16 位、y 在高 16 位；必须按 short 取，
                //   否则副屏/负坐标会被解成很大的正数。
                int lp = m.LParam.ToInt32();
                Point cp = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));

                // 1) 四条边 + 四个角 = 缩放热区。
                //    WM_NCCALCSIZE 把系统那圈边框去掉了，热区只能自己报，否则窗口拖不动大小。
                //    最大化时不许缩放（和系统行为一致）。
                if (!IsZoomed(Handle))
                {
                    bool l = cp.X < GRIP, r = cp.X >= ClientSize.Width - GRIP;
                    bool t = cp.Y < GRIP, b = cp.Y >= ClientSize.Height - GRIP;
                    if (t && l) { m.Result = (IntPtr)HTTOPLEFT; return; }
                    if (t && r) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                    if (b && l) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                    if (b && r) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                    if (l) { m.Result = (IntPtr)HTLEFT; return; }
                    if (r) { m.Result = (IntPtr)HTRIGHT; return; }
                    if (t) { m.Result = (IntPtr)HTTOP; return; }
                    if (b) { m.Result = (IntPtr)HTBOTTOM; return; }
                }

                // 2) 顶栏空白处当"标题栏"用：能按住拖窗口、双击最大化、右键出系统菜单。
                //    落在顶栏上的按钮上就不返回 HTCAPTION，否则按钮点不动。
                if (hudTop != null && cp.Y >= 0 && cp.Y < hudTop.Height && !OverTopBarButton(cp))
                {
                    m.Result = (IntPtr)HTCAPTION;
                    return;
                }

                base.WndProc(ref m);
                return;
            }
            base.WndProc(ref m);
        }

        // ---------------------------------------------------- 拖动中的「整窗快照预览」
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        const uint PW_RENDERFULLCONTENT = 2;   // 连子窗口一起画进 DC（不加这个只画窗体自己）

        // 按下缩放的那一刻拍一张整窗画面。⚠ 必须在窗口**还没变尺寸之前**拍（WM_ENTERSIZEMOVE 正是这个点），
        // 晚了就只能拍到已经拉伸过的残帧。拍不动也不算错：把 snapArmed 留成 false，这一趟就照常重绘。
        void ArmSnap()
        {
            snapArmed = false;
            try
            {
                if (dragSnap != null) { dragSnap.Dispose(); dragSnap = null; }
                Size sz = ClientSize;
                if (sz.Width <= 0 || sz.Height <= 0 || !IsHandleCreated) return;
                Bitmap bmp = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(Handle, hdc, PW_RENDERFULLCONTENT); }
                    finally { g.ReleaseHdc(hdc); }
                }
                dragSnap = bmp;
                snapArmed = true;
            }
            catch
            {
                if (dragSnap != null) { try { dragSnap.Dispose(); } catch { } dragSnap = null; }
                snapArmed = false;
            }
        }

        // 真的开始变尺寸 → 把顶层子控件全藏起来（一整棵子树就都不画了，也不会跟着布局挪位），
        // 窗体自己铺那张快照。⚠ 只藏【本来就是可见的】那几个，恢复时照单放回 ——
        // 活动栏（activityBar）平时是隐藏的，不能顺手点亮。
        void BeginSnap()
        {
            if (dragSnap == null) { snapArmed = false; return; }
            try
            {
                snapShow.Clear();
                foreach (Control c in Controls)
                    if (c.Visible) { snapShow.Add(c); c.Visible = false; }
                snapActive = true;
                Invalidate();
            }
            catch
            {
                snapActive = false;
                foreach (Control c in snapShow) { try { c.Visible = true; } catch { } }
                snapShow.Clear();
                snapArmed = false;
            }
        }

        // 收工：把子控件放回原样、扔掉快照、整窗重画一遍。可以重复调用。
        void EndSnap()
        {
            snapArmed = false;
            if (snapActive)
            {
                snapActive = false;
                foreach (Control c in snapShow) { try { c.Visible = true; } catch { } }
            }
            snapShow.Clear();
            if (dragSnap != null) { try { dragSnap.Dispose(); } catch { } dragSnap = null; }
            try { Invalidate(true); } catch { }
        }

        // 快照预览期间，整窗就画这一张图：按当前客户区大小铺满 = 整幅界面跟着窗口实时缩放。
        // 子控件都藏着，所以这里画的什么就是用户看到的全部。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (snapActive && dragSnap != null)
            {
                try { e.Graphics.DrawImage(dragSnap, ClientRectangle); }
                catch { }
                return;
            }
            base.OnPaintBackground(e);
        }

        // 这个点（窗体客户区坐标）是不是落在顶栏的某个按钮上。
        // 落在按钮上的必须放行给按钮自己处理，不能当成"拖标题栏"。
        bool OverTopBarButton(Point clientPt)
        {
            try
            {
                if (hudTop == null) return false;
                Point inTop = hudTop.PointToClient(PointToScreen(clientPt));
                foreach (Control c in hudTop.Controls)
                    if ((c is ButtonBase || c is HudCapButton) && c.Bounds.Contains(inTop)) return true;
            }
            catch { }
            return false;
        }

        // ---------------------------------------------------------------- 无边框窗口：子窗口协作
        // ⚠ 这是这次改造最难的一处，踩坑记录：
        //   WM_NCHITTEST 不是发给顶层窗口的，而是发给【光标底下那个子窗口】的。
        //   顶栏是个 Panel、四条边上盖着 SplitContainer 的子面板、还有状态栏和活动栏 ——
        //   实测每个"装饰区"坐标底下都是子窗口。子窗口自己回 HTCLIENT，点击当场被吃掉，
        //   顶层 WndProc 根本轮不到（真实拖动期间一条 NCHITTEST 日志都没有）。
        //   办法：给落在装饰区里的子窗口装钩子，命中装饰区时一律回 HTTRANSPARENT。
        //   HTTRANSPARENT 的文档语义正是我们要的 ——"消息继续交给同线程底下那层窗口，
        //   直到某个窗口返回别的值"，最终就落到窗体自己的 WndProc 上，
        //   于是移动/缩放的还是系统原生那套循环（Aero Snap、双击最大化、拖下来还原都还在）。
        class ChromeHook : NativeWindow
        {
            readonly MainForm form;
            public ChromeHook(MainForm f) { form = f; }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_NCHITTEST)
                {
                    try
                    {
                        if (form.ChromeHit(m.LParam) != 0) { m.Result = (IntPtr)(-1); return; }   // HTTRANSPARENT
                    }
                    catch { }
                }
                base.WndProc(ref m);
            }
        }

        readonly List<ChromeHook> chromeHooks = new List<ChromeHook>();

        // 这条屏幕坐标算哪种"窗口装饰"：顶栏空白 = HTCAPTION，四条边/四角 = 缩放，其它 = 0。
        // 窗体和子窗口的钩子共用同一套判断，保证两边结论一致。
        int ChromeHit(IntPtr lParam)
        {
            try
            {
                int lp = lParam.ToInt32();
                Point cp = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (!IsZoomed(Handle))
                {
                    bool l = cp.X < GRIP, r = cp.X >= ClientSize.Width - GRIP;
                    bool t = cp.Y < GRIP, b = cp.Y >= ClientSize.Height - GRIP;
                    if (t && l) return HTTOPLEFT;
                    if (t && r) return HTTOPRIGHT;
                    if (b && l) return HTBOTTOMLEFT;
                    if (b && r) return HTBOTTOMRIGHT;
                    if (l) return HTLEFT;
                    if (r) return HTRIGHT;
                    if (t) return HTTOP;
                    if (b) return HTBOTTOM;
                }
                if (hudTop != null && cp.Y >= 0 && cp.Y < hudTop.Height && !OverTopBarButton(cp)) return HTCAPTION;
            }
            catch { }
            return 0;
        }

        // 重装所有钩子。布局定下来之后调用；重复调用没关系（先摘再装）。
        void InstallChromeHooks()
        {
            foreach (ChromeHook hk in chromeHooks) { try { hk.ReleaseHandle(); } catch { } }
            chromeHooks.Clear();
            try { InstallChromeHooks(this); } catch { }
        }

        void InstallChromeHooks(Control root)
        {
            foreach (Control c in root.Controls)
            {
                try
                {
                    if (c.IsHandleCreated && TouchesChrome(c))
                    {
                        ChromeHook hk = new ChromeHook(this);
                        hk.AssignHandle(c.Handle);
                        chromeHooks.Add(hk);
                    }
                }
                catch { }
                InstallChromeHooks(c);
            }
        }

        // 这个控件的矩形有没有碰到"装饰区"（四条边的热区 + 整条顶栏）。
        // 只给碰到的装钩子，省得给几百个控件都套一层。
        bool TouchesChrome(Control c)
        {
            try
            {
                Rectangle b = RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
                int w = ClientSize.Width, h = ClientSize.Height;
                if (hudTop != null && b.IntersectsWith(new Rectangle(0, 0, w, hudTop.Height))) return true;
                if (b.IntersectsWith(new Rectangle(0, 0, w, GRIP))) return true;              // 上边
                if (b.IntersectsWith(new Rectangle(0, h - GRIP, w, GRIP))) return true;      // 下边
                if (b.IntersectsWith(new Rectangle(0, 0, GRIP, h))) return true;             // 左边
                if (b.IntersectsWith(new Rectangle(w - GRIP, 0, GRIP, h))) return true;      // 右边
            }
            catch { }
            return false;
        }

        // 三个窗口按钮贴在顶栏右上角，整条顶栏高
        void LayoutCaptionButtons()
        {
            try
            {
                if (hudTop == null || btnMin == null) return;
                int h = hudTop.Height;
                btnClose.SetBounds(hudTop.Width - CAPBTN_W, 0, CAPBTN_W, h);
                btnMax.SetBounds(hudTop.Width - CAPBTN_W * 2, 0, CAPBTN_W, h);
                btnMin.SetBounds(hudTop.Width - CAPBTN_W * 3, 0, CAPBTN_W, h);
            }
            catch { }
        }

        // 最大化状态变了 → 中间那个按钮在「最大化」和「还原」两种图标之间切换
        void SyncCaptionButtons()
        {
            try
            {
                if (btnMax == null) return;
                bool max = (WindowState == FormWindowState.Maximized);
                if (btnMax.Maximized != max) { btnMax.Maximized = max; btnMax.Invalidate(); }
                LayoutCaptionButtons();
            }
            catch { }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        // 让 Windows 把标题栏也画成深色（Win10 1809+）
        void EnableDarkTitleBar()
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
                DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
                // 现在没有系统标题栏了，但 WS_THICKFRAME 还会让 DWM 在窗口外围留 1px 边框，
                // 默认跟着深色模式渲成近黑 —— 在 #4A4C54 的顶栏边上照样扎眼。直接染成顶栏色。
                int border = ColorTranslator.ToWin32(HudPanel);   // COLORREF 是 0x00BBGGRR
                DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));   // DWMWA_BORDER_COLOR
                int round = 2;                                                 // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));    // DWMWA_WINDOW_CORNER_PREFERENCE
            }
            catch { }
        }

        // ============================================================ 背景图
        // 按摆放方式把原图摆进 dst 矩形；ox/oy 是额外位移（像素，来自 X/Y 轴滑杆）
        static void DrawImageByLayout(Graphics g, Image img, Rectangle dst, string layout, int ox, int oy)
        {
            if (img == null || dst.Width <= 0 || dst.Height <= 0) return;
            if (layout == "stretch")
            {
                // 拉伸是铺满整块的，位移只能靠"整体挪一下、超出部分裁掉"
                g.DrawImage(img, new Rectangle(dst.X + ox, dst.Y + oy, dst.Width, dst.Height));
                return;
            }
            if (layout == "center")
            {
                g.DrawImage(img, new Rectangle(dst.X + (dst.Width - img.Width) / 2 + ox,
                                               dst.Y + (dst.Height - img.Height) / 2 + oy,
                                               img.Width, img.Height));
                return;
            }
            if (layout == "tile")
            {
                // 平铺：位移就是"从哪开始铺"，用取模保证图样连续
                int sx = dst.X + (ox % img.Width);
                int sy = dst.Y + (oy % img.Height);
                for (int y = sy - img.Height; y < dst.Bottom; y += img.Height)
                    for (int x = sx - img.Width; x < dst.Right; x += img.Width)
                        g.DrawImage(img, x, y, img.Width, img.Height);
                return;
            }
            // fill：等比放大到填满，多出来的裁掉（不变形）；fit：等比缩到能完整放下（可能留边）
            double s = (layout == "fit")
                ? Math.Min((double)dst.Width / img.Width, (double)dst.Height / img.Height)
                : Math.Max((double)dst.Width / img.Width, (double)dst.Height / img.Height);
            int w = Math.Max(1, (int)Math.Round(img.Width * s));
            int h = Math.Max(1, (int)Math.Round(img.Height * s));
            g.DrawImage(img, new Rectangle(dst.X + (dst.Width - w) / 2 + ox, dst.Y + (dst.Height - h) / 2 + oy, w, h));
        }

        // 模糊：先缩小再放大（GDI+ 没有自带模糊，这招又快又够用）
        static Bitmap BlurBitmap(Bitmap src, int radius)
        {
            int f = Math.Max(2, radius / 3 + 2);
            int sw = Math.Max(1, src.Width / f), sh = Math.Max(1, src.Height / f);
            using (Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, new Rectangle(0, 0, sw, sh), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
                }
                Bitmap big = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(big))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(small, new Rectangle(0, 0, src.Width, src.Height), 0, 0, sw, sh, GraphicsUnit.Pixel);
                }
                return big;
            }
        }

        // 右侧内容区（队列 + 终端那一块）在"窗体客户区"坐标系里的矩形。
        // 背景图就铺在这一块里；左栏控制面板不在其中，所以不会被图覆盖。
        // 面板收起/拖窄时 Panel2 会变宽 → 这个矩形跟着变 → 图跟着铺满（用户要的行为）。
        Rectangle ContentRect()
        {
            try
            {
                Control p = mainSplit.Panel2;
                Rectangle scr = p.RectangleToScreen(p.ClientRectangle);
                Point tl = PointToClient(scr.Location);
                if (scr.Width > 0 && scr.Height > 0) return new Rectangle(tl.X, tl.Y, scr.Width, scr.Height);
            }
            catch { }
            return new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        }

        // 背景图的目标范围：content = 只铺右侧内容区（默认）；global = 全局铺满整个窗口（控制面板也透图）。
        // 建缓存、算裁剪区都走这里，切范围时只改 cfg.BgScope 就行。
        Rectangle BgTargetRect()
        {
            if (cfg.BgScope == "global") return new Rectangle(Point.Empty, ClientSize);
            return ContentRect();
        }

        // 下拉框选完之后整片蓝底不消（用户报的 bug）。
        // ⚠ 试过把 SelectionLength 归零，没用 —— 那不是"文字选中"，是**焦点渲染**：
        //   DropDownList 组合框只要还有焦点，系统就会把当前项画成高亮，按不掉。
        //   正解是把焦点交出去。用 BeginInvoke 推迟一拍，等 WinForms 自己处理完再交。
        void UpdateMemHint()
        {
            if (lblMemHint == null) return;
            int n = Math.Max(1, Math.Min(6, cfg.ParallelCount));
            double gb = n * 550 / 1024.0;
            lblMemHint.Text = "约 " + gb.ToString("0.0") + " GB";
        }

        // 左栏自适应：拖宽控制面板时，卡片拉伸到视口宽、队列/环境按钮均分一行、大按钮铺满整行
        void LayoutCardWidth()
        {
            if (leftView == null || hudCards == null) return;
            try
            {
                int w = leftView.ClientSize.Width - leftScroll.Width - 12;   // 卡片目标宽 = 视口 - 滚动条 - 余量
                if (w < 300) w = 300;
                foreach (FlowLayoutPanel card in hudCards)
                {
                    card.MinimumSize = new Size(w, 0);   // AutoSize 卡片的宽度下限，拖窄也不会比 300 小
                    card.Width = w;
                }
                foreach (Panel t in hudTitles) t.Width = w - 8;          // 标题分隔线跟着卡片走
                int inner = w - 16;                                      // 卡片内宽（左右 Padding 4+4）
                foreach (Control c in hudFull) c.Width = inner - 4;      // 大按钮铺满整行（扣自身 Margin）
                foreach (FlowLayoutPanel row in hudRows)
                {
                    int n = row.Controls.Count;
                    if (n == 0) continue;
                    int bw = (inner - n * 4) / n;                        // 每枚按钮均分行宽（Margin 2+2）
                    foreach (Control b in row.Controls) b.Width = bw;
                }
                if (cbMic != null && lblMic != null && lblMic.Width > 0)
                    cbMic.Width = inner - lblMic.Width - 14;             // 录音模式下拉也铺满
            }
            catch { }
        }

        void ClearComboHighlight(ComboBox cb)
        {
            if (cb == null) return;
            try
            {
                cb.SelectionLength = 0;
                if (!cb.Focused) return;          // 程序初始化设值时不要抢焦点
                BeginInvoke((MethodInvoker)delegate
                {
                    try { if (cb.Focused) ActiveControl = null; }   // 焦点还给窗体本身，不留任何控件高亮
                    catch { }
                });
            }
            catch { }
        }

        // ---- 滚轮分流（WheelRouter 调进来的）----
        // 返回 true = 这条滚轮消息我们吃掉了（别再往下发）。
        // 判断顺序：先看光标是不是压在【自绘滚动条自己】身上（那三条是 list/leftView 的兄弟控件，
        // 不在它们的客户区里，不单独判就永远轮不到），再按区域分。
        internal bool RouteWheel(int delta, Point screenPt)
        {
            try
            {
                int clicks = delta / 120;          // 一格 120；高精度滚轮可能给出 ±40 这种，除以 120 就是 0
                if (clicks == 0) return false;
                // 队列：自绘滚动条 /
                if (listScroll != null && listScroll.Visible && OverControl(listScroll, screenPt))
                {
                    listScroll.Value -= clicks * 3;   // 与光标压在列表上时同一步长
                    return true;
                }
                // 左栏：自绘滚动条
                if (leftScroll != null && leftScroll.Visible && OverControl(leftScroll, screenPt))
                {
                    leftScroll.Value -= clicks * 3 * 20;
                    leftScroll.Flash();          // 左栏那条平时藏着，滚的时候要把它叫出来
                    return true;
                }
                // 队列列表本体
                if (list != null && listScroll != null && listScroll.Visible && OverControl(list, screenPt))
                {
                    listScroll.Value -= clicks * 3;
                    return true;
                }
                // 左栏视口（换了自绘滚动条之后 AutoScroll 没了，滚轮得自己转发）
                if (leftView != null && leftScroll != null && leftScroll.Visible && OverControl(leftView, screenPt))
                {
                    leftScroll.Value -= clicks * 3 * 20;
                    leftScroll.Flash();          // 同上：滚的时候让它露一下脸
                    return true;
                }
                // 终端。它的自绘滚动条是 BgLogView 的【子控件】，落在 logBox 客户区里，
                // 所以压在条上也会走到这里 —— 正是想要的（压条 = 滚终端）。
                if (logBox != null && OverControl(logBox, screenPt))
                {
                    logBox.ScrollBy(clicks);
                    return true;
                }
                // 滚轮停在下拉框上不要改它的值（WinForms 默认会一格格改，用户实测把 6 滚成了 1）
                if (OverControl(cbParallel, screenPt) || OverControl(cbMic, screenPt)) return true;
            }
            catch { }
            return false;
        }

        // 光标（屏幕坐标）是不是停在这个控件身上。顺带挡掉没建句柄/不可见的控件。
        static bool OverControl(Control c, Point screenPt)
        {
            if (c == null || !c.IsHandleCreated || !c.Visible) return false;
            try { return c.ClientRectangle.Contains(c.PointToClient(screenPt)); }
            catch { return false; }
        }

        // ---- 自绘滚动条的同步 ----
        // 左栏：内容比视口高就出现滑块，滑块值 = 内容上移多少像素
        // SplitContainer 的 DoubleBuffered 是 protected，只能反射打开。
        // 打开后拖分割线/窗口缩放时分隔条和底色的重绘走缓冲，闪烁显著减少。
        static void BufferSplitter(Control c)
        {
            try
            {
                System.Reflection.PropertyInfo p = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (p != null) p.SetValue(c, true, null);
            }
            catch { }
        }

        // ⚠ 这里原来有 SetRedrawTree() / FreezePaint()（拖分隔条/拖窗口时对整棵树发 WM_SETREDRAW=0）。
        //   2026-10-03 整段删除，原因见 WndProc 顶上那段实测记录：那套东西会把 WS_VISIBLE 抹掉，
        //   导致任务栏丢按钮 + 窗口点不动，而且漏一次就永久卡死。防闪改走"实时重绘 + 拉伸预览"。
        // ⚠ LockWindowUpdate 早先也试过，只挡 WM_PAINT、挡不住子控件自己往屏幕画，同样不用。

        void SyncLeftScroll(bool unused)
        {
            if (syncingLeft) return;
            syncingLeft = true;
            try
            {
                if (leftScroll == null || top == null || leftView == null) return;
                int viewW = leftView.ClientSize.Width;
                int viewH = leftView.ClientSize.Height;
                if (viewW <= 0 || viewH <= 0) return;
                int contentH = Math.Max(top.Height, top.PreferredSize.Height);
                int maxV = Math.Max(0, contentH - viewH);
                leftScroll.LargeChange = Math.Max(1, viewH);
                leftScroll.Maximum = maxV;
                bool need = maxV > 0;
                if (leftScroll.Visible != need) leftScroll.Visible = need;
                // 内容至少铺满视口宽（AutoSize 面板的宽是内容撑的，不撑满会露出缝）
                Size ms = top.MinimumSize;
                if (ms.Width != viewW) top.MinimumSize = new Size(viewW, 0);
                if (top.Top != -leftScroll.Value) top.Top = -leftScroll.Value;
            }
            catch { }
            finally { syncingLeft = false; }
        }

        // 队列列表：行数比可见行多就出现滑块；拖动它 → 用 TopItem 把对应那行顶到最上面
        [DllImport("user32.dll")] static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);

        // 藏掉队列列表的原生滚动条 —— **竖向和横向两条都要藏**（但保留 WS_VSCROLL，滚动能力才在）。
        // ⚠ 只藏 SB_VERT 是不够的：列宽加起来超过客户区宽时 comctl32 会再弹一条**横向**原生滚动条，
        //   那条又丑、滚轮停在它上面还会变成"左右滚"（用户实测）。所以这里用 SB_BOTH。
        // comctl32 在内容/滚动状态变化时会自己再把它显出来，所以每次同步滚动条时都补一刀。
        void HideNativeListScrollbar()
        {
            try
            {
                if (list == null || !list.IsHandleCreated) return;
                ShowScrollBar(list.Handle, 3 /*SB_BOTH*/, false);
                // 万一列还是比客户区宽（窗口被拖得很窄），横向滚动范围就又存在了：comctl32 选中某行时会
                // 自己横向挪一下，而横条已经被藏掉 —— 挪过去就回不来。这种时候把它按回最左边。
                int used = 0;
                for (int i = 0; i < list.Columns.Count - 1; i++) used += list.Columns[i].Width;
                if (used + 80 > list.ClientSize.Width)
                    SendMessage(list.Handle, 0x1014 /*LVM_SCROLL*/, (IntPtr)(-10000), IntPtr.Zero);
            }
            catch { }
        }

        // 列宽自适应，**保证列加起来永远不超过客户区宽** —— 这是"原生横条 / 滚轮当左右滚 /
        // 选中某行时自己横向挪走"这一串问题的总根：只要没有横向滚动范围，它们全都不存在。
        // 规则：文件列优先 290、窄了压到 120；说明列吃掉剩下的，太宽（>360）就把多余的还给文件列。
        void FitListColumns()
        {
            try
            {
                if (list == null || !list.IsHandleCreated || list.Columns.Count < 6) return;
                int clientW = list.ClientSize.Width;
                if (clientW <= 0) return;
                const int iFile = 1, iDesc = 5;
                int others = 0;
                for (int i = 0; i < list.Columns.Count; i++)
                    if (i != iFile && i != iDesc) others += list.Columns[i].Width;
                int avail = clientW - others - 2;                       // 留给 文件 + 说明 的宽度
                int fileW = Math.Min(290, Math.Max(120, avail - 90));
                int descW = avail - fileW;
                if (descW > 360) { fileW = Math.Min(420, fileW + (descW - 360)); descW = 360; }
                if (descW < 60) descW = 60;
                if (list.Columns[iFile].Width != fileW) list.Columns[iFile].Width = fileW;
                if (list.Columns[iDesc].Width != descW) list.Columns[iDesc].Width = descW;
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)] struct HRect { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out HRect r);

        // 表头（"# 文件 状态 …"那一行）的高度。
        // ⚠ ListView 的 ClientSize 把表头也算在客户区里，算"能放几行条目"时必须扣掉它，
        //   否则会多算一行 → 滚动条的最大值少一行 → 最后一行永远滚不出完整的一行。
        //   实测：374 高的列表，代码算成 18 行、实际只放得下 17 行，滚到底时第 20 行被底边切掉一半。
        int ListHeaderHeight()
        {
            try
            {
                if (list == null || !list.IsHandleCreated) return 0;
                IntPtr hdr = SendMessage(list.Handle, 0x101F /*LVM_GETHEADER*/, IntPtr.Zero, IntPtr.Zero);
                if (hdr == IntPtr.Zero) return 0;
                HRect r;
                if (!GetWindowRect(hdr, out r)) return 0;
                return Math.Max(0, r.B - r.T);
            }
            catch { return 0; }
        }

        void SyncListScroll()
        {
            if (syncingList) return;
            syncingList = true;
            try
            {
                FitListColumns();
                HideNativeListScrollbar();
                if (listScroll == null || list == null) return;
                int rowH = 20;
                try { if (list.Items.Count > 0) rowH = Math.Max(1, list.GetItemRect(0).Height); } catch { }
                // ⚠ 表头（"# 文件 状态 …"那一行）也占着客户区，但它不是条目行 ——
                //   不扣掉它，"能放几行"就会多算一行，滚动条最大值少一行，
                //   结果【最后一行永远只能露出一半】（实测 374 高的列表：代码算 18 行，实际只放得下 17 行，
                //   滚到底时第 20 行被底边切掉一半 —— 用户报的"少了最后一行"）。
                int viewH = list.ClientSize.Height - ListHeaderHeight();
                int visible = Math.Max(1, viewH / Math.Max(1, rowH));
                int maxV = Math.Max(0, list.Items.Count - visible);
                listScroll.LargeChange = visible;
                listScroll.Maximum = maxV;
                bool need = maxV > 0;
                if (listScroll.Visible != need) listScroll.Visible = need;
                if (listScroll.Value > maxV) listScroll.Value = maxV;
            }
            catch { }
            finally { syncingList = false; }
        }

        // 列表自己滚动过（键盘、EnsureVisible）之后，把滑块位置对回来
        void ListScrollFollow()
        {
            try
            {
                if (listScroll == null || list == null || list.Items.Count == 0) return;
                ListViewItem t = list.TopItem;
                int idx = (t == null) ? 0 : t.Index;
                syncingList = true;
                listScroll.Value = Math.Min(idx, listScroll.Maximum);
                syncingList = false;
            }
            catch { syncingList = false; }
        }

        // 内容区变了（面板收起/拖动/窗口缩放）就重排背景图，防抖 200ms
        void ScheduleBgRebuild()
        {
            // 拖窗口期间不排重建：200ms 定时器会在拖动中途 fire，重建大位图（双三次缩放+模糊+alpha 合成）
            // 是几十~一百多毫秒的重活，紧跟着全窗重绘，拖动会一顿一顿的。
            // 拖动期间画面由 Bg.PaintRect 的"拉伸预览"顶着（实时跟随），松手时 WM_EXITSIZEMOVE
            // 会补调一次 OnContentGeometryChanged → 这里再排上，按最终尺寸建一张准的。
            if (isResizing) return;
            if (bgTimer == null) return;
            try { bgTimer.Stop(); bgTimer.Start(); }
            catch { }
        }

        // 右侧内容区几何变了：拖分割线、收起/展开面板、窗口缩放都会走到这里。
        // 两件事都要做，缺一不可：
        //   1) **立刻**刷新 Bg.Clip —— 图绝不越出内容区，哪怕缓存还是旧的那张；
        //   2) 防抖后重建缓存 —— 修正图的缩放与位移。拖动时每帧重建一张上千像素的位图太慢，所以延后。
        void OnContentGeometryChanged()
        {
            try
            {
                // ⚠ 最小化的时候 mainSplit.Panel2 的屏幕矩形是 (-32000,-32000) 那一套，
                //   ContentRect() 换算出来是垃圾值。不加判断就写进 Bg.Clip 的话，
                //   从任务栏恢复的那一瞬间图会被整块裁掉 —— 又是一次闪。
                //   所以这里挡住两种坏值：最小化状态，和算出来根本不在窗口范围内的矩形。
                if (WindowState != FormWindowState.Minimized)
                {
                    Rectangle cr = BgTargetRect();
                    if (cr.Width > 0 && cr.Height > 0 &&
                        cr.Right <= ClientSize.Width + 8 && cr.Bottom <= ClientSize.Height + 8)
                        Bg.Clip = cr;
                }
                // 左栏在 FixedPanel.Panel1 下不会因为拖分割线改尺寸（变的只有右栏），
                // 所以刚才渗上去的旧图不会自己消失 —— 得主动让它重画一遍。
                // 拖窗口期间也要（2026-10-03 改）：现在拖动期间不再冻结重绘，左栏也得跟着实时重画，
                // 否则全局背景下左栏那张图会慢半拍（正是用户报的"图片不实时跟随"）。
                if (mainSplit != null) mainSplit.Panel1.Invalidate(true);
            }
            catch { }
            ScheduleBgRebuild();
        }

        // 重建整张背景（窗口大小变了、或者设置改了，都要重建）
        void RebuildBackground()
        {
            try
            {
                // 最小化时不重建：这时候算出来的内容区是垃圾值（窗口被挪到 -32000,-32000），
                // 重建出来只会是错的。恢复的时候会再触发一次。
                if (WindowState == FormWindowState.Minimized) return;
                // ⚠ 不能像以前那样先把 Bg.Cache 置空再重建。
                //   重建要做双三次缩放 + 模糊 + alpha 合成，这几十~一百多毫秒里所有面板
                //   都只能画纯色 —— 用户报的"从任务栏恢复时背景图一闪"就是这么来的
                //   （实测：恢复后 0~100ms 内容是纯底色，150ms 才回来）。
                //   改成先建好新的、整体换上去，最后才释放旧的：任何一帧都有图可画。
                Bitmap oldCache = Bg.Cache;
                BackgroundImage = null;                       // 图不再铺在窗体上，只铺在右侧内容区
                BackgroundImageLayout = ImageLayout.None;
                // 图的覆盖范围：默认只铺右侧内容区（队列+终端）；global 模式铺满整个窗口（控制面板也透图）
                Rectangle cr = BgTargetRect();
                int w = cr.Width, h = cr.Height;
                if (w <= 0 || h <= 0) return;
                Bg.CacheOrigin = cr.Location;
                Bg.Clip = cr;                                 // 裁剪区和缓存同源，任何时候都不会互相打架
                if (string.IsNullOrEmpty(cfg.BgImage) || !File.Exists(cfg.BgImage))
                {
                    Bg.Cache = null;
                    if (oldCache != null) { try { oldCache.Dispose(); } catch { } }
                    RefreshAllBg();
                    return;
                }

                // 原图只在换路径时读一次
                if (bgSource == null || bgSourcePath != cfg.BgImage)
                {
                    if (bgSource != null) { bgSource.Dispose(); bgSource = null; }
                    // 用 FileStream 读，读完就关，不会把用户的图片文件锁住
                    using (FileStream fs = new FileStream(cfg.BgImage, FileMode.Open, FileAccess.Read))
                    using (Image tmp = Image.FromStream(fs))
                        bgSource = new Bitmap(tmp);   // 复制一份，脱离流
                    bgSourcePath = cfg.BgImage;
                }

                Bitmap laid = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(laid))
                {
                    g.Clear(HudBg);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    // X/Y 轴位移按"窗口尺寸的百分比"存，这样换窗口大小也能保持相对位置
                    int ox = (int)Math.Round(w * cfg.BgOffsetX / 100.0);
                    int oy = (int)Math.Round(h * cfg.BgOffsetY / 100.0);
                    DrawImageByLayout(g, bgSource, new Rectangle(0, 0, w, h), cfg.BgLayout, ox, oy);
                }
                if (cfg.BgBlur > 0) { Bitmap b = BlurBitmap(laid, cfg.BgBlur); laid.Dispose(); laid = b; }

                // 不透明度：把图按 alpha 混到 HudBg 上（这样"越透明越接近底色"，而不是越透明越像破洞）
                int a = cfg.BgOpacity; if (a < 0) a = 0; if (a > 100) a = 100;
                Bitmap mixed = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(mixed))
                {
                    g.Clear(HudBg);
                    if (a > 0)
                    {
                        ImageAttributes ia = new ImageAttributes();
                        ColorMatrix cm = new ColorMatrix();
                        cm.Matrix33 = a / 100f;                 // 整体 alpha 缩放
                        ia.SetColorMatrix(cm);
                        g.DrawImage(laid, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                        ia.Dispose();
                    }
                }
                laid.Dispose();
                Bg.Cache = mixed;                       // 新图就位
                if (oldCache != null) { try { oldCache.Dispose(); } catch { } }   // 旧图现在才能扔
                RefreshAllBg();
            }
            catch (Exception ex)
            {
                Log.Write("背景图加载失败：" + ex.Message);
                // 出错了就留着旧的那张 —— 总比整片变成纯色好
                RefreshAllBg();
            }
        }

        // 让所有容器重画背景
        void RefreshAllBg()
        {
            SyncBgTheme();
            try { Invalidate(true); } catch { }
            foreach (Control c in AllControls(this))
                if (c is BgPanel || c is BgFlowPanel) { try { c.Invalidate(); } catch { } }
            // 设计图（图二）：卡片和窗口底色【统一】，不再亮一档 —— 结构全靠描边/分隔线撑。
            // 有背景图时卡片整块【透明】：图从父链（top/leftView 都是 Bg 面板）完整透出来，
            // 不再是一块块实色"黑框"挡住全屏背景（用户要求）。没图时保持实色 HudBg，观感不变。
            if (hudCards != null)
            {
                foreach (Control card in hudCards)
                {
                    card.BackColor = (Bg.Cache != null) ? Color.Transparent : HudBg;
                    try { card.Invalidate(); } catch { }
                }
            }
        }

        // "有没有背景图"这个状态变了，才重新套一遍主题。
        // ⚠ 不能每次重建都套：ApplyHudTheme 会给每个控件 new Font(...)，拖滑杆时会被调上千次，GDI 句柄会漏。
        static bool bgThemeApplied = false;
        void SyncBgTheme()
        {
            bool has = (Bg.Cache != null);
            if (has == bgThemeApplied) return;
            bgThemeApplied = has;
            ApplyHudTheme(this);
            try { if (status != null) status.BackColor = Alpha(HudBg, 170); } catch { }
        }

        static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control d in AllControls(c)) yield return d;
            }
        }

        // 窗口不透明度：**固定 100%**。
        // 用户要求「文字透明度不变，只有背景图片透明度变」。
        // ⚠ 说清楚：窗口级 alpha（Form.Opacity）是让"桌面透出来"的唯一手段，而它一定会连文字一起变淡 ——
        //   这是 Windows 的合成方式决定的，不是调节参数能绕开的。所以这里干脆不用它，
        //   透明度交给「图片不透明度」那一根滑杆（它只影响背景图，完全不碰文字）。
        void ApplyWindowOpacity()
        {
            try { Opacity = 1.0; }
            catch { }
        }

        // ---------------------------------------------------- 背景设置对话框
        // 一行滑杆：标题 + 滑杆 + 实时数值
        TrackBar MakeSlider(Control host, int y, string title, int min, int max, int val, string suffix, EventHandler onValue)
        {
            Label t = new Label();
            t.Text = title; t.SetBounds(16, y + 4, 96, 20); t.ForeColor = HudText;
            TrackBar bar = new TrackBar();
            bar.SetBounds(112, y - 5, 320, 34);
            bar.Minimum = min; bar.Maximum = max;
            bar.TickFrequency = Math.Max(1, (max - min) / 10);
            bar.Value = Math.Min(max, Math.Max(min, val));
            bar.BackColor = HudBg;
            Label v = new Label();
            v.Text = bar.Value + suffix;
            v.SetBounds(440, y + 4, 82, 20);
            v.ForeColor = HudAccent2;
            bar.ValueChanged += delegate
            {
                v.Text = bar.Value + suffix;
                if (onValue != null) onValue(bar, EventArgs.Empty);
            };
            host.Controls.Add(t); host.Controls.Add(bar); host.Controls.Add(v);
            return bar;
        }

        void OnBackgroundSettings()
        {
            // 滑杆是实时预览的，所以先把原值留一份，「取消」时原样还回去
            string oImg = cfg.BgImage, oLay = cfg.BgLayout, oScope = cfg.BgScope;
            int oOp = cfg.BgOpacity, oBlur = cfg.BgBlur, oWin = cfg.WindowOpacity;
            int oox = cfg.BgOffsetX, ooy = cfg.BgOffsetY;

            Form dlg = new Form();
            dlg.Text = "背景设置";
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MinimizeBox = false;
            dlg.MaximizeBox = false;
            dlg.ShowInTaskbar = false;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.ClientSize = new Size(540, 312);
            dlg.BackColor = HudBg;
            dlg.ForeColor = HudText;
            dlg.Font = Ui(9F);

            Label lImg = new Label();
            lImg.Text = "背景图片："; lImg.SetBounds(16, 20, 96, 20); lImg.ForeColor = HudText;
            TextBox txt = new TextBox();
            txt.SetBounds(112, 17, 306, 24);
            txt.ReadOnly = true;
            txt.BackColor = HudPanel2; txt.ForeColor = HudText; txt.BorderStyle = BorderStyle.FixedSingle;
            txt.Text = (cfg.BgImage == null) ? "" : cfg.BgImage;
            Button bBrowse = new Button();
            bBrowse.Text = "浏览…"; bBrowse.SetBounds(424, 16, 52, 26); bBrowse.FlatStyle = FlatStyle.Flat;
            bBrowse.BackColor = HudOn; bBrowse.ForeColor = HudText; bBrowse.FlatAppearance.BorderColor = HudLine;
            bBrowse.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Title = "选一张背景图片";
                    ofd.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|所有文件|*.*";
                    if (!string.IsNullOrEmpty(cfg.BgImage)) ofd.InitialDirectory = Path.GetDirectoryName(cfg.BgImage);
                    if (ofd.ShowDialog(dlg) == DialogResult.OK)
                    {
                        cfg.BgImage = ofd.FileName;
                        txt.Text = ofd.FileName;
                        RebuildBackground();
                    }
                }
            };
            Button bClear = new Button();
            bClear.Text = "清除"; bClear.SetBounds(480, 16, 48, 26); bClear.FlatStyle = FlatStyle.Flat;
            bClear.BackColor = HudOff; bClear.ForeColor = HudDim; bClear.FlatAppearance.BorderColor = HudLine;
            bClear.Click += delegate { cfg.BgImage = ""; txt.Text = ""; RebuildBackground(); };

            // 三行滑杆
            // 回调里用 sender 而不是捕获 sOp 变量 —— 捕获的话编译器会报"使用了未赋值的局部变量"
            // ⚠ 滑杆必须走【防抖重建】(ScheduleBgRebuild)，不能每格都 RebuildBackground：
            //   重建一次要做整幅高质量缩放 + 模糊 + alpha 合成（几十~一百多毫秒），而拖滑杆每秒能发
            //   几十次 ValueChanged —— 结果是界面被反复锁住、整窗 Invalidate(true) 一次接一次，
            //   看起来就是"调背景图时一直闪"（用户实测）。防抖后停手 200ms 才重建一张。
            MakeSlider(dlg, 58, "图片不透明度", 0, 100, cfg.BgOpacity, "%", delegate(object s, EventArgs e)
            { cfg.BgOpacity = ((TrackBar)s).Value; ScheduleBgRebuild(); });
            MakeSlider(dlg, 92, "模糊", 0, 60, cfg.BgBlur, "", delegate(object s, EventArgs e)
            { cfg.BgBlur = ((TrackBar)s).Value; ScheduleBgRebuild(); });
            // 整窗不透明度那根滑杆已去掉：窗口固定不透明，文字永远实心
            MakeSlider(dlg, 126, "水平移动 X", -100, 100, cfg.BgOffsetX, "%", delegate(object s, EventArgs e)
            { cfg.BgOffsetX = ((TrackBar)s).Value; ScheduleBgRebuild(); });
            MakeSlider(dlg, 160, "垂直移动 Y", -100, 100, cfg.BgOffsetY, "%", delegate(object s, EventArgs e)
            { cfg.BgOffsetY = ((TrackBar)s).Value; ScheduleBgRebuild(); });

            // 放置方式
            Label lLay = new Label();
            lLay.Text = "放置方式："; lLay.SetBounds(16, 210, 96, 20); lLay.ForeColor = HudText;
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.SetBounds(112, 206, 200, 24);
            cb.BackColor = HudPanel2; cb.ForeColor = HudText; cb.FlatStyle = FlatStyle.Flat;
            cb.Items.Add("填充（等比铺满，多的裁掉）");
            cb.Items.Add("适应（等比完整显示）");
            cb.Items.Add("拉伸（铺满，会变形）");
            cb.Items.Add("平铺（原图重复）");
            cb.Items.Add("居中（原始大小）");
            string[] keys = new string[] { "fill", "fit", "stretch", "tile", "center" };
            int li = Array.IndexOf(keys, cfg.BgLayout);
            cb.SelectedIndex = (li < 0) ? 0 : li;
            cb.SelectedIndexChanged += delegate { cfg.BgLayout = keys[cb.SelectedIndex]; RebuildBackground(); };

            // 背景范围：只铺右侧内容区（默认） / 全局铺满整个窗口（控制面板也透图）
            Label lScope = new Label();
            lScope.Text = "背景范围："; lScope.SetBounds(16, 236, 96, 20); lScope.ForeColor = HudText;
            ComboBox cbScope = new ComboBox();
            cbScope.DropDownStyle = ComboBoxStyle.DropDownList;
            cbScope.SetBounds(112, 232, 240, 24);
            cbScope.BackColor = HudPanel2; cbScope.ForeColor = HudText; cbScope.FlatStyle = FlatStyle.Flat;
            cbScope.Items.Add("内容区（队列 + 终端）");
            cbScope.Items.Add("全局（整个窗口，控制面板也透图）");
            cbScope.SelectedIndex = (cfg.BgScope == "global") ? 1 : 0;
            cbScope.SelectedIndexChanged += delegate
            {
                cfg.BgScope = (cbScope.SelectedIndex == 1) ? "global" : "content";
                try { Bg.Clip = BgTargetRect(); } catch { }   // 裁剪区立刻切，不等重建
                RebuildBackground();
            };

            Button bOk = new Button();
            bOk.Text = "确定"; bOk.SetBounds(348, 260, 84, 30); bOk.FlatStyle = FlatStyle.Flat;
            bOk.BackColor = HudOn; bOk.ForeColor = HudText; bOk.FlatAppearance.BorderColor = HudLine;
            bOk.DialogResult = DialogResult.OK;
            Button bCancel = new Button();
            bCancel.Text = "取消"; bCancel.SetBounds(440, 260, 84, 30); bCancel.FlatStyle = FlatStyle.Flat;
            bCancel.BackColor = HudOff; bCancel.ForeColor = HudDim; bCancel.FlatAppearance.BorderColor = HudLine;
            bCancel.DialogResult = DialogResult.Cancel;

            dlg.Controls.Add(lImg); dlg.Controls.Add(txt); dlg.Controls.Add(bBrowse); dlg.Controls.Add(bClear);
            dlg.Controls.Add(lLay); dlg.Controls.Add(cb);
            dlg.Controls.Add(lScope); dlg.Controls.Add(cbScope);
            dlg.Controls.Add(bOk); dlg.Controls.Add(bCancel);
            dlg.AcceptButton = null;
            dlg.CancelButton = bCancel;

            DialogResult r = dlg.ShowDialog(this);
            if (r == DialogResult.OK)
            {
                cfg.Save();
                Log.Write("背景设置：图片=" + (string.IsNullOrEmpty(cfg.BgImage) ? "<无>" : cfg.BgImage)
                          + "  不透明度=" + cfg.BgOpacity + "%  模糊=" + cfg.BgBlur
                          + "  放置=" + cfg.BgLayout + "  位移=" + cfg.BgOffsetX + "," + cfg.BgOffsetY
                          + "  范围=" + (cfg.BgScope == "global" ? "全局" : "内容区"));
            }
            else
            {
                // 取消：把实时预览改掉的值全部还原
                cfg.BgImage = oImg; cfg.BgLayout = oLay; cfg.BgScope = oScope;
                cfg.BgOpacity = oOp; cfg.BgBlur = oBlur; cfg.WindowOpacity = oWin;
                cfg.BgOffsetX = oox; cfg.BgOffsetY = ooy;
                try { Bg.Clip = BgTargetRect(); } catch { }
                RebuildBackground();
                ApplyWindowOpacity();
            }
            dlg.Dispose();
        }

        // 上下分栏固定 3:5（列表 3 / 终端 2），窗口一改尺寸就重算
        void FitLastColumn()
        {
            try
            {
                if (list == null || list.Columns.Count == 0) return;
                int used = 0;
                for (int i = 0; i < list.Columns.Count - 1; i++) used += list.Columns[i].Width;
                int rest = list.ClientSize.Width - used;
                if (rest > 60 && list.Columns[list.Columns.Count - 1].Width != rest)
                    list.Columns[list.Columns.Count - 1].Width = rest;
            }
            catch { }
        }

        void ToggleLogCollapse()
        {
            logCollapsed = !logCollapsed;
            if (logHeader != null) logHeader.Invalidate();
            ApplySplitRatio();
        }

        void ApplySplitRatio()
        {
            if (splitRatioLocked) return;
            splitRatioLocked = true;
            try
            {
                int h = split.Height;
                if (h <= 0) return;
                int usable = h - split.SplitterWidth;
                if (usable <= 0) return;
                // 终端折叠时：只留边框标题 24 + 折叠条 22 + 上下留白 9 ≈ 55px
                if (logCollapsed)
                {
                    int d = usable - 55 - split.Panel2MinSize;
                    if (d < split.Panel1MinSize) d = split.Panel1MinSize;
                    if (d != split.SplitterDistance) split.SplitterDistance = d;
                }
                else
                {
                    int d = (int)Math.Round(usable * 3.0 / 5.0);
                    int max = usable - split.Panel2MinSize;
                    if (d > max) d = max;
                    if (d < split.Panel1MinSize) d = split.Panel1MinSize;
                    if (d != split.SplitterDistance) split.SplitterDistance = d;
                }
                FitLastColumn();
            }
            catch { }
            finally { splitRatioLocked = false; }
        }

        // ---- 左侧控制面板：宽度 / 收起 / 展开（学 VS Code 的侧边栏）----
        // 只负责"把宽度设成 w"，不碰 suppressSplitterEvent 标记（调用方自己管）
        void SetPanelWidthRaw(int w)
        {
            if (mainSplit == null || mainSplit.Panel1Collapsed) return;
            int max = mainSplit.Width - mainSplit.SplitterWidth - mainSplit.Panel2MinSize;
            if (w > max) w = max;
            if (w < 160) w = 160;
            if (w != mainSplit.SplitterDistance) mainSplit.SplitterDistance = w;
        }

        // 窗口真正撑开之后才能定最小尺寸（顺序不能反：先把分隔位置挪进合法区间，再收紧 Panel2MinSize）
        void InitPanelLayout()
        {
            try
            {
                int w = mainSplit.Width;
                if (w <= 0) return;
                if (cfg.PanelWidth >= 160) panelWidth = cfg.PanelWidth;   // 上次记住的宽度
                mainSplit.Panel1MinSize = 0;                       // 允许拖到 0 → 触发自动收起
                int safe = w - mainSplit.SplitterWidth - 320;      // 先退到一个两边都合法的位置
                if (safe < 0) safe = 0;
                int d = panelWidth < safe ? panelWidth : safe;
                if (d != mainSplit.SplitterDistance) mainSplit.SplitterDistance = d;
                mainSplit.Panel2MinSize = 320;                     // 现在收紧才安全
                // 上次退出时是收起的 → 这次也按收起还原；否则展开成记住的宽度
                if (cfg.PanelCollapsed) SetPanelCollapsed(true);
                else SetPanelWidthRaw(panelWidth);
            }
            catch { }
            finally
            {
                suppressSplitterEvent = false; panelStateReady = true;
                // 面板宽度到这里才最终定下来，背景图现在建才是对的。
                // 布局过程中排下的那些防抖重建（SetPanelCollapsed / Panel2.SizeChanged 触发的）
                // 全都作废，这里直接建一张准的。
                try { if (bgTimer != null) bgTimer.Stop(); } catch { }
                try { Bg.Clip = BgTargetRect(); } catch { }
                RebuildBackground();
            }
        }

        // 面板宽度 / 收起状态变化后写盘，下次启动还原
        void SavePanelState()
        {
            if (!panelStateReady) return;
            try
            {
                cfg.PanelWidth = panelWidth;
                cfg.PanelCollapsed = mainSplit.Panel1Collapsed;
                cfg.Save();
            }
            catch { }
        }

        void SetPanelCollapsed(bool collapsed)
        {
            // 记住进入前的屏蔽状态并原样还回去 —— 否则在 InitPanelLayout 里调用时
            // 会提前把屏蔽解除，后面的布局动作就会被当成"用户拖动"
            bool prevSuppress = suppressSplitterEvent;
            suppressSplitterEvent = true;
            try
            {
                if (collapsed)
                {
                    // 先记住当前宽度，展开时原样还原
                    if (!mainSplit.Panel1Collapsed && mainSplit.SplitterDistance > 0)
                        panelWidth = mainSplit.SplitterDistance;
                    mainSplit.Panel1Collapsed = true;
                    btnPanel.Text = "▶  展开面板";
                }
                else
                {
                    mainSplit.Panel1Collapsed = false;
                    SetPanelWidthRaw(panelWidth);
                    btnPanel.Text = "◀  收起面板";
                }
                activityBar.Visible = collapsed;    // 收起时左边那条窄竖条才出现
                OnContentGeometryChanged();         // 内容区宽度变了：先裁好，再重排
                InstallChromeHooks();               // 活动栏出现/消失会改变"谁压在左边热区上"，钩子要重装
            }
            catch { }
            finally { suppressSplitterEvent = prevSuppress; SavePanelState(); }
        }

        // 分隔条被拖动：拖得比 110px 还窄就当作"想收起"，直接折叠（VS Code 就是这个手感）
        void OnPanelSplitterMoved()
        {
            if (suppressSplitterEvent) { splitterDragging = false; return; }
            try
            {
                bool byMouse = splitterDragging;     // SplitterMoving 只在真的拖动时才触发
                splitterDragging = false;
                if (mainSplit.Panel1Collapsed) return;
                if (mainSplit.SplitterDistance < 110) { SetPanelCollapsed(true); return; }
                panelWidth = mainSplit.SplitterDistance;    // 记住新宽度，下次收起/展开还用它
                OnContentGeometryChanged();                 // 内容区宽度变了：先裁好，再重排
                // ⚠ 只有用户真的拖过才写盘：窗口布局过程中的程序性变化也会触发 SplitterMoved，
                //   那时宽度可能正被约束夹着（实测把 322 夹成 303 存进了设置）。
                if (byMouse) SavePanelState();
            }
            catch { }
        }

        // 表头：深底 + 蓝字 + 一条下划线
        void OnHudDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            if (headFont == null) headFont = new Font(list.Font, FontStyle.Bold);
            using (SolidBrush bb = new SolidBrush(HudHead))
                e.Graphics.FillRectangle(bb, e.Bounds);
            using (Pen p = new Pen(HudLine))
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            // 表头文字：设计图里表头是浅色加粗
            TextRenderer.DrawText(e.Graphics, e.Header.Text, headFont, e.Bounds, HudText,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        // ⚠ 行背景【不在这里画】—— 整行铺底会毁掉这一行的文字，见下面 PaintCellBack 的说明。
        //   这里实现 OwnerDraw 的 DrawItem（.NET 靠它才会继续发 DrawSubItem），但不画任何东西。
        void OnHudDrawItem(object sender, DrawListViewItemEventArgs e)
        {
        }

        // 单元格背景：这一格的那块底由这一格自己铺（本批=绿底；选中=亮蓝底；隔行条纹；有背景图就贴图）
        // ⚠ 必须**一格一格**铺，绝不能在这里替整行铺底 —— 用户报的"rep 放进队列后文件名/状态空白、
        //   只剩个序号，点一下才回来"就是这么来的（实测埋点复现）：
        //     · 列表开了 LVS_EX_DOUBLEBUFFER（BgListView 的 DoubleBuffered），comctl32 某些趟
        //       （改焦点/选中状态那几笔）是拿**裁剪区=整个客户区**的内存 DC 重画某几行的
        //       （实测 e.Graphics 的 clip 是 0,0,784x374，整行都在裁剪区里，铺底不受限）；
        //     · 而那一趟 WinForms 只发 DrawItem + **第 0 列**的 DrawSubItem（实测每个条目只有 1 个 SUB）；
        //     · 以前在这里整行铺底 → 刚画好的文件名/状态被整行抹平，只补回一个序号。
        //   一格一格铺之后，那种"只重画第 0 列"的一趟顶多把序号列重画一遍，别的列原样不动。
        void PaintCellBack(Graphics g, ListViewItem item, Rectangle cell, int colIndex)
        {
            if (cell.Width <= 0 || cell.Height <= 0) return;
            // 最后一列顺手把右边剩下的那截（列宽之外、行宽之内）也铺上，否则会露出 ListView 自己的底色
            if (list.Columns.Count > 0 && colIndex == list.Columns.Count - 1)
            {
                int right = item.Bounds.Right;
                if (right > cell.Right) cell.Width = right - cell.Left;
            }
            bool batch = item.BackColor == Color.FromArgb(214, 244, 214);   // 被标记成"本批"的那几行
            if (Bg.Cache == null)
            {
                Color bg;
                if (item.Selected) bg = HudSel;
                else if (batch) bg = HudBatch;
                else bg = HudBg;
                using (SolidBrush bb = new SolidBrush(bg)) g.FillRectangle(bb, cell);
                // 隔行稍微深一点，长列表更好扫
                if (item.Index % 2 == 1 && !item.Selected && !batch)
                {
                    using (SolidBrush bb = new SolidBrush(HudPanel))
                        g.FillRectangle(bb, cell);
                }
            }
            else
            {
                // 有背景图：控件不会替我们铺背景（owner-draw + Opaque），所以每一格自己把
                // 对应区域的那块图画上去，然后再叠一层半透明强调色（不透明色会把图盖死）
                Bg.PaintRect(list, g, cell);
                Color tint = Color.Empty;
                if (item.Selected) tint = Color.FromArgb(120, HudSel);
                else if (batch) tint = Color.FromArgb(110, HudBatch);
                else if (item.Index % 2 == 1) tint = Color.FromArgb(40, 0, 0, 0);
                if (tint != Color.Empty)
                    using (SolidBrush bb = new SolidBrush(tint)) g.FillRectangle(bb, cell);
            }
            // "本批"那几行左边再压一条绿条，一眼能认出来
            if (batch && !item.Selected && colIndex == 0)
            {
                using (SolidBrush bb = new SolidBrush(HudBatchBar))
                    g.FillRectangle(bb, item.Bounds.Left, cell.Top, 2, cell.Height);
            }
        }

        // 单元格文字：行自己的前景色（状态色）优先
        void OnHudDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            Color fg = e.Item.ForeColor;
            if (fg == Color.Black || fg == Color.Empty) fg = HudText;
            else if (fg == Color.FromArgb(0, 120, 0)) fg = HudAccent2;      // 完成 / 已录制
            else if (fg == Color.FromArgb(180, 0, 0)) fg = HudRed;          // 失败
            else if (fg == Color.FromArgb(190, 120, 0)) fg = HudOrange;     // 注意
            PaintCellBack(e.Graphics, e.Item, e.Bounds, e.ColumnIndex);     // 先铺这一格的底，再写字
            Rectangle r = e.Bounds;
            if (e.ColumnIndex == 0) r = new Rectangle(r.Left + 6, r.Top, r.Width - 6, r.Height);

            // 状态列（第2列）：画彩色圆点徽标
            if (e.ColumnIndex == 2)
            {
                string st = e.SubItem.Text;
                Color dotColor = HudDim;
                if (st == "等待") dotColor = HudDim;
                else if (st == "录制中") dotColor = HudAccent;
                // ⚠ "已录制" 也是"已经有视频了"，和"完成"一样该亮绿点；漏了它会显示成灰点（和文字颜色对不上）
                else if (st == "完成" || st == "已录制") dotColor = HudAccent2;
                else if (st == "失败") dotColor = HudRed;
                else if (st == "注意") dotColor = HudOrange;
                int dotSize = 8;
                int dotX = r.Left + 2;
                int dotY = r.Top + (r.Height - dotSize) / 2;
                using (SolidBrush dotBrush = new SolidBrush(dotColor))
                    e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
                r = new Rectangle(r.Left + dotSize + 6, r.Top, r.Width - dotSize - 6, r.Height);
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, r, fg,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        Button MakeButton(string text, int w, EventHandler h, Color back)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(w, 28);
            b.Margin = new Padding(2, 2, 2, 2);
            b.Click += h;
            if (back != Color.Empty)
            {
                b.BackColor = back;
                b.ForeColor = Color.White;
                b.FlatStyle = FlatStyle.Flat;
            }
            return b;
        }

        // ---------------------------------------------------- 拖放
        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                hint.Text = "松手即加入队列  ↓";
            }
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            hint.Text = "";
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;
            AddPaths(paths);
            if (cfg.AutoStartOnDrop) StartRecording();
        }

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
            Ui(delegate
            {
                // ⚠ 必须用 BeginUpdate/EndUpdate 把"加条目"整个包起来。
                //   这是 owner-draw 的 ListView：每 Items.Add 一次它就自己重画一次，而重画的时候
                //   后面的条目还没排好版 —— 结果就是"一部分行画出来了、后面的行只有序号是空的，
                //   再点一下才补上"（用户报的"放进来一开始不显示，点一下才出来"）。
                //   顺带也解决了性能问题：放几百个 rep 时不再逐条重画。
                list.BeginUpdate();
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
                        list.Items.Add(job.Item);
                        added++;
                    }
                }
                }
                finally
                {
                    list.EndUpdate();     // 到这里才统一重画一次
                    list.Invalidate();    // 保险：再失效一遍，确保所有新行都补上
                }
            });
            SyncListScroll();     // 条目数变了，自绘滚动条要跟着更新
            if (added > 0) Log.Write("加入队列 " + added + " 个 rep");
            else Log.Write("没有可加入的 rep（重复或不是 .rep）");
            // 需求：检测到"已经录制过"的文件 → 问一次是否重录
            if (already.Count > 0) AskReRecord(already);
            // 少一步操作：数量自动跟着"放进来几个"走，并把这一批要录的高亮出来
            AfterQueueChanged(added);
        }

        // 放进来的 rep 里有已经录过的 → 问一次（一次问清，不逐条骚扰）
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
                DialogResult dr = MessageBox.Show(this, msg, "这些 rep 已经录制过了", MessageBoxButtons.YesNo,
                                                  MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (dr == DialogResult.Yes)
                {
                    foreach (Job j in already)
                    {
                        j.Force = true;
                        SetJob(j, "等待", "你选了重录（会覆盖原视频）");
                    }
                    Log.Write("你选择重录：" + already.Count + " 个（状态已改成「等待」，会覆盖原来的视频）");
                }
                else
                {
                    foreach (Job j in already) SetJob(j, "已录制", "已有视频（你选择跳过）");
                    Log.Write("你选择跳过：" + already.Count + " 个已录制过的 rep");
                }
            }
            catch (Exception ex) { Log.Write("询问重录失败：" + ex.Message); }
        }

        // 拖入/命令行加完 rep 之后：
        //   · 放进来 ≤6 个 → 「同时录制数量」= 放进来几个
        //   · 放进来 >6 个 → 数量 = 6（一次最多 6 个），队列里最前面 6 个就是这一批，其余的自动排到下一批
        // 并把"下一批会录的那几行"高亮出来（浅绿+加粗），用户不用再手动选一遍。
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
                        if (cbParallel != null && cbParallel.SelectedIndex != want - 1) cbParallel.SelectedIndex = want - 1;
                        Log.Write("放进来 " + added + " 个 rep → 「同时录制数量」自动设为 " + want +
                                  (added > 6 ? "（一次最多 6 个，剩下的会自动排到下一批）" : ""));
                    }
                }
                HighlightBatchRows();
                try { SetStatusText("就绪 · 本批 " + Math.Max(1, Math.Min(6, cfg.ParallelCount)) + " 个（列表里浅绿高亮的就是）"); } catch { }
            }
            catch { }
        }

        Font batchFont = null;
        Font headFont = null;      // 列表表头用的加粗等宽字体（"? 文件 状态 …" 那行）
        // 高亮"下一次点开始录制会录的那几行"：
        //   列表里选中了行 → 只高亮选中的（选中 = 一律重录）
        //   没选中 → 队列里最前面的 ParallelCount 个「等待/失败」项
        void HighlightBatchRows()
        {
            try
            {
                if (list == null) return;
                if (batchFont == null) batchFont = new Font(list.Font, FontStyle.Bold);
                int per = Math.Max(1, Math.Min(6, cfg.ParallelCount));
                List<Job> sel = new List<Job>();
                foreach (ListViewItem it in list.SelectedItems)
                {
                    Job sj = it.Tag as Job;
                    if (sj != null && !sel.Contains(sj)) sel.Add(sj);
                }
                int marked = 0;
                // ⚠ 同样要 BeginUpdate/EndUpdate：下面每改一次 BackColor/Font，ListView 就重画一次，
                //   几百个条目时会卡，而且分批重画容易画出"半截"的状态。
                list.BeginUpdate();
                try
                {
                lock (jobsLock)
                {
                    foreach (Job j in jobs)
                    {
                        if (j.Item == null) continue;
                        bool on;
                        if (sel.Count > 0) on = sel.Contains(j);
                        else if (pinnedBatch != null && pinnedBatch.Count > 0) on = pinnedBatch.Contains(j);
                        // 没选中时：和 RunParallel 实际会取的那批完全一致（只算「等待」的）
                        else if (j.Status == "等待" && marked < per) { on = true; marked++; }
                        else on = false;
                        j.Item.BackColor = on ? Color.FromArgb(214, 244, 214) : Color.Empty;
                        j.Item.Font = on ? batchFont : list.Font;
                    }
                }
                }
                finally { list.EndUpdate(); list.Invalidate(); }
            }
            catch { }
        }

        // ---------------------------------------------------- 队列线程
        void StartQueue()
        {
            if (worker != null && worker.IsAlive) { Log.Write("队列已在运行，新加入的会排在后面"); return; }
            cancel = false;
            worker = new Thread(RunQueue);
            worker.IsBackground = true;
            worker.Start();
        }

        void RunQueue()
        {
            try
            {
                SetStatusText("准备 OBS…");
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
                Ui(delegate
                {
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                });
                if (cancel) SetStatusText("已停止");
                else SetStatusText("队列结束");
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

        void MarkPendingFailed(string reason)
        {
            Ui(delegate
            {
                lock (jobsLock)
                {
                    foreach (Job j in jobs)
                        if (j.Status == "等待") SetJob(j, "失败", reason);
                }
            });
        }

        // ---------------------------------------------------- OBS 准备
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

        // 切到录制专用场景 / 切回原来场景
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

        // 恢复 OBS 原来的录像目录（录制时被改成了 rep 所在文件夹）
        void RestoreRecordDirectory()
        {
            if (originalRecordDir == null || !obs.IsConnected) return;
            if (string.IsNullOrEmpty(originalRecordDir)) return;
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["recordDirectory"] = originalRecordDir;
            obs.Request("SetRecordDirectory", r);
            Log.Write("已还原 OBS 录像目录：" + originalRecordDir);
        }

        // 把录制前记下的一批 OBS 输入静音状态还回去。
        // 麦克风和桌面音频原来各抄了一份一模一样的循环（只有日志文案不同），合并到这里。
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

        // 录制期间临时静音麦克风（全局音频会被一起录进去）
        // 录音模式：gameOnly = 录制时静音麦克风；gameAndMic = 录制时保持/开启麦克风（解说）
        // 无论哪种模式，录制结束后都会把麦克风恢复成原来的状态。
        void ApplyMicMode(bool beforeRecording)
        {
            try
            {
                if (beforeRecording)
                {
                    bool wantMuted = (cfg.MicMode != "gameAndMic");
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

        // 让录制里只有游戏声音：新增「应用程序音频采集(th123.exe)」，并临时静音 OBS 的桌面音频
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
                            Log.Write("已静音 OBS 桌面音频（" + (cfg.RecordGameAudio ? "避免录进其他系统声音/重复" : "「录打击音效」已关，保证录像无声") + "）：" + name);
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

        // 录制中保护：窗口一旦被最小化，Windows 就不再为它做桌面合成，任何窗口采集方式都只能拿到黑屏。
        // （实测：最小化后 WGC / BitBlt 全黑；把窗口挪到屏幕外也一样黑。）
        // 所以只能把窗口还原回来，保住这段录像，并提示用户"要挡就盖住，别最小化"。
        int minimizedFixCount = 0;
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
                Log.Write("检测到你把游戏窗口最小化了 —— 最小化后 Windows 不再为它合成画面，录出来会是黑屏；" +
                          "已自动把窗口还原（第 " + minimizedFixCount + " 次）。录制期间请用别的窗口【盖住】它，不要最小化。");
            }
            catch { }
        }

        // 录制中定期报告音频电平，用来确认"游戏声音确实被录进去了"
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

        // 同一个源在同一场景里只允许有一个元素，多余的删掉（否则源列表/混音器里会堆一堆同名项）
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

        // 把采集源（画面 + 音频）绑到给定的窗口串。单录和并行都用它，
        // 这样窗口串可以由我们自己定（游戏/模组的标题会变，靠它匹配窗口就会录成全黑）。
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

        // 单录：把游戏窗口标题固定成 ASCII 记号再绑定采集源，并在录制期间盯着别被改回去。
        // 为什么：采集源是按窗口标题匹配的，而游戏/模组自己会改标题（实测：绑定那一刻标题是
        // 日文原版（读出来还是乱码），随后被模组改成中文版 → 匹配不上 → 整段录像全黑 34 秒）。
        // 并行路径一直是靠"强制标题 + 看守线程"解决的，这里给单录补上同一套。
        const string SingleWinTitle = "th123 录制中";
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

        // 录制期间：标题被游戏改回去就再改回来（标题一变采集源就抓不到窗口）
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

        static string MethodName(int m)
        {
            if (m == 1) return "BitBlt";
            if (m == 2) return "Windows 图形捕获(WGC)";
            return "自动";
        }

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

        // 开录前的画面自检：优先 WGC（能抓到被遮挡的窗口），不行再退回自动/BitBlt
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
            Log.Write("警告：采集画面仍然是黑的，录像可能是黑屏 —— 请在 OBS 里手动换捕获方式/源；可看 " + LastFramePath);
            return false;
        }

        // 单录前确认「画面采集源」在录制场景里是显示状态。
        // 并行录制会把它临时隐藏（否则画面会漏进格子），如果那次程序被强制结束，隐藏状态会留在 OBS 里，
        // 之后每次单录都录出全黑（音频却正常）。这里兜底修回来。
        // 上次程序被强杀（任务管理器结束/崩溃/断电）时，OBS 是独立进程不会跟着退出，可能还在录：
        // 这会让后续 SetVideoSettings / StartRecord 统统报 500，而且一直往磁盘写。发现就停掉。
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
                    Log.Write("画面采集源原本是隐藏的（上次并行录制被强制结束留下的），已重新显示 —— 否则这段录像会是全黑");
                }
            }
            catch { }
        }

        // 录制中也定时自检：窗口被其他窗口挡住时 BitBlt 会变黑，这里自动切到 WGC 尝试恢复
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

        static readonly string LastFramePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.lastframe.jpg");

        // 截图：优先截整个场景（这样"画面叠加/多录了一份"之类的问题能在截图里直接看出来），
        // 场景截不了再退回截单个源。
        byte[] Shot()
        {
            if (captureScene != null && captureScene != captureSource)
            {
                try { return ShotOf(captureScene); }
                catch { }
            }
            return ShotOf(captureSource);
        }

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

        // ---------------------------------------------------- 单个任务
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
                        SetJob(job, "跳过", "已有录像文件（可在设置里关掉“跳过已录制”）");
                        Log.Write("跳过（已有录像）：" + Path.GetFileName(job.Rep));
                        return;
                    }
                }
                if ((forceRecord || job.Force) && cfg.SkipExisting)
                    Log.Write("你明确选了重录它 → 即使已有录像也重录：" + Path.GetFileName(job.Rep));
                SetJob(job, "准备", "");
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



                SetJob(job, "启动游戏", "");
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

                try { if (game.HasExited) { SetJob(job, "失败", "游戏启动后立刻退出"); return; } }
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
                SetJob(job, "录制中", "0:00");
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
                    SetJob(job, "录制中", string.Format("{0}:{1:00}", (int)el.TotalMinutes, el.Seconds));
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
                    Log.Write("游戏已退出、画面已经黑了：不再等 " + cfg.PostRollSeconds + " 秒收尾（免得结尾拖一段黑）");
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
                    SetJob(job, "失败", "拿不到录像文件");
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
                        Log.Write("⚠ 音频自检：整段几乎没有游戏声音（最大电平 " + jobMaxGameLevel.ToString("0.000") +
                                  "）—— 录像可能是静音，检查 OBS 里「" + AudioSourceName + "」源有没有绑上游戏窗口");
                        if (note == "") note = "录成了静音？(音频自检)";
                    }
                    else
                        Log.Write("音频自检：游戏声音正常（最大电平 " + jobMaxGameLevel.ToString("0.000") + "）");
                }

                SetJob(job, note == "" ? "完成" : "注意", note);
                Log.Write((note == "" ? "完成：" : "完成（" + note + "）：") + Path.GetFileName(target) +
                          "  " + (job.Size / 1048576.0).ToString("0.0") + " MB  视频时长 " + FmtDur(job.VideoSeconds) +
                          "（任务耗时 " + FmtDur((job.Ended - job.Started).TotalSeconds) + "）");
                if (note.StartsWith("格式")) Log.Write("转封装：ffmpeg -i \"" + target + "\" -c copy \"" + Path.ChangeExtension(target, ".mp4") + "\"");

                // 需求：录完问用户要把视频放哪
                string saveNote = "";
                if (cfg.AskSaveLocation && !queueSaveDecided)
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
                    SetJob(job, note == "" ? "完成" : "注意", saveNote);
                }
            }
            catch (Exception ex)
            {
                SetJob(job, "失败", ex.Message);
                Log.Write("任务失败：" + Path.GetFileName(job.Rep) + " —— " + ex.Message);
                try { if (obs.IsConnected) obs.Request("StopRecord", null); } catch { }
                KillGame();
            }
        }

        // 录完后把结尾那段黑屏剪掉：游戏退出时会先黑一下（实测约 1 秒），再加上"停录前的等待秒数"，
        // 开头黑屏修剪：单录这条路径和并行一样，也要把结尾那段黑剪掉
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

        // 录完后询问保存位置（在 UI 线程上弹模态框，返回 "keep" 或目标文件夹）
        // count > 1 时表示这是一批（并行），弹窗里说明一共几个。
        string AskSaveLocationDialog(Job job)
        {
            return AskSaveLocationDialog(job, 1);
        }

        string AskSaveLocationDialog(Job job, int count)
        {
            string result = "keep";
            try
            {
                object r = Invoke(new Func<string>(delegate
                {
                    using (Form dlg = new Form())
                    {
                        dlg.Text = "录制完成 —— 视频要放在哪里？";
                        dlg.Font = Font;
                        dlg.ClientSize = new Size(520, 240);
                        dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                        dlg.MinimizeBox = false;
                        dlg.MaximizeBox = false;
                        dlg.StartPosition = FormStartPosition.CenterScreen;

                        Label info = new Label();
                        info.SetBounds(14, 12, 490, 70);
                        if (count > 1)
                            info.Text = "这一批 " + count + " 个视频都录好了（例如：" + Path.GetFileName(job.Output) + "，" +
                                        (job.Size / 1048576.0).ToString("0.0") + " MB，" + FmtDur(job.VideoSeconds) + "）\r\n" +
                                        "现在位置：" + Path.GetDirectoryName(job.Output);
                        else
                            info.Text = "已录好：" + Path.GetFileName(job.Output) + "\r\n" +
                                        (job.Size / 1048576.0).ToString("0.0") + " MB，时长 " + FmtDur(job.VideoSeconds) + "\r\n" +
                                        "现在位置：" + Path.GetDirectoryName(job.Output);

                        Label q = new Label();
                        q.SetBounds(14, 88, 490, 20);
                        q.Text = count > 1 ? "这 " + count + " 个视频要放到哪里？" : "你想把这个视频放到哪里？";
                        Font bold = new Font(Font, FontStyle.Bold);
                        q.Font = bold;

                        CheckBox chkAll = new CheckBox();
                        chkAll.SetBounds(14, 116, 490, 22);
                        chkAll.Text = "本队列后面录好的视频也都这样处理（不再询问）";

                        Button bKeep = new Button();
                        bKeep.Text = "留在 rep 旁边";
                        bKeep.SetBounds(14, 156, 130, 34);
                        bKeep.DialogResult = DialogResult.OK;

                        Button bPick = new Button();
                        bPick.Text = "另存到文件夹…";
                        bPick.SetBounds(154, 156, 140, 34);
                        bPick.DialogResult = DialogResult.Yes;

                        Button bNoAsk = new Button();
                        bNoAsk.Text = "以后不再询问";
                        bNoAsk.SetBounds(304, 156, 130, 34);
                        bNoAsk.DialogResult = DialogResult.No;

                        dlg.Controls.Add(info);
                        dlg.Controls.Add(q);
                        dlg.Controls.Add(chkAll);
                        dlg.Controls.Add(bKeep);
                        dlg.Controls.Add(bPick);
                        dlg.Controls.Add(bNoAsk);
                        dlg.AcceptButton = bKeep;

                        DialogResult dr = dlg.ShowDialog(this);
                        if (chkAll.Checked) queueSaveDecided = true;

                        if (dr == DialogResult.Yes)
                        {
                            string picked = PickSaveFolder(dlg);
                            if (picked != null)
                            {
                                cfg.Save();
                                if (chkAll.Checked) queueSaveFolder = picked;
                                result = picked;
                            }
                        }
                        else if (dr == DialogResult.No)
                        {
                            cfg.AskSaveLocation = false;
                            cfg.Save();
                            Log.Write("已关闭“每次询问保存位置”，可在设置里重新打开");
                        }
                    }
                    return result;
                }));
                if (r != null) result = Convert.ToString(r);
            }
            catch (Exception ex) { Log.Write("询问保存位置失败：" + ex.Message); }
            return result;
        }

        // 把录好的视频移动到指定文件夹（同名就加序号）
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

        // ---------------------------------------------------- UI 辅助
        void Ui(Action a)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch { }
        }

        void SetJob(Job job, string status, string detail)
        {
            job.Status = status;
            job.Detail = detail;
            Ui(delegate
            {
                if (job.Item == null) return;
                job.Item.SubItems[2].Text = status;
                // 用时：优先显示成品视频的真实时长（用户要的就是这个）；读不出来才退回任务耗时。
                // 以前并行任务连 Started 都没设，显示成 (现在 - 0001年) 那种天文数字。
                bool hasDur = job.Started != DateTime.MinValue && job.Ended != DateTime.MinValue && job.Ended >= job.Started;
                job.Item.SubItems[3].Text = job.VideoSeconds > 0.5
                    ? FmtDur(job.VideoSeconds)
                    : (hasDur ? FmtDur((job.Ended - job.Started).TotalSeconds)
                              : (job.Started != DateTime.MinValue ? "…" : ""));
                job.Item.SubItems[4].Text = job.Size > 0 ? (job.Size / 1048576.0).ToString("0.0") + " MB" : "";
                job.Item.SubItems[5].Text = detail;
                if (status == "完成" || status == "已录制") job.Item.ForeColor = Color.FromArgb(0, 120, 0);
                else if (status == "失败") job.Item.ForeColor = Color.FromArgb(180, 0, 0);
                else if (status == "注意") job.Item.ForeColor = Color.FromArgb(190, 120, 0);
                else job.Item.ForeColor = Color.Black;
                // ⚠ 这里【不能】再 EnsureVisible。
                //   并行录制时 SetJob 是【每秒】被调一次的（"并行中 M:SS" 那个进度），
                //   原来那句会把列表一秒一次拽回正在录制的那几行 —— 用户滚到别处看队列，
                //   下一秒就被弹回来（用户报的"滚动队列会自动回到正在录的那块"）。
                //   现在改成：只在【一批刚开始】的时候挪一次（ScrollListToBatch），录制过程中不碰用户的滚动条。
                SyncListScroll();
                ListScrollFollow();
                // 某一条录完了 → 高亮自动挪到下一批（用户不用再选）
                if (status == "完成" || status == "失败" || status == "注意" || status == "已录制")
                    HighlightBatchRows();
            });
        }

        // 把列表挪到这一批的第一行（只有"新的一批要开始了"这一刻才调）。
        // 为什么单独抽出来：原来那句 EnsureVisible 在 SetJob 里，而 SetJob 录制期间每秒都跑
        // —— 用户一滚队列就被拽回来。挪一次是提示"开始录这几行了"，每秒挪一次是抢用户的滚动条。
        void ScrollListToBatch(List<Job> batch)
        {
            Ui(delegate
            {
                try
                {
                    if (batch == null || batch.Count == 0 || list == null) return;
                    Job first = null;
                    foreach (Job j in batch) { if (j != null && j.Item != null) { first = j; break; } }
                    if (first == null) return;
                    list.EnsureVisible(first.Item.Index);
                    SyncListScroll();
                    ListScrollFollow();
                }
                catch { }
            });
        }

        void SetStatusText(string s)
        {
            Ui(delegate { statusLabel.Text = s; });
        }

        void OnLogLine(string line)
        {
            Ui(delegate
            {
                logBox.AppendText("> " + line + Environment.NewLine);
                // 折叠时把最新一条日志写到摘要条
                if (logCollapsed)
                {
                    string t = line;
                    if (t.Length > 60) t = t.Substring(0, 57) + "...";
                    lastLogLine = t;
                    if (logHeader != null) logHeader.Invalidate();
                }
            });
        }

        void RefreshEnvLabel()
        {
            Ui(delegate
            {
                string obsExe = (!string.IsNullOrEmpty(cfg.ObsExe) && File.Exists(cfg.ObsExe)) ? cfg.ObsExe : Settings.FindObsExe();
                string game = (!string.IsNullOrEmpty(cfg.GameDir) && File.Exists(Path.Combine(cfg.GameDir, "th123.exe"))) ? cfg.GameDir : Settings.FindGameDir();
                string s = "";
                if (obsExe == null) s += "OBS：未安装（winget install --id OBSProject.OBSStudio -e）";
                else s += "OBS：已就绪";
                if (game == null) s += "    游戏：未找到 th123.exe";
                else s += "    游戏：已就绪";
                obsState.Text = s;
                obsState.ForeColor = (obsExe == null || game == null) ? HudRed : HudDim;
            });
        }

        // ---------------------------------------------------- 按钮
        // 列表里选中几个 rep，「同时录制数量」就跟着变成几个（最多 6）——
        // 用户实测反馈：希望这个数字跟着选择走，而且这个控件要显眼一点。
        // 用户要求的交互：**选中几行（蓝色）之后，点列表下方的空白处 → 蓝色取消、这几行变成绿色标记**
        // （绿色 = "本批就是这几个"，和"数量跟随选择"一致）。所以这里不是"保住蓝色选择"，
        // 而是把当前选择**钉成本批**（pinnedBatch），然后清掉蓝色选择、只留绿色。
        // 实测：ListView 会在点击过程中一条条取消选择（日志里那串 5→4→3→2→1 就是它干的），
        // 所以先抑制"数量跟随"，再等 120 毫秒（点击彻底处理完）才落定。
        List<ListViewItem> emptyClickKeep = new List<ListViewItem>();
        System.Windows.Forms.Timer emptyClickTimer = null;
        void KeepSelectionOnEmptyClick(MouseEventArgs me)
        {
            try
            {
                if (me.Button != MouseButtons.Left) return;
                if (marqueeOn) return;    // 正在框选：松开那一下不算"点空白处"，别把框出来的行钉成本批
                if (list.HitTest(me.Location).Item != null) return;      // 点在条目上 → 正常处理
                if (list.SelectedItems.Count > 0)                        // Down 时选择还在，先记下来
                {
                    emptyClickKeep.Clear();
                    foreach (ListViewItem it in list.SelectedItems) emptyClickKeep.Add(it);
                }
                if (emptyClickKeep.Count == 0) return;
                suppressSelSync = true;                                  // 接下来 ListView 的连锁不再改数量
                if (emptyClickTimer == null)
                {
                    emptyClickTimer = new System.Windows.Forms.Timer();
                    emptyClickTimer.Interval = 120;
                    emptyClickTimer.Tick += delegate
                    {
                        try
                        {
                            emptyClickTimer.Stop();
                            // 钉成本批（绿色标记）
                            pinnedBatch.Clear();
                            foreach (ListViewItem it in emptyClickKeep)
                            {
                                Job pj = it.Tag as Job;
                                if (pj != null && !pinnedBatch.Contains(pj)) pinnedBatch.Add(pj);
                            }
                            try
                            {
                                suppressSelSync = true;
                                foreach (ListViewItem it in list.Items) it.Selected = false;   // 蓝的取消掉
                            }
                            catch { }
                            finally { suppressSelSync = false; }
                            HighlightBatchRows();                            // 只剩绿色标记
                            SyncParallelCountToSelection();                  // 数量 = 本批个数
                            Log.Write("你点在了列表空白处 —— 已把这 " + pinnedBatch.Count +
                                      " 个标记为「本批」（绿色），数量也跟着改成 " + Math.Min(6, pinnedBatch.Count) + " 个");
                        }
                        catch { }
                    };
                }
                emptyClickTimer.Stop();
                emptyClickTimer.Start();
            }
            catch { }
        }

        // ---------------------------------------------------- 框选（自己画那一层框）
        // ⚠ 为什么要自己实现：列表开着 FullRowSelect，一行从左到右都算"条目"，而 comctl32 的规则是
        //   **按在条目上 = 拖条目，不起框选**，只有按在完全没有条目的空白处才起框。我们的列基本铺满
        //   整宽，空白只剩最后一行下面那一块 → 框只能从下往上拉，选不出"中间一段"（用户实测）。
        //   所以：在**行上**按下左键拖动 → 自己画一个半透明蓝框、实时选中框到的行；
        //   最后一行下面那块空白照旧交给 comctl32 的原生框选，不去抢它。
        bool marqueeArmed = false;                  // 在行上按下了左键，还没拖出框
        bool marqueeOn = false;                     // 框正在拖
        bool marqueeAdd = false;                    // 按住 Ctrl = 追加到原来的选中
        Point marqueeAnchor, marqueeNow;
        Rectangle marqueeLast = Rectangle.Empty;    // 上一帧的框，用来只重画脏的那块
        readonly List<ListViewItem> marqueeBase = new List<ListViewItem>();
        HudMarquee marquee;                         // 那层半透明框（独立分层窗体，见类定义）

        Rectangle MarqueeRect()
        {
            int x1 = Math.Min(marqueeAnchor.X, marqueeNow.X), x2 = Math.Max(marqueeAnchor.X, marqueeNow.X);
            int y1 = Math.Min(marqueeAnchor.Y, marqueeNow.Y), y2 = Math.Max(marqueeAnchor.Y, marqueeNow.Y);
            return new Rectangle(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        void MarqueeDown(MouseEventArgs me)
        {
            try
            {
                marqueeArmed = marqueeOn = false;
                if (me.Button != MouseButtons.Left) return;
                if (list.HitTest(me.Location).Item == null) return;   // 空白区 → 让原生框选去处理
                marqueeArmed = true;
                marqueeAnchor = marqueeNow = me.Location;
                marqueeAdd = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            }
            catch { }
        }

        void MarqueeMove(MouseEventArgs me)
        {
            try
            {
                if (!marqueeArmed && !marqueeOn) return;
                // ⚠ 这里**不能**用 me.Button 判断按键：WinForms 给 WM_MOUSEMOVE 的 Button 是 None，
                //   拿它当"松手了"会第一下移动就把框取消掉（实测）。按没按着只看 armed/on 这两个状态
                //   —— 它们由 MouseDown/MouseUp 维护，框选期间 list.Capture 也保证收得到 Up。
                if (!marqueeOn)
                {
                    // 动够 4px 才算框选；没动的话就是普通点击，交给 ListView 自己选中
                    if (Math.Abs(me.X - marqueeAnchor.X) < 4 && Math.Abs(me.Y - marqueeAnchor.Y) < 4) return;
                    marqueeOn = true;
                    marqueeBase.Clear();
                    if (marqueeAdd) foreach (ListViewItem it in list.SelectedItems) marqueeBase.Add(it);
                    try { list.Capture = true; } catch { }        // 拖到列表外面也继续收得到消息
                }
                marqueeNow = me.Location;
                Rectangle r = MarqueeRect();
                MarqueeApply(r);
                MarqueeShow(r);                                    // 框交给独立那层窗体去画
                marqueeLast = r;
            }
            catch { }
        }

        void MarqueeUp(MouseEventArgs me)
        {
            try
            {
                if (marqueeOn) { marqueeNow = me.Location; MarqueeApply(MarqueeRect()); }
                bool was = marqueeOn;
                MarqueeEnd();
                if (was) SyncParallelCountToSelection();    // 框完了再让「同时录制数量」跟着走一次
            }
            catch { }
        }

        // 把那层框摆到列表里 r 这个矩形上（r 是列表客户区坐标 → 换算成屏幕坐标）
        void MarqueeShow(Rectangle r)
        {
            try
            {
                if (marquee == null)
                {
                    marquee = new HudMarquee();
                    marquee.Owner = this;      // 跟着主窗口走：主窗口一关它也跟着关
                    marquee.Show();
                    marquee.Visible = false;
                }
                Point tl = list.PointToScreen(new Point(r.X, r.Y));
                marquee.Bounds = new Rectangle(tl.X, tl.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
                if (!marquee.Visible) marquee.Visible = true;
                marquee.Invalidate();
            }
            catch { }
        }

        void MarqueeEnd()
        {
            marqueeArmed = marqueeOn = false;
            marqueeLast = Rectangle.Empty;
            try { if (marquee != null) marquee.Visible = false; } catch { }
            if (list == null) return;
            try
            {
                try { if (list.Capture) list.Capture = false; } catch { }
                list.Invalidate();
            }
            catch { }
        }

        // 按框到的范围改选中状态（Ctrl 追加时保留原来选中的那几个）
        void MarqueeApply(Rectangle r)
        {
            if (list == null) return;
            bool old = suppressSelSync;
            suppressSelSync = true;      // 拖的过程中别让「数量跟随选择」连锁乱跳/反复写设置
            try
            {
                foreach (ListViewItem it in list.Items)
                {
                    bool hit = it.Bounds.IntersectsWith(r);
                    if (marqueeAdd && !hit) hit = marqueeBase.Contains(it);
                    if (it.Selected != hit) it.Selected = hit;
                }
            }
            finally { suppressSelSync = old; }
        }

        void SyncParallelCountToSelection()
        {
            try
            {
                int n = list == null ? 0 : list.SelectedItems.Count;
                bool fromPin = false;
                if (n <= 0 && pinnedBatch != null && pinnedBatch.Count > 0) { n = pinnedBatch.Count; fromPin = true; }
                if (n <= 0) { if (lblParHint != null) lblParHint.Text = ""; return; }
                int want = Math.Min(6, n);
                if (cbParallel != null && cbParallel.SelectedIndex != want - 1)
                    cbParallel.SelectedIndex = want - 1;     // 走 SelectedIndexChanged 里的保存/日志
                if (lblParHint != null)
                    lblParHint.Text = n > 6
                        ? "（你" + (fromPin ? "标记" : "选中") + "了 " + n + " 个，上限 6 → 会分 " + ((n + 5) / 6) + " 批）"
                        : "（= 你" + (fromPin ? "标记" : "选中") + "的 " + n + " 个）";
                HighlightBatchRows();
            }
            catch { }
        }

        // 「开始录制」一个按钮搞定（用户要求：不要再分成"开始录制/并行录制/录制选中的"三个）：
        //   · 列表里选中了 rep → 只录这些（明确选中 = 强制重录，不管有没有已经录过）
        //   · 没选中 → 录队列里最靠前的那些「等待」项
        //   · 要录的数量 ≥2 → 按「同时录制数量」并行，超过就自动分批；只有 1 个 → 走普通单录
        // 需求 0：把"视频放哪里"的询问提前到点「开始录制」的时候 —— 选好了才开始录。
        // 返回 false = 用户点了「取消（先不录）」，这次就不录了。
        bool AskSaveLocationUpFront(int count)
        {
            queueSaveDecided = false;
            queueSaveFolder = null;
            bool go = true;
            try
            {
                Invoke(new Action(delegate
                {
                    using (Form dlg = new Form())
                    {
                        dlg.Text = "开始录制 —— 视频要放在哪里？";
                        dlg.Font = Font;
                        dlg.ClientSize = new Size(544, 214);
                        dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                        dlg.MinimizeBox = false;
                        dlg.MaximizeBox = false;
                        dlg.StartPosition = FormStartPosition.CenterScreen;

                        Label info = new Label();
                        info.SetBounds(14, 12, 516, 56);
                        info.Text = "这次要录 " + count + " 个 rep" + (count > 6 ? "（会按「同时录制数量」自动分批）" : "") + "。\r\n" +
                                    "先选好视频放在哪里，选完就开始录。";

                        Label hint = new Label();
                        hint.SetBounds(14, 74, 516, 20);
                        hint.ForeColor = HudDim;      // 次要信息，跟着主题走（原来是写死的浅蓝）
                        hint.Text = "上次用的位置：" + (string.IsNullOrEmpty(cfg.LastSaveFolder) ? "（还没选过）" : cfg.LastSaveFolder);

                        CheckBox chkRemember = new CheckBox();
                        chkRemember.SetBounds(14, 100, 516, 22);
                        chkRemember.Text = "以后不再询问（都用这次的选择；可在「设置…」里重新打开）";

                        Button bKeep = new Button();
                        bKeep.Text = "和 rep 放在一起";
                        bKeep.SetBounds(14, 138, 160, 34);
                        bKeep.DialogResult = DialogResult.OK;

                        Button bPick = new Button();
                        bPick.Text = "另存到文件夹…";
                        bPick.SetBounds(188, 138, 150, 34);
                        bPick.DialogResult = DialogResult.Yes;

                        Button bCancel = new Button();
                        bCancel.Text = "取消（先不录）";
                        bCancel.SetBounds(352, 138, 156, 34);
                        bCancel.DialogResult = DialogResult.Cancel;

                        dlg.Controls.Add(info);
                        dlg.Controls.Add(hint);
                        dlg.Controls.Add(chkRemember);
                        dlg.Controls.Add(bKeep);
                        dlg.Controls.Add(bPick);
                        dlg.Controls.Add(bCancel);
                        dlg.AcceptButton = bKeep;
                        dlg.CancelButton = bCancel;

                        DialogResult dr = dlg.ShowDialog(this);
                        if (dr == DialogResult.Yes)
                        {
                            string picked = PickSaveFolder(dlg);
                            if (picked != null) queueSaveFolder = picked;
                            else dr = DialogResult.OK;   // 没挑文件夹 → 当作"和 rep 放在一起"
                        }
                        if (dr == DialogResult.Cancel) { go = false; return; }
                        if (chkRemember.Checked)
                        {
                            cfg.AskSaveLocation = false;
                            Log.Write("已记住：以后不再询问保存位置（可在「设置…」里重新打开）");
                        }
                        queueSaveDecided = true;
                        cfg.Save();
                    }
                }));
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

        // 「开始录制」按钮和「拖入后自动开始」走的是同一条逻辑（这样自动开始时也会用上并行/轮录/保存询问）
        void OnStart(object sender, EventArgs e)
        {
            StartRecording();
        }

        void StartRecording()
        {
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
                    foreach (ListViewItem it in list.SelectedItems)
                    {
                        Job j = it.Tag as Job;
                        if (j != null && !sel.Contains(j)) sel.Add(j);
                    }
                    if (sel.Count == 0 && pinnedBatch != null && pinnedBatch.Count > 0)
                        foreach (Job pj in pinnedBatch) if (!sel.Contains(pj)) sel.Add(pj);
                }
                manualSel = !round && sel.Count > 0;
                if (round && sel.Count == 0)
                {
                    Log.Write("轮录：队列里没有待录的 rep（都录过了？点「刷新」重新检查，或先在列表里选中要重录的）");
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
                Log.Write("==== 轮录开始：从队列第一个 rep 开始数，共 " + sel.Count + " 个待录，每批 " + per + " 个" +
                          (per == 1 ? "（每批 1 个 → 走单录，依次录）" : "（最后不足 " + per + " 个的按实际数量录）") +
                          "  ※ 轮录模式下不看你的选中/绿色标记，一律从队列最前面开始 ====");
            }

            // 需求 0：保存位置提前问，选好了再开录
            if (cfg.AskSaveLocation)
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
                Log.Write("开始录制：待录 " + pending + " 个，同时录 " + per + " 个" +
                          (pending > per ? "（只录这一批 " + per + " 个，想全录完请勾「轮录」）" : "") +
                          (pending == 1 ? "（只有 1 个，按普通单录走）" : ""));
            }
            parallelRequested = true;
            Ui(delegate { btnStart.Enabled = false; btnStop.Enabled = true; });
            StartQueue();
        }

        void OnStop(object sender, EventArgs e)
        {
            // 队列/批次根本没在跑的时候点「停止」什么都不该做。
            // 以前无条件走停止流程：空队列点一下也会写「已请求停止……」并把状态卡在「正在停止…」，
            // 看起来像把界面弄坏了（用户实测）。
            bool running = worker != null && worker.IsAlive;
            if (!running)
            {
                Log.Write("现在没有在录制，不需要停止" + (list != null && list.Items.Count == 0 ? "（队列是空的）" : "") + "。要点开始录制请按「开始录制」");
                cancel = false;
                SetStatusText("就绪");
                Ui(delegate { btnStop.Enabled = false; btnStart.Enabled = true; });
                return;
            }
            cancel = true;
            Log.Write("已请求停止……");
            SetStatusText("正在停止…");
            Ui(delegate { btnStop.Enabled = false; });   // 已经在停了，别让它被连点
        }

        void OnClear(object sender, EventArgs e)
        {
            if (worker != null && worker.IsAlive) { Log.Write("队列运行中，先停止再清空"); return; }
            lock (jobsLock) { jobs.Clear(); }
            // ⚠ 「钉住的本批」（pinnedBatch）存的是 Job 对象，队列清空后它们已经不在 jobs 里了。
            //   不同步清掉的话，下次放进新 rep 时 HighlightBatchRows 会拿这批**已经不存在的 job**
            //   去比对（pinnedBatch 非空就只认它），结果一行都不高亮 —— 用户实测：清空队列后
            //   再拖进来，状态栏写着「本批 6 个」，列表里却一条绿的都没有。
            pinnedBatch.Clear();
            list.BeginUpdate();
            try { list.Items.Clear(); }
            finally { list.EndUpdate(); list.Invalidate(); }   // 清空同样要统一重画，别留下残影
            SyncListScroll();
            Log.Write("队列已清空");
        }

        // 找一个 rep 已经录好的视频（优先 job 记录的位置，其次 rep 旁边，最后上次另存的文件夹）
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

        // 刷新：重新检查队列里每个 rep 到底有没有录好的视频（视频被删掉的会变回「等待」）
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
                        SetJob(j, "已录制", "已有视频，点开始会跳过");
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
                        SetJob(j, "等待", "还没有视频，会被录制");
                        waiting++;
                    }
                }
            }
            Log.Write("已刷新队列：有视频 " + done + " 个，待录制 " + waiting + " 个" +
                      (skip > 0 ? "，正在处理中 " + skip + " 个（未改动）" : ""));
        }

        void OnRemove(object sender, EventArgs e)        {
            if (worker != null && worker.IsAlive) { Log.Write("队列运行中，不能移除"); return; }
            List<Job> del = new List<Job>();
            foreach (ListViewItem it in list.SelectedItems) del.Add((Job)it.Tag);
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
                foreach (Job j in del) { jobs.Remove(j); list.Items.Remove(j.Item); }
                for (int i = 0; i < list.Items.Count; i++) list.Items[i].SubItems[0].Text = (i + 1).ToString();
            }
            foreach (Job dj in del) pinnedBatch.Remove(dj);
            HighlightBatchRows();   // 移除后本批高亮要跟着挪
            SyncListScroll();
            Log.Write("已从队列移除 " + del.Count + " 个 rep");
        }

        // 「标记本批」：把当前蓝色选中的几行钉成本批（绿色），再把蓝色取消掉 ——
        // 跟"点列表下方空白处"那条路做的事完全一样，只是不用去找空白处（队列塞满时没有空白处可点）。
        void OnMarkBatch(object sender, EventArgs e)
        {
            try
            {
                List<Job> sel = new List<Job>();
                foreach (ListViewItem it in list.SelectedItems)
                {
                    Job j = it.Tag as Job;
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
                try { foreach (ListViewItem it in list.Items) it.Selected = false; }
                catch { }
                finally { suppressSelSync = false; }
                HighlightBatchRows();
                SyncParallelCountToSelection();
                Log.Write("已把选中的 " + sel.Count + " 个标记为「本批」（绿色），数量也跟着改成 " +
                          Math.Min(6, sel.Count) + " 个");
            }
            catch (Exception ex) { Log.Write("标记本批失败：" + ex.Message); }
        }

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
                    ? "找不到！并行录制会失败 —— 把 ffmpeg.exe 放到本程序同一个文件夹里就行"
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
                        Log.Write("ReplayDnD mod（游戏能不能「带 rep 直接播放」靠它）：" +
                                  (on ? "已启用 ✓" : (found ? "没有启用 ⚠" : "整合包里没有这个 mod ⚠")));
                        if (!on)
                            Log.Write("  ⚠ 请在整合包的 mod 配置菜单里启用 ReplayDnD（或 ReplayDnD-old），" +
                                      "否则点开始录制后游戏只会停在标题画面、录出来的不是对局。本程序只读不改这个文件。");
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
        }

        void OnPrepare(object sender, EventArgs e)
        {
            try
            {
                if (!EnsureObs()) return;
                PrepareObs(true);
                Log.Write("OBS 准备完毕");
            }
            catch (Exception ex) { Log.Write("准备 OBS 失败：" + ex.Message); }
        }

        void OnSettings(object sender, EventArgs e)
        {
            Form f = new Form();
            f.Text = "设置";
            f.Font = Font;
            f.ClientSize = new Size(560, 480);
            f.StartPosition = FormStartPosition.CenterParent;
            PropertyGrid pg = new PropertyGrid();
            pg.Dock = DockStyle.Fill;
            pg.SelectedObject = cfg;
            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 44;
            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(360, 8);
            ok.Size = new Size(80, 28);
            ok.DialogResult = DialogResult.OK;
            Button cancelBtn = new Button();
            cancelBtn.Text = "取消";
            cancelBtn.Location = new Point(450, 8);
            cancelBtn.Size = new Size(80, 28);
            cancelBtn.DialogResult = DialogResult.Cancel;
            bar.Controls.Add(ok);
            bar.Controls.Add(cancelBtn);
            f.Controls.Add(pg);
            f.Controls.Add(bar);
            f.AcceptButton = ok;
            f.CancelButton = cancelBtn;
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                cfg.Save();
                RefreshEnvLabel();
                Log.Write("设置已保存");
            }
        }

        // 声音开关：一个按钮搞定，点一下切换；按钮文字/颜色实时反映当前状态。
        // 静音的是「默认输出设备」（端点静音），OBS 的进程音频采集不受影响，所以录像里仍有游戏原声。
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

        // 录制期间静音的是"默认输出设备"，如果程序被杀掉、没走到恢复那一步，整台电脑会一直没声音。
        // 所以静音时留一个标记文件，正常恢复时删掉；下次启动看到残留标记 = 上次没恢复成功，直接帮你打开声音。
        static string MuteFlagPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.已静音待恢复"); }
        }

        static void MarkSystemMuted(bool muted)
        {
            try
            {
                if (muted) File.WriteAllText(MuteFlagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Encoding.UTF8);
                else if (File.Exists(MuteFlagPath)) File.Delete(MuteFlagPath);
            }
            catch { }
        }

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
                        Log.Write("检测到上次录制期间静音了系统声音、程序没来得及恢复（可能是被强制结束）——已自动帮你把声音打开");
                    else
                        Log.Write("想恢复上次的静音但失败了：" + m2);
                }
                File.Delete(MuteFlagPath);
                RefreshSoundButtons();
            }
            catch { }
        }

        // 让按钮一眼看出当前状态
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
                HudSetToggle(btnSound, false);
            }
            else
            {
                btnSound.Text = "🔊 声音：开 ✓";
                btnSound.Width = 148;
                HudSetToggle(btnSound, true);
            }
        }

        // 「录打击音效」开关：决定【录进视频里】有没有游戏声音。
        // 和左边「声音」按钮完全独立：那个决定你耳朵听不听得到，这个决定视频里有没有。
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
                    Log.Write("因为关掉了「录打击音效」，已自动把「录BGM」也关掉（连游戏里的 BGM 都不放），确保录出来完全无声");
                }
                else ApplyBgmSetting(false);
            }
            cfg.Save();
            Log.Write(cfg.RecordGameAudio
                ? "【录打击音效：开】录出来的视频会包含游戏声音（音效/BGM 都照实录；BGM 有没有由「录BGM」单独决定）"
                : "【录打击音效：关】录出来的视频将完全没有声音，「录BGM」同时被锁住（不可点）");
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

        // 把设置落到 OBS 的「应用程序音频采集」源上：不开就把它静音
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

        // 录像音量（增益）：直接设 OBS 音频源的音量倍数，不重编码。
        // 用户问"调电脑音量会不会影响录像" —— 不会（OBS 抓的是进程音频流，任务栏音量是输出设备音量）；
        // 想提前把录像声音定好，就用设置里的 RecordVolume，或者音量合成器里 th123 那一条。
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

        void RefreshGameAudioButton()
        {
            if (btnGameAudio == null) return;
            if (cfg.RecordGameAudio)
            {
                btnGameAudio.Text = "🔊 录打击音效：开 ✓";
                HudSetToggle(btnGameAudio, true);
            }
            else
            {
                btnGameAudio.Text = "🔊 录打击音效：关 ✕";
                HudSetToggle(btnGameAudio, false);
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

        // ==================== 「录BGM」开关 ====================
        // 游戏播 rep 时放不放背景音乐，由 ReplayDnD.ini 里的 MuteMusic 决定：
        //   MuteMusic=1（整合包默认）→ 不放 BGM，只有音效；=0 → 放 BGM。
        // 所以这个按钮做的事就是：开 → 把 ini 改成 0；关 → 改回 1。（游戏启动时才读这个 ini，改完下次开录就生效）
        static readonly string[] BgmIniSubPaths = new string[] { @"modules\ReplayDnD\ReplayDnD.ini", @"modules\ReplayDnD-old\ReplayDnD.ini" };

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

        // 读 ini 里现在的 MuteMusic；返回 null 表示没找到
        bool? ReadBgmMuted()
        {
            try
            {
                foreach (string p in BgmIniPaths())
                {
                    string t = File.ReadAllText(p);
                    System.Text.RegularExpressions.Match m =
                        System.Text.RegularExpressions.Regex.Match(t, @"(?im)^\s*MuteMusic\s*=\s*(\d+)\s*$");
                    if (m.Success) return m.Groups[1].Value.Trim() != "0";
                }
            }
            catch { }
            return null;
        }

        // 把「录BGM」设置写进 ini（0 = 有 BGM，1 = 没 BGM），只改 MuteMusic 那一行
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

        void OnBgmToggle(object sender, EventArgs e)
        {
            cfg.RecordBgm = !cfg.RecordBgm;
            cfg.Save();
            ApplyBgmSetting(true);
            RefreshBgmButton();
            Log.Write(cfg.RecordBgm
                ? "【录BGM：开】录像里会带背景音乐（已把 ReplayDnD.ini 的 MuteMusic 改成 0）"
                : "【录BGM：关】录像里只有音效、没有背景音乐（已把 MuteMusic 改回 1）");
            if (cfg.RecordBgm)
                Log.Write("提醒：BGM 有版权风险（B 站等平台会检测），用不用你自己判断。");
        }

        void RefreshBgmButton()
        {
            if (btnBgm == null) return;
            if (cfg.RecordBgm)
            {
                btnBgm.Text = "录BGM：开 ✓";
                HudSetToggle(btnBgm, true);
            }
            else
            {
                btnBgm.Text = "录BGM：关 ✕";
                HudSetToggle(btnBgm, false);
            }
        }

        // 启动时把 ini 的真实状态同步到设置和按钮上
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
                    Log.Write("因为「录打击音效」是关的，已把 BGM 一并关掉（MuteMusic 改回 1），保证录出来完全无声");
                }
                RefreshBgmButton();
                Log.Write("BGM 录制：" + (cfg.RecordBgm
                    ? "开（ReplayDnD.ini 里 MuteMusic=0，录像会带背景音乐）"
                    : "关（ReplayDnD.ini 里 MuteMusic=1，录像只有音效）") + "；想改就点界面上的「录BGM」按钮");
            }
            else
            {
                RefreshBgmButton();
                Log.Write("没找到 ReplayDnD.ini，读不到也改不了 BGM 开关（检查「设置…」里的游戏目录）");
            }
        }

        // 「另存到文件夹…」——两处"问视频放哪里"的对话框共用这一段，原来是各抄一份。
        // 返回 null 表示用户没挑文件夹（调用方各自决定怎么处理：一个当作"留在 rep 旁边"，
        // 一个当作"和 rep 放在一起"）。挑到了就把路径记进设置里，下次直接用。
        string PickSaveFolder(IWin32Window owner)
        {
            using (FolderBrowserDialog fb = new FolderBrowserDialog())
            {
                fb.Description = "选择保存视频的文件夹";
                fb.ShowNewFolderButton = true;
                try
                {
                    if (!string.IsNullOrEmpty(cfg.LastSaveFolder) && Directory.Exists(cfg.LastSaveFolder))
                        fb.SelectedPath = cfg.LastSaveFolder;
                }
                catch { }
                if (fb.ShowDialog(owner) != DialogResult.OK) return null;
                cfg.LastSaveFolder = fb.SelectedPath;
                return fb.SelectedPath;
            }
        }

        void OnOpenLog(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(Log.FilePath)) Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
                else Log.Write("日志文件还没生成");
            }
            catch { }
        }

        // ============================================================ 并行录制
        bool parallelRequested = false;
        List<Job> selectedBatch = null;   // 「录制选中的」交给队列线程的那批 rep
        volatile bool forceRecord = false; // 「录制选中的」期间忽略「已有录像就跳过」
        bool obsStuckRetried = false;      // 这一批是否已经因为「OBS 没真正开录」自动重启过 OBS（只重试一次）
        List<Job> pinnedBatch = new List<Job>();   // 点空白处"钉"下来的本批（绿色标记），当作手动选中用
        bool suppressSelSync = false;      // 恢复列表选择期间抑制"数量跟随选择"（否则会连锁乱跳）

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

        // 把窗口的"客户区左上角"移动到屏幕 (x,y)
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

        // 游戏加载完成后会自己设置窗口标题（例如加上 "Giuroll ..."），所以我们先等标题全部稳定，
        // 再强制改成 "th123 #N" 并反复确认——OBS 只能靠标题区分三个同名窗口。
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

        // OBS 的窗口串格式是「标题:类名:exe」——注意顺序，不是 exe:类名:标题！
        // 之前一直拼反了：OBS 日志里会看到 executable = 窗口标题（例如 executable: th123 #1），
        // 单文件录制只有唯一一个窗口时靠 OBS 的兜底还能抓到；但并行三个实例同 exe 同类名时，
        // 三个采集源会互相抢到同一个窗口 —— 实测三格录出来是同一场对局、声音也因此张冠李戴。
        static string MakeWindowString(string title, string cls)
        {
            return (title == null ? "" : title) + ":" + (cls == null ? "" : cls) + ":th123.exe";
        }

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

        // 进程音频采集（wasapi_process_output_capture）的 settings。
        // window  = 窗口串（格式是「标题:类名:exe」，见 OBS 源码 ms_build_window_strings）；
        // priority= 1 = WINDOW_PRIORITY_TITLE，即「标题必须完全相等」。
        //   OBS 的枚举是 0=CLASS / 1=TITLE / 2=EXE，rating 用标题的字符串距离算：
        //     0（默认）→ 类名匹配后按"标题距离最近"挑，六个 th123.exe 一起跑时标题一旦对不上就会
        //       随便挑一个，于是两格锁到同一个进程 —— 实测两格音轨逐样本完全相同（声音张冠李戴）。
        //     2 → 只要 exe 相同就按标题距离挑，一样会撞（实测 3 格撞在一起）。
        //     1 → 标题不等就当作找不到，宁可不给声音；等看门狗把标题改回来它会自动重新解析到对的进程。
        static Dictionary<string, object> AudioInputSettings(string window)
        {
            Dictionary<string, object> a = new Dictionary<string, object>();
            a["window"] = window;
            a["priority"] = 1;
            return a;
        }
        // 窗口采集（window_capture，画面）的 settings。
        // priority=1 同样表示「标题必须完全相等」：OBS 的 window_capture 也有这个设置，
        // 默认 0 是按类名匹配、再用标题的字符串距离挑窗口 —— 六个同 exe 的窗口时同样会挑错。
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

        // 重设采集源的窗口串 —— 但只在真的变了的时候才动。
        // 每次重设都会让 OBS 把采集设备重建一次，画面会闪一帧黑（用户实测：开录后第 9~10 秒那一帧黑
        // 就是"开播后再刷一遍绑定"干的）。所以这里记一份"已经设过的窗口串"，一样就跳过。
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

        void SetInputSettingsRaw(string inputName, Dictionary<string, object> settings)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["inputName"] = inputName;
            r["inputSettings"] = settings;
            obs.Request("SetInputSettings", r);
        }

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

        // 显示/隐藏场景里的某个元素（并行期间藏掉单文件用的采集源）
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

        // 把某格采集元素摆到画布 (x,y)、并缩放到正好铺满 cw×ch。
        // ⚠ 千万不能自己算 scale = cw / 源宽：绑定窗口那一刻 OBS 报的"源尺寸"还是没解析出来的占位值
        //   （实测日志：报 640×480、点进去回读画布内尺寸 0×0，而真实抓到的是 1920×1440），
        //   于是缩放算小/算成 1，格子里只录到画面左上角的一小块 —— 用户实测："画面像被放大裁切过"。
        //   改用 OBS 的「边界框」(bounds)：不用我们知道源尺寸，OBS 自己把源缩放到正好装进 cw×ch，
        //   以后源尺寸再变（游戏窗口被 WindowResizer 放大等）也会自动重新适配。
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
                Log.Write("并行：第 " + idx + " 格摆到 (" + x + "," + y + ") 并用边界框缩放到 " + cw + "×" + ch +
                          "（OBS 报的源尺寸 " + sw + "×" + sh + "，只是参考值）");
            }
            catch (Exception ex) { Log.Write("并行：第 " + idx + " 格设置位置失败 " + ex.Message); }
        }

        void SetAudioTracksRaw(string inputName, int trackNo)
        {
            Dictionary<string, object> tracks = new Dictionary<string, object>();
            for (int k = 1; k <= 6; k++) tracks[k.ToString()] = (k == trackNo);
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["inputName"] = inputName;
            r["inputAudioTracks"] = tracks;
            obs.Request("SetInputAudioTracks", r);
        }

        // 静音/取消静音某个 OBS 音频源（并行录制按「录打击音效」开关用）
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

        void SetProfileParamRaw(string cat, string name, string val)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["parameterCategory"] = cat;
            r["parameterName"] = name;
            r["parameterValue"] = val;
            obs.Request("SetProfileParameter", r);
        }

        static string FindFfmpeg()
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

        // 用 ffmpeg 把合并视频里第 idx 格裁出来，并配上第 idx 条音轨。
        // maxSeconds > 0 时按它裁剪（这一格的对局什么时候结束就录到什么时候，
        // 免得后面跟着一大段黑屏 —— 录制要等所有实例都退出才停）。
        // startSec：从合并录像的第几秒开始切（用来剪掉这格开头那段"游戏还没画出画面"的黑屏）
        // maxSeconds：这格总共保留到第几秒（相对合并录像，用来剪掉对局结束后的多余画面）
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
                            Log.Write("并行切分：第 " + attemptNo + " 种参数组合才成功（" +
                                      (attemptNo > 2 ? "这台机器多半没有 NVIDIA 显卡，" + enc + " 用不了 → 已自动换成 libx264 软件编码" : "退化成不带音频") + "）");
                        return true;
                    }
                    err = "ffmpeg 退出码 " + p.ExitCode;
                    if (p.ExitCode != 0 && attemptNo == 1 && encArgs.Count > 1)
                        Log.Write("并行切分：" + enc + " 失败（可能是这台机器没有对应显卡），正在自动改用 libx264 软件编码重试…");
                }
                catch (Exception ex) { err = ex.Message; }
            }
            return false;
        }

        // 判断某个采集源的画面是不是"静止不动"（游戏标题画面是静止的；比赛画面会动）。
        // 用来确认"rep 真的开始播了"，没开始就再拖一次。
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

        // 记下 OBS 原来的录像目录（只记第一次）。
        // 以前只在普通队列流程里记，导致"一开机就直接并行录制"时 originalRecordDir 是 null，
        // 跑完就不会把录像目录还原回去。
        void CaptureOriginalRecordDir()
        {
            if (originalRecordDir != null) return;
            try { originalRecordDir = Convert.ToString(obs.Request("GetRecordDirectory", null)["recordDirectory"]); }
            catch { }
        }

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
                Log.Write("并行录制：没有待录制的 rep（都录过了？可以点「刷新」看看；或者先选中几行再点「录制选中的」）");
                return;
            }
            obsStuckRetried = false;
            RunParallelBatch(batch);
        }

        // 跑一批（1~10 个）
        void RunParallelBatch(List<Job> batch)
        {
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
            ScrollListToBatch(batch);   // 新的一批开始了，把列表挪过去看一眼（之后整批都不再动它）

            // OBS 最多 6 条音轨：超过 6 格时每格的声音没法分开，整批录成无声，避免出现"画面是这局、声音是那局"
            bool cellAudio = cfg.RecordGameAudio && batch.Count <= 6;
            if (batch.Count > 6 && cfg.RecordGameAudio)
                Log.Write("⚠ 同时录 " + batch.Count + " 个，但 OBS 最多只有 6 条音轨 —— 每格的声音没法分开，"
                          + "这一批会录成【无声】视频（想要每格各自的声音，请把「同时录制数量」降到 6 以内）");

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
                    SetJob(batch[i], "启动游戏", "并行 " + (i + 1) + "/" + batch.Count);
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
                    if (h2 == IntPtr.Zero) Log.Write("并行：第 " + (i + 1) + " 个实例的窗口没出现（这一格会是空的）");
                }
                // 窗口尺寸（每格按游戏客户区大小排，保持 4:3 不变形）
                for (int i = 0; i < handles2.Count; i++)
                {
                    if (handles2[i] == IntPtr.Zero) continue;
                    RECT cr;
                    if (GetClientRect(handles2[i], out cr) && cr.Right > 0 && cr.Bottom > 0 && CW == 0)
                    {
                        CW = cr.Right; CH = cr.Bottom;
                        if (CW > 1920 || CH > 1440) { CW = 640; CH = 480; }   // 异常大就按游戏原生尺寸
                    }
                }
                if (CW <= 0 || CH <= 0) { CW = 640; CH = 480; }

                // 网格排布：让中继画布尽量方正（6 格 = 3×2 = 1920×960，而不是 3840×480 那种 8:1 的长条，
                // 后者在 OBS 预览里看着就像"画面没摆好"）。OBS 画布上限 4096，超了就折行。
                {
                    int maxCanvas = 4096;
                    cols = (int)Math.Ceiling(Math.Sqrt(batch.Count * (double)CH / CW));
                    if (cols < 1) cols = 1;
                    rows = (batch.Count + cols - 1) / cols;
                    while (cols * CW > maxCanvas && cols > 1) { cols--; rows = (batch.Count + cols - 1) / cols; }
                    while (rows * CH > maxCanvas && rows > 1) { rows--; cols = (batch.Count + rows - 1) / rows; }
                    if (rows * CH > maxCanvas || cols * CW > maxCanvas)
                        Log.Write("⚠ 并行：画布 " + (cols * CW) + "×" + (rows * CH) + " 超过 OBS 上限 " + maxCanvas +
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
                    Log.Write("并行：❌ OBS 没有真正开始录像（StartRecord 返回成功但 outputActive=false）。" +
                              "常见原因：OBS 里还有一段没停干净的录像、OBS 卡住、录制目录不可写、或录制格式/音轨设置异常。");
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
                    foreach (Job j in batch) SetJob(j, "失败", "OBS 没有真正开始录像（已中止，避免白等）");
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
                    ? "开（每格各自一条音轨，切分后各带自己的原声）"
                    : (cfg.RecordGameAudio ? "关（同时录的格数超过 6，OBS 音轨不够分，这一批无声）"
                                           : "关（各格音源已静音，切分出来的视频是无声的）")));
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
                            Log.Write("并行：⚠ 第 " + (i + 1) + " 格开播后一直是黑屏（重新绑定也没救回来）——这一格很可能没录上，" +
                                      "建议停止后单独重录这一条");
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
                                Log.Write("并行：检测到第 " + (i + 1) + " 格游戏窗口被最小化 —— 最小化后 Windows 不再合成画面、" +
                                          "那一格会变黑；已自动把窗口还原（第 " + minimizedFixCount + " 次）。" +
                                          "录制期间请用别的窗口【盖住】它，不要最小化。");
                            }
                            catch { }
                        }
                    }
                    if ((DateTime.Now - t0).TotalMinutes > cfg.MaxMinutes) { timeout = true; break; }
                    TimeSpan el = DateTime.Now - t0;
                    string txt = "并行中 " + ((int)el.TotalMinutes) + ":" + el.Seconds.ToString("00");
                    foreach (Job j in batch) SetJob(j, "录制中", txt);
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
                                Log.Write("并行：StopRecord 没给出文件，但我在录像目录里找到了刚录的：" +
                                          newest.Name + "（" + (newest.Length / 1048576.0).ToString("0.0") + " MB）");
                            }
                            else Log.Write("并行：录像目录里也找不到刚录出来的文件：" + dir +
                                           "（很可能 OBS 根本没在录 —— 请看上面有没有「OBS 没有真正开始录像」那行）");
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
                                SetJob(batch[i], "完成", (outBytes / 1048576.0).ToString("0.0") + " MB（并行切分）");
                                Log.Write("并行切分完成（第 " + (i + 1) + " 格）：" + Path.GetFileName(batch[i].Output) +
                                          "  " + (outBytes / 1048576.0).ToString("0.0") + " MB  视频时长 " + FmtDur(batch[i].VideoSeconds));
                                lock (splitGate) okCount++;
                            }
                            else
                            {
                                SetJob(batch[i], "失败", "切分失败");
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
                    if (cfg.AskSaveLocation && !queueSaveDecided)
                    {
                        string choice = AskSaveLocationDialog(batch[0], okCount);
                        if (!string.IsNullOrEmpty(choice) && choice != "keep")
                            foreach (Job j in batch)
                            {
                                if (j.Output == null || !File.Exists(j.Output)) continue;
                                string moved = MoveOutputTo(j, choice);
                                if (moved != null) { j.Output = moved; j.Size = new FileInfo(moved).Length; SetJob(j, "完成", "已放到 " + Path.GetDirectoryName(moved)); }
                            }
                    }
                    else if (!string.IsNullOrEmpty(queueSaveFolder))
                    {
                        foreach (Job j in batch)
                        {
                            if (j.Output == null || !File.Exists(j.Output)) continue;
                            string moved = MoveOutputTo(j, queueSaveFolder);
                            if (moved != null) { j.Output = moved; j.Size = new FileInfo(moved).Length; SetJob(j, "完成", "已放到 " + queueSaveFolder); }
                        }
                    }
                }
            }
            else
            {
                Log.Write("并行录制：没有拿到合并视频文件，无法切分");
                foreach (Job j in batch) SetJob(j, "失败", "并行录制没拿到文件");
            }
            Log.Write("==== 并行录制结束 ====");
        }

        void OnListDoubleClick(object sender, EventArgs e)
        {
            if (list.SelectedItems.Count == 0) return;
            Job j = (Job)list.SelectedItems[0].Tag;
            try
            {
                string p = File.Exists(j.Output) ? j.Output : j.Rep;
                Process.Start("explorer.exe", "/select,\"" + p + "\"");
            }
            catch { }
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            cancel = true;
            try { EndSnap(); } catch { }     // 万一在拖动中被关掉，快照位图要扔掉
            // 摘掉滚轮分流器：它握着对本窗体的引用，留着会让窗体关不掉（也免得窗口没了还去动控件）
            try { if (wheelRouter != null) Application.RemoveMessageFilter(wheelRouter); }
            catch { }
            // 还在录的话不要直接退出，否则 OBS 会一直在后台录下去
            if (!closing && worker != null && worker.IsAlive)
            {
                e.Cancel = true;
                closing = true;
                SetStatusText("正在停止录制并收尾…");
                Log.Write("关闭窗口：先停止录制并收尾…");
                Thread t = new Thread(delegate()
                {
                    try { worker.Join(20000); }
                    catch { }
                    Ui(delegate { Close(); });
                });
                t.IsBackground = true;
                t.Start();
                return;
            }
            cfg.Save();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 无界面模式：RepRecorder.exe --stop-record   （停止 OBS 上正在进行的录像）
            if (args != null && args.Length >= 1 && args[0] == "--stop-record")
            {
                StopRecordingCli();
                return;
            }

            // 无界面模式：RepRecorder.exe --set-record-dir "C:\xxx"
            if (args != null && args.Length >= 2 && args[0] == "--set-record-dir")
            {
                SetRecordDirCli(args[1]);
                return;
            }

            // 无界面模式：RepRecorder.exe --mic test|on|off   （全局麦克风静音自检/开关）
            if (args != null && args.Length >= 2 && args[0] == "--mic")
            {
                MicCli(args[1]);
                return;
            }

            // 无界面模式：RepRecorder.exe --game-sound on|off   （按进程静音/恢复游戏声音）
            if (args != null && args.Length >= 2 && args[0] == "--game-sound")
            {
                GameSoundCli(args[1]);
                return;
            }

            // 无界面模式：RepRecorder.exe --audio-meter 15   （订阅音频电平事件，逐秒打印各源电平）
            if (args != null && args.Length >= 2 && args[0] == "--audio-meter")
            {
                AudioMeterCli(args[1]);
                return;
            }

            // 无界面模式：RepRecorder.exe --sys-mute on|off|test   （端点静音：静音默认输出设备）
            if (args != null && args.Length >= 2 && args[0] == "--sys-mute")
            {
                SysMuteCli(args[1]);
                return;
            }

            // 无界面模式：RepRecorder.exe --obs-req GetVersion   （发任意 obs-websocket 请求，调试用）
            if (args != null && args.Length >= 2 && args[0] == "--obs-req")
            {
                ObsReqCli(args[1]);
                return;
            }

            // ---- 界面选择（2026-10-03 阶段 7 切换：默认已经是新界面）----
            // 不带参数 = WPF 新界面；加 --old 才是下面那套 WinForms（留作退路，行为一字未改）。
            // --wpf 仍然认（当同义词忽略），这样 Desktop 上那个旧快捷方式和 启动WPF.bat 不会失效。
            // ⚠ 必须在 Application.EnableVisualStyles 之后、创建任何窗口之前返回，两个栈别混着起。
            bool useOldUi = false;
            if (args != null)
                foreach (string a in args)
                    if (a == "--old" || a == "--winforms") { useOldUi = true; break; }
            if (!useOldUi) { Ui.Start(args); return; }

            try
            {
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                Log.Write("程序崩溃：" + ex.ToString());
                MessageBox.Show(ex.ToString(), "RepRecorder 出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

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

        // 全局音频设备（麦克风/Aux 等）不出现在 GetInputList 里，
        // 但它们的名字存在 OBS 的场景集合 JSON 中，这里把它读出来。
        public static List<string> FindGlobalAudioNames(bool micOnly)
        {
            List<string> result = new List<string>();
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                          @"obs-studio\basic\scenes");
                if (!Directory.Exists(dir)) return result;
                List<string> files = new List<string>(Directory.GetFiles(dir, "*.json"));
                files.Sort(delegate(string a, string b)
                {
                    return File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a));
                });
                foreach (string f in files)
                {
                    try
                    {
                        Dictionary<string, object> root = (Dictionary<string, object>)
                            new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8));
                        if (root == null) continue;
                        foreach (KeyValuePair<string, object> kv in root)
                        {
                            bool isMic = kv.Key.StartsWith("AuxAudioDevice");
                            bool isDesk = kv.Key.StartsWith("DesktopAudioDevice");
                            if (!isMic && !(isDesk && !micOnly)) continue;
                            Dictionary<string, object> d = kv.Value as Dictionary<string, object>;
                            if (d == null || !d.ContainsKey("name") || d["name"] == null) continue;
                            string n = Convert.ToString(d["name"]);
                            if (n.Length > 0 && !result.Contains(n)) result.Add(n);
                        }
                        if (result.Count > 0) break;   // 只看最新的那个场景集合
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        // 收集"麦克风类"或"桌面音频类"目标：可枚举的对应 kind 输入 + 全局音频设备。
        // 这两件事原来各写了一份，只有 kind 字符串和设备类别不同，合并成一个。
        static List<string> FindAudioTargets(ObsClient obs, string kindA, string kindB, bool wantMic)
        {
            List<string> names = new List<string>();
            try
            {
                foreach (object o in ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]))
                {
                    Dictionary<string, object> m = (Dictionary<string, object>)o;
                    string kind = Convert.ToString(m["unversionedInputKind"]);
                    if (kind != kindA && kind != kindB) continue;
                    string n = Convert.ToString(m["inputName"]);
                    if (!names.Contains(n)) names.Add(n);
                }
            }
            catch { }
            // ⚠ 找桌面音频时必须排掉"已经被当成麦克风加进来"的那些名字：全局设备可能两边都出现，
            //   只按 kind 过滤是不够的（原版就是靠这个 Contains 挡住的，合并时别丢）。
            List<string> mics = wantMic ? null : FindGlobalAudioNames(true);
            foreach (string n in FindGlobalAudioNames(wantMic))
                if ((mics == null || !mics.Contains(n)) && !names.Contains(n)) names.Add(n);
            return names;
        }

        public static List<string> FindMicTargets(ObsClient obs)
        {
            return FindAudioTargets(obs, "wasapi_input_capture", "coreaudio_input_capture", true);
        }

        public static List<string> FindDesktopTargets(ObsClient obs)
        {
            return FindAudioTargets(obs, "wasapi_output_capture", "coreaudio_output_capture", false);
        }

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

        static void MicCli(string mode)      {
            try
            {
                Settings cfg = Settings.Load();
                using (ObsClient obs = new ObsClient())
                {
                    obs.Connect(cfg.Port, cfg.Password);
                    List<string> all = FindGlobalAudioNames(false);
                    Log.Write("--mic " + mode + "：JSON 里的全局音频设备 = " +
                              (all.Count == 0 ? "<无>" : string.Join(" / ", all.ToArray())));
                    List<string> targets = FindMicTargets(obs);
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
    }

    // ============================================================ 按进程静音游戏声音（Windows Core Audio）
    // 只改该进程音频会话的静音状态，不改输出设备：
    //   - 正常玩游戏 → 点「打开声音」照常有声音
    //   - 录屏时 → 点「静音」你自己听不到，但 OBS 的「应用程序音频采集」抓的是进程声音流，录像仍有原声
    public static class GameAudio
    {
        // 端点静音：静音"默认输出设备"本身。作用在输出设备层，比进程音频会话更靠后，
        // 所以不会掐断 OBS 的进程音频采集（进程静音实测会掐断，所以按钮改用这个）。
        public static bool SetSystemMute(bool mute, out string message)
        {
            message = "";
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object volObj = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                if (hr != 0) { message = "取默认输出设备失败 hr=" + hr; return false; }
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out volObj);
                if (hr != 0) { message = "Activate 失败 hr=" + hr; return false; }
                IAudioEndpointVolume vol = (IAudioEndpointVolume)volObj;
                Guid ctx = Guid.Empty;
                hr = vol.SetMute(mute ? 1 : 0, ref ctx);
                if (hr != 0) { message = "SetMute 失败 hr=" + hr; return false; }
                int now;
                vol.GetMute(out now);
                message = "默认输出设备现在" + (now != 0 ? "已静音" : "未静音");
                return (now != 0) == mute;
            }
            catch (Exception ex) { message = ex.Message; return false; }
            finally
            {
                if (volObj != null) { try { Marshal.ReleaseComObject(volObj); } catch { } }
                if (device != null) { try { Marshal.ReleaseComObject(device); } catch { } }
                if (enumerator != null) { try { Marshal.ReleaseComObject(enumerator); } catch { } }
            }
        }

        public static bool GetSystemMute(out bool muted, out string message)
        {
            muted = false;
            message = "";
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object volObj = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                if (hr != 0) { message = "取默认输出设备失败 hr=" + hr; return false; }
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out volObj);
                if (hr != 0) { message = "Activate 失败 hr=" + hr; return false; }
                IAudioEndpointVolume vol = (IAudioEndpointVolume)volObj;
                int m;
                hr = vol.GetMute(out m);
                if (hr != 0) { message = "GetMute 失败 hr=" + hr; return false; }
                muted = (m != 0);
                message = "读取成功";
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
            finally
            {
                if (volObj != null) { try { Marshal.ReleaseComObject(volObj); } catch { } }
                if (device != null) { try { Marshal.ReleaseComObject(device); } catch { } }
                if (enumerator != null) { try { Marshal.ReleaseComObject(enumerator); } catch { } }
            }
        }

        public static bool SetProcessMute(int pid, bool mute, out string message)
        {
            message = "";
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object managerObj = null;
            IAudioSessionManager2 manager = null;
            IAudioSessionEnumerator sessions = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);   // eRender, eMultimedia
                if (hr != 0) { message = "取默认输出设备失败 hr=" + hr; return false; }

                Guid iid = typeof(IAudioSessionManager2).GUID;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out managerObj);   // CLSCTX_ALL
                if (hr != 0) { message = "Activate 失败 hr=" + hr; return false; }
                manager = (IAudioSessionManager2)managerObj;

                hr = manager.GetSessionEnumerator(out sessions);
                if (hr != 0) { message = "取音频会话列表失败 hr=" + hr; return false; }

                int count = 0;
                sessions.GetCount(out count);
                int hit = 0;
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl = null;
                    try
                    {
                        sessions.GetSession(i, out ctl);
                        if (ctl == null) continue;
                        IAudioSessionControl2 ctl2 = ctl as IAudioSessionControl2;
                        if (ctl2 == null) continue;
                        uint spid = 0;
                        ctl2.GetProcessId(out spid);
                        if ((int)spid != pid) continue;
                        ISimpleAudioVolume vol = ctl as ISimpleAudioVolume;
                        if (vol == null) continue;
                        Guid ctx = Guid.Empty;
                        vol.SetMute(mute, ref ctx);
                        hit++;
                    }
                    catch { }
                    finally { if (ctl != null) { try { Marshal.ReleaseComObject(ctl); } catch { } } }
                }
                message = hit > 0
                    ? ("已" + (mute ? "静音" : "恢复") + " " + hit + " 个音频会话")
                    : "没有找到该进程的音频会话（游戏此时没有在发声？）";
                return hit > 0;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
            finally
            {
                if (sessions != null) { try { Marshal.ReleaseComObject(sessions); } catch { } }
                if (manager != null) { try { Marshal.ReleaseComObject(manager); } catch { } }
                if (managerObj != null && !object.ReferenceEquals(managerObj, manager)) { try { Marshal.ReleaseComObject(managerObj); } catch { } }
                if (device != null) { try { Marshal.ReleaseComObject(device); } catch { } }
                if (enumerator != null) { try { Marshal.ReleaseComObject(enumerator); } catch { } }
            }
        }
    }

    // ============================================================ 背景图支持
    // 关键点：必须重写 OnPaintBackground，不能挂 Paint 事件。
    //   子控件设置 BackColor = Color.Transparent 时，WinForms 调用的是父控件的
    //   OnPaintBackground（经 InvokePaintBackground），Paint 事件压根不会被触发 ——
    //   挂 Paint 事件的话，子控件透明出来的是父控件的底色，不是背景图。
    public static class Bg
    {
        // 背景图只画在【右侧内容区】（队列 + 终端）。左栏控制面板、顶栏、状态栏都不画。
        //   Cache        = 内容区大小、已按摆放/不透明度/模糊处理好的位图
        //   CacheOrigin  = 内容区左上角在"窗体客户区"坐标系里的位置
        public static Bitmap Cache;
        public static Point CacheOrigin = Point.Empty;

        // 图像允许出现的范围（"窗体客户区"坐标系）= 右侧内容区。
        // ⚠ 这是**权威裁剪区**，几何一变就由窗体同步刷新。
        //   CacheOrigin 只是建图那一刻的快照，天生会落后于布局（拖分割线 / 收起面板 / 启动时
        //   InitPanelLayout 还没跑）。只拿它裁剪的话，陈旧的那张宽图会一直画到左边控制面板上
        //   —— 用户报的"图片不跟随分割线"就是这么来的（实测 CacheOrigin.X=54，而面板归位后是 380）。
        //   Width<=0 表示"还没有内容区"，此时不裁剪。
        public static Rectangle Clip = Rectangle.Empty;

        // 控件左上角在"窗体客户区"坐标系里的位置
        public static Point OffsetIn(Control c)
        {
            Form f = c.FindForm();
            if (f == null) return Point.Empty;
            return f.PointToClient(c.PointToScreen(Point.Empty));
        }

        // 把控件内的矩形 r 铺上对应的背景图。超出图区的部分自动裁掉 ——
        // 这就是"左栏不画图"的实现方式：左栏整个落在图区外，交集为空，直接不画，保留它自己的底色。
        //
        // ⚠ 2026-10-03 改：目标区改成【实时的 Clip】，缓存位图按比例拉伸过去 —— 这就是"缩放窗口 /
        //   拖分隔条时图片实时跟随"的实现。以前是拿 CacheOrigin + Cache.Size 1:1 死贴，
        //   而缓存位图在整个拖动过程中【不会重建】（见 ScheduleBgRebuild：拖动期间根本不排重建），
        //   于是新露出来的那块没有任何像素可画 —— 用户看到的就是"刚缩放出来的区域变透明，
        //   松手才恢复"。拉伸预览画的正是"松手后重建出来会是什么样"，松手时无缝接上。
        //   尺寸一致时（日常绝大多数时候）走下面那条整数路径，和以前一模一样，不会平白变慢或变糊。
        public static void PaintRect(Control c, Graphics g, Rectangle r)
        {
            if (Cache == null || r.Width <= 0 || r.Height <= 0) return;
            // 图该铺到哪儿（"窗体客户区"坐标系）：Clip 是权威（几何一变就由窗体同步刷新）；
            // 它还没定下来时（Width<=0）退回建图那一刻的快照。
            Rectangle dest = (Clip.Width > 0 && Clip.Height > 0) ? Clip : new Rectangle(CacheOrigin, Cache.Size);
            if (dest.Width <= 0 || dest.Height <= 0) return;
            Point o = OffsetIn(c);
            // 图在"这个控件的坐标系"里占的矩形
            Rectangle destInCtl = new Rectangle(dest.X - o.X, dest.Y - o.Y, dest.Width, dest.Height);
            Rectangle inter = Rectangle.Intersect(r, destInCtl);
            if (inter.Width <= 0 || inter.Height <= 0) return;
            if (dest.Width == Cache.Width && dest.Height == Cache.Height)
            {
                // 尺寸一致：老路径，1:1 直贴（显式给源/目标矩形，避免 DrawImage 按图片 DPI 缩放）
                g.DrawImage(Cache, inter,
                            inter.X - destInCtl.X, inter.Y - destInCtl.Y, inter.Width, inter.Height,
                            GraphicsUnit.Pixel);
                return;
            }
            // 尺寸不一致 = 正在缩放 / 正在拖分隔条，缓存还是旧那张：整块拉过去当预览
            float sx = Cache.Width / (float)dest.Width, sy = Cache.Height / (float)dest.Height;
            g.DrawImage(Cache, inter,
                        (inter.X - destInCtl.X) * sx, (inter.Y - destInCtl.Y) * sy,
                        inter.Width * sx, inter.Height * sy, GraphicsUnit.Pixel);
        }

        public static void Paint(Control c, Graphics g)
        {
            PaintRect(c, g, c.ClientRectangle);
        }

        // 这个控件底下"应该是什么颜色"：自己有实底色就用自己，透明就往父控件上找。
        // ⚠ 自绘控件（ButtonBase 系）设了 ControlStyles.Opaque —— 背景不会被自动擦掉，
        //   必须先整块填一遍，否则上一次画的文字会留下重影（实测）。
        public static Color EffectiveBack(Control c)
        {
            if (c.BackColor != Color.Transparent) return c.BackColor;
            Control p = c.Parent;
            while (p != null)
            {
                if (p.BackColor != Color.Transparent) return p.BackColor;
                p = p.Parent;
            }
            return MainForm.HudBg;
        }

        public static void Clear(Control c, Graphics g)
        {
            using (SolidBrush b = new SolidBrush(EffectiveBack(c)))
                g.FillRectangle(b, c.ClientRectangle);
        }
    }

    // 顶栏右边的窗口按钮（最小化 / 最大化-还原 / 关闭）。
    // 窗口是无边框的，系统标题栏连按钮一起没了 —— 这三个得自己画。
    // 字形用线画，不用「─ ☐ ✕」这类字符：字体里到底有没有这个字形、基线在哪、大小对不对，
    // 得看机器说话（这软件已经踩过 TextRenderer 不吃旋转变换的坑）；画线是确定的。
    public class HudCapButton : Control
    {
        public const int KindMin = 0, KindMax = 1, KindClose = 2;

        readonly int kind;
        bool hover = false;

        public bool Maximized;      // 只有「最大化」按钮用：最大化时改画"还原"那个双层方框

        public HudCapButton(int kind)
        {
            this.kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // ⚠ 不能写 Color.Transparent：Control 基类没开 SupportsTransparentBackColor，
            //   设透明会直接抛 ArgumentException（实测，整个程序起不来）。反正 OnPaintBackground
            //   是整块填的，给个实色就行。
            BackColor = MainForm.HudPanel;
            TabStop = false;                    // 别把键盘焦点从主界面抢走
            Cursor = Cursors.Default;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        // 按钮底色（悬停时会变），顺便给 OnPaint 填"还原"图标那个遮挡用
        Color Face()
        {
            if (!hover) return MainForm.HudPanel;
            return (kind == KindClose) ? MainForm.HudRed : MainForm.HudPanel2;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Face()))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int cx = Width / 2, cy = Height / 2;
            Color ink = hover ? (kind == KindClose ? Color.White : MainForm.HudText) : MainForm.HudDim;

            if (kind == KindMin)
            {
                // 减号：画实心 1px 矩形比 DrawLine 更稳，永远是干净的一像素
                using (SolidBrush b = new SolidBrush(ink)) g.FillRectangle(b, cx - 5, cy, 10, 1);
                return;
            }
            if (kind == KindClose)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;   // 斜线不抗锯齿会有台阶
                using (Pen p = new Pen(ink, 1.4f))
                {
                    g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                }
                return;
            }
            // 最大化 / 还原：直角方框，不抗锯齿，1px 线最清晰
            g.SmoothingMode = SmoothingMode.None;
            using (Pen p = new Pen(ink, 1f))
            {
                if (!Maximized)
                {
                    g.DrawRectangle(p, cx - 5, cy - 5, 10, 10);
                }
                else
                {
                    // 后面那个框
                    g.DrawRectangle(p, cx - 5, cy - 5, 8, 8);
                    // 前面那个框：先用按钮底色盖掉重叠部分，再描边（标准"还原"图标就这么画）
                    using (SolidBrush b = new SolidBrush(Face()))
                        g.FillRectangle(b, cx - 1, cy - 1, 8, 8);
                    g.DrawRectangle(p, cx - 2, cy - 2, 8, 8);
                }
            }
        }
    }

    // 会画背景图的 Panel / FlowLayoutPanel（顶栏、左栏、三块边框面板用）
    public class BgPanel : Panel
    {
        public BgPanel() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 先整块填底色再叠图：面板落在背景图范围外的部分（比如左栏）就不会留下残影
            Bg.Clear(this, e.Graphics);
            if (Bg.Cache != null) Bg.Paint(this, e.Graphics);
        }
    }

    public class BgFlowPanel : FlowLayoutPanel
    {
        public BgFlowPanel() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Bg.Clear(this, e.Graphics);
            if (Bg.Cache != null) Bg.Paint(this, e.Graphics);
        }
    }

    // 滚轮分流器。挂在消息泵上（Application.AddMessageFilter），把 WM_MOUSEWHEEL 按【光标位置】
    // 交给该滚的那一块，而不是交给"有焦点的那个控件"。真要干的事都在 MainForm.RouteWheel 里。
    public class WheelRouter : IMessageFilter
    {
        const int WM_MOUSEWHEEL = 0x020A;
        readonly MainForm form;

        public WheelRouter(MainForm form) { this.form = form; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;    // 走开得早，别拖慢其它消息
            // ⚠ delta 是 wParam 的高 16 位，必须是【有符号】的：滚轮向上是 +120、向下是 -120，
            //   直接当无符号取会变成 65416 这种，除以 120 整个方向就乱了。
            int delta = (short)((long)m.WParam >> 16);
            try { return form.RouteWheel(delta, Cursor.Position); }
            catch { return false; }
        }
    }

    // 自绘滚动条。
    // 原生滚动条只有"系统浅色 / 深色"两套可挑，配不上我们的中灰主题（实测原生渲染出来是 #171717，
    // 压在 #3C3E45 的面板上非常突兀）。所以自己画：槽用面板色、滑块用主题色，拖动/点击/滚轮都自己处理。
    public class HudScrollBar : Control
    {
        int min = 0, max = 0, val = 0;
        int large = 1;                 // 可见范围（决定滑块长度）
        bool dragging = false;
        int dragOff = 0;

        // ---- 自动隐藏（给左栏用）----
        // 左栏那一列控件本来就比视口高约 1.8 倍，滚动条一直是"用得上"的状态，于是常驻画在那里
        // （用户报的"不好看"）。AutoHide = 平时【不画出来】（铺成周围底色，看不出这里有个控件）。
        // ⚠ 只在【真的用它】的时候才露面：滚轮滚它 / 滚左栏 / 点它 / 拖它。
        //   以前还挂了"鼠标划过就出现"，用户嫌烦（每次划过都弹一下）—— 已经去掉，划过不算用它。
        // ⚠ 只改"画不画"：Visible、Maximum、拖动、滚轮转发，全都没动。
        public bool AutoHide = false;
        public Color IdleBack = MainForm.HudPanel;   // 隐藏时铺的底色（铺成周围那个色才叫看不见）
        bool active = false;
        System.Windows.Forms.Timer idleTimer;

        public void Flash()            // 外部动了它（比如窗体那边转发滚轮进来）也叫醒一下
        {
            if (!AutoHide) return;
            Wake();
        }

        void Wake()
        {
            try
            {
                if (!AutoHide) return;
                if (idleTimer == null)
                {
                    idleTimer = new System.Windows.Forms.Timer();
                    idleTimer.Interval = 1200;
                    idleTimer.Tick += delegate
                    {
                        idleTimer.Stop();
                        // 还在拖 → 继续留着（拖到一半消失就太怪了）。别的都不留：
                        // 鼠标只是压在它上面不算"在用"，1.2 秒后照常收起来。
                        if (dragging) { idleTimer.Start(); return; }
                        active = false;
                        Invalidate();
                    };
                }
                idleTimer.Stop();
                idleTimer.Start();
                if (!active) { active = true; Invalidate(); }
            }
            catch { }
        }

        public event EventHandler ValueChanged;

        public HudScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = 10;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return min; } set { min = value; if (val < min) val = min; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; if (val > max) val = max; Invalidate(); } }
        public int LargeChange { get { return large; } set { large = Math.Max(1, value); Invalidate(); } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        Rectangle Thumb()
        {
            int h = ClientSize.Height;
            int range = max - min;                 // 可滚动范围
            int total = range + large;             // 内容总长
            if (total <= 0) return new Rectangle(0, 0, Width, h);
            int th = Math.Max(22, (int)((long)h * large / total));
            if (th > h) th = h;
            int span = h - th;
            int top = (range <= 0) ? 0 : (int)((long)span * (val - min) / range);
            return new Rectangle(0, top, Width, th);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // 自动隐藏且没被唤醒 → 整条铺成周围的面板色，等于"这里没有东西"
            if (AutoHide && !active)
            {
                using (SolidBrush b = new SolidBrush(IdleBack))
                    g.FillRectangle(b, ClientRectangle);
                // 背景模式下光铺实色底色会露出一条深色竖缝 —— 把图也铺上，这条才真的"隐形"
                if (Bg.Cache != null) Bg.Paint(this, g);
                return;
            }
            using (SolidBrush b = new SolidBrush(MainForm.HudPanel2))
                g.FillRectangle(b, ClientRectangle);
            Rectangle t = Thumb();
            using (SolidBrush b = new SolidBrush(dragging ? MainForm.HudAccent : MainForm.HudLine))
                g.FillRectangle(b, t);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Wake();                                   // 点它也是"用上了"
            Rectangle t = Thumb();
            if (t.Contains(e.Location)) { dragging = true; dragOff = e.Y - t.Top; }
            else Value = val + (e.Y < t.Top ? -large : large);   // 点槽里空白 = 翻一页
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                Wake();                               // 拖着的时候一直留着，别在拖动中消失
                int span = ClientSize.Height - Thumb().Height;
                int range = max - min;
                if (span > 0 && range > 0)
                {
                    int top = Math.Max(0, Math.Min(span, e.Y - dragOff));
                    Value = min + (int)Math.Round((double)top * range / span);
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Wake();                               // 滚它就说明"用上了" → 让它露面
            Value = val - (e.Delta / 120) * 3;
            base.OnMouseWheel(e);
        }
    }

    // 自绘复选框。
    // 系统画的那个小方框在深色底上要么是白底黑勾、要么和背景糊在一起（用户反馈"勾选颜色看不清晰"），
    // 所以整个自己画：方框用主题色，勾用薄荷绿，未勾中时是暗底 + 灰边。
    // 框选那层"半透明蓝框"。
    // ⚠ 为什么不用"在 ListView 的 WM_PAINT 之后补画"（第一版就是那样，用户实测"框一直闪"）：
    //   半透明填充每帧会再叠一层（22%+22%+…），所以每帧都必须让列表把那块**重画干净**；
    //   列表是自绘的，60 行时那块很大，"列表重画"和"补画框"之间屏幕会闪一下。
    //   改成独立的一层窗体（自带透明度）之后，框画在自己的窗口上，**列表一帧都不用重画**。
    public class HudMarquee : Form
    {
        const int WS_EX_TOOLWINDOW = 0x00000080;   // 不进 alt-tab
        const int WS_EX_TRANSPARENT = 0x00000020;  // 不吃鼠标
        const int WS_EX_NOACTIVATE = 0x08000000;   // 不抢焦点

        public HudMarquee()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // 填充用"亮一档的冷蓝"：框里那几行拖的过程中已经是蓝的（选中态）了，再叠半透明蓝还是蓝，
            // 看不出"框住了一块"；叠亮色才像 Explorer 那样明显亮一层（实测像素：叠强调蓝只有 #5E70B1，
            // 跟选中行 #58628C 几乎分不出来）。
            BackColor = MainForm.HudLog;
            Opacity = 0.42;                      // 整层半透明（边框也跟着淡，所以边框另外挑更亮的颜色画）
            TopMost = true;
            SetStyle(ControlStyles.Selectable, false);
            Visible = false;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(MainForm.HudText, 2f))     // 填充已经是亮蓝了，边框得再亮一档才分得出来
                e.Graphics.DrawRectangle(p, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        }
    }

    // 自绘下拉框。
    // ⚠ WinForms 的 ComboBox（DropDownList）**不吃 BackColor**：只要程序开了视觉样式
    //   （Application.EnableVisualStyles），它就一定画成系统那套白底 + 系统色下拉箭头按钮 ——
    //   压在近黑底上就它一块白（用户报的"全屏最亮的那块"）。实测两件事单独做都不够：
    //     · 只设 FlatStyle.Flat + BackColor —— 完全无效，还是白的；
    //     · 只调 SetWindowTheme(h,"","") —— 框面变深了，右边那个箭头按钮还是系统色。
    //   所以这里一起做：先关掉系统主题（BackColor 才生效），再在 base 画完之后**把整个客户区
    //   重画一遍**（框面底色 + 选中项文字 + 右侧箭头），把系统那套彻底盖掉。
    //   下拉列表里的条目走 DrawItem 自己画。
    public class HudCombo : ComboBox
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        public Color Face = MainForm.HudPanel2;      // 合起来那条框的底色
        public Color Ink = MainForm.HudText;

        public HudCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 18;
            FlatStyle = FlatStyle.Flat;
            BackColor = Face;
            ForeColor = Ink;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DrawItem += OnDrawRow;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetWindowTheme(Handle, "", ""); } catch { }     // 关掉系统主题，BackColor 才生效
        }

        // 下拉列表里的每一行（列表底色比那条框再暗一档，选中行才用亮一档的蓝）
        void OnDrawRow(object sender, DrawItemEventArgs e)
        {
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? MainForm.HudOn : MainForm.HudPanel))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0 && e.Index < Items.Count)
                TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font,
                                      new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                                      Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F || m.Msg == 0x0005)               // WM_PAINT / WM_SIZE
            {
                try { PaintFace(); } catch { }
            }
        }

        // 合起来那一条：底色 + 文字 + 右侧箭头（系统画的那套全被这层盖掉）
        void PaintFace()
        {
            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                Rectangle rc = ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;
                int arrow = 18;
                if (Bg.Cache != null)
                {
                    // 有背景图：先铺图再压一层半透明面色 —— 框不再是实色黑块，字也照样清楚
                    Bg.Paint(this, g);
                    using (SolidBrush vb = new SolidBrush(Color.FromArgb(150, Face.R, Face.G, Face.B)))
                        g.FillRectangle(vb, rc);
                }
                else using (SolidBrush b = new SolidBrush(Face)) g.FillRectangle(b, rc);
                using (Pen p = new Pen(MainForm.HudLine)) g.DrawRectangle(p, 0, 0, rc.Width - 1, rc.Height - 1);
                TextRenderer.DrawText(g, Text, Font,
                                      new Rectangle(5, 0, rc.Width - arrow - 8, rc.Height), Ink,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                using (Pen p2 = new Pen(MainForm.HudLine)) g.DrawLine(p2, rc.Width - arrow, 1, rc.Width - arrow, rc.Height - 2);
                int cx = rc.Width - arrow / 2, cy = rc.Height / 2 + 1;      // 小箭头
                using (Pen p3 = new Pen(MainForm.HudDim, 1.6f))
                {
                    g.DrawLine(p3, cx - 4, cy - 2, cx, cy + 2);
                    g.DrawLine(p3, cx, cy + 2, cx + 4, cy - 2);
                }
            }
        }
    }

    public class HudCheck : CheckBox
    {
        public HudCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = MainForm.HudText;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // ⚠ 必须先整块填底：ButtonBase 设了 ControlStyles.Opaque，背景不会自动擦，
            //   不填的话上一次的文字会留下来变成重影（实测）
            Bg.Clear(this, g);
            if (Bg.Cache != null) Bg.Paint(this, g);

            int bs = 14;
            int by = (ClientSize.Height - bs) / 2;
            Rectangle box = new Rectangle(1, by, bs, bs);

            using (SolidBrush b = new SolidBrush(Checked ? MainForm.HudAccent : MainForm.HudPanel2))
                g.FillRectangle(b, box);
            using (Pen p = new Pen(Checked ? MainForm.HudAccent : MainForm.HudLine))
                g.DrawRectangle(p, box);

            if (Checked)
            {
                using (Pen p = new Pen(Color.White, 2f))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawLines(p, new Point[]
                    {
                        new Point(box.Left + 3, box.Top + 7),
                        new Point(box.Left + 6, box.Top + 10),
                        new Point(box.Right - 3, box.Top + 4)
                    });
                }
            }

            int tx = box.Right + 6;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, ClientSize.Width - tx, ClientSize.Height),
                                  Enabled ? ForeColor : MainForm.HudDim,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // 能透出背景图的 ListView。
    // 分两块画：
    //   · 每一【行】的图由 OnHudDrawItem 负责（列表本来就是全自绘的，天然合适）
    //   · 最后一行【下面】那块空白由这里补 —— 控件在 owner-draw 模式下不走 WM_ERASEBKGND
    //     （它设了 ControlStyles.Opaque），只能等它自己画完，再用 WM_PAINT 补一遍。
    public class BgListView : ListView
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WRect r);
        [StructLayout(LayoutKind.Sequential)] struct WRect { public int L, T, R, B; }

        // 队列为空时居中显示的提示文字（由 MainForm 设）。必须在本控件的 WM_PAINT 末尾画，
        // 不能挂 Paint 事件 —— 原生 ListView 的绘制会把 Paint 事件画的字盖掉（实测）。
        public string EmptyHint = "";

        public BgListView() { DoubleBuffered = true; }

        int HeaderHeight()
        {
            try
            {
                IntPtr hdr = SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);   // LVM_GETHEADER
                if (hdr != IntPtr.Zero)
                {
                    WRect r;
                    if (GetWindowRect(hdr, out r)) return r.B - r.T;
                }
            }
            catch { }
            return 0;
        }

        void PaintEmptyArea(Graphics g)
        {
            if (Bg.Cache == null) return;
            int startY = (Items.Count > 0) ? Items[Items.Count - 1].Bounds.Bottom : HeaderHeight();
            int ch = ClientSize.Height;
            if (startY >= ch) return;
            Rectangle empty = new Rectangle(0, startY, ClientSize.Width, ch - startY);
            Bg.PaintRect(this, g, empty);
        }

        [DllImport("user32.dll")] static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out WRect r);
        [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from, IntPtr to, ref WPoint pts, int n);
        [StructLayout(LayoutKind.Sequential)] struct WPoint { public int X, Y; }

        // 非客户区（= 原生滚动条待的那一圈）自己刷成列表底色。
        // 藏滚动条（ShowScrollBar）只是把它按回去，comctl32 在滚动状态一变时又会自己把它显出来，
        // 而真正【画】那条滚动条的是 DefWindowProc 处理 WM_NCPAINT 那一步 —— 于是屏幕上闪一下
        // 系统浅色的滚动条（用户报的"白色滚动条闪烁"）。把 WM_NCPAINT 拦下来自己画，它就永远画不出来。
        // 注意：这里只拦【绘制】，一点没碰滚动能力。
        void PaintNcBand()
        {
            if (!IsHandleCreated) return;
            WRect wr, cr;
            if (!GetWindowRect(Handle, out wr)) return;
            if (!GetClientRect(Handle, out cr)) return;
            WPoint p; p.X = 0; p.Y = 0;
            MapWindowPoints(Handle, IntPtr.Zero, ref p, 1);      // 客户区原点 → 屏幕坐标
            int offX = p.X - wr.L, offY = p.Y - wr.T;            // 非客户区四条带子的宽度
            int w = wr.R - wr.L, h = wr.B - wr.T;
            int right = offX + (cr.R - cr.L), bottom = offY + (cr.B - cr.T);
            if (offX <= 0 && offY <= 0 && right >= w && bottom >= h) return;   // 本来就没有非客户区
            IntPtr hdc = GetWindowDC(Handle);
            if (hdc == IntPtr.Zero) return;
            try
            {
                using (Graphics g = Graphics.FromHdc(hdc))
                {
                    // 底：不透明那一层（有背景图时图是半透明的，得先垫一层实色）
                    Color baseC = (BackColor.A == 255) ? BackColor : MainForm.HudBg;
                    using (SolidBrush b = new SolidBrush(baseC))
                    {
                        if (offY > 0) g.FillRectangle(b, 0, 0, w, offY);
                        if (bottom < h) g.FillRectangle(b, 0, bottom, w, h - bottom);
                        if (offX > 0) g.FillRectangle(b, 0, 0, offX, h);
                        if (right < w) g.FillRectangle(b, right, 0, w - right, h);
                    }
                    // 面：背景图（和"补空白区"用的是同一套，位置才对得上）
                    if (Bg.Cache != null)
                    {
                        if (right < w) Bg.PaintRect(this, g, new Rectangle(right, 0, w - right, h));
                        if (bottom < h) Bg.PaintRect(this, g, new Rectangle(0, bottom, w, h - bottom));
                    }
                }
            }
            finally { ReleaseDC(Handle, hdc); }
        }

        protected override void WndProc(ref Message m)
        {
            // WM_NCPAINT：见 PaintNcBand 上面的说明。拦下来就不往下传，DefWindowProc 也就不会去画滚动条。
            if (m.Msg == 0x0085)
            {
                try { PaintNcBand(); } catch { }
                return;
            }
            base.WndProc(ref m);
            // WM_SIZE：客户区一变（典型情况就是 comctl32 刚给自己塞进一条原生滚动条）就把它按回去。
            // ⚠ 放在这里而不是 WM_PAINT 里：这一步会改窗口的非客户区，在绘制过程中改会重入。
            if (m.Msg == 0x0005)
            {
                try { if (IsHandleCreated) ShowScrollBar(Handle, 3 /*SB_BOTH*/, false); } catch { }
            }
            if (m.Msg == 0x000F)   // WM_PAINT：原生画完了，最后再补空白区 + 空态提示（这样才盖得住）
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(Handle))
                    {
                        if (Bg.Cache != null) PaintEmptyArea(g);
                        if (Items.Count == 0 && !string.IsNullOrEmpty(EmptyHint))
                        {
                            Size sz = TextRenderer.MeasureText(EmptyHint, Font);
                            int top = HeaderHeight();
                            int x = (ClientSize.Width - sz.Width) / 2;
                            int y = top + (ClientSize.Height - top - sz.Height) / 2;
                            TextRenderer.DrawText(g, EmptyHint, Font, new Point(x, y), MainForm.HudDim);
                        }
                    }
                }
                catch { }
            }
        }
    }

    // 能透出背景图的终端。TextBox 的 BackColor 不支持 alpha，只能换成一个自己画的控件：
    // 按行折行、自己滚、自己画。
    public class BgLogView : Control
    {
        readonly List<string> raw = new List<string>();    // 原始行
        readonly List<string> disp = new List<string>();   // 折好行之后的显示行
        readonly Dictionary<char, int> charW = new Dictionary<char, int>();   // 每个字符的宽度缓存
        readonly HudScrollBar bar;
        int lineH = 16;
        int lastWrapWidth = -1;
        const int MaxRaw = 2000;                            // 只留最近这么多原始行，免得越跑越慢

        public BgLogView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            bar = new HudScrollBar();
            bar.Dock = DockStyle.Right;
            bar.Width = 10;
            bar.ValueChanged += delegate { Invalidate(); };
            Controls.Add(bar);
            lineH = Math.Max(8, Font.Height);
            UpdateBar(false);            // 一开始日志是空的 → 先判断一次，把滚动条藏好
            // 原来的 TextBox 能选中复制，自绘控件不行 → 至少给个"复制全部"
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("复制全部日志", null, delegate
            {
                try { Clipboard.SetText(string.Join(Environment.NewLine, raw.ToArray())); }
                catch { }
            });
            ContextMenuStrip = menu;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            lineH = Math.Max(8, Font.Height);
            charW.Clear();          // 换字体了，之前缓存的字符宽度作废
            RewrapAll();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int w = TextWidth();
            if (w != lastWrapWidth)
            {
                RewrapAll();                    // 宽度变了才需要重新折行
                return;
            }
            // ⚠ 只改了高度的情况（最典型的就是拖「队列 / 终端」那条分割线）：
            //   不用重新折行，但"一屏能显示几行"变了，滚动范围必须跟着重算。
            //   原来这里什么都不做 —— 面板拖矮之后就滚不到最后几行，拖高反而能滚出一片空白。
            bool wasBottom = IsAtBottom();
            UpdateBar(false);
            if (wasBottom) bar.Value = bar.Maximum;   // 本来就贴着底部 → 继续贴着
            Invalidate();
        }

        int TextWidth()
        {
            return Math.Max(32, ClientSize.Width - bar.Width - 6);
        }

        // 单个字符的宽度（带缓存）
        // ⚠ 不能每折一行就调一次 MeasureText：那是逐字符测量，2000 行的日志在改窗口大小时
        //   要重排几十万次，会卡好几秒。终端本来就是等宽字体，按字符缓存宽度即可。
        int WidthOf(char c)
        {
            int w;
            if (charW.TryGetValue(c, out w)) return w;
            w = TextRenderer.MeasureText(c.ToString(), Font, new Size(int.MaxValue, int.MaxValue),
                                         TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            if (w <= 0) w = 1;
            charW[c] = w;
            return w;
        }

        List<string> WrapLine(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) { outp.Add(""); return outp; }
            int maxW = TextWidth();
            int cur = 0;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                int w = WidthOf(s[i]);
                if (sb.Length > 0 && cur + w > maxW)
                {
                    outp.Add(sb.ToString());
                    sb.Length = 0;
                    cur = 0;
                }
                sb.Append(s[i]);
                cur += w;
            }
            outp.Add(sb.ToString());
            return outp;
        }

        void RewrapAll()
        {
            // ⚠ 重排【不能】强行拉到底。原来这里写死 UpdateBar(true)，结果：
            //   横向改一下窗口尺寸、或者日志超过 MaxRaw 被裁剪，都会把正在往上翻看的用户
            //   一把拽回最底下（用户报的）。改成：只有原本就贴在底部（在跟日志尾巴）时才跟着走，
            //   否则停在原来的位置（Value 的 setter 会自己把越界夹回去）。
            bool wasBottom = IsAtBottom();
            int oldVal = bar.Value;
            lastWrapWidth = TextWidth();
            disp.Clear();
            foreach (string s in raw)
                foreach (string w in WrapLine(s)) disp.Add(w);
            UpdateBar(false);
            bar.Value = wasBottom ? bar.Maximum : oldVal;
            Invalidate();                       // 重排后一定要重画，否则画面还是旧的
        }

        bool IsAtBottom()
        {
            return bar.Value >= bar.Maximum;   // 自绘滚动条：Maximum 就是"能滚到的最大值"
        }

        void UpdateBar(bool goBottom)
        {
            int visible = Math.Max(1, ClientSize.Height / lineH);
            bar.Minimum = 0;
            bar.Maximum = Math.Max(0, disp.Count - visible);   // 第一行最多能滚到"最后 N 行刚好填满"
            bar.LargeChange = visible;
            // 不够长就藏起来 —— 和列表、左栏那两处保持一致（那两个也是不够长就 Visible=false）。
            // ⚠ TextWidth() 里始终扣掉了 bar.Width，所以藏起来不会改变文字宽度 →
            //   不会触发 OnSizeChanged → 不会来回重排（重排 2000 行很贵）。
            bool need = bar.Maximum > 0;
            if (bar.Visible != need) bar.Visible = need;
            if (goBottom) bar.Value = bar.Maximum;
        }

        // 滚轮：WinForms 会把 WM_MOUSEWHEEL 发给【有焦点的控件】再往上冒泡，
        // 鼠标停在终端上时焦点常常在别处 —— 所以窗体那边会按光标位置转发进来（见 MainForm 的 MouseWheel）。
        public void ScrollBy(int wheelClicks)
        {
            try
            {
                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines <= 0) lines = 3;
                // ⚠ 方向：Windows 里"滚轮向上滚"给的是【正】delta，而向上滚应该让内容往下走、
                //   Value 减小 —— 所以是【减】。这里原来写的是加，整个滚动方向是反的（用户报的）。
                //   列表和左栏那两处转发、以及 HudScrollBar.OnMouseWheel 都是减，只有这里是加 —— 就是它错了。
                bar.Value = bar.Value - wheelClicks * lines;   // 越界由 Value 自己夹
            }
            catch { }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(e.Delta / 120);
            base.OnMouseWheel(e);
        }

        public void AppendText(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            bool atBottom = IsAtBottom();
            string[] parts = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int n = parts.Length;
            if (n > 0 && parts[n - 1].Length == 0) n--;     // 结尾那个换行不算一行
            for (int i = 0; i < n; i++)
            {
                raw.Add(parts[i]);
                foreach (string w in WrapLine(parts[i])) disp.Add(w);
            }
            if (raw.Count > MaxRaw)
            {
                raw.RemoveRange(0, raw.Count - MaxRaw);
                RewrapAll();                                 // 偶尔才走一次
                return;
            }
            UpdateBar(atBottom);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Bg.Cache != null) Bg.Paint(this, g);
            else g.Clear(MainForm.HudBg);
            int first = bar.Value;
            int y = 1;
            int h = ClientSize.Height;
            for (int i = first; i < disp.Count && y < h; i++)
            {
                TextRenderer.DrawText(g, disp[i], Font, new Point(3, y), MainForm.HudLog,
                                      TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                y += lineH;
            }
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
                     [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IntPtr properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        int GetAudioSessionControl(ref Guid AudioSessionGuid, int StreamFlags, out IntPtr SessionControl);
        int GetSimpleAudioVolume(ref Guid AudioSessionGuid, int StreamFlags, out ISimpleAudioVolume AudioVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
        int RegisterSessionNotification(IntPtr SessionNotification);
        int UnregisterSessionNotification(IntPtr SessionNotification);
        int RegisterDuckNotification(string sessionID, IntPtr duckNotification);
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        int GetCount(out int SessionCount);
        int GetSession(int SessionCount, out IAudioSessionControl Session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        int GetProcessId(out uint retVal);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid eventContext);
        int GetMasterVolume(out float level);
        int SetMute(bool mute, ref Guid eventContext);
        int GetMute(out bool mute);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out int channelCount);
        int SetMasterVolumeLevel(float level, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float level);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute(int mute, ref Guid eventContext);
        int GetMute(out int mute);
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        int VolumeStepUp(ref Guid eventContext);
        int VolumeStepDown(ref Guid eventContext);
        int QueryHardwareSupport(out uint mask);
        int GetVolumeRange(out float min, out float max, out float increment);
    }
}
