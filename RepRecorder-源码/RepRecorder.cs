// RepRecorder —— 把非想天则 .rep 拖进来，自动用 OBS 依次录成 mp4
// 依赖：.NET Framework 4.x（Windows 自带）、OBS Studio（obs-websocket 5.x，需启用）
// 编译：csc.exe /nologo /target:winexe /codepage:65001 /out:RepRecorder.exe
//         /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll RepRecorder.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("shell32.dll")] static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);
        [StructLayout(LayoutKind.Sequential)]
        struct DROPFILES { public uint pFiles; public int x; public int y; public int fNC; public int fWide; }
        const uint WM_DROPFILES = 0x0233;

        // 把文件"拖"进某个窗口。
        // ⚠ 2026-09-26 实测：合成 WM_DROPFILES 对当前这版 ReplayDnD-old / ReplayDnD **完全无效**
        //   （投递后游戏一直停在标题画面，既不开始播也不退出），mod 多半走的是 OLE 拖放，只有真人鼠标拖才有用。
        //   所以并行录制已经改成"把 rep 当命令行参数重启实例"（见 RunParallel 第 6 步），这个函数仅留作参考。
        static void DropFileOnWindow(IntPtr hwnd, string file)
        {
            int size = Marshal.SizeOf(typeof(DROPFILES));
            IntPtr p = Marshal.AllocHGlobal(size + (file.Length + 2) * 2);
            try
            {
                DROPFILES df = new DROPFILES();
                df.pFiles = (uint)size;
                df.fWide = 1;
                Marshal.StructureToPtr(df, p, false);
                byte[] bytes = Encoding.Unicode.GetBytes(file + "\0\0");
                Marshal.Copy(bytes, 0, (IntPtr)((long)p + size), bytes.Length);
                SendMessage(hwnd, WM_DROPFILES, p, IntPtr.Zero);
            }
            finally { Marshal.FreeHGlobal(p); }
        }
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
        readonly List<string> mutedInputs = new List<string>();
        readonly Dictionary<string, bool> micOriginalMute = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> deskOriginalMute = new Dictionary<string, bool>();

        ListView list;
        TextBox logBox;
        Label hint;
        Label obsState;
        Panel hudTop;              // 顶部状态条
        Label hudLeftTitle;        // 左侧面板的小标题
        Label lblParHint;
        Button btnStart, btnStop, btnClear, btnRemove, btnCheck, btnPrepare, btnSet, btnOpenLog;
        Label lblSoundNote;
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

        public MainForm(string[] args)
        {
            cfg = Settings.Load();
            Text = "Rep → MP4 录制器（OBS）";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1180, 780);   // 左侧控制面板竖排要够高，不然会被裁
            MinimumSize = new Size(760, 520);
            BackColor = Color.FromArgb(9, 13, 26);          // 蓝黑主题：窗体底色
            ForeColor = Color.FromArgb(198, 216, 255);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            StartPosition = FormStartPosition.CenterScreen;

            // ---- 蓝黑 HUD 风格：左边控制面板 / 右边 rep 列表 / 下面终端 ----
            FlowLayoutPanel top = new FlowLayoutPanel();
            top.Dock = DockStyle.Left;            // 左边一整列：所有按钮和选项
            top.Width = 312;
            top.FlowDirection = FlowDirection.TopDown;
            top.WrapContents = false;
            top.AutoScroll = true;                // 窗口矮的时候可以滚
            top.Padding = new Padding(10, 8, 8, 8);

            FlowLayoutPanel row1 = new FlowLayoutPanel();
            row1.AutoSize = true;
            row1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row1.FlowDirection = FlowDirection.TopDown;
            row1.WrapContents = false;
            row1.Margin = new Padding(0, 0, 0, 6);

            btnStart = MakeButton("开始录制", 116, OnStart, Color.FromArgb(0, 120, 60));
            btnStop = MakeButton("停止", 72, OnStop, Color.FromArgb(160, 60, 60));
            btnClear = MakeButton("清空队列", 88, OnClear, Color.Empty);
            btnRemove = MakeButton("移除选中", 88, OnRemove, Color.Empty);
            btnRefresh = MakeButton("刷新", 60, OnRefreshQueue, Color.Empty);
            btnCheck = MakeButton("检查环境", 88, OnCheck, Color.Empty);
            btnPrepare = MakeButton("准备 OBS（自动建源+绑定窗口）", 208, OnPrepare, Color.Empty);
            btnSet = MakeButton("设置…", 72, OnSettings, Color.Empty);
            btnOpenLog = MakeButton("日志", 60, OnOpenLog, Color.Empty);
            btnSound = MakeButton("声音：开 ✓", 124, OnSoundToggle, Color.Empty);
            btnSound.FlatStyle = FlatStyle.Flat;
            lblSoundNote = new Label();
            lblSoundNote.AutoSize = true;
            lblSoundNote.ForeColor = Color.DimGray;
            lblSoundNote.Margin = new Padding(4, 9, 12, 0);
            lblSoundNote.Text = "（整个电脑静音，录出来的视频有声音）";
            btnGameAudio = MakeButton("录打击音效：开 ✓", 168, OnGameAudioToggle, Color.Empty);
            btnGameAudio.FlatStyle = FlatStyle.Flat;
            btnBgm = MakeButton("录BGM：关 ✕", 116, OnBgmToggle, Color.Empty);
            btnBgm.FlatStyle = FlatStyle.Flat;

            tip = new ToolTip();
            tip.SetToolTip(btnSound, "点一下切换【整个电脑静音 / 恢复声音】：静音时你自己（以及整个电脑）都听不到声音，\r\n" +
                                     "但录出来的视频仍然有完整的游戏原声（OBS 抓的是游戏进程的音频流，端点静音掐不断它）。录完会自动恢复。");
            tip.SetToolTip(lblSoundNote, "说明：这个按钮管的是【你自己听不听得到】——它静音的是整台电脑的默认输出设备；\r\n" +
                                         "录像里照样有游戏声音（录不录声音由右边的「录打击音效」决定）。");
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

            chkAuto = new CheckBox();
            chkAuto.Text = "拖入后自动开始";
            chkAuto.AutoSize = true;
            chkAuto.Margin = new Padding(6, 8, 12, 0);
            chkAuto.Checked = cfg.AutoStartOnDrop;
            chkAuto.CheckedChanged += delegate { cfg.AutoStartOnDrop = chkAuto.Checked; cfg.Save(); };

            chkPrepare = new CheckBox();
            chkPrepare.Text = "自动准备采集源并绑定游戏窗口";
            chkPrepare.AutoSize = true;
            chkPrepare.Margin = new Padding(6, 8, 12, 0);
            chkPrepare.Checked = cfg.AutoPrepareObs;
            chkPrepare.CheckedChanged += delegate { cfg.AutoPrepareObs = chkPrepare.Checked; cfg.Save(); };

            // 画布选项已移到「设置…」里（分类「画布」→ AutoCanvas），主界面不再重复放一个复选框
            chkCloseObs = new CheckBox();
            chkCloseObs.Text = "录完关闭 OBS";
            chkCloseObs.AutoSize = true;
            chkCloseObs.Margin = new Padding(6, 8, 12, 0);
            chkCloseObs.Checked = cfg.CloseObsAfter;
            chkCloseObs.CheckedChanged += delegate { cfg.CloseObsAfter = chkCloseObs.Checked; cfg.Save(); };

            // 轮录：点一次「开始录制」就把队列里所有待录的 rep 一批接一批全部录完（不用守着电脑）
            chkRound = new CheckBox();
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
            cbMic = new ComboBox();
            cbMic.DropDownStyle = ComboBoxStyle.DropDownList;
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
            };

            hint = new Label();
            hint.AutoSize = true;
            hint.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            hint.ForeColor = Color.FromArgb(30, 90, 170);
            hint.Text = "把 .rep 文件（可多选，也可以整个文件夹）拖到这里  ↓";
            hint.Margin = new Padding(6, 8, 16, 0);

            row1.Controls.Add(btnStart); row1.Controls.Add(btnStop); row1.Controls.Add(btnClear);
            row1.Controls.Add(btnRemove); row1.Controls.Add(btnRefresh); row1.Controls.Add(btnCheck);
            // 「准备 OBS」和「仅打开 OBS」放在一起，方便对照
            row1.Controls.Add(btnPrepare);
            row1.Controls.Add(btnSet); row1.Controls.Add(btnOpenLog); row1.Controls.Add(btnSound); row1.Controls.Add(lblSoundNote); row1.Controls.Add(btnGameAudio); row1.Controls.Add(btnBgm);

            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.AutoSize = true;
            row2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row2.FlowDirection = FlowDirection.TopDown;
            row2.WrapContents = false;
            row2.Margin = new Padding(0, 0, 0, 6);
            // 同时录制数量：1~10 的下拉列表（取代以前只能进设置里改的数字）
            Label lblPar = new Label();
            lblPar.Text = "同时录制数量：";
            lblPar.AutoSize = true;
            lblPar.Margin = new Padding(14, 10, 2, 0);
            // 这个数量很关键（决定并行几格 / 分几批），做成醒目的琥珀色再加粗，别让人找不到
            lblPar.ForeColor = Color.FromArgb(178, 92, 0);
            lblPar.Font = new Font(Font, FontStyle.Bold);
            cbParallel = new ComboBox();
            cbParallel.DropDownStyle = ComboBoxStyle.DropDownList;
            cbParallel.Width = 96;
            cbParallel.Margin = new Padding(0, 5, 0, 0);
            cbParallel.BackColor = Color.FromArgb(255, 236, 179);   // 淡琥珀底，比周围控件显眼
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
            };

            row2.Controls.Add(chkAuto); row2.Controls.Add(chkPrepare);
            row2.Controls.Add(chkCloseObs); row2.Controls.Add(chkRound);
            row2.Controls.Add(lblPar); row2.Controls.Add(cbParallel);
            // 选中几个 rep 就自动把数量改成几个（省得每次去点下拉框），并在旁边说明
            lblParHint = new Label();
            lblParHint.AutoSize = true;
            lblParHint.ForeColor = Color.FromArgb(178, 92, 0);
            lblParHint.Margin = new Padding(4, 10, 0, 0);
            row2.Controls.Add(lblParHint);
            row2.Controls.Add(lblMic); row2.Controls.Add(cbMic);
            // tooltip 要等控件建好之后再挂（早挂会 SetToolTip(null) 直接崩）
            tip.SetToolTip(cbParallel, "一次同时开几个游戏实例（1~6）。\r\n" +
                                       "1 = 相当于普通单录；3 个约 1.6 GB 内存；6 个约 3.3 GB。\r\n" +
                                       "上限锁在 6：OBS 只有 6 条音轨，再多就没法给每格单独放声音，会录出没声音的视频。");
            tip.SetToolTip(btnStart, "点一下就开始录：\r\n" +
                                     "· 列表里选中了 rep → 只录选中的那些（不管之前有没有录过，一律重录）\r\n" +
                                     "· 没选中 → 录队列里最靠前的待录 rep\r\n" +
                                     "· 要录多个 → 按右边的「同时录制数量」并行，超过就自动分批；只有 1 个就按普通单录走\r\n" +
                                     "（原来的「并行录制」「录制选中的」两个按钮已经并进这个按钮了）");

            FlowLayoutPanel row3 = new FlowLayoutPanel();
            row3.AutoSize = true;
            row3.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row3.FlowDirection = FlowDirection.TopDown;
            row3.WrapContents = false;
            row3.Margin = new Padding(0, 0, 0, 6);
            row3.Controls.Add(hint); row3.Controls.Add(obsState);

            top.Controls.Add(row1);
            top.Controls.Add(row2);
            top.Controls.Add(row3);

            // ---- 列表 + 日志 ----
            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.HideSelection = false;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.FromArgb(9, 13, 26);
            list.ForeColor = Color.FromArgb(198, 216, 255);
            list.Font = new Font("Microsoft YaHei UI", 9F);
            list.OwnerDraw = true;                 // 自己画：表头/行都跟着蓝黑风格
            list.DrawColumnHeader += OnHudDrawColumnHeader;
            list.DrawItem += OnHudDrawItem;
            list.DrawSubItem += OnHudDrawSubItem;
            list.Columns.Add("#", 44);
            list.Columns.Add("文件", 380);
            list.Columns.Add("状态", 90);
            list.Columns.Add("用时", 86);
            list.Columns.Add("大小", 90);
            list.Columns.Add("说明", 240);
            list.DoubleClick += OnListDoubleClick;

            logBox = new TextBox();
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.BackColor = Color.FromArgb(7, 10, 20);
            logBox.ForeColor = Color.FromArgb(150, 200, 255);
            logBox.Font = new Font("Consolas", 9F);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;   // 上面列表、下面终端
            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(logBox);
            split.BackColor = Color.FromArgb(38, 62, 110);
            split.Panel1.BackColor = Color.FromArgb(9, 13, 26);
            split.Panel2.BackColor = Color.FromArgb(7, 10, 20);

            status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("就绪");
            status.Items.Add(statusLabel);
            status.BackColor = Color.FromArgb(11, 16, 32);
            status.ForeColor = Color.FromArgb(150, 200, 255);
            statusLabel.ForeColor = Color.FromArgb(150, 200, 255);
            status.SizingGrip = false;

            // 顶部条：应用名 + 环境状态（像截图里那条状态栏）
            hudTop = new Panel();
            hudTop.Dock = DockStyle.Top;
            hudTop.Height = 30;
            hudTop.BackColor = Color.FromArgb(11, 16, 32);
            Label hudTitle = new Label();
            hudTitle.Text = "REP → MP4  录制器   (OBS)";
            hudTitle.AutoSize = true;
            hudTitle.ForeColor = Color.FromArgb(120, 190, 255);
            hudTitle.Font = new Font("Consolas", 10F, FontStyle.Bold);
            hudTitle.Location = new Point(12, 7);
            hudTop.Controls.Add(hudTitle);
            obsState.Location = new Point(280, 8);
            obsState.BackColor = Color.Transparent;
            hudTop.Controls.Add(obsState);
            hudTop.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen hudPen1 = new Pen(HudLine))
                    pe.Graphics.DrawLine(hudPen1, 0, hudTop.Height - 1, hudTop.Width, hudTop.Height - 1);
            };

            // 分区标题（截图里那种 | 名字 | 的框）
            hudLeftTitle = MakeHudSectionTitle("控制面板 / CONTROL", 0);
            top.Controls.Add(hudLeftTitle);
            top.Controls.SetChildIndex(hudLeftTitle, 0);

            Controls.Add(split);
            Controls.Add(top);
            Controls.Add(status);
            Controls.Add(hudTop);

            // 整体套上蓝黑主题 + 窗口标题栏也变深色
            ApplyHudTheme(this);
            EnableDarkTitleBar();
            // 主题之后再钉一次尺寸与强调色（FlowLayoutPanel 里下拉框容易被挤小/被主题改掉琥珀底）
            try
            {
                cbParallel.Width = 96;
                cbParallel.BackColor = Color.FromArgb(52, 40, 12);
                cbParallel.ForeColor = Color.FromArgb(255, 214, 120);
                cbMic.Width = 210;
                lblParHint.ForeColor = Color.FromArgb(255, 196, 90);
                if (hint != null) hint.ForeColor = Color.FromArgb(120, 190, 255);
            }
            catch { }

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
            // 鼠标滚轮滚过「同时录制数量」时不要改它 —— WinForms 默认会一格格改，
            // 用户实测：滚动列表时顺手把数量从 6 滚成了 1（日志里那一串 5→4→3→2→1）。
            cbParallel.MouseWheel += delegate(object s, MouseEventArgs me)
            {
                HandledMouseEventArgs h = me as HandledMouseEventArgs;
                if (h != null) h.Handled = true;
            };

            // 每 2 秒同步一次"系统声音是否静音"，让按钮颜色/文字始终反映真实状态
            soundTimer = new System.Windows.Forms.Timer();
            soundTimer.Interval = 2000;
            soundTimer.Tick += delegate { RefreshSoundButtons(); };
            soundTimer.Start();

            Shown += delegate
            {
                try { split.SplitterDistance = 380; } catch { }
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

        // ============================================================ 蓝黑 HUD 风格
        // 取自用户给的参考图：深蓝黑底 + 蓝色描边 + 终端里的等宽字。
        static readonly Color HudBg = Color.FromArgb(9, 13, 26);
        static readonly Color HudPanel = Color.FromArgb(14, 20, 38);
        static readonly Color HudLine = Color.FromArgb(38, 62, 110);
        static readonly Color HudText = Color.FromArgb(198, 216, 255);
        static readonly Color HudDim = Color.FromArgb(120, 158, 214);
        static readonly Color HudAccent = Color.FromArgb(90, 170, 255);
        static readonly Color HudGreen = Color.FromArgb(24, 120, 74);
        static readonly Color HudRed = Color.FromArgb(140, 40, 52);
        static readonly Color HudGrey = Color.FromArgb(40, 52, 80);

        // 分区小标题：像参考图里 | 控制面板 | 那样的一条
        Label MakeHudSectionTitle(string text, int width)
        {
            Label l = new Label();
            l.Text = "| " + text + " |";
            l.AutoSize = true;
            l.ForeColor = HudAccent;
            l.Font = new Font("Consolas", 9.5F, FontStyle.Bold);
            l.Margin = new Padding(0, 2, 0, 4);
            if (width > 0) l.Width = width;
            return l;
        }

        // 把"语义色"（绿=开 / 红=停 / 灰=关）换成蓝黑主题里的对应色
        static Color HudMapBack(Color c)
        {
            if (c == Color.FromArgb(0, 120, 60)) return HudGreen;
            if (c == Color.FromArgb(160, 60, 60)) return HudRed;
            if (c == Color.FromArgb(190, 60, 60)) return HudRed;
            if (c == Color.FromArgb(120, 120, 120)) return HudGrey;
            if (c == Color.FromArgb(180, 180, 180)) return Color.FromArgb(28, 36, 58);
            return HudPanel;
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
                        bool colored = b.BackColor != SystemColors.Control && b.BackColor != Color.Empty &&
                                       b.BackColor != HudPanel && b.BackColor != Color.Transparent;
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = HudLine;
                        b.FlatAppearance.BorderSize = 1;
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 46, 84);
                        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 32, 60);
                        if (colored) b.BackColor = HudMapBack(b.BackColor);
                        else b.BackColor = HudPanel;
                        if (b.ForeColor != Color.White && b.ForeColor != Color.DimGray) b.ForeColor = HudText;
                        b.Font = new Font("Microsoft YaHei UI", 9F);
                    }
                    CheckBox cb = c as CheckBox;
                    if (cb != null)
                    {
                        cb.ForeColor = HudText;
                        cb.BackColor = Color.Transparent;
                        cb.FlatStyle = FlatStyle.Flat;
                        cb.Font = new Font("Microsoft YaHei UI", 9F);
                    }
                    ComboBox cmb = c as ComboBox;
                    if (cmb != null)
                    {
                        cmb.FlatStyle = FlatStyle.Flat;
                        cmb.BackColor = Color.FromArgb(18, 26, 46);
                        cmb.ForeColor = HudText;
                        cmb.Font = new Font("Microsoft YaHei UI", 9F);
                    }
                    Label lb = c as Label;
                    if (lb != null)
                    {
                        if (lb.BackColor != Color.Transparent) lb.BackColor = Color.Transparent;
                        if (lb != hudLeftTitle && lb.ForeColor != HudAccent)
                        {
                            bool dim = lb.ForeColor == Color.DimGray;
                            lb.ForeColor = dim ? HudDim : HudText;
                        }
                    }
                    FlowLayoutPanel flp = c as FlowLayoutPanel;
                    if (flp != null) flp.BackColor = Color.Transparent;
                    Panel pn = c as Panel;
                    if (pn != null && pn != hudTop) pn.BackColor = HudBg;
                    ApplyHudTheme(c);
                }
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
            }
            catch { }
        }

        // 表头：深底 + 蓝字 + 一条下划线
        void OnHudDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(16, 24, 44)))
                e.Graphics.FillRectangle(bb, e.Bounds);
            using (Pen p = new Pen(HudLine))
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, e.Bounds, HudAccent,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        // 行背景：整行画（本批=绿色底；选中=亮蓝底）
        void OnHudDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            Color bg;
            if (e.Item.Selected) bg = Color.FromArgb(30, 62, 124);
            else if (e.Item.BackColor == Color.Empty || e.Item.BackColor == Color.White) bg = HudBg;
            else if (e.Item.BackColor == Color.FromArgb(214, 244, 214)) bg = Color.FromArgb(20, 68, 48);   // 本批绿
            else bg = HudBg;
            using (SolidBrush bb = new SolidBrush(bg)) e.Graphics.FillRectangle(bb, e.Bounds);
            if (e.Item.Index % 2 == 1 && !e.Item.Selected && bg == HudBg)
            {
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(12, 18, 34)))
                    e.Graphics.FillRectangle(bb, e.Bounds);
            }
        }

        // 单元格文字：行自己的前景色（状态色）优先
        void OnHudDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            Color fg = e.Item.ForeColor;
            if (fg == Color.Black || fg == Color.Empty) fg = HudText;
            else if (fg == Color.FromArgb(0, 120, 0)) fg = Color.FromArgb(110, 230, 160);
            else if (fg == Color.FromArgb(180, 0, 0)) fg = Color.FromArgb(255, 130, 130);
            else if (fg == Color.FromArgb(190, 120, 0)) fg = Color.FromArgb(255, 200, 110);
            Rectangle r = e.Bounds;
            if (e.ColumnIndex == 0) r = new Rectangle(r.Left + 6, r.Top, r.Width - 6, r.Height);
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
            hint.Text = "把 .rep 文件（可多选，也可以整个文件夹）拖到这里  ↓";
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
            });
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
                    foreach (KeyValuePair<string, bool> kv in micOriginalMute)
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
                    if (micOriginalMute.Count > 0) Log.Write("已把麦克风恢复成录制前的状态");
                    micOriginalMute.Clear();
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
                    foreach (KeyValuePair<string, bool> kv in deskOriginalMute)
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
                    if (deskOriginalMute.Count > 0) Log.Write("已恢复 OBS 桌面音频");
                    deskOriginalMute.Clear();
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
                                if (fb.ShowDialog(dlg) == DialogResult.OK)
                                {
                                    cfg.LastSaveFolder = fb.SelectedPath;
                                    cfg.Save();
                                    if (chkAll.Checked) queueSaveFolder = fb.SelectedPath;
                                    result = fb.SelectedPath;
                                }
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
                list.EnsureVisible(job.Item.Index);
                // 某一条录完了 → 高亮自动挪到下一批（用户不用再选）
                if (status == "完成" || status == "失败" || status == "注意" || status == "已录制")
                    HighlightBatchRows();
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
                obsState.ForeColor = (obsExe == null || game == null) ? Color.Firebrick : Color.SeaGreen;
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
                        hint.ForeColor = Color.FromArgb(120, 158, 214);
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
                                if (fb.ShowDialog(dlg) == DialogResult.OK)
                                {
                                    queueSaveFolder = fb.SelectedPath;
                                    cfg.LastSaveFolder = fb.SelectedPath;
                                }
                                else dr = DialogResult.OK;   // 没挑文件夹 → 当作"和 rep 放在一起"
                            }
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
            list.Items.Clear();
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
            lock (jobsLock)
            {
                foreach (Job j in del) { jobs.Remove(j); list.Items.Remove(j.Item); }
                for (int i = 0; i < list.Items.Count; i++) list.Items[i].SubItems[0].Text = (i + 1).ToString();
            }
            foreach (Job dj in del) pinnedBatch.Remove(dj);
            HighlightBatchRows();   // 移除后本批高亮要跟着挪
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
                btnSound.Text = "声音：未知";
                btnSound.Width = 124;
                btnSound.BackColor = Color.Gainsboro;
                btnSound.ForeColor = Color.Black;
                return;
            }
            if (muted)
            {
                btnSound.Text = "声音：静音 ✕（录完自动恢复）";
                btnSound.Width = 236;
                btnSound.BackColor = Color.FromArgb(190, 60, 60);
                btnSound.ForeColor = Color.White;
            }
            else
            {
                btnSound.Text = "声音：开 ✓";
                btnSound.Width = 124;
                btnSound.BackColor = Color.FromArgb(0, 120, 60);
                btnSound.ForeColor = Color.White;
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
                btnGameAudio.Text = "录打击音效：开 ✓";
                btnGameAudio.BackColor = Color.FromArgb(0, 120, 60);
                btnGameAudio.ForeColor = Color.White;
            }
            else
            {
                btnGameAudio.Text = "录打击音效：关 ✕";
                btnGameAudio.BackColor = Color.FromArgb(120, 120, 120);
                btnGameAudio.ForeColor = Color.White;
            }
            // 「录打击音效」关掉 = 录出来完全无声 → 顺手把「录BGM」锁成关（连游戏里也不放 BGM）
            if (btnBgm != null)
            {
                btnBgm.Enabled = cfg.RecordGameAudio;
                if (!cfg.RecordGameAudio)
                {
                    btnBgm.Text = "录BGM：关 ✕（已锁）";
                    btnBgm.BackColor = Color.FromArgb(180, 180, 180);
                    btnBgm.ForeColor = Color.DimGray;
                }
                else RefreshBgmButton();
            }
        }

        // 提醒：ReplayDnD.ini 里 MuteMusic=1 时，游戏播放 rep 根本不放 BGM，
        // 这时「录打击音效：开」也只能录到音效 —— 想连 BGM 一起录必须点「录BGM」把它改成 0。
        void WarnIfBgmMuted()
        {
            try
            {
                if (string.IsNullOrEmpty(cfg.GameDir)) return;
                foreach (string sub in new string[] { @"modules\ReplayDnD\ReplayDnD.ini", @"modules\ReplayDnD-old\ReplayDnD.ini" })
                {
                    string p = Path.Combine(cfg.GameDir, sub);
                    if (!File.Exists(p)) continue;
                    string t = File.ReadAllText(p);
                    if (System.Text.RegularExpressions.Regex.IsMatch(t, @"(?im)^\s*MuteMusic\s*=\s*1\s*$"))
                        Log.Write("提醒：" + sub + " 里 MuteMusic=1 —— 游戏播放 rep 时不放背景音乐，" +
                                  "所以录像里只有音效、没有 BGM。想连 BGM 一起录就点界面上的「录BGM」按钮。");
                }
            }
            catch { }
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
                btnBgm.BackColor = Color.FromArgb(0, 120, 60);
            }
            else
            {
                btnBgm.Text = "录BGM：关 ✕";
                btnBgm.BackColor = Color.FromArgb(120, 120, 120);
            }
            btnBgm.ForeColor = Color.White;
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

        // 旧方式（按进程静音，会让录像也没声音），只留给命令行诊断用
        void SetGameAudioMute(bool mute)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("th123");
                if (ps.Length == 0)
                {
                    Log.Write("游戏没在运行，无法" + (mute ? "静音" : "打开") + "游戏声音");
                    return;
                }
                foreach (Process p in ps)
                {
                    string msg;
                    bool ok = GameAudio.SetProcessMute(p.Id, mute, out msg);
                    Log.Write("游戏声音（PID " + p.Id + "）：" + msg + (ok ? "" : "（失败）"));
                }
                if (mute) Log.Write("已静音游戏声音：你自己听不到，但录像里仍会录到游戏原声");
                else Log.Write("已恢复游戏声音");
            }
            catch (Exception ex) { Log.Write("切换游戏声音失败：" + ex.Message); }
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

        // 只录界面上选中的那些 rep（逻辑已并进「开始录制」按钮，这里保留给将来的命令行/脚本用）
        void OnRecordSelected(object sender, EventArgs e)
        {
            OnStart(sender, e);
        }

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

        string BuildWindowStringForProcess(Process p)
        {
            string title = null, cls = null;
            int pid = 0;
            try { pid = p.Id; } catch { return null; }
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint wp;
                GetWindowThreadProcessId(h, out wp);
                if (wp == (uint)pid && IsWindowVisible(h))
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
            if (title == null) return null;
            return MakeWindowString(title, cls);
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
            // 降级成单录时，也要把采集源准备好并切到录制场景（单录流程自己不做这些）
            if (batch.Count == 1)
            {
                Log.Write("这一批只有 1 个，改成普通单录");
                if (cfg.AutoPrepareObs) PrepareObs(false);
                if (cfg.AutoPrepareObs && cfg.UseDedicatedScene) SwitchScene(true);
                CaptureOriginalRecordDir();
                ProcessJob(batch[0]);
                return;
            }
            if (!cfg.AutoPrepareObs) { Log.Write("并行录制需要打开「自动准备采集源并绑定游戏窗口」，已中止"); return; }

            // 队列里的「用时」要靠 Started/Ended 相减：并行这条路径以前从没设过 Started，
            // 结果列表里显示成 (现在 - 0001年) 那种天文数字（63926114748s）
            DateTime batchStart = DateTime.Now;
            for (int i = 0; i < batch.Count; i++) batch[i].Started = batchStart;

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

            try
            {
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                Log.Write("程序崩溃：" + ex.ToString());
                MessageBox.Show(ex.Message, "RepRecorder 出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        // 收集所有"麦克风类"目标：可枚举的输入 + 全局麦克风设备
        public static List<string> FindMicTargets(ObsClient obs)
        {
            List<string> names = new List<string>();
            try
            {
                foreach (object o in ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]))
                {
                    Dictionary<string, object> m = (Dictionary<string, object>)o;
                    string kind = Convert.ToString(m["unversionedInputKind"]);
                    if (kind != "wasapi_input_capture" && kind != "coreaudio_input_capture") continue;
                    string n = Convert.ToString(m["inputName"]);
                    if (!names.Contains(n)) names.Add(n);
                }
            }
            catch { }
            foreach (string n in FindGlobalAudioNames(true))
                if (!names.Contains(n)) names.Add(n);
            return names;
        }

        // 收集所有"桌面音频"目标：可枚举的输出采集输入 + 全局桌面音频设备
        public static List<string> FindDesktopTargets(ObsClient obs)
        {
            List<string> names = new List<string>();
            try
            {
                foreach (object o in ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]))
                {
                    Dictionary<string, object> m = (Dictionary<string, object>)o;
                    string kind = Convert.ToString(m["unversionedInputKind"]);
                    if (kind != "wasapi_output_capture" && kind != "coreaudio_output_capture") continue;
                    string n = Convert.ToString(m["inputName"]);
                    if (!names.Contains(n)) names.Add(n);
                }
            }
            catch { }
            List<string> mics = FindGlobalAudioNames(true);
            foreach (string n in FindGlobalAudioNames(false))
                if (!mics.Contains(n) && !names.Contains(n)) names.Add(n);
            return names;
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
