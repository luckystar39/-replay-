// RepRecorder —— 把非想天则 .rep 拖进来，自动用 OBS 依次录成 mp4
// 依赖：.NET Framework 4.x（Windows 自带）、OBS Studio（obs-websocket 5.x，需启用）
// 编译：见 工具\编译.bat（系统自带 csc.exe + WPF 参考程序集，整份源文件列表都在那里）。
//
// 这个文件是【与界面无关的核心】：设置、日志、环境探测、任务模型、OBS WebSocket 客户端、
// 命令行入口、系统音频。界面在 Ui*.cs（WPF）、录制逻辑在 Recorder.cs。
//
// ⚠ 2026-10-12 之前这里还装着整个旧 WinForms 界面（MainForm + 十几个自绘控件类，约 7600 行），
//   那一版作为独立快照留在 ..\..\RepRecorder-旧版WinForms\ 里（含源码与可运行的 exe）。
//   所以这里不再有任何 System.Windows.Forms 依赖 —— 别再引回来。
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace RepRecorder
{
    // ============================================================ 版本
    // 2026-10-10：这个软件分发出去之后，用户报 bug 说不清自己用的是哪一版，也确认不了
    // 他手上是哪份 —— 所以从这一版开始叫 1.0。
    // ⚠ 别再手改版本号：双击 RepRecorder-WPF\发版.bat，它会把这里和 AssemblyInfo.cs
    //   两处一起改，然后编译、核对、打两个包、生成 version.json。
    //   （手改的话这两处都得改；另有两份「说明」里也印着版本号供人工核对。）
    //   ⚠ 编译.bat 里【没有】/version: 开关 —— 那两句版本号属性全在 AssemblyInfo.cs 里。
    public static class AppInfo
    {
        public const string Version = "1.1.3";
    }

    // ============================================================ 设置
    public class Settings
    {
        [Category("路径"), Description("obs64.exe 的完整路径。留空自动搜索。")]
        public string ObsExe { get; set; }
        [Category("路径"), Description("th123 文件夹（里面有 th123.exe）。留空自动搜索。")]
        public string GameDir { get; set; }
        [Category("路径"), Description("ffmpeg.exe 的完整路径。留空自动搜索。\r\n" +
            "只有并行录制（同时录 2 个以上）和切分才用它，单个录制不需要。")]
        public string FfmpegExe { get; set; }
        [Category("更新"), Description("上次检查更新的时间（ISO-8601 UTC，内部用）。")]
        public string LastUpdateCheckUtc { get; set; }
        [Category("更新"), Description("用户点过「以后再说」的版本号 —— 不再为它弹提示（内部用）。")]
        public string SkippedUpdateVersion { get; set; }
        [Category("OBS 连接"), Description("obs-websocket 端口，默认 4455。")]
        public int Port { get; set; }
        [Category("OBS 连接"), Description("obs-websocket 密码。留空则自动从 OBS 配置文件读取。")]
        public string Password { get; set; }
        [Category("并行录制"), Description("一次同时开几个游戏实例来录（1~6，默认 3）。1 = 相当于普通单录。\r\n" +
            "上限锁在 6 是因为 OBS 最多只有 6 条音轨 —— 超过 6 格每格的声音就没法分开，会录成没声音的视频。\r\n" +
            "内存参考：每实例约 550 MB，6 个约 3.3 GB。")]
        public int ParallelCount { get; set; }
        [Category("并行录制"), Description("并行录制时把游戏窗口设成多大（三档，就是游戏里的 Alt+大键盘1/2/3）：\r\n" +
            "1 = 640×480：最省资源，但成品要放大 2.25 倍，会明显发糊；\r\n" +
            "2 = 1280×960：默认。成品只放大 1.125 倍，几乎原生，画质和资源最平衡；\r\n" +
            "3 = 1920×1440：完全不放大（切分时 1:1 裁），但画布大 25%、6 个游戏要渲染 2.25 倍像素，\r\n" +
            "弱显卡可能掉帧、中间文件也大 25%。\r\n" +
            "只对【并行录制】生效（单录那条路不改窗口，大小由游戏自己决定）；只改录制时那一次窗口，**不动**用户磁盘上的游戏配置；屏幕装不下所选档时会自动退回能装下的档。")]
        public int GameScale { get; set; }
        [Category("并行录制"), Description("切分并行视频时，同时跑几个 ffmpeg（默认 3）。\r\n" +
            "并行录完是一整张合并视频，要按格裁成 N 个文件，这一步是重编码，最花时间。\r\n" +
            "同时跑多个能快好几倍（切 6 格从 3 分钟降到 1 分钟左右）；\r\n" +
            "但 N 卡的 NVENC 同时编码路数有限（家用卡一般 3~8 路），设太大反而会回退到很慢的软件编码。")]
        public int SplitParallel { get; set; }
        [Category("并行录制"), Description("切分并行视频用哪张显卡/CPU 出成品：\r\n" +
            "h264_nvenc = NVIDIA 显卡（默认，快）、h264_amf = AMD 显卡（快）、libx264 = CPU 软编（兼容，慢）。\r\n" +
            "选错了也不会失败：用不了就自动改用 CPU 软编（慢），日志里会写明实际走的是哪一档。")]
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
        // 【有效】录音模式：并行（同时录 ≥2 个）时强制 gameOnly（2026-10-08 用户要求）。
        // 并行的意思是几个游戏同时跑，麦克风会被同时采进好几条音轨 —— 纯粹是噪音。
        // 界面上那个下拉会锁成「只录游戏声音」（见 ShellWindow.ApplyMicLock），
        // 这里保证【录制路径】也照同一条规矩走：光锁界面不够，因为 MicMode 是存在文件里的，
        // 用户把数量从 1 改成 3 时它还是旧值。
        // ⚠ 只读，不动 MicMode 本身 —— 数量改回 1 之后，他原来选的「游戏+麦克风」要能回来。
        // ⚠ [ScriptIgnore] 不能少：JavaScriptSerializer 会把**所有公开属性**都写进 JSON，
        //   包括只有 get 的。不挡的话每次存设置都会多出一行 EffectiveMicMode ——
        //   那是个算出来的值，写进文件里既没用又会误导（改它也不生效）。
        [ScriptIgnore]
        public string EffectiveMicMode
        {
            get { return (ParallelCount >= 2) ? "gameOnly" : MicMode; }
        }
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
        [Category("录音"), Description("音频比画面【提前】多少毫秒（默认 80）。正数 = 把声音往前挪。\r\n" +
            "为什么默认 80：2026-10-11 用户报「切分出来的音频和游戏原声对不上」。\r\n" +
            "实测（画面运动量 vs 声音能量 做互相关，三个成品一致）：音频比画面慢 77~87 毫秒。\r\n" +
            "排查结论：ffmpeg 切分不引入偏移（-ss 在 -i 前是输入级 seek，音视频同一刀），\r\n" +
            "并行路径也不是（单文件模式录的同一批 rep 一样慢），源头在采集那一步 ——\r\n" +
            "OBS 那边默认的音频同步偏移就是 0，而游戏音频经 wasapi_process_output_capture\r\n" +
            "（应用程序音频采集）进来时会带上游戏自己的输出缓冲延迟。\r\n" +
            "⚠ 这一项补偿的是「录进去的声音比画面慢」，代价是成品不再完全等于玩家当时听到的。\r\n" +
            "⚠ 这个延迟和机器的音频设备缓冲有关，别人机器上未必也是 80，所以做成可调。\r\n" +
            "设成 0 = 不补偿；负数 = 反过来把声音往后推。")]
        public int AudioLeadMs { get; set; }
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
        [Category("界面"), Description("背景图的摆放方式（对齐方式）：\r\nfill=等比填满并裁掉多余（对应 Wallpaper Engine 的「覆盖」）；\r\nfit=等比完整显示，可能留边（「适应」）；stretch=拉伸铺满，会变形；\r\ntile=原图平铺；center=原始大小居中。\r\n【注意】早先还有个 free=自由（两轴不限、可拖出空白），2026-10-07 合并进 fill 后删除，\r\n读到 free 会被自动改成 fill（见 Load 里的 SettingsVersion<10 迁移）。")]
        public string BgLayout { get; set; }
        [Category("界面"), Description("背景图的模糊程度（0~60）。模糊一点能让上面的文字更好读。默认 0。")]
        public int BgBlur { get; set; }
        [Category("界面"), Description("背景图的水平偏移（按窗口宽度的百分比算）。0 = 居中，负=往左，正=往右。\r\n【上下限不是固定的】跟着摆放方式走：fill 只给被裁掉的那点余量（滑到头正好贴边、不露底）；\r\nfit/center 只给留边的那点余量；只有 tile 不限。")]
        public int BgOffsetX { get; set; }
        [Category("界面"), Description("背景图的垂直偏移（按窗口高度的百分比算）。0 = 居中，负=往上，正=往下。上下限同 BgOffsetX。")]
        public int BgOffsetY { get; set; }
        [Category("界面"), Description("背景图是否左右镜像（水平翻转）。对应 Wallpaper Engine 属性里的「翻转」。")]
        public bool BgFlipH { get; set; }
        [Category("界面"), Description("背景图是否上下镜像（垂直翻转）。Wallpaper Engine 没有独立开关，这里是补全的。")]
        public bool BgFlipV { get; set; }
        [Category("界面"), Description("背景图的缩放百分比（10~400，100 = 原始比例不缩放）。\r\n和翻转、位移一样，是独立于摆放方式的一层变换。")]
        public int BgZoom { get; set; }
        [Category("界面"), Description("背景图的范围：content=只铺右侧内容区（队列+终端，默认）；global=全局铺满整个窗口（控制面板也透出背景图）。")]
        public string BgScope { get; set; }
        [Category("界面"), Description("WPF 界面的面板底色强度（0~100）。0 = 面板完全透明、背景图零遮挡；\r\n越大越暗、压在背景图上的文字越好读。默认 25（图片仍一眼看清，文字有保障）。")]
        public int PanelScrim { get; set; }
        [Category("界面"), Description("【已停用】整窗不透明度。窗口现在固定 100% 不透明（让文字永远实心）；\r\n想调背景的浓淡请用「背景…」对话框里的「图片不透明度」。")]
        public int WindowOpacity { get; set; }
        [Browsable(false)]
        public int SettingsVersion { get; set; }   // 老设置文件里没有这一项（=0），用它判断要不要补默认值
        [Browsable(false)]
        // 2026-10-08：首次启动的环境自检弹过没有。老设置文件里没有这一项 → 反序列化出来 false
        // → 新用户（和老用户的新机器）第一次启动会弹一次，之后就再也不打扰。
        // 只用来记「弹过了」，不代表"当时没问题" —— 用户点「检查环境」永远会弹。
        public bool EnvChecked { get; set; }

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
            AudioLeadMs = 80;         // 把声音往前挪 80ms：实测成品里音频比画面慢 77~87ms（详见属性上的说明）
            KeepGameCapturable = true;
            ParallelCount = 3;
            GameScale = 2;             // 游戏窗口 1280×960：成品只放大 1.125 倍，画质/资源最平衡
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
            BgFlipH = false;           // 左右镜像
            BgFlipV = false;           // 上下镜像
            BgZoom = 100;              // 100 = 不缩放
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
            if (s.SettingsVersion < 9)
            {
                // 老设置文件里没有"翻转/缩放" → 两边都不镜像、不缩放（观感和以前完全一致）
                if (s.BgZoom <= 0) s.BgZoom = 100;
                s.SettingsVersion = 9;
            }
            if (s.SettingsVersion < 10)
            {
                // 「自由」摆放方式已合并进「覆盖」并删除（用户 2026-10-07 要求：
                // 两者渲染本来就一样，只差在"没余量的那根轴能不能动"，而"覆盖"的定义
                // 就是永不露底）。free → fill 的归一放在下面【版本号之外】，见那里的说明。
                s.SettingsVersion = 10;
            }
            if (s.SettingsVersion < 11)
            {
                // 老设置文件里没有"音频提前"这一项 → 补上默认 80ms（实测音频比画面慢 77~87ms）。
                // 只在升级时补一次：之后用户主动填 0（不补偿）会被正常保留。
                if (s.AudioLeadMs == 0) s.AudioLeadMs = 80;
                s.SettingsVersion = 11;
            }
            // 对齐方式的合法值就这 5 个。归一成 fill 的这一步**故意不放进版本号判断**：
            // ComputePanRange 和 LayoutIndex 现在都假设"至少是这 5 个之一"（遇到 free
            // 会静默走默认分支），而版本号守不住这个不变量 —— 用户手改设置文件、
            // 或者从旧备份还原，都能绕过去，然后下拉显示「覆盖」、cfg 里却还是 "free"。
            // 这行是幂等的，每次 Load 跑一遍不花什么。
            if (s.BgLayout != "fill" && s.BgLayout != "fit" && s.BgLayout != "stretch"
                && s.BgLayout != "center" && s.BgLayout != "tile")
                s.BgLayout = "fill";
            // 2026-10-08：界面那个「录音模式」下拉写的是 "both"，而录制路径认的是 "gameAndMic"
            // （ApplyMicMode 里就是 `!= "gameAndMic"`）—— 于是选了「游戏 + 麦克风」，
            // 录制时麦克风照样被静音，这个选项等于从来没生效过（实测确认：set:cbMic=1 之后
            // 设置文件里是 "both"，而 wantMuted 算出 True）。
            // 写的那边已经改成 "gameAndMic"，这里把老文件里的 "both" 归一化掉 ——
            // 也是无条件跑的：改过设置文件、从旧备份还原都能绕开版本号，这条幂等、很便宜。
            if (s.MicMode == "both") s.MicMode = "gameAndMic";
            // 2026-10-09：切分并发数封顶从 6 收到 3（用户拍板）。
            // 理由：天花板本来就在"格子数"上（见 Recorder.RunParallelBatch 里 `conc > batch.Count`），
            //   4/5/6 这三档只有"一次真录 4~6 个"的人用得上；而切分链路是
            //   crop → lanczos 缩放到 1440×1080 → fade（**这几步跑在 CPU 上**）→ NVENC，
            //   开到 3 以上主要是在抢 CPU 和抢功耗，收益小而风险大 ——
            //   老驱动 + GeForce 的并发编码路数一旦被顶穿，ffmpeg 起不来 nvenc 会**掉到
            //   libx264 纯软编**，慢好几倍，用户只知道"数字调大了怎么反而更慢"。
            //   用户原话：他的 4060 机器就已经吃得很高，得按普通用户的 CPU 来。
            // ⚠ 这条**必须**是无条件的：界面下拉一旦只列 1~3，而设置文件里还存着 4/5/6，
            //   ChoiceRow 会静默回落到第一项（UiDialogs.cs `if (idx < 0) idx = 0;`），
            //   再被「确定」把所有字段写回去 —— 老用户会莫名其妙从 6 变成 1（最慢）。
            //   放在版本号判断之外，手改设置文件、从旧备份还原都绕不过去（同上面两条）。
            if (s.SplitParallel > 3) s.SplitParallel = 3;
            // 「游戏画面大小」同一个道理，而且这里是**两件事**：
            //   ① 老设置文件里没有这个字段（=0）→ 给默认 2；② 手改过 4/5/6 → 夹到 3。
            //   ⚠ 必须无条件夹：界面下拉只列 1/2/3，而 ChoiceRow 遇到对不上的值会静默回落到第一项
            //     （UiDialogs.cs `if (idx < 0) idx = 0;`），再被「确定」写回去 —— 那样用户会
            //     莫名其妙变成"最糊"的 Alt+1。和上面 SplitParallel 那条是同一个坑。
            if (s.GameScale <= 0) s.GameScale = 2;
            if (s.GameScale > 3) s.GameScale = 3;
            // ⚠ 必须在 FillDefaults 之前：FillDefaults 里会调 FindFfmpeg，而它是 static、
            //   只能读这份镜像。顺序反了就等于"设置里的 ffmpeg 路径第一次启动时不生效"。
            FfmpegOverride = s.FfmpegExe == null ? "" : s.FfmpegExe;
            s.FillDefaults();
            return s;
        }

        public void Save()
        {
            FfmpegOverride = FfmpegExe == null ? "" : FfmpegExe;   // 镜像给 static 的 FindFfmpeg
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
            // ffmpeg：2026-10-10 起也存进设置。以前它没有字段，每次调用都现搜 ——
            // 用户自己下了一份放在别处就永远找不到，而且是三个依赖里唯一不能手动指定的。
            if (string.IsNullOrEmpty(FfmpegExe) || !File.Exists(FfmpegExe))
            {
                string found = FindFfmpeg();
                if (found != null) FfmpegExe = found;
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

        // ---------------------------------------------------------- 找游戏目录
        //
        // 分层回退（2026-10-09 重做）：
        //   cfg      —— 设置里已经存好了，什么都不用找（FillDefaults 判断，不在这里）
        //   near     —— 程序旁边：本目录 / 上一级 / 这两处的一级子目录
        //   launcher —— SokuLauncher.json 的 SokuDirPath（整合包自己声明游戏在哪）
        //   scan     —— 各固定盘走一遍（后台线程，深度 MaxScanDepth）
        //
        // 为什么要有后两层：以前的 FindGameDir 只在"程序附近两圈"里找，前提是录制器和
        // 游戏在同一个整合包里。新用户从 RepRecorder-发给别人.zip 拿到录制器、游戏放在
        // 别处（或者干脆把整合包文件夹改了名/挪了盘）时，它永远找不到，只能自己进设置里
        // 指 —— 而环境卡又只说"未找到 th123.exe"，新用户根本不知道该指哪。
        //
        // 为什么全盘扫描要放后台：深度 8 在 NVMe 上 2~3 秒，机械硬盘上十几秒。同步跑在
        // UI 线程上，Windows 会在这段时间把标题栏画成"未响应"。所以 FindGameDir 自己
        // 永远不阻塞：便宜的层当场给答案，贵的丢给后台线程，结果由界面每 2 秒取一次
        // （两个界面各有一个 2 秒声音定时器，见 UiWindow.cs / Core.cs 的 soundTimer）。
        // 走那条路，回填必然发生在 UI 线程上，不需要任何跨线程碰控件。
        static readonly object scanGate = new object();   // 只保护下面这两个"哪一层命中的"标记
        static string lastVia = "";       // 最近一次解析是哪一层命中的（调试接口报给脚本看）
        static string fmLastVia = "";

        // 两个「只在便宜的办法都不管用时，才在后台跑一次的查找」：游戏目录、ffmpeg。
        // 螺纹（锁、后台线程、只跑一轮、异常兜底）只写一份 —— 抄两份迟早跑偏，
        // 而且这里任何一处漏清状态，界面那个"等扫描落定再弹"的自检窗就永远不弹。
        static readonly LazyScan gameScan = new LazyScan("游戏");
        static readonly LazyScan fmScan = new LazyScan("ffmpeg");

        const int MaxScanDepth = 8;
        const int ScanBudgetMs = 60000;   // 机械硬盘上别空转

        // 全盘扫描这条路走的目录名黑名单：系统目录 + 一票缓存目录。
        // 和 FindObsExe 不同，那里只列盘根一层，漏了就漏了；这里要往下钻，踩进
        // C:\Windows 或者 node_modules 就是几十秒白费。
        static readonly string[] ScanSkipDirs = new string[] {
            "windows", "$recycle.bin", "system volume information", "program files",
            "program files (x86)", "programdata", "appdata", "windowsapps", "recovery",
            "perflogs", "$winreagent", "$windows.~bt", "$windows.~ws", "$sysreset",
            "node_modules", "onedrivetemp", ".git", ".svn", ".hg", "__pycache__",
            "venv", ".venv", "site-packages", ".gradle", ".nuget", "obj"
        };

        // 名字像游戏的先走 —— 命中的话后面那些深目录一个都不用碰，多数情况毫秒级返回。
        static readonly string[] ScanHints = new string[] {
            "th123", "soku", "非想天则", "东方", "touhou", "整合包", "th105", "游戏", "game"
        };

        // "这确实是游戏目录"的旁证。全盘乱撞的风险比"用户亲手指的目录"大得多，所以扫描这条
        // 路上要求 th123.exe 旁边至少还有这些之一；near/launcher 两层不要求，维持老标准，
        // 免得把老用户本来能用的配置判成无效。
        static readonly string[] GameMarkers = new string[] {
            "th123a.dat", "th123b.dat", "th123c.dat", "configex123.ini",
            "SWRSToys.ini", "ModLoaderSettings.json", "score123.dat"
        };

        // 「只在需要时跑一次」的后台查找。
        // 三条性质都是踩出来的，别改：
        //   · 对调用方**永不阻塞** —— 还在扫就先返回 null，结果由界面每 2 秒的定时器取走
        //     （那条路径本来就在 UI 线程上，回填时一行跨线程代码都不用写）。
        //   · 收尾必须在 finally 里 —— 首次自检窗要等 Running 变 false 才肯弹。
        //   · 异常绝不能逃出线程 —— .NET 4 里后台线程的未处理异常会直接结束整个进程。
        class LazyScan
        {
            readonly object gate = new object();
            readonly string what;
            string found; bool running, done; long ms;

            public LazyScan(string what) { this.what = what; }

            public bool Running { get { lock (gate) return running; } }
            public long Ms { get { lock (gate) return ms; } }
            public string Cached { get { lock (gate) return found; } }

            public void EnsureStarted(Func<string> work)
            {
                lock (gate) { if (running || done) return; running = true; }
                Thread t = new Thread(delegate ()
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    string r = null;
                    try { r = work(); }
                    catch (Exception ex) { Log.Write("自动找" + what + "出错：" + ex.Message); }
                    finally
                    {
                        sw.Stop();
                        lock (gate) { found = r; ms = sw.ElapsedMilliseconds; running = false; done = true; }
                    }
                    try
                    {
                        Log.Write(r == null
                            ? "自动找" + what + "：扫了一遍，没找到（" + sw.ElapsedMilliseconds + " ms）"
                            : "自动找" + what + "：找到 " + r + "（" + sw.ElapsedMilliseconds + " ms）");
                    }
                    catch { }
                });
                t.IsBackground = true;      // 关程序时别挂着不走
                t.Name = "找" + what;
                t.Start();
            }

            // 「检查环境」用：清掉"已经扫过"的标记，让它能重新扫一次。
            // 正在扫就什么都不做（不然会起第二个线程，两份结果互相盖）。
            public void Reset()
            {
                lock (gate) { if (running) return; found = null; done = false; ms = 0; }
            }
        }

        public static bool GameScanRunning { get { return gameScan.Running; } }
        // 首次自检那个窗要等【两个】自动查找都落定才弹 —— 只等游戏的话，
        // ffmpeg 会原地重演同一个矛盾（卡片是快照，弹完就不会再变）。
        public static bool AnyScanRunning { get { return gameScan.Running || fmScan.Running; } }
        public static long GameScanMs { get { return gameScan.Ms; } }
        public static bool FfmpegScanRunning { get { return fmScan.Running; } }
        public static long FfmpegScanMs { get { return fmScan.Ms; } }
        public static string LastGameVia { get { lock (scanGate) { return lastVia; } } }
        public static string LastFfmpegVia { get { lock (scanGate) { return fmLastVia; } } }

        public static string FindGameDir()
        {
            string via;
            return ResolveGameDir(out via);
        }

        // 不阻塞：便宜的层当场返回，贵的（scan）只读后台线程的缓存。
        public static string ResolveGameDir(out string via)
        {
            via = "";

            string near = FindGameDirNearby();
            if (near != null) { lock (scanGate) lastVia = via = "near"; return near; }

            string lc = FindGameViaSokuLauncher();
            if (lc != null) { lock (scanGate) lastVia = via = "launcher"; return lc; }

            gameScan.EnsureStarted(ScanDrivesForGame);
            string scanned = gameScan.Cached;
            if (scanned != null) { lock (scanGate) lastVia = via = "scan"; return scanned; }
            return null;   // 还在扫，先报没有 —— 界面过两秒自己会取到
        }

        // near 和 launcher 两层共用的一组"附近"目录：程序目录、上一级、上两级。
        // 上两级是给 launcher 用的：整合包常常是 D:\整合包\RepRecorder-WPF\ 这种排法，
        // SokuLauncher.json 在 D:\整合包\【完整包】…\ 里，离程序两跳。
        static List<string> NearbyRoots()
        {
            List<string> roots = new List<string>();
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string cur = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                roots.Add(cur);
                for (int i = 0; i < 2; i++)
                {
                    DirectoryInfo up = Directory.GetParent(cur);
                    if (up == null) break;
                    cur = up.FullName;
                    roots.Add(cur);
                }
            }
            catch { }
            return roots;
        }

        static string FindGameDirNearby()
        {
            // 只在程序目录和它的上一级里找（保持原样：范围越宽越容易认错）
            List<string> roots = new List<string>();
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                roots.Add(baseDir);
                DirectoryInfo up = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (up != null) roots.Add(up.FullName);
            }
            catch { }
            // ⚠ 不能"取第一个命中"（2026-10-10 改）：Directory.GetDirectories 给的是文件
            //   系统自己的顺序，不是"哪个更像真的"。程序旁边有两份游戏时（一份旧备份、
            //   一份在用的），挂上哪份纯看运气 —— 录出来是怪结果，还查不出原因。
            //   改成把所有命中收集起来、用和走盘同一套排序挑（PickBestGameDir）。
            List<string> hits = new List<string>();
            foreach (string root in roots)
            {
                try { if (IsGameDirAt(root)) hits.Add(Path.Combine(root, "th123")); } catch { }
                try
                {
                    foreach (string sub in Directory.GetDirectories(root))
                        if (IsGameDirAt(sub)) hits.Add(Path.Combine(sub, "th123"));
                }
                catch { }
            }
            return PickBestGameDir(hits);
        }

        static bool IsGameDirAt(string parent)
        {
            try { return File.Exists(Path.Combine(Path.Combine(parent, "th123"), "th123.exe")); }
            catch { return false; }
        }

        // 整合包自己的 SokuLauncher.json 写着 SokuDirPath / SokuFileName —— 这是最权威的
        // 线索，而且**不依赖目录名**：用户把整合包文件夹改成"新建文件夹"、或者整个挪到别的
        // 盘，只要启动器还在，就能解出游戏在哪。纯靠目录名扫要脆弱得多。
        static string FindGameViaSokuLauncher()
        {
            foreach (string root in NearbyRoots())
            {
                // 每个 root 自己 + 它的一级子目录
                string[] dirs;
                try { dirs = Directory.GetDirectories(root); } catch { dirs = new string[0]; }
                string json = Path.Combine(root, "SokuLauncher.json");
                if (File.Exists(json)) { string g = GameDirFromSokuJson(json); if (g != null) return g; }
                string launcher = Path.Combine(root, "SokuLauncher.exe");
                if (File.Exists(launcher))
                {
                    string d = Path.Combine(Path.Combine(root, "th123"), "th123.exe");
                    if (File.Exists(d)) return Path.Combine(root, "th123");
                }
                foreach (string sub in dirs)
                {
                    try
                    {
                        json = Path.Combine(sub, "SokuLauncher.json");
                        if (File.Exists(json)) { string g = GameDirFromSokuJson(json); if (g != null) return g; }
                        // 只有启动器没有 json：把启动器所在目录当整合包根试一下约定俗成的 th123\
                        if (File.Exists(Path.Combine(sub, "SokuLauncher.exe")) &&
                            File.Exists(Path.Combine(Path.Combine(sub, "th123"), "th123.exe")))
                            return Path.Combine(sub, "th123");
                    }
                    catch { }
                }
            }
            return null;
        }

        static string GameDirFromSokuJson(string jsonPath)
        {
            try
            {
                Dictionary<string, object> d = (Dictionary<string, object>)new JavaScriptSerializer()
                    .DeserializeObject(File.ReadAllText(jsonPath, Encoding.UTF8));
                string dirName = (d != null && d.ContainsKey("SokuDirPath")) ? Convert.ToString(d["SokuDirPath"]) : null;
                string exeName = (d != null && d.ContainsKey("SokuFileName")) ? Convert.ToString(d["SokuFileName"]) : null;
                if (string.IsNullOrEmpty(exeName)) exeName = "th123.exe";
                if (string.IsNullOrEmpty(dirName))
                {
                    // 字段名对不上（别的整合包版本）—— 别静默失效，留个痕，不然"为什么没找到"查不出来
                    Log.Write("SokuLauncher.json 里没有 SokuDirPath，这版整合包字段名可能不同：" + jsonPath);
                    return null;
                }
                string baseDir = Path.GetDirectoryName(jsonPath);
                string dir = Path.IsPathRooted(dirName) ? dirName : Path.Combine(baseDir, dirName);
                if (File.Exists(Path.Combine(dir, exeName))) return dir;
                Log.Write("SokuLauncher.json 指向的目录里没有 " + exeName + "：" + dir);
                return null;
            }
            catch (Exception ex) { Log.Write("读 SokuLauncher.json 失败：" + ex.Message); return null; }
        }

        // 各固定盘走一遍，返回所有候选。游戏和 ffmpeg 共用（只差"什么算命中"和
        // "哪些目录名像目标、先走一步"这两张表）。
        static List<string> ScanFixedDrives(Func<string, bool> isTarget, string[] hints, string what)
        {
            List<string> hits = new List<string>();
            Stopwatch sw = Stopwatch.StartNew();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                // 只扫固定盘（和 FindObsExe 一致）：U 盘/光驱慢得没边，插着的读卡器还能卡死
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                try { WalkForTarget(d.RootDirectory, 0, sw, hits, isTarget, hints); } catch { }
                if (sw.ElapsedMilliseconds > ScanBudgetMs)
                {
                    Log.Write("自动找" + what + "：超过 " + (ScanBudgetMs / 1000) + " 秒，没扫完就收工了");
                    break;
                }
            }
            return hits;
        }

        // 给调试接口的"限定范围扫描"：同步、不写共享缓存。验收脚本靠它把扫描算法
        // 单独拎出来测 —— 在开发机上整盘扫必然会撞到仓库里那个真游戏、以及一堆别的
        // 软件捆绑的 ffmpeg，分不清找到的到底是"被测的那个"还是它们。
        static List<string> ScanUnder(string root, Func<string, bool> isTarget, string[] hints, string what)
        {
            List<string> hits = new List<string>();
            try { WalkForTarget(new DirectoryInfo(root), 0, Stopwatch.StartNew(), hits, isTarget, hints); }
            catch (Exception ex) { Log.Write("限定范围找" + what + "出错：" + ex.Message); }
            return hits;
        }

        static string ScanDrivesForGame()
        {
            return PickBestGameDir(ScanFixedDrives(IsStrictGameDir, ScanHints, "游戏"));
        }

        public static string ScanForGameUnder(string root)
        {
            return PickBestGameDir(ScanUnder(root, IsStrictGameDir, ScanHints, "游戏"));
        }

        static string PickBestGameDir(List<string> hits)
        {
            if (hits == null || hits.Count == 0) return null;
            if (hits.Count == 1) return hits[0];
            // 有几份副本时别"取第一个"：遍历顺序是文件系统自己的顺序，不是"最近用的"，
            // 挂上一份旧的、mod 没配好的副本录出来是怪结果，报错还看不懂。
            hits.Sort(delegate (string a, string b)
            {
                int sa = ScoreGameDir(a), sb = ScoreGameDir(b);
                if (sa != sb) return sb.CompareTo(sa);
                try { return Directory.GetLastWriteTimeUtc(b).CompareTo(Directory.GetLastWriteTimeUtc(a)); }
                catch { return 0; }
            });
            for (int i = 1; i < hits.Count; i++) Log.Write("自动找游戏：没选它（" + ScoreGameDir(hits[i]) + " 分）：" + hits[i]);
            return hits[0];
        }

        // 有启动器在同一级的整合包最可信（说明这份是从启动器启动的、配置齐）
        static int ScoreGameDir(string dir)
        {
            try
            {
                string parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent)) return 0;
                if (File.Exists(Path.Combine(parent, "SokuLauncher.json"))) return 1000;
                if (File.Exists(Path.Combine(parent, "SokuLauncher.exe"))) return 800;
            }
            catch { }
            return 0;
        }

        static void WalkForTarget(DirectoryInfo dir, int depth, Stopwatch sw, List<string> hits,
                                  Func<string, bool> isTarget, string[] hints)
        {
            if (dir == null) return;
            if (depth > MaxScanDepth || sw.ElapsedMilliseconds > ScanBudgetMs) return;
            if (isTarget(dir.FullName)) { hits.Add(dir.FullName); return; }   // 找到就不再往下钻
            if (depth == MaxScanDepth) return;

            DirectoryInfo[] subs;
            try { subs = dir.GetDirectories(); } catch { return; }
            Array.Sort(subs, delegate (DirectoryInfo a, DirectoryInfo b)
            {
                return HintRank(a.Name, hints).CompareTo(HintRank(b.Name, hints));
            });
            foreach (DirectoryInfo sub in subs)
            {
                try
                {
                    // 不跟 junction / 符号链接：C: 上的 "Application Data" 那种是 reparse point，
                    // 跟进去会绕回自己，深度 8 也照样能转半天
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (SkipDirName(sub.Name)) continue;
                }
                catch { continue; }
                WalkForTarget(sub, depth + 1, sw, hits, isTarget, hints);
            }
        }

        static bool SkipDirName(string name)
        {
            string n = name.Trim().ToLowerInvariant();
            foreach (string s in ScanSkipDirs) if (n == s) return true;
            return false;
        }

        static int HintRank(string name, string[] hints)
        {
            string n = name.ToLowerInvariant();
            foreach (string h in hints) if (n.IndexOf(h) >= 0) return 0;
            return 1;
        }

        static bool IsStrictGameDir(string dir)
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, "th123.exe"))) return false;
                foreach (string m in GameMarkers)
                    if (File.Exists(Path.Combine(dir, m))) return true;
            }
            catch { }
            return false;
        }

        // 后台扫描的结果由界面每 2 秒取一次（两个界面各有一个 2 秒的 soundTimer）。
        // 那条路径本来就是 UI 线程，所以回填不需要任何跨线程调用 —— 特意这么设计：
        // Ui.OnUi 在 Application 还没建好时会直接在调用线程上跑（Ui.cs 里那句
        // `app == null → return f()`），扫描快的话正好能撞上，从后台线程碰控件。
        // 返回 true 表示这次真的填了，调用方据此决定要不要刷状态栏。
        public static bool AdoptScannedGameDir(Settings s)
        {
            if (s == null) return false;
            string g = gameScan.Cached;
            if (string.IsNullOrEmpty(g)) return false;
            if (!string.IsNullOrEmpty(s.GameDir) && File.Exists(Path.Combine(s.GameDir, "th123.exe"))) return false;
            try
            {
                Log.Write("自动找到游戏目录：" + g);
                s.GameDir = g;
                s.Save();
                // lastVia 也要在这里记：这个目录是【扫描】给的，但回填它的是界面定时器，
                // 不经过 ResolveGameDir。不记的话，谁先跑（定时器还是调用方）会让
                // 调试接口的 via 时有时无 —— 验收脚本会随机假红（2026-10-09 验证WPF-15
                // 就是这么红过一次：game dir 明明找到了，via 却是空的）。
                lock (scanGate) lastVia = "scan";
                return true;
            }
            catch (Exception ex) { Log.Write("回填游戏目录失败：" + ex.Message); return false; }
        }

        // ---------------------------------------------------------- 找 ffmpeg
        //
        // 2026-10-10 加的。以前 ffmpeg 没有任何设置字段，只在几个固定位置现搜，而且
        // **每次调用都重搜**。用户自己下载后放在别处（最常见：照教程解压到 D:\ffmpeg
        // 但忘了加 PATH）就永远找不到 —— 它又是三个依赖里唯一不能手动指定的，用户没救。
        //
        // 分层和游戏一样，但多一道**实跑校验**。因为 ffmpeg.exe 这个名字太不特殊：
        // 开发机上 D 盘 6 层内有 5 个，只有 1 个是用户自己的，其余全是别的软件捆在
        // resources\ 里的（其中一份还是音频专用版，编不了视频）。所以走盘找到的候选
        // 必须跑一次、确认能编 H.264 才算数。
        //
        // 只有走盘这条路校验。前面那些固定位置不校验：它们每次调用都会跑，起子进程太贵，
        // 而且那些位置本来就可信（分发包里带的那份、WinGet、C:\ffmpeg\bin）。
        static readonly string[] FfmpegScanHints = new string[] { "ffmpeg", "bin", "tools" };

        // 设置里那个 ffmpeg 路径的静态镜像。FindFfmpeg 是 static，而调用点近十个、跨两个
        // 界面 —— 逐个改成"传 cfg 进来"太容易漏一两处，漏了的表现是"某个入口死活不认
        // 用户指定的 ffmpeg"。这个项目已经被"漏接一根线"咬过好几次，所以宁可留一个
        // 只有一个写入方（Settings.Load / Save）的可变全局。
        static string FfmpegOverride = "";

        public static string FindFfmpeg()
        {
            // 0) 设置里存着的：用户手选的，或者上一次自动找到后写回去的
            string ov = FfmpegOverride;
            if (!string.IsNullOrEmpty(ov) && File.Exists(ov)) { lock (scanGate) fmLastVia = "cfg"; return ov; }

            // 1) 程序自己旁边（分发包里就带一份）；顺带看看 bin\ 和上一级 ——
            //    用户常常把整个 ffmpeg 解压到录制器旁边
            try
            {
                string bd = AppDomain.CurrentDomain.BaseDirectory;
                string[] rel = new string[] { "ffmpeg.exe", @"bin\ffmpeg.exe", @"..\ffmpeg.exe", @"..\bin\ffmpeg.exe" };
                foreach (string r in rel)
                {
                    string c = Path.GetFullPath(Path.Combine(bd, r));
                    if (File.Exists(c)) { lock (scanGate) fmLastVia = "near"; return c; }
                }
            }
            catch { }

            // 2) WinGet（Links 是垫片，Packages 是本体；版本号在目录名里所以只能通配）
            try
            {
                string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string links = Path.Combine(lad, @"Microsoft\WinGet\Links\ffmpeg.exe");
                if (File.Exists(links)) { lock (scanGate) fmLastVia = "winget"; return links; }
                string pkg = Path.Combine(lad, @"Microsoft\WinGet\Packages");
                if (Directory.Exists(pkg))
                    foreach (string d in Directory.GetDirectories(pkg, "Gyan.FFmpeg*"))
                        foreach (string b in Directory.GetDirectories(d, "ffmpeg*"))
                        {
                            string c = Path.Combine(b, @"bin\ffmpeg.exe");
                            if (File.Exists(c)) { lock (scanGate) fmLastVia = "winget"; return c; }
                        }
            }
            catch { }

            // 3) 常见安装位置（教程里最经典的 C:\ffmpeg\bin、以及 scoop / choco）
            foreach (string c in FixedFfmpegCandidates())
            {
                try { if (File.Exists(c)) { lock (scanGate) fmLastVia = "fixed"; return c; } }
                catch { }
            }

            // 4) PATH 里
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (path != null)
                    foreach (string d in path.Split(';'))
                    {
                        if (d.Trim().Length == 0) continue;
                        string p = Path.Combine(d.Trim(), "ffmpeg.exe");
                        if (File.Exists(p)) { lock (scanGate) fmLastVia = "path"; return p; }
                    }
            }
            catch { }

            // 5) 都不在 → 后台走一遍盘（不阻塞：还在扫就先返回 null，界面过两秒自己取）
            fmScan.EnsureStarted(ScanDrivesForFfmpeg);
            string scanned = fmScan.Cached;
            if (scanned != null) { lock (scanGate) fmLastVia = "scan"; return scanned; }
            return null;
        }

        static List<string> FixedFfmpegCandidates()
        {
            List<string> cands = new List<string>();
            try
            {
                // 每个固定盘的 <盘>:\ffmpeg\bin\ffmpeg.exe —— 网上教程的标准装法
                foreach (DriveInfo d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Fixed && d.IsReady)
                        cands.Add(Path.Combine(d.RootDirectory.FullName, @"ffmpeg\bin\ffmpeg.exe"));
            }
            catch { }
            try
            {
                cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"ffmpeg\bin\ffmpeg.exe"));
                cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"ffmpeg\bin\ffmpeg.exe"));
                cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ffmpeg\bin\ffmpeg.exe"));
                string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                cands.Add(Path.Combine(up, @"scoop\shims\ffmpeg.exe"));
                cands.Add(Path.Combine(up, @"scoop\apps\ffmpeg\current\bin\ffmpeg.exe"));
                cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"chocolatey\bin\ffmpeg.exe"));
            }
            catch { }
            return cands;
        }

        static bool HasFfmpegExe(string dir)
        {
            try { return File.Exists(Path.Combine(dir, "ffmpeg.exe")); } catch { return false; }
        }

        static string ScanDrivesForFfmpeg()
        {
            return PickUsableFfmpeg(ScanFixedDrives(HasFfmpegExe, FfmpegScanHints, "ffmpeg"));
        }

        // 给调试接口用（验证WPF-17）：限定范围同步扫一遍，返回候选个数好断言"排序看到了几个"。
        public static string ScanForFfmpegUnder(string root, out int count)
        {
            List<string> hits = ScanUnder(root, HasFfmpegExe, FfmpegScanHints, "ffmpeg");
            count = hits.Count;
            return PickUsableFfmpeg(hits);
        }

        // 先按"像不像用户特意装的"排序，再逐个**实跑**验，第一个通过的胜出。
        // 不能随便挑一个：audio-only 的、被裁剪过的、挂羊头卖狗肉的，都会让切分直接失败，
        // 而那种"录完了但切不出来"的报错极难看懂。
        static string PickUsableFfmpeg(List<string> dirs)
        {
            if (dirs == null || dirs.Count == 0) return null;
            dirs.Sort(CompareFfmpegDirs);
            int tried = 0;
            for (int i = 0; i < dirs.Count; i++)
            {
                if (tried >= 8)   // 起子进程不便宜，前几个都不行就别把整盘跑完
                {
                    Log.Write("自动找 ffmpeg：前 8 个候选都不能用，先不试了");
                    break;
                }
                string exe = Path.Combine(dirs[i], "ffmpeg.exe");
                tried++;
                if (IsUsableFfmpeg(exe)) return exe;
                Log.Write("自动找 ffmpeg：不能用，跳过 " + exe);
            }
            return null;
        }

        static int CompareFfmpegDirs(string a, string b)
        {
            int sa = ScoreFfmpegDir(a), sb = ScoreFfmpegDir(b);
            if (sa != sb) return sb.CompareTo(sa);
            int da = DepthOf(a), db = DepthOf(b);
            if (da != db) return da.CompareTo(db);        // 层级越浅越像"用户自己装的"
            try { return Directory.GetLastWriteTimeUtc(b).CompareTo(Directory.GetLastWriteTimeUtc(a)); }
            catch { return 0; }
        }

        static int DepthOf(string path)
        {
            try { return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length; }
            catch { return 99; }
        }

        // "这像不像用户特意装的一份 ffmpeg"——只看路径长相；能不能用另有一道实跑校验。
        // dir 一般就是那个 bin 目录，所以看它的上一级叫什么。
        static int ScoreFfmpegDir(string dir)
        {
            int s = 0;
            try
            {
                string low = dir.ToLowerInvariant();
                string gp = Path.GetFileName(Path.GetDirectoryName(dir));
                gp = gp == null ? "" : gp.ToLowerInvariant();
                if (gp == "ffmpeg") s += 100;                     // C:\ffmpeg\bin
                else if (gp.IndexOf("ffmpeg") >= 0) s += 80;      // ffmpeg-7.1-full_build\bin
                if (low.IndexOf(@"\scoop\") >= 0 || low.IndexOf(@"\chocolatey\") >= 0) s += 90;
                // 一堆软件把 ffmpeg 捆在 resources\ 里 —— 开发机上 5 个里有 4 个是这种
                if (low.IndexOf(@"\resources\") >= 0) s -= 100;
                if (low.IndexOf(@"\app\") >= 0 || low.IndexOf(@"\plugins\") >= 0 || low.IndexOf(@"\modules\") >= 0) s -= 60;
                if (low.IndexOf("audio") >= 0) s -= 100;          // 音频专用版，编不了视频
            }
            catch { }
            return s;
        }

        // 实跑一次，确认"它真的是 ffmpeg，而且能编 H.264"。
        // 程序后面用的是 -c:v h264_nvenc、没 N 卡退到 libx264 —— 所以 libx264 是硬门槛。
        static bool IsUsableFfmpeg(string exe)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, "-hide_banner -encoders");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    // 必须异步读：编码器列表上百 KB，同步读会和子进程互相等死
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    // ⚠ 必须有超时：扫到的只是个"叫 ffmpeg.exe 的文件"，可能是 GUI 程序、
                    //   也可能直接挂住 —— 不能让走盘线程被它拖死。
                    if (!p.WaitForExit(10000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    p.WaitForExit();     // 等异步读取收尾，不然字符串可能还没拼完
                    bool ok;
                    lock (sb) ok = sb.ToString().IndexOf("libx264", StringComparison.OrdinalIgnoreCase) >= 0;
                    return ok;
                }
            }
            catch { return false; }
        }

        // 和游戏那边同一条路子：后台走盘的结果由界面每 2 秒的定时器取走（UI 线程），
        // 取到就写进设置 —— 下一次启动第 0 步直接命中，不用再搜。
        public static bool AdoptScannedFfmpeg(Settings s)
        {
            if (s == null) return false;
            string f = fmScan.Cached;
            if (string.IsNullOrEmpty(f)) return false;
            if (!string.IsNullOrEmpty(s.FfmpegExe) && File.Exists(s.FfmpegExe)) return false;
            try
            {
                Log.Write("自动找到 ffmpeg：" + f);
                s.FfmpegExe = f;
                s.Save();
                lock (scanGate) fmLastVia = "scan";
                return true;
            }
            catch (Exception ex) { Log.Write("回填 ffmpeg 失败：" + ex.Message); return false; }
        }

        // 给「检查环境」按钮用的：让用户能当场重新找一次。
        // 扫描是每个进程只跑一轮的，不重启一下的话，用户刚装上游戏/ffmpeg 就得关掉程序
        // 重开才找得到 —— 而「检查环境」正是他会去点的那个按钮。
        public static void RestartSearchesIfNeeded(Settings s)
        {
            if (s == null) return;
            if (string.IsNullOrEmpty(s.GameDir) || !File.Exists(Path.Combine(s.GameDir, "th123.exe")))
            {
                gameScan.Reset();
                string via;
                ResolveGameDir(out via);
            }
            if (string.IsNullOrEmpty(s.FfmpegExe) || !File.Exists(s.FfmpegExe))
            {
                fmScan.Reset();
                FindFfmpeg();
            }
            // OBS 同理：它现在也有缓存了（见 FindObsExe），用户当场装上 OBS 之后
            // 得靠这一下把"上次没找到"那个结果清掉，否则永远读到缓存里的 null。
            if (string.IsNullOrEmpty(s.ObsExe) || !File.Exists(s.ObsExe))
                ResetObsScan();
        }

        // 注册表卸载项里的候选（OBS 可能装在非默认目录，比如 D:\OBS\obs-studio）。
        // ⚠ HKLM 和 HKCU 都要看（2026-10-10 补 HKCU）：默认写 HKLM，但按用户安装 / 被改过
        // 的机器只会在 HKCU 里留一条 —— 以前那种一律读不到。
        // 单独抽成一个方法是为了能被验收脚本直接读到：机器上已经装了真 OBS 时，
        // "最后选中了谁"验证不了 HKCU 这半边（真 OBS 总会赢），得能单独看候选清单。
        public static List<string> ObsRegistryCandidates()
        {
            List<string> cands = new List<string>();
            try
            {
                Microsoft.Win32.RegistryKey[] roots = new Microsoft.Win32.RegistryKey[] {
                    Microsoft.Win32.Registry.LocalMachine,
                    Microsoft.Win32.Registry.CurrentUser
                };
                string[] subKeys = new string[] {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                foreach (Microsoft.Win32.RegistryKey root in roots)
                    foreach (string sk in subKeys)
                    {
                        using (Microsoft.Win32.RegistryKey rk = root.OpenSubKey(sk))
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
            return cands;
        }

        public static string FindObsExe()
        {
            // 2026-10-12 加缓存：这个函数每调一次都要读 HKLM+HKCU 的卸载项、再把每个固定盘的
            // 根目录枚举一遍，而它被 Settings.Load→FillDefaults、Recorder.RefreshEnvLabel、
            // EnsureObs、OnCheck 反复调（都在 UI 线程上）—— 用户那份设置里 OBS 路径一旦失效，
            // 每次刷新/开录都要重做一遍。游戏目录和 ffmpeg 的查找本来就有缓存，唯独漏了它。
            //
            // 「没找到」也缓存（记成 done），不然每次调用还是要扫一遍；
            // 代价是用户当场装上 OBS 后得靠 RestartSearchesIfNeeded（「检查环境」会调）清缓存。
            lock (obsScanGate) { if (obsScanDone) return obsScanFound; }

            string result = FindObsExeUncached();

            lock (obsScanGate) { obsScanFound = result; obsScanDone = true; }
            return result;
        }

        static string obsScanFound;
        static bool obsScanDone;
        static readonly object obsScanGate = new object();

        /// <summary>清掉 FindObsExe 的缓存，让它下次真的重扫一遍（「检查环境」用）。</summary>
        public static void ResetObsScan()
        {
            lock (obsScanGate) { obsScanFound = null; obsScanDone = false; }
        }

        static string FindObsExeUncached()
        {
            List<string> cands = new List<string>();

            // 1) 注册表卸载项（见 ObsRegistryCandidates）
            cands.AddRange(ObsRegistryCandidates());

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

        // ⚠ 日志**故意**保持"每行开→写→关"（2026-10-12 查过并决定不改）。
        //   原因：常开一个 StreamWriter 会占住写句柄，而 `File.ReadAllLines`（用 FileShare.Read 打开）
        //   就读不了了 —— 实测报"文件正由另一进程使用"。而程序自己的调试命令 `log:`
        //   （DebugPort.LogTail 用 File.ReadAllLines）就是这条，验证WPF-02 正在查它；
        //   用户那边用来打开日志的编辑器也可能中招。
        //   换来的只是每行省一次 open/close，而录制期间日志也就每秒几行 —— 量级可以忽略。
        //   （Copy-Item 和 Get-Content 不受影响，但"把日志发给我"这条路不该拿它去赌。）
        //   所以这里只把编码器提出来复用，去掉每行一次 new UTF8Encoding(false)。
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void Write(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (gate)
            {
                try { File.AppendAllText(FilePath, line + Environment.NewLine, Utf8NoBom); }
                catch { }
            }
            Action<string> h = OnLine;
            if (h != null)
            {
                try { h(line); } catch { }
            }
        }
    }

    // ============================================================ 环境自检
    //
    // 「这台机器缺什么」以前散在四个地方：RefreshEnvLabel 只报 OBS 和游戏目录两项、
    // OnCheck 把五项写进日志（用户得自己去翻终端）、FillDefaults 只管往设置里补值、
    // 三个 Find* 各找各的。新用户的第一次体验就烂在这里 —— 环境卡写着「已就绪」，
    // 实际却连不上 OBS（从别人那儿拷来的旧密码就是这种情况，2026-10-08 查实）。
    // 现在收口成一个对象：环境卡读它、「检查环境」弹它、验收脚本也能问它。
    internal class EnvItem
    {
        public string Key;        // game / obs / websocket / ffmpeg / mod
        public string Label;      // 界面上显示的名字
        public bool Ok;
        public string Value;      // 找到了就是路径/端口；没找到是一句说明
        public string Fix;        // 怎么修；Ok 时是空串
    }

    internal class EnvReport
    {
        public List<EnvItem> Items = new List<EnvItem>();

        public EnvItem Get(string key)
        {
            foreach (EnvItem it in Items) if (it.Key == key) return it;
            return null;
        }

        public bool AllOk
        {
            get { foreach (EnvItem it in Items) if (!it.Ok) return false; return true; }
        }

        static void Add(List<EnvItem> l, string key, string label, bool ok, string val, string fix)
        {
            EnvItem it = new EnvItem();
            it.Key = key; it.Label = label; it.Ok = ok; it.Value = val;
            it.Fix = ok ? "" : fix;
            l.Add(it);
        }

        // ⚠ 这里只做【便宜】的检查（文件在不在、配置怎么写的）。
        //   真去连一次 OBS 是 OnCheck 的事 —— 那要等好几秒，不能放在"窗口刚显示"那一步。
        public static EnvReport Build(Settings cfg)
        {
            EnvReport r = new EnvReport();
            if (cfg == null) return r;

            // ---- 游戏目录 ----
            bool gameOk = !string.IsNullOrEmpty(cfg.GameDir) && File.Exists(Path.Combine(cfg.GameDir, "th123.exe"));
            Add(r.Items, "game", "游戏目录", gameOk,
                gameOk ? cfg.GameDir : "未找到",
                "在「设置…」里把「游戏目录」指向整合包里的 th123 文件夹");

            // ---- OBS ----
            bool obsOk = !string.IsNullOrEmpty(cfg.ObsExe) && File.Exists(cfg.ObsExe);
            Add(r.Items, "obs", "OBS", obsOk,
                obsOk ? cfg.ObsExe : "未安装",
                "装一个 OBS Studio（28 或更高）；装完先启动一次再关掉，让 OBS 生成自己的配置");

            // ---- WebSocket（端口/密码都从 OBS 自己的配置里读；OBS 关着时程序会帮你打开它）----
            bool wsOk = false;
            string wsVal = "读不到 OBS 的配置";
            string wsFix = "先启动一次 OBS（它才会生成配置文件）再关掉，然后点「检查环境」";
            try
            {
                string p = Settings.ObsWebSocketConfigPath;
                if (File.Exists(p))
                {
                    bool enabled = false;
                    Dictionary<string, object> root = (Dictionary<string, object>)
                        new JavaScriptSerializer().DeserializeObject(File.ReadAllText(p, Encoding.UTF8));
                    if (root != null && root.ContainsKey("server_enabled") && root["server_enabled"] != null)
                        enabled = Convert.ToBoolean(root["server_enabled"]);
                    if (enabled)
                    {
                        wsOk = true;
                        wsVal = "127.0.0.1:" + cfg.Port +
                                "（" + (string.IsNullOrEmpty(cfg.Password) ? "密码为空" : "密码已获取") + "）";
                        wsFix = "";
                    }
                    else
                    {
                        wsVal = "没启用";
                        // OBS 正在跑的时候改不了它的配置（写了会被它覆盖回去，见 EnsureWebSocketEnabled）
                        wsFix = Process.GetProcessesByName("obs64").Length > 0
                            ? "先把 OBS 彻底关掉（托盘里也要退），再点「检查环境」—— 程序会自动帮你打开它"
                            : "点「准备 OBS」会自动帮你打开它";
                    }
                }
            }
            catch { }
            Add(r.Items, "websocket", "WebSocket", wsOk, wsVal, wsFix);

            // ---- ffmpeg（并行录制才要它，单录不用）----
            string ffm = null;
            try { ffm = Recorder.FindFfmpeg(); } catch { }
            // 2026-10-09 用户改的：程序不再代下 ffmpeg（分发包里本来就带一份），
            // 这里只负责【说清楚缺什么、去哪拿】。
            Add(r.Items, "ffmpeg", "ffmpeg", ffm != null,
                ffm != null ? ffm : "未找到（并行录制需要，单个录制不需要）",
                "程序已经把常见位置和整块硬盘都找过了。自己下一份（www.gyan.dev/ffmpeg/builds 的 " +
                "ffmpeg-release-essentials.zip），解压出 bin\\ffmpeg.exe 放到 RepRecorder.exe 旁边；" +
                "或者去「设置…」里把「ffmpeg 程序」直接指过去");

            // ---- ReplayDnD mod（新机器最容易漏的一步：漏了游戏会停在标题画面）----
            bool modOk = false;
            string modVal = "没检查（游戏目录不对）";
            string modFix = "先把「游戏目录」指对";
            if (gameOk)
            {
                try
                {
                    string mls = Path.Combine(cfg.GameDir, "ModLoaderSettings.json");
                    if (!File.Exists(mls)) { modVal = "整合包里没有这个文件"; modFix = "确认游戏目录指对了（里面应该有 ModLoaderSettings.json）"; }
                    else
                    {
                        // 只【读】不写：写它会和 mod loader 抢文件、把它弄崩（README 第十六节）
                        Dictionary<string, object> root = (Dictionary<string, object>)
                            new JavaScriptSerializer().DeserializeObject(File.ReadAllText(mls, Encoding.UTF8));
                        bool on = false, found = false;
                        Dictionary<string, object> mods =
                            (root != null && root.ContainsKey("modules")) ? root["modules"] as Dictionary<string, object> : null;
                        if (mods != null)
                            foreach (KeyValuePair<string, object> kv in mods)
                            {
                                if (kv.Key.IndexOf("ReplayDnD", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                found = true;
                                Dictionary<string, object> v = kv.Value as Dictionary<string, object>;
                                if (v != null && v.ContainsKey("enabled") && v["enabled"] != null && Convert.ToBoolean(v["enabled"]))
                                    on = true;
                            }
                        modOk = on;
                        modVal = on ? "已启用" : (found ? "没启用" : "整合包里没有");
                        modFix = "去整合包的 mod 配置菜单里启用 ReplayDnD，否则录出来是标题画面、不是对局";
                        if (on) modFix = "";
                    }
                }
                catch (Exception ex) { modVal = "检查失败：" + ex.Message; }
            }
            Add(r.Items, "mod", "ReplayDnD mod", modOk, modVal, modFix);

            return r;
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
        // 2026-10-11：询问框多了一个「再录一次（旧的保留，新的另存）」。
        // 和 Force 的区别：Force 只管"即使已有录像也要录"，而这一位管"录出来【不许盖掉】原来那份"，
        // 成品会落到 xxx (2).mp4 / xxx (3).mp4 …（见 Recorder.OutputPathFor）。
        public bool KeepOld;

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

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // 这里原本有 Application.EnableVisualStyles() 和 SetCompatibleTextRenderingDefault()
            // —— 2026-10-12 跟着旧 WinForms 界面一起删了。[STAThread] 留着：WPF 同样需要 STA。

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

            // ---- 界面：2026-10-12 起只有 WPF 版 ----
            // 旧的 WinForms 界面连同 --old 一起删掉了；旧版本体作为独立快照留在
            // RepRecorder-旧版WinForms\ 里（那份是完整的旧程序，双击还能跑）。
            // --wpf / --old / --winforms 仍然"认"，只是从参数里摘掉 ——
            // UiOnShown 会把收到的每个参数当拖拽进来的文件路径交给 AddPaths（Recorder.cs:408），
            // 不摘掉的话 --old 会被当成一个不存在的 .rep 去加队列。
            // 留着它们是为了桌面上那个旧快捷方式、或者谁手里还留着的旧 bat 不会因此报错。
            List<string> keep = new List<string>();
            if (args != null)
                foreach (string a in args)
                    if (a != "--wpf" && a != "--old" && a != "--winforms") keep.Add(a);

            Ui.Start(keep.ToArray());
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

        // 缓存"默认端点的音量接口"。
        // 2026-10-12：这个函数被界面上那个 2 秒定时器（UiRefreshSoundButtons）反复调，纯为轮询
        // "系统是不是被静音了"。以前每次都新建 MMDeviceEnumerator → 取默认端点 → Activate 音量接口，
        // 用完立刻全放掉 —— 每 2 秒一轮 COM 对象的建/销。缓存住之后只剩一次 GetMute。
        // 设备热插拔/默认设备切换会让缓存的接口失效：那时 hr 非 0 或直接抛异常，
        // 就丢掉缓存、下一次重建（DropVolCache）。
        static IAudioEndpointVolume cachedVol;

        static void DropVolCache()
        {
            IAudioEndpointVolume v = cachedVol;
            cachedVol = null;
            if (v != null) { try { Marshal.ReleaseComObject(v); } catch { } }
        }

        public static bool GetSystemMute(out bool muted, out string message)
        {
            muted = false;
            message = "";
            try
            {
                if (cachedVol == null)
                {
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
                        cachedVol = (IAudioEndpointVolume)volObj;
                        volObj = null;          // 归缓存所有了，别在 finally 里放掉
                    }
                    finally
                    {
                        if (volObj != null) { try { Marshal.ReleaseComObject(volObj); } catch { } }
                        if (device != null) { try { Marshal.ReleaseComObject(device); } catch { } }
                        if (enumerator != null) { try { Marshal.ReleaseComObject(enumerator); } catch { } }
                    }
                }

                int m;
                int r = cachedVol.GetMute(out m);
                if (r != 0) { DropVolCache(); message = "GetMute 失败 hr=" + r; return false; }
                muted = (m != 0);
                message = "读取成功";
                return true;
            }
            catch (Exception ex)
            {
                DropVolCache();       // 设备没了 / 接口死了 → 丢掉缓存，下一次重建
                message = ex.Message;
                return false;
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
