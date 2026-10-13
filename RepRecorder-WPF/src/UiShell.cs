// ============================================================
//  UiShell.cs —— Ui 门面缺的那几个小方法 + 队列行模型 + 调试接口
//  （2026-10-03 一次误删之后重建：验收脚本 验证WPF-00..06 是行为规格，重建后要全绿）
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace RepRecorder
{
    static partial class Ui
    {
        // 让无边框对话框能像普通窗口一样拖着走（主窗口靠 WindowChrome，不需要这个）。
        // 挂在整个窗口上，但鼠标按在滑块 / 按钮 / 下拉框 / 输入框上时不拖 ——
        // 否则调一下滑块就把整个对话框拖跑了。
        public static void MakeDraggable(Window w)
        {
            w.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState != MouseButtonState.Pressed) return;
                if (HitInteractive(e.OriginalSource as DependencyObject, w)) return;
                try { w.DragMove(); }
                catch { }   // 按下到进入 DragMove 之间鼠标已经松开的话会抛，忽略即可
            };
        }

        static bool HitInteractive(DependencyObject d, Window stop)
        {
            while (d != null && d != stop)
            {
                if (d is Slider || d is ButtonBase || d is ComboBox || d is TextBox || d is ScrollBar) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        // 分隔条：平时跟面板同一层淡底色，鼠标压上去亮一档（和旧版手感一致）。
        // ⚠ 这里【不能】用 Style 的 Setter 来设背景：Style 密封时会把 Setter 里的画刷冻结，
        //   而面板底色是共享的可变画刷（拖滑块要实时改），一冻结再改就抛
        //   InvalidOperationException，整个界面直接崩（2026-10-04 踩过）。
        //   所以背景直接赋在实例上，悬停高亮用鼠标事件做。
        public static void StyleSplitter(GridSplitter s)
        {
            // 平时完全透明 —— 新设计稿里卡片之间就是一道空隙，没有可见的分隔条。
            // 光标会变成双向箭头，鼠标压上去也有一档高亮，知道这里能拖。
            s.Background = Brushes.Transparent;
            s.MouseEnter += delegate { s.Background = Br(Palette.HudAccent, 0xCC); };
            s.MouseLeave += delegate { s.Background = Brushes.Transparent; };
        }

        // 永远异步：press/set 用这个 —— 按钮可能打开模态对话框，
        // 在 UI 线程上同步执行会把 Dispatcher.Invoke 一直挡住（调试接口就再也回不了话）。
        public static void PostAsync(Action a)
        {
            Application app = Application.Current;
            if (app == null) { a(); return; }
            app.Dispatcher.BeginInvoke(a);
        }

        // 在所有打开的窗口里按 AutomationId 找（对话框是独立窗口，主窗口的树里没有）
        public static FrameworkElement FindAnywhere(string id)
        {
            Application app = Application.Current;
            if (app == null) return null;
            foreach (Window w in app.Windows)
            {
                FrameworkElement fe = DebugPort.FindIn(w, id);
                if (fe != null) return fe;
            }
            return null;
        }

        public static string StatusColorOf(string status)
        {
            if (status == "录制中") return "#4F74D8";
            if (status == "完成" || status == "已录制") return "#37C8A0";
            if (status == "失败") return "#FF6767";
            if (status == "注意") return "#FFCA85";
            return "#93A0B4";
        }

        // 「影子控件」用：按 AutomationId 找界面上的元素（走可视树，UIA 看不到的也能找到）
        public static FrameworkElement Elm(string id)
        {
            ShellWindow w = W;
            if (w == null) return null;
            return DebugPort.FindIn(w, id);
        }

        public static void SetButtonText(string id, string text)
        {
            Post(delegate
            {
                Button b = Elm(id) as Button;
                if (b == null) return;
                object cur = b.Content;
                if (cur is string || cur == null) b.Content = text;
                else
                {
                    // 带图标的按钮（开始录制 / 停止）内容是一个 Grid：第 0 列图标、第 1 列文字。
                    // 不认这个形状的话，SetButtonText 会一声不响地什么都不做 —— 逻辑那边
                    // 写了 btnXxx.Text = "..." 也看不出没生效。
                    Grid g = cur as Grid;
                    if (g != null)
                    {
                        foreach (UIElement u in g.Children)
                        {
                            TextBlock t2 = u as TextBlock;
                            if (t2 != null && Grid.GetColumn(t2) == 1) { t2.Text = text; return; }
                        }
                        return;
                    }
                    TextBlock t = cur as TextBlock;
                    if (t != null) t.Text = text;
                }
            });
        }

        public static string GetButtonText(string id)
        {
            Button b = Elm(id) as Button;
            if (b == null) return "";
            if (b.Content is string) return (string)b.Content;
            TextBlock t = b.Content as TextBlock;
            if (t != null) return t.Text;
            Grid g = b.Content as Grid;
            if (g != null)
            {
                foreach (UIElement u in g.Children)
                {
                    TextBlock t2 = u as TextBlock;
                    if (t2 != null && Grid.GetColumn(t2) == 1) return t2.Text;
                }
            }
            return "";
        }

        public static void SetButtonEnabled(string id, bool on)
        {
            Post(delegate { FrameworkElement e = Elm(id); if (e != null) e.IsEnabled = on; });
        }

        public static void SetButtonForeColor(string id, Drawing.Color c)
        {
            Post(delegate { Control e = Elm(id) as Control; if (e != null) e.Foreground = Br(c); });
        }

        public static void SetButtonBorderColor(string id, Drawing.Color c)
        {
            Post(delegate { Control e = Elm(id) as Control; if (e != null) e.BorderBrush = Br(c); });
        }

        public static void SetButtonBackColor(string id, Drawing.Color c)
        {
            Post(delegate { Control e = Elm(id) as Control; if (e != null) e.Background = Br(c); });
        }

        public static void SetLabelText(string id, string text)
        {
            Post(delegate
            {
                TextBlock t = Elm(id) as TextBlock;
                if (t != null) t.Text = text == null ? "" : text;
            });
        }
    }

    // ================================================================ 队列行模型
    // 2026-10-12 实现 INotifyPropertyChanged。以前是个纯 POCO，改完字段只能靠
    // queueList.Items.Refresh() 让绑定生效 —— 那是**整表重建容器**：录制中每来一次状态更新
    // （UpdateRow）、每删一行（RenumberRows）、每标一次本批（HighlightBatchRows），
    // 整个队列的行容器都要重建一遍，队列一长就肉眼可见地闪。
    // 现在改哪个属性只通知那一行的那一个属性，那三处 Refresh 全删了。
    public class RowVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        void Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;   // 没变就别通知，省掉无谓的重画
            field = value;
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        // 下面这 8 个是**行模板里真正绑定的**（{Binding Index/File/Status/Dur/Size/Detail/StatusColor/Batch}），
        // 改属性名要连模板里的绑定一起改。
        string index = "", file = "", status = "等待", dur = "", size = "", detail = "", statusColor = "#93A0B4";
        bool batch;

        public string Index { get { return index; } set { Set(ref index, value, "Index"); } }
        public string File { get { return file; } set { Set(ref file, value, "File"); } }
        public string Status { get { return status; } set { Set(ref status, value, "Status"); } }
        public string Dur { get { return dur; } set { Set(ref dur, value, "Dur"); } }
        public string Size { get { return size; } set { Set(ref size, value, "Size"); } }
        public string Detail { get { return detail; } set { Set(ref detail, value, "Detail"); } }
        public string StatusColor { get { return statusColor; } set { Set(ref statusColor, value, "StatusColor"); } }   // "#37C8A0" 这种，绑定时自动转 Brush
        public bool Batch { get { return batch; } set { Set(ref batch, value, "Batch"); } }                          // "本批"那几行：绿底 + 左侧绿条

        // 这三个不参与绑定，保持自动属性（改了也不会触发通知，界面本来就不看它们）
        public string RepPath { get; set; }
        public string OutputPath { get; set; }
        public Job Job { get; set; }                 // 这一行对应哪个 Job
    }

    // ================================================================ 调试接口
    //  只有带 --debug-port=<端口> 启动时才监听，只绑 127.0.0.1，一行命令一行 JSON。
    //  为什么要有它：换到 WPF 之后整棵树只有一个 HWND，旧脚本那套"找子窗口句柄 + LVM_*"全失效。
    //  交互交给 UIA（按 AutomationId），内部状态交给这里。
    public static class DebugPort
    {
        static TcpListener srv;
        static Window win;

        public static void TryStart(string[] args, Window w)
        {
            win = w;
            int port = 0;
            if (args != null)
                foreach (string a in args)
                    if (a != null && a.StartsWith("--debug-port="))
                        int.TryParse(a.Substring("--debug-port=".Length), out port);
            if (port <= 0) return;
            try
            {
                srv = new TcpListener(IPAddress.Loopback, port);
                srv.Start();
            }
            catch (Exception ex)
            {
                Log.Write("调试接口起不来（端口 " + port + "）：" + ex.Message);
                return;
            }
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    try
                    {
                        TcpClient c = srv.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(delegate (object o) { Serve((TcpClient)o); }, c);
                    }
                    catch { return; }
                }
            });
            t.IsBackground = true;
            t.Start();
            Log.Write("调试接口已监听 127.0.0.1:" + port);
        }

        static void Serve(TcpClient c)
        {
            try
            {
                using (c)
                using (NetworkStream ns = c.GetStream())
                using (StreamReader r = new StreamReader(ns, Encoding.UTF8))
                using (StreamWriter w = new StreamWriter(ns, new UTF8Encoding(false)))
                {
                    w.AutoFlush = true;
                    string line = r.ReadLine();
                    w.WriteLine(Handle(line == null ? "" : line.Trim()));
                }
            }
            catch { }
        }

        // 协议：status / bgstate / rows / clear / addrows:<n> / addlog:<n> / loglines[:N] / log[:N]
        //       / text:<id> / find:<id> / press:<id> / set:<id>=<值>
        static string Handle(string cmd)
        {
            try
            {
                if (cmd == "" || cmd == "status") return Status();
                if (cmd == "bgstate") return Ui.OnUi<string>(delegate { ShellWindow sw = win as ShellWindow; return sw == null ? "{}" : sw.BgState(); });
                if (cmd == "env") return Ui.OnUi<string>(delegate { return EnvJson(); });
                if (cmd == "obsreg") return ObsRegJson();
                if (cmd.StartsWith("cmpver:")) return CmpVerJson(cmd.Substring(7));   // "cmpver:" 是 7 个字符
                if (cmd.StartsWith("updatecheck:")) return UpdateCheckJson(cmd.Substring(12));   // "updatecheck:" 是 12 个字符
                if (cmd.StartsWith("updateapply:")) return UpdateApplyJson(cmd.Substring(12));   // "updateapply:" 也是 12 个字符
                if (cmd == "updatecheck") return UpdateCheckJson(UpdateCheck.ManifestUrl);
                if (cmd == "findgame") return FindGameJson();
                if (cmd.StartsWith("findgame:scope=")) return FindGameScopedJson(cmd.Substring(15));   // "findgame:scope=" 是 15 个字符
                if (cmd == "findffmpeg") return FindFfmpegJson();
                if (cmd.StartsWith("findffmpeg:scope=")) return FindFfmpegScopedJson(cmd.Substring(17));   // "findffmpeg:scope=" 是 17 个字符
                if (cmd == "rows") return RowsJson();
                if (cmd == "clear") { Ui.OnUi<int>(delegate { ShellWindow sw = win as ShellWindow; if (sw != null) sw.Rows.Clear(); return 0; }); return "{\"ok\":true}"; }
                if (cmd.StartsWith("addrows:")) return AddRows(cmd.Substring(8));
                if (cmd.StartsWith("addlog:")) return AddLog(cmd.Substring(7));
                if (cmd.StartsWith("loglines")) return LogLinesJson(cmd);
                if (cmd.StartsWith("log")) return LogTail(cmd);
                if (cmd.StartsWith("text:")) return Text(cmd.Substring(5));
                if (cmd.StartsWith("find:")) return Find(cmd.Substring(5));
                if (cmd.StartsWith("press:")) return Press(cmd.Substring(6));
                if (cmd.StartsWith("set:")) return Set(cmd.Substring(4));
                if (cmd == "selcount") return SelCount();
                if (cmd == "clearselect") { Ui.OnUi<int>(delegate { ShellWindow w2 = win as ShellWindow; if (w2 != null) w2.ClearSelection(); return 0; }); return "{\"ok\":true}"; }
                if (cmd.StartsWith("selectrange:")) return SelectRows(cmd.Substring(12));
                if (cmd.StartsWith("select:")) return SelectRows(cmd.Substring(7));
                if (cmd.StartsWith("enabled:")) return EnabledOf(cmd.Substring(8));
                if (cmd == "sysmute") return SysMuteRead();
                if (cmd.StartsWith("sysmute:")) return SysMuteWrite(cmd.Substring(8));
                return "{\"error\":\"unknown command\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":" + Json(ex.Message) + "}";
            }
        }

        static string Status()
        {
            return Ui.OnUi<string>(delegate
            {
                ShellWindow sw = win as ShellWindow;
                if (sw == null) return "{\"wpf\":true,\"stage\":\"5-wired\"}";
                Settings c = sw.CfgPublic;
                // recording 从「停止」按钮的可用状态推出来 —— SetRecordingLook() 是唯一改它的地方，
                // 以前这里硬编码 false，脚本按它判「在不在录」会一律误判。
                bool rec = sw.StopEnabledNow;
                return "{\"wpf\":true,\"stage\":\"7-switched\",\"version\":" + Json(AppInfo.Version)
                     + ",\"recording\":" + (rec ? "true" : "false") + ","
                     + "\"queue\":" + sw.Rows.Count + ","
                     + "\"statusText\":" + Json(sw.StatusText == null ? "" : sw.StatusText.Text) + ","
                     + "\"env\":" + Json(sw.StatusEnv == null ? "" : sw.StatusEnv.Text) + ","
                     + "\"panel\":" + (int)Math.Round(sw.PanelWidthNow) + ","
                     + "\"collapsed\":" + (sw.CollapsedNow ? "true" : "false") + ","
                     + "\"parallel\":" + (c == null ? 0 : c.ParallelCount) + ","
                     + "\"mic\":" + Json(c == null ? "" : c.MicMode) + ","
                     // 录制时【真正生效】的录音模式：并行（数量≥2）时它是 gameOnly，
                     // 哪怕上面那个 mic 还存着 gameAndMic（界面会锁住、设置不改）。
                     // 单看 mic 看不出锁有没有生效，所以这里单独报一个。
                     + "\"micEff\":" + Json(c == null ? "" : c.EffectiveMicMode) + ","
                     + "\"win\":" + Json((int)sw.ActualWidth + "x" + (int)sw.ActualHeight) + "}";
            });
        }

        // 「这台机器缺什么」—— 环境自检的机器可读版。
        // 2026-10-08：以前只有 OnCheck 把五项写进日志，验收脚本一个字都读不到，
        // 所以"新用户缺 ffmpeg 时到底会怎样"这件事没法自动测。这条命令就是给它开的门。
        static string EnvJson()
        {
            try
            {
                EnvReport r = EnvReport.Build(Settings.Load());
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"allOk\":").Append(r.AllOk ? "true" : "false").Append(",\"items\":[");
                for (int i = 0; i < r.Items.Count; i++)
                {
                    EnvItem it = r.Items[i];
                    if (i > 0) sb.Append(",");
                    sb.Append("{\"key\":").Append(Json(it.Key))
                      .Append(",\"ok\":").Append(it.Ok ? "true" : "false")
                      .Append(",\"value\":").Append(Json(it.Value))
                      .Append(",\"fix\":").Append(Json(it.Fix))
                      .Append("}");
                }
                sb.Append("]}");
                return sb.ToString();
            }
            catch (Exception ex) { return "{\"error\":" + Json(ex.Message) + "}"; }
        }

        // 「自动找游戏」的机器可读版（2026-10-09）。
        // via 报的是【哪一层命中的】：cfg / near / launcher / scan —— 验证WPF-15 靠它断言
        // 分层回退真的按设计的顺序走，而不是"反正最后能找到就算过"。
        //   near     程序旁边；launcher 整合包的 SokuLauncher.json；scan 后台全盘走一遍
        static string FindGameJson()
        {
            Settings c = Settings.Load();   // 启动时该发生的解析已经发生了
            // Path 要和 WPF 的 System.Windows.Shapes.Path 区分开，这里写全名
            bool cfgValid = !string.IsNullOrEmpty(c.GameDir) && File.Exists(System.IO.Path.Combine(c.GameDir, "th123.exe"));
            string via;
            string dir;
            // 设置里已经有可用的目录时【不解析】—— 解析会顺手起一次后台扫描，
            // 那样这条命令自己就把"没有配置才扫"这件事测没了。
            if (cfgValid) { dir = c.GameDir; via = Settings.LastGameVia; }
            else dir = Settings.ResolveGameDir(out via);
            return GameJson(dir, via, cfgValid);
        }

        // 限定范围同步扫一遍。给脚本用：在开发机上整盘扫必然会撞到仓库里那个真游戏，
        // 分不清找到的是被测目标还是它，所以把扫描算法单独拎出来喂一个小目录。
        static string FindGameScopedJson(string root)
        {
            string dir = Settings.ScanForGameUnder(root);
            return GameJson(dir, dir == null ? "" : "scan", false);
        }

        static string GameJson(string dir, string via, bool cfgValid)
        {
            return "{\"found\":" + (dir == null ? "false" : "true")
                 + ",\"dir\":" + Json(dir)
                 + ",\"via\":" + Json(via)
                 + ",\"cfgValid\":" + (cfgValid ? "true" : "false")
                 + ",\"scanning\":" + (Settings.GameScanRunning ? "true" : "false")
                 + ",\"ms\":" + Settings.GameScanMs + "}";
        }

        // 「自动找 ffmpeg」的机器可读版（2026-10-10）。和 findgame 同一套路子：
        // via 报哪一层命中的（cfg / near / winget / fixed / path / scan）。
        // 只在找游戏时用的 scope 版本这里也有，而且额外报 count —— 走盘是"先全收集再排序"
        // 的，脚本要能断言"它确实看见了那几个候选"，不然"排序对不对"根本无从验起。
        static string FindFfmpegJson()
        {
            Settings c = Settings.Load();
            bool cfgValid = !string.IsNullOrEmpty(c.FfmpegExe) && File.Exists(c.FfmpegExe);
            string path;
            string via;
            // 设置里有可用值就【不解析】—— 解析会顺手起一次后台走盘，
            // 那样这条命令自己就把"没有配置才扫"这件事测没了（同 FindGameJson）。
            if (cfgValid) { path = c.FfmpegExe; via = "cfg"; }
            else { path = Settings.FindFfmpeg(); via = Settings.LastFfmpegVia; }
            return FfmpegJson(path, via, -1);
        }

        static string FindFfmpegScopedJson(string root)
        {
            int n;
            string path = Settings.ScanForFfmpegUnder(root, out n);
            return FfmpegJson(path, path == null ? "" : "scan", n);
        }

        static string FfmpegJson(string path, string via, int count)
        {
            return "{\"found\":" + (path == null ? "false" : "true")
                 + ",\"path\":" + Json(path)
                 + ",\"via\":" + Json(via)
                 + ",\"count\":" + count
                 + ",\"scanning\":" + (Settings.FfmpegScanRunning ? "true" : "false")
                 + ",\"ms\":" + Settings.FfmpegScanMs + "}";
        }

        // 「从注册表里读到了哪些候选」的机器可读版（2026-10-10）。
        // 为什么需要它：FindObsExe 现在 HKLM 和 HKCU 都读，但一台【已经装了真 OBS】的机器上，
        // "最后选中了谁"永远是那个真 OBS —— 光看结果没法知道 HKCU 那半边有没有生效。
        // 所以把这一步的候选单独报出来，脚本才能断言"HKCU 里那条确实被看到了"。
        static string ObsRegJson()
        {
            try
            {
                List<string> cs = Settings.ObsRegistryCandidates();
                StringBuilder sb = new StringBuilder("{\"cands\":[");
                for (int i = 0; i < cs.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(Json(cs[i]));
                }
                return sb.Append("]}").ToString();
            }
            catch (Exception ex) { return "{\"error\":" + Json(ex.Message) + "}"; }
        }

        // 版本比较的机器可读版（给 验证WPF-18）。两个版本号用 | 隔开。
        // 结果直接就是 CompareVersions 的返回值：1 / 0 / -1，无效时是 int.MinValue。
        static string CmpVerJson(string arg)
        {
            int bar = arg.IndexOf('|');
            if (bar < 0) return "{\"error\":\"need a|b\"}";
            int r = UpdateCheck.CompareVersions(arg.Substring(0, bar), arg.Substring(bar + 1));
            return "{\"result\":" + r + "}";
        }

        // 检查更新的机器可读版（给 验证WPF-18）。
        // result = newer / same / older / invalid / off；失败时 error 有话说。
        // 正式地址从 UpdateCheck.ManifestUrl 来（空 = 没配，返回 off）。
        static string UpdateCheckJson(string url)
        {
            string err;
            string r = UpdateCheck.CheckOnce(url, out err);
            return "{\"result\":" + Json(r) + ",\"error\":" + Json(err.Length == 0 ? null : err) + "}";
        }

        // 「把更新包装到某个 exe 上」的机器可读版（给 验证WPF-18）。
        // 格式：updateapply:<zip路径>|<目标exe路径>[|<期望sha256>]
        // ⚠ 这是【生产代码里的测试缝】：它允许指定任意的替换目标，为的是验收脚本能拿
        //   一个临时目录里的假 exe 当靶子 —— 绝不能让它去替换正在运行的那个程序。
        //   只有带 --debug-port 启动时才可达（正式用户不会带这个参数）。
        //   第三段就是 sha 校验不通过那条断言用的。
        static string UpdateApplyJson(string arg)
        {
            string[] a = arg.Split('|');
            if (a.Length < 2) return "{\"error\":\"need zip|target[|sha]\"}";
            UpdateInfo info = new UpdateInfo();
            info.Version = "test";
            info.Urls.Add("file:///" + a[0].Replace('\\', '/'));   // WebRequest 支持 file://
            string err;
            string sha = a.Length > 2 ? a[2] : null;
            // Path 要和 WPF 的 System.Windows.Shapes.Path 区分开，这里写全名
            bool ok = UpdateCheck.DownloadAndApply(info, a[1], System.IO.Path.GetDirectoryName(a[1]), out err, sha);
            return "{\"result\":" + Json(ok ? "ok" : "fail") + ",\"error\":" + Json(err.Length == 0 ? null : err) + "}";
        }

        static string RowsJson()
        {
            return Ui.OnUi<string>(delegate
            {
                ShellWindow sw = win as ShellWindow;
                if (sw == null) return "{\"rows\":[],\"count\":0}";
                StringBuilder sb = new StringBuilder("{\"rows\":[");
                int n = 0;
                foreach (RowVm r in sw.Rows)
                {
                    if (n++ > 0) sb.Append(",");
                    sb.Append("{\"i\":").Append(Json(r.Index))
                      .Append(",\"file\":").Append(Json(r.File))
                      .Append(",\"status\":").Append(Json(r.Status))
                      .Append(",\"dur\":").Append(Json(r.Dur))
                      .Append(",\"size\":").Append(Json(r.Size))
                      .Append(",\"detail\":").Append(Json(r.Detail))
                      .Append(",\"batch\":").Append(r.Batch ? "true" : "false").Append("}");
                }
                sb.Append("],\"count\":").Append(sw.Rows.Count).Append("}");
                return sb.ToString();
            });
        }

        static string AddRows(string arg)
        {
            int n = 1;
            int.TryParse(arg, out n);
            if (n <= 0) n = 1;
            if (n > 5000) n = 5000;
            return Ui.OnUi<string>(delegate
            {
                ShellWindow sw = win as ShellWindow;
                if (sw == null) return "{\"error\":\"no window\"}";
                string[] st = { "等待", "录制中", "完成", "失败", "注意", "已录制" };
                for (int i = 0; i < n; i++)
                {
                    int k = sw.Rows.Count % st.Length;
                    RowVm r = new RowVm();
                    r.Index = (sw.Rows.Count + 1).ToString();
                    r.File = "2026-08-18 21-56-59.rep";
                    r.Status = st[k];
                    r.Dur = k == 2 ? "01:18" : (k == 1 ? "00:42" : "");
                    r.Size = "3.1 MB";
                    r.Detail = "test row " + (sw.Rows.Count + 1);
                    r.StatusColor = Ui.StatusColorOf(st[k]);
                    sw.Rows.Add(r);
                }
                return "{\"ok\":true,\"count\":" + sw.Rows.Count + "}";
            });
        }

        static string AddLog(string arg)
        {
            int n = 1;
            int.TryParse(arg, out n);
            if (n <= 0) n = 1;
            if (n > 5000) n = 5000;
            return Ui.OnUi<string>(delegate
            {
                ShellWindow sw = win as ShellWindow;
                if (sw == null) return "{\"error\":\"no window\"}";
                for (int i = 1; i <= n; i++)
                    sw.AppendLog("16:23:13  test log line " + i + "  "
                                 + "D:/some/very/long/path/without/any/spaces/at/all/that/keeps/going/"
                                 + new string('x', 120 + (i % 7) * 20));
                return "{\"ok\":true,\"shown\":" + sw.LogLines.Count + "}";
            });
        }

        static string LogLinesJson(string cmd)
        {
            int n = 20;
            int c = cmd.IndexOf(':');
            if (c > 0) int.TryParse(cmd.Substring(c + 1), out n);
            if (n <= 0 || n > 2000) n = 20;
            return Ui.OnUi<string>(delegate
            {
                ShellWindow sw = win as ShellWindow;
                if (sw == null) return "{\"lines\":[],\"shown\":0}";
                int total = sw.LogLines.Count;
                int from = Math.Max(0, total - n);
                StringBuilder sb = new StringBuilder("{\"lines\":[");
                for (int i = from; i < total; i++)
                {
                    if (i > from) sb.Append(",");
                    sb.Append(Json(sw.LogLines[i]));
                }
                sb.Append("],\"shown\":").Append(total).Append(",\"collapsed\":").Append(sw.LogCollapsed ? "true" : "false").Append("}");
                return sb.ToString();
            });
        }

        static string LogTail(string cmd)
        {
            int n = 20;
            int colon = cmd.IndexOf(':');
            if (colon > 0) int.TryParse(cmd.Substring(colon + 1), out n);
            if (n <= 0 || n > 2000) n = 20;
            string[] all = new string[0];
            try { if (File.Exists(Log.FilePath)) all = File.ReadAllLines(Log.FilePath); }
            catch { }
            int from = Math.Max(0, all.Length - n);
            StringBuilder sb = new StringBuilder("{\"lines\":[");
            for (int i = from; i < all.Length; i++)
            {
                if (i > from) sb.Append(",");
                sb.Append(Json(all[i]));
            }
            sb.Append("],\"total\":").Append(all.Length).Append("}");
            return sb.ToString();
        }

        // 按钮内容是 Grid（图标 + 文字）时，把里面的文字拼出来 ——
        // 不然 Content.ToString() 只会返回 "System.Windows.Controls.Grid"，
        // 调试接口的 text: 命令（验收脚本在用）就变成一句废话。
        static string TextIn(object content)
        {
            if (content == null) return "";
            TextBlock tb0 = content as TextBlock;
            if (tb0 != null) return tb0.Text;
            DependencyObject d = content as DependencyObject;
            if (d == null) return content.ToString();
            StringBuilder sb = new StringBuilder();
            CollectText(d, sb);
            return sb.Length > 0 ? sb.ToString() : content.ToString();
        }

        static void CollectText(DependencyObject d, StringBuilder sb)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(d, i);
                TextBlock tb = c as TextBlock;
                if (tb != null)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(tb.Text);
                }
                else CollectText(c, sb);
            }
        }

        static string Text(string id)
        {
            return Ui.OnUi<string>(delegate
            {
                FrameworkElement fe = Ui.FindAnywhere(id);
                if (fe == null) return "{\"error\":" + Json("not found: " + id) + "}";
                TextBlock tb = fe as TextBlock;
                if (tb != null) return "{\"text\":" + Json(tb.Text) + "}";
                CheckBox cb = fe as CheckBox;
                if (cb != null) return "{\"text\":" + Json(cb.IsChecked == true ? "true" : "false") + "}";
                ComboBox combo = fe as ComboBox;
                if (combo != null) return "{\"text\":" + Json(combo.SelectedItem == null ? "" : combo.SelectedItem.ToString())
                    + ",\"index\":" + combo.SelectedIndex + "}";
                Button b = fe as Button;
                if (b != null) return "{\"text\":" + Json(TextIn(b.Content)) + "}";
                TextBox box = fe as TextBox;
                if (box != null) return "{\"text\":" + Json(box.Text) + "}";
                Slider sl = fe as Slider;
                if (sl != null) return "{\"text\":" + Json(((int)sl.Value).ToString()) + "}";
                return "{\"error\":" + Json("no text on: " + fe.GetType().Name) + "}";
            });
        }

        static string Find(string id)
        {
            return Ui.OnUi<string>(delegate
            {
                FrameworkElement fe = Ui.FindAnywhere(id);
                if (fe == null) return "{\"found\":false}";
                // vis/enabled 是后加的（纯增量，老脚本只看 found/type 不受影响）：
                // 「位置」那两行会按对齐方式整行藏掉，不报可见性就没法验收。
                return "{\"found\":true,\"type\":" + Json(fe.GetType().Name)
                     + ",\"vis\":\"" + fe.Visibility + "\""
                     + ",\"enabled\":" + (fe.IsEnabled ? "true" : "false") + "}";
            });
        }

        static string Press(string id)
        {
            return Ui.OnUi<string>(delegate
            {
                FrameworkElement fe = Ui.FindAnywhere(id);
                if (fe == null) return "{\"error\":" + Json("not found: " + id) + "}";
                Button b = fe as Button;
                if (b != null)
                {
                    // ⚠ 动作必须异步派发：按钮可能打开【模态】对话框（比如「背景…」），
                    //   ShowDialog 会把 UI 线程的 Invoke 一直挡住不返回 —— 那样这条命令的超时就先到了，
                    //   后面所有命令也会排在它后面一起卡死。查找仍同步（错误照样立即返回）。
                    Ui.PostAsync(delegate { try { b.Focus(); } catch { } b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
                    return "{\"ok\":true}";
                }
                CheckBox cb2 = fe as CheckBox;
                if (cb2 != null)
                {
                    // ⚠ 光改 IsChecked 不等于「用户点了一下」：本程序所有勾选框都是挂在 Click 上
                    //   （保存设置、改 ini 那些），程序化改 IsChecked 根本不触发 Click ——
                    //   以前这条命令就是这样，勾选框看着翻了、设置却没落盘。翻完再把 Click 补上。
                    Ui.PostAsync(delegate
                    {
                        cb2.IsChecked = !(cb2.IsChecked == true);
                        cb2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    });
                    return "{\"ok\":true}";
                }
                return "{\"error\":" + Json("not pressable: " + fe.GetType().Name) + "}";
            });
        }

        static string Set(string kv)
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) return "{\"error\":\"need id=value\"}";
            string id = kv.Substring(0, eq);
            string val = kv.Substring(eq + 1);
            return Ui.OnUi<string>(delegate
            {
                FrameworkElement fe = Ui.FindAnywhere(id);
                if (fe == null) return "{\"error\":" + Json("not found: " + id) + "}";
                CheckBox cb = fe as CheckBox;
                if (cb != null)
                {
                    // 同 press：设完值要补一次 Click，否则挂在 Click 上的保存逻辑不跑
                    Ui.PostAsync(delegate
                    {
                        cb.IsChecked = (val == "true" || val == "1");
                        cb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    });
                    return "{\"ok\":true}";
                }
                ComboBox combo = fe as ComboBox;
                if (combo != null) { Ui.PostAsync(delegate { int i; if (int.TryParse(val, out i)) combo.SelectedIndex = i; }); return "{\"ok\":true}"; }
                TextBox tb = fe as TextBox;
                if (tb != null) { Ui.PostAsync(delegate { tb.Text = val; }); return "{\"ok\":true}"; }
                Slider sl = fe as Slider;
                if (sl != null) { Ui.PostAsync(delegate { double d; if (double.TryParse(val, out d)) sl.Value = d; }); return "{\"ok\":true}"; }
                return "{\"error\":" + Json("cannot set: " + fe.GetType().Name) + "}";
            });
        }

        // ---------------------------------------------------------------- 队列选中
        // 旧脚本用 LVM_GETSELECTEDCOUNT(0x1032) / LVM_SETITEMSTATE 跨进程读写列表选中，
        // WPF 没有 ListView 句柄，这几条命令就是那两个消息的替代品。
        // 「标记本批」「选择保持与滚轮」「轮录从队首」「录制选中的」全靠它。
        static string SelCount()
        {
            return Ui.OnUi<string>(delegate
            {
                ShellWindow sw = win as ShellWindow;
                int n = (sw == null || sw.QueueList == null) ? 0 : sw.QueueList.SelectedItems.Count;
                return "{\"sel\":" + n + ",\"batch\":" + Ui.ParallelCount + "}";
            });
        }

        // spec 用界面上那个行号（1 起，跟列表左列显示的一致）：
        //   select:3        选中第 3 行
        //   selectrange:3-7 选中第 3~7 行
        //   select:-1       等同 clearselect
        static string SelectRows(string spec)
        {
            ShellWindow sw = win as ShellWindow;
            if (sw == null || sw.QueueList == null) return "{\"error\":\"no window\"}";
            int a, b;
            int dash = spec.IndexOf('-');
            if (dash > 0)
            {
                if (!int.TryParse(spec.Substring(0, dash), out a) ||
                    !int.TryParse(spec.Substring(dash + 1), out b)) return "{\"error\":\"bad range: " + spec + "\"}";
            }
            else
            {
                if (!int.TryParse(spec, out a)) return "{\"error\":\"bad index: " + spec + "\"}";
                b = a;
            }
            if (a < 0) { Ui.PostAsync(delegate { sw.ClearSelection(); }); return "{\"ok\":true,\"cleared\":true}"; }
            int lo = a - 1, hi = b - 1;
            Ui.PostAsync(delegate
            {
                ListBox lb = sw.QueueList;
                for (int i = lo; i <= hi; i++)
                {
                    if (i < 0 || i >= lb.Items.Count) continue;
                    if (!lb.SelectedItems.Contains(lb.Items[i])) lb.SelectedItems.Add(lb.Items[i]);
                }
            });
            return "{\"ok\":true}";
        }

        static string EnabledOf(string id)
        {
            return Ui.OnUi<string>(delegate
            {
                FrameworkElement fe = Ui.FindAnywhere(id);
                if (fe == null) return "{\"error\":" + Json("not found: " + id) + "}";
                return "{\"enabled\":" + (fe.IsEnabled ? "true" : "false") + "}";
            });
        }

        // 系统静音：旧脚本用反射调 exe 里的 GameAudio.GetSystemMute/SetSystemMute，
        // 现在直接在进程里读/写端点静音（就是「录制时自动静音」用的那套）。
        static string SysMuteRead()
        {
            try
            {
                bool muted; string msg;
                GameAudio.GetSystemMute(out muted, out msg);
                return "{\"muted\":" + (muted ? "true" : "false") + ",\"msg\":" + Json(msg) + "}";
            }
            catch (Exception ex) { return "{\"error\":" + Json(ex.Message) + "}"; }
        }

        static string SysMuteWrite(string v)
        {
            try
            {
                string msg;
                bool on = (v == "on" || v == "true" || v == "1");
                GameAudio.SetSystemMute(on, out msg);
                bool now; string msg2;
                GameAudio.GetSystemMute(out now, out msg2);
                return "{\"ok\":true,\"muted\":" + (now ? "true" : "false") + ",\"msg\":" + Json(msg) + "}";
            }
            catch (Exception ex) { return "{\"error\":" + Json(ex.Message) + "}"; }
        }

        // 按 AutomationId 在可视树里找（Grid/Border 这类没有 automation peer 的元素，
        // 挂在它们上面的 id 用 UIA 找不到，但这里能找到 —— 所以脚本用 debug-port 更皮实）
        public static FrameworkElement FindIn(DependencyObject root, string id)
        {
            if (root == null) return null;
            FrameworkElement fe = root as FrameworkElement;
            if (fe != null && AutomationProperties.GetAutomationId(fe) == id) return fe;
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(root); } catch { return null; }
            for (int i = 0; i < n; i++)
            {
                FrameworkElement r = FindIn(VisualTreeHelper.GetChild(root, i), id);
                if (r != null) return r;
            }
            return null;
        }

        public static string Encode(string s) { return Json(s); }

        public static string Json(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
