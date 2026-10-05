// ============================================================
//  UiDialogs.cs —— 三个对话框：视频放哪 / 设置 / 背景设置
//  （2026-10-03 误删后重建；「视频放哪」的对外语义见 Recorder.AskSaveLocation* 的注释）
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RepRecorder
{
    // ================================================================ 询问框（视频放哪）
    //  Choice 0 = 第一个（留在 / 和 rep 放在一起）  1 = 另存到文件夹…  2 = 第三个（以后不再询问 / 取消）
    class AskDialog : Window
    {
        public int Choice = -1;
        public bool Checked = false;
        public string Picked = null;
        readonly string last;

        public AskDialog(bool upfront, string info, string lastFolder, string checkText)
        {
            last = lastFolder == null ? "" : lastFolder;
            Title = upfront ? "开始录制 —— 视频要放在哪里？" : "录制完成 —— 视频要放在哪里？";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 470;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Br(MainForm.HudBg);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Ui.Id(this, "askDialog");
            Ui.MakeDraggable(this);         // 无边框窗口，要手动让它能拖

            StackPanel col = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            TextBlock tb = Ui.Txt(info, 12.5, MainForm.HudText, false);
            tb.TextWrapping = TextWrapping.Wrap;
            col.Children.Add(tb);

            if (!string.IsNullOrEmpty(checkText))
            {
                CheckBox chk = Ui.HudCheck(checkText, "askRemember", false, 10);
                chk.Checked += delegate { Checked = chk.IsChecked == true; };
                chk.Unchecked += delegate { Checked = chk.IsChecked == true; };
                col.Children.Add(chk);
            }

            string[] labels = upfront
                ? new string[] { "和 rep 放在一起", "另存到文件夹…", "取消（先不录）" }
                : new string[] { "留在 rep 旁边", "另存到文件夹…", "以后不再询问" };
            string[] ids = new string[] { "askKeep", "askPick", "askCancel" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                col.Children.Add(DlgButton(labels[i], ids[i], delegate { Choose(idx); }));
            }
            Content = col;
        }

        Button DlgButton(string text, string id, Action onClick)
        {
            Button b = Ui.FlatButton(text, id);
            b.Height = 28;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Margin = new Thickness(0, 6, 0, 0);
            b.Click += delegate { onClick(); };
            return b;
        }

        void Choose(int idx)
        {
            if (idx == 2) { Choice = 2; Close(); return; }
            if (idx == 1)
            {
                try
                {
                    System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog();
                    d.Description = "视频保存到哪个文件夹？";
                    if (!string.IsNullOrEmpty(last) && Directory.Exists(last)) d.SelectedPath = last;
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    Picked = d.SelectedPath;
                    Choice = 1;
                    Close();
                    return;
                }
                catch (Exception ex) { RepRecorder.Log.Write("选文件夹失败：" + ex.Message); return; }
            }
            Choice = 0;
            Close();
        }
    }

    // ================================================================ 设置窗
    //  旧版是个 PropertyGrid，WPF 版一开始是"反射列出所有公开属性 + 一个文本框"。
    //  2026-10-05 用户要求重做，现在：
    //   · 全中文标签；**只列用户真该改的项**（面板宽度、背景图那些有自己的入口，不再堆在这里）
    //   · 取值固定的都用下拉（同时录制数量、录音模式、编码器、帧率…），是非题用勾选框
    //   · 路径自动填好（Settings.Load → FillDefaults 会探测游戏目录 / OBS / 端口 / 密码），
    //     旁边给「浏览」按钮；顶部还有一个「重新自动检测」
    //   · 密码用密码框 —— 原来明文印在输入框里
    class SettingsBox : Window
    {
        public bool Ok = false;
        readonly Settings cfg;

        class Field
        {
            public System.Reflection.PropertyInfo Pi;
            public Func<string> Read;      // 统一给字符串，确定时按属性类型转换
            public TextBox Box;            // 路径类才有（「浏览」和「自动检测」要改它）
        }
        readonly List<Field> fields = new List<Field>();

        System.Reflection.PropertyInfo Prop(string name)
        {
            return typeof(Settings).GetProperty(name);
        }

        void Add(Field f) { if (f.Pi != null) fields.Add(f); }

        Grid MakeRow(string label, double top)
        {
            Grid g = new Grid { Margin = new Thickness(0, top, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock lb = Ui.Txt(label, 12.5, MainForm.HudText, false);
            lb.VerticalAlignment = VerticalAlignment.Center;
            lb.TextWrapping = TextWrapping.Wrap;
            lb.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(lb, 0);
            g.Children.Add(lb);
            return g;
        }

        void Hint(Grid g, string text)
        {
            TextBlock h = Ui.Txt(text, 11, MainForm.HudDim, false);
            h.VerticalAlignment = VerticalAlignment.Center;
            h.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(h, 2);
            g.Children.Add(h);
        }

        void Section(StackPanel col, string title)
        {
            TextBlock t = Ui.Txt(title, 12, MainForm.HudAccent, true);
            t.Margin = new Thickness(0, 14, 0, 2);
            col.Children.Add(t);
            Rectangle r = new Rectangle { Height = 1, Fill = Ui.Br(MainForm.HudLine), HorizontalAlignment = HorizontalAlignment.Stretch };
            r.Margin = new Thickness(0, 2, 0, 2);
            col.Children.Add(r);
        }

        void ToggleRow(StackPanel col, string label, string name, string hint)
        {
            CheckBox cb = Ui.HudCheck("", "set_" + name, cfg != null && Convert.ToBoolean(Prop(name).GetValue(cfg, null)), 0);
            Field f = new Field();
            f.Pi = Prop(name);
            f.Read = delegate { return cb.IsChecked == true ? "true" : "false"; };
            Add(f);
            Grid g = MakeRow(label, 5);
            Grid.SetColumn(cb, 1);
            cb.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(cb);
            if (!string.IsNullOrEmpty(hint)) Hint(g, hint);
            col.Children.Add(g);
        }

        void ChoiceRow(StackPanel col, string label, string name, string[] displays, string[] values)
        {
            string cur = cfg == null ? "" : Convert.ToString(Prop(name).GetValue(cfg, null));
            int idx = Array.IndexOf(values, cur);
            if (idx < 0) idx = 0;
            ComboBox cb = Ui.HudCombo("set_" + name, 210, displays, idx);
            cb.HorizontalAlignment = HorizontalAlignment.Left;
            Field f = new Field();
            f.Pi = Prop(name);
            f.Read = delegate { return values[cb.SelectedIndex < 0 ? 0 : cb.SelectedIndex]; };
            Add(f);
            Grid g = MakeRow(label, 5);
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            col.Children.Add(g);
        }

        void NumberRow(StackPanel col, string label, string name, string hint)
        {
            TextBox tb = new TextBox();
            tb.Width = 120; tb.Height = 24;
            tb.FontFamily = new FontFamily("Consolas"); tb.FontSize = 12;
            tb.Background = Ui.Br(MainForm.HudPanel2);
            tb.Foreground = Ui.Br(MainForm.HudText);
            tb.BorderBrush = Ui.Br(MainForm.HudLine);
            tb.Text = cfg == null ? "" : Convert.ToString(Prop(name).GetValue(cfg, null));
            Ui.Id(tb, "set_" + name);
            Field f = new Field();
            f.Pi = Prop(name);
            f.Read = delegate { return tb.Text; };
            Add(f);
            Grid g = MakeRow(label, 5);
            Grid.SetColumn(tb, 1);
            tb.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(tb);
            if (!string.IsNullOrEmpty(hint)) Hint(g, hint);
            col.Children.Add(g);
        }

        void PasswordRow(StackPanel col, string label, string name, string hint)
        {
            PasswordBox pb = new PasswordBox();
            pb.Width = 240; pb.Height = 24;
            pb.FontFamily = new FontFamily("Consolas"); pb.FontSize = 12;
            pb.Background = Ui.Br(MainForm.HudPanel2);
            pb.Foreground = Ui.Br(MainForm.HudText);
            pb.BorderBrush = Ui.Br(MainForm.HudLine);
            pb.Password = cfg == null ? "" : Convert.ToString(Prop(name).GetValue(cfg, null));
            Ui.Id(pb, "set_" + name);
            Field f = new Field();
            f.Pi = Prop(name);
            f.Read = delegate { return pb.Password; };
            Add(f);
            Grid g = MakeRow(label, 5);
            Grid.SetColumn(pb, 1);
            pb.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(pb);
            if (!string.IsNullOrEmpty(hint)) Hint(g, hint);
            col.Children.Add(g);
        }

        void PathRow(StackPanel col, string label, string name, bool folder, string hint)
        {
            TextBox tb = new TextBox();
            tb.Height = 24;
            tb.FontFamily = new FontFamily("Consolas"); tb.FontSize = 11.5;
            tb.Background = Ui.Br(MainForm.HudPanel2);
            tb.Foreground = Ui.Br(MainForm.HudText);
            tb.BorderBrush = Ui.Br(MainForm.HudLine);
            tb.Text = cfg == null ? "" : Convert.ToString(Prop(name).GetValue(cfg, null));
            Ui.Id(tb, "set_" + name);

            Field f = new Field();
            f.Pi = Prop(name);
            f.Box = tb;
            f.Read = delegate { return tb.Text; };
            Add(f);

            Grid g = MakeRow(label, 5);
            // 用 Grid 而不是横向 StackPanel：路径一般都很长，要让输入框把剩余宽度吃掉，
            // 不然 300 宽一截根本看不全（用户 2026-10-05 截图里游戏目录就是这么被截断的）。
            Grid right = new Grid();
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tb, 0);
            right.Children.Add(tb);
            Button br = Ui.FlatButton("浏览…", "setBrowse_" + name);
            br.Width = 62; br.Height = 24; br.Margin = new Thickness(6, 0, 0, 0);
            br.Click += delegate
            {
                // 认不出来就让用户在资源管理器里自己指一个（不去猜、不去改别人的磁盘）
                try
                {
                    if (folder)
                    {
                        System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog();
                        d.Description = label;
                        if (!string.IsNullOrEmpty(tb.Text) && Directory.Exists(tb.Text)) d.SelectedPath = tb.Text;
                        if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) tb.Text = d.SelectedPath;
                    }
                    else
                    {
                        System.Windows.Forms.OpenFileDialog d = new System.Windows.Forms.OpenFileDialog();
                        d.Filter = "程序|*.exe|所有文件|*.*";
                        if (!string.IsNullOrEmpty(tb.Text) && File.Exists(tb.Text))
                        {
                            d.InitialDirectory = System.IO.Path.GetDirectoryName(tb.Text);
                            d.FileName = System.IO.Path.GetFileName(tb.Text);
                        }
                        if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) tb.Text = d.FileName;
                    }
                }
                catch (Exception ex) { RepRecorder.Log.Write("选路径失败：" + ex.Message); }
            };
            right.Children.Add(br);
            Grid.SetColumn(br, 1);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            if (!string.IsNullOrEmpty(hint)) Hint(g, hint);
            col.Children.Add(g);
        }

        public SettingsBox(Settings c)
        {
            cfg = c;
            Title = "设置";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Width = 640; Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Br(MainForm.HudBg);
            Ui.Id(this, "settingsDialog");
            Ui.MakeDraggable(this);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            // 对话框是独立窗口，不会继承主窗口的 Resources —— 不显式给这一句，
            // 这里露出的是系统原生那条浅灰滚动条（2026-10-05 用户报的"非常不好看"）。
            Resources.Add(typeof(ScrollBar), Ui.HudScrollBar(8));

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 14, 18, 4) };
            TextBlock title = Ui.Txt("设置", 13.5, MainForm.HudText, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            Button auto = Ui.FlatButton("重新自动检测", "setAuto");
            auto.Width = 108; auto.Height = 24; auto.Margin = new Thickness(16, 0, 0, 0);
            auto.Click += delegate
            {
                cfg.FillDefaults();
                foreach (Field f in fields)
                    if (f.Box != null) f.Box.Text = Convert.ToString(f.Pi.GetValue(cfg, null));
                RepRecorder.Log.Write("设置窗：已重新自动检测游戏目录 / OBS / 端口 / 密码");
            };
            head.Children.Add(auto);
            root.Children.Add(head);

            StackPanel col = new StackPanel { Margin = new Thickness(18, 0, 12, 8) };

            Section(col, "OBS 与游戏");
            PathRow(col, "OBS 程序", "ObsExe", false, "");
            PathRow(col, "游戏目录", "GameDir", true, "（里面要有 th123.exe）");
            NumberRow(col, "obs-websocket 端口", "Port", "");
            PasswordRow(col, "obs-websocket 密码", "Password", "（自动从 OBS 里读）");

            Section(col, "录制");
            ChoiceRow(col, "同时录制数量", "ParallelCount",
                      new string[] { "1", "2", "3", "4", "5", "6" },
                      new string[] { "1", "2", "3", "4", "5", "6" });
            ToggleRow(col, "轮录（录完一批自动接着录下一批）", "RoundRecord", "");
            ChoiceRow(col, "录音模式", "MicMode",
                      new string[] { "只录游戏声音", "游戏 + 麦克风" },
                      new string[] { "gameOnly", "gameAndMic" });
            ToggleRow(col, "录打击音效（视频里带游戏声音）", "RecordGameAudio", "关了 = 视频完全无声");
            ToggleRow(col, "录 BGM（背景音乐）", "RecordBgm", "只在「录打击音效」开着时可用");
            ToggleRow(col, "录制时静音系统里其他声音", "GameAudioOnly", "");
            NumberRow(col, "录像音量", "RecordVolume", "（1.0 = 原样）");
            NumberRow(col, "单个 rep 最长录制（分钟）", "MaxMinutes", "");
            NumberRow(col, "开录前等待（秒）", "PreRollSeconds", "");
            NumberRow(col, "录完再多录（秒）", "PostRollSeconds", "");
            ToggleRow(col, "旁边已有录像就跳过", "SkipExisting", "");
            ToggleRow(col, "录完关闭 OBS", "CloseObsAfter", "");
            PathRow(col, "视频默认保存位置", "LastSaveFolder", true, "（留空 = 和 rep 放一起）");

            Section(col, "并行与画面");
            ToggleRow(col, "自动准备采集源并绑定游戏窗口", "AutoPrepareObs", "并行录制必须开");
            ToggleRow(col, "用专用场景", "UseDedicatedScene", "");
            ToggleRow(col, "防止游戏窗口被最小化", "KeepGameCapturable", "最小化那一格会录成黑屏");
            ToggleRow(col, "单录自动设画布 1440×1080@60", "AutoCanvas", "");
            ChoiceRow(col, "单录画布宽", "CanvasWidth",
                      new string[] { "1280", "1440", "1920", "2560" },
                      new string[] { "1280", "1440", "1920", "2560" });
            ChoiceRow(col, "单录画布高", "CanvasHeight",
                      new string[] { "720", "1080", "1440" },
                      new string[] { "720", "1080", "1440" });
            ChoiceRow(col, "单录帧率", "CanvasFps",
                      new string[] { "30", "60" },
                      new string[] { "30", "60" });
            NumberRow(col, "单录裁边（像素）", "SingleEdgeInset", "");
            NumberRow(col, "并行裁边（像素）", "ParallelEdgeInset", "");

            Section(col, "切分");
            ChoiceRow(col, "同时跑几个 ffmpeg", "SplitParallel",
                      new string[] { "1", "2", "3", "4", "5", "6" },
                      new string[] { "1", "2", "3", "4", "5", "6" });
            ChoiceRow(col, "切分编码器", "ParallelSplitEncoder",
                      new string[] { "NVIDIA 显卡（快）", "CPU 软编（兼容）" },
                      new string[] { "h264_nvenc", "libx264" });
            NumberRow(col, "切分质量 CQ", "ParallelSplitCQ", "（越小越清晰、文件越大，23 是常用值）");

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Content = col;
            Ui.PinScrollBarWidth(sv, 8);
            Grid.SetRow(sv, 1);
            root.Children.Add(sv);

            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 18, 0) };
            Button ok = Ui.FlatButton("确定", "setOk");
            ok.Width = 84; ok.Height = 28; ok.Margin = new Thickness(0, 0, 8, 0);
            ok.Click += delegate
            {
                foreach (Field f in fields)
                {
                    string t = f.Read();
                    try
                    {
                        if (f.Pi.PropertyType == typeof(string)) f.Pi.SetValue(cfg, t, null);
                        else if (f.Pi.PropertyType == typeof(int)) { int n; if (int.TryParse(t, out n)) f.Pi.SetValue(cfg, n, null); }
                        else if (f.Pi.PropertyType == typeof(double)) { double d; if (double.TryParse(t, out d)) f.Pi.SetValue(cfg, d, null); }
                        else if (f.Pi.PropertyType == typeof(bool)) f.Pi.SetValue(cfg, t == "true", null);
                    }
                    catch { }
                }
                Ok = true;
                Close();
            };
            bar.Children.Add(ok);
            Button cancel = Ui.FlatButton("取消", "setCancel");
            cancel.Width = 84; cancel.Height = 28;
            cancel.Click += delegate { Close(); };
            bar.Children.Add(cancel);
            Grid.SetRow(bar, 2);
            root.Children.Add(bar);

            Content = root;
        }
    }

    // ================================================================ 背景设置窗
    //  旧版那个「背景设置」对话框的 WPF 版：改一下实时预览一下，取消时把值原样还回去。
    class BackgroundBox : Window
    {
        readonly ShellWindow owner;
        readonly Settings cfg;
        readonly string oImg, oLay, oScope;
        readonly int oOp, oBlur, oOx, oOy, oScrim;
        TextBox txtPath;

        public BackgroundBox(ShellWindow w)
        {
            owner = w; cfg = w.CfgPublic;
            oImg = cfg.BgImage; oLay = cfg.BgLayout; oScope = cfg.BgScope;
            oOp = cfg.BgOpacity; oBlur = cfg.BgBlur; oOx = cfg.BgOffsetX; oOy = cfg.BgOffsetY;
            oScrim = cfg.PanelScrim;

            Title = "背景设置";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 470;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Br(MainForm.HudBg);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Ui.Id(this, "bgDialog");
            Ui.MakeDraggable(this);         // 无边框窗口，要手动让它能拖

            StackPanel col = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

            StackPanel r0 = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock lp = Ui.Txt("背景图片：", 12.5, MainForm.HudText, false);
            lp.Width = 96; lp.VerticalAlignment = VerticalAlignment.Center;
            txtPath = new TextBox();
            txtPath.Width = 240; txtPath.Height = 24;
            txtPath.FontFamily = new FontFamily("Consolas"); txtPath.FontSize = 11.5;
            txtPath.Background = Ui.Br(MainForm.HudPanel2);
            txtPath.Foreground = Ui.Br(MainForm.HudText);
            txtPath.BorderBrush = Ui.Br(MainForm.HudLine);
            txtPath.Text = cfg.BgImage == null ? "" : cfg.BgImage;
            txtPath.TextChanged += delegate { cfg.BgImage = txtPath.Text; owner.ApplyBackground(); };
            Ui.Id(txtPath, "bgImage");
            r0.Children.Add(lp); r0.Children.Add(txtPath);
            Button bBrowse = Ui.FlatButton("浏览…", "bgBrowse");
            bBrowse.Width = 56; bBrowse.Height = 24; bBrowse.Margin = new Thickness(6, 0, 0, 0);
            bBrowse.Click += delegate
            {
                try
                {
                    System.Windows.Forms.OpenFileDialog d = new System.Windows.Forms.OpenFileDialog();
                    d.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        txtPath.Text = d.FileName;
                        cfg.BgImage = d.FileName;
                        owner.ApplyBackground();
                    }
                }
                catch (Exception ex) { RepRecorder.Log.Write("选图片失败：" + ex.Message); }
            };
            r0.Children.Add(bBrowse);
            Button bClear = Ui.FlatButton("清除", "bgClear");
            bClear.Width = 48; bClear.Height = 24; bClear.Margin = new Thickness(6, 0, 0, 0);
            bClear.Click += delegate
            {
                txtPath.Text = "";
                cfg.BgImage = "";
                owner.ApplyBackground();
            };
            r0.Children.Add(bClear);
            col.Children.Add(r0);

            // 滑块显示的是「透明度」（0 = 图片最清晰、100 = 图片完全看不见），
            // 存盘的 BgOpacity 仍然是不透明度——只在滑块这一层翻转，老设置和调试接口都不受影响。
            col.Children.Add(SliderRow("图片透明度", "bgOpacity", 0, 100, 100 - cfg.BgOpacity, "%", delegate(double v) { cfg.BgOpacity = (int)(100 - v); }));
            // 面板底色强度：0 = 面板全透明、图片零遮挡；越大文字越好读。
            // 走共享画刷，改完所有面板立刻跟着变，不用重建界面。
            col.Children.Add(SliderRow("面板暗化", "panelScrim", 0, 100, cfg.PanelScrim, "%", delegate(double v) { cfg.PanelScrim = (int)v; Ui.SetScrim(cfg.PanelScrim); }));
            col.Children.Add(SliderRow("模糊", "bgBlur", 0, 60, cfg.BgBlur, "", delegate(double v) { cfg.BgBlur = (int)v; }));
            col.Children.Add(SliderRow("水平移动 X", "bgOffX", -100, 100, cfg.BgOffsetX, "%", delegate(double v) { cfg.BgOffsetX = (int)v; }));
            col.Children.Add(SliderRow("垂直移动 Y", "bgOffY", -100, 100, cfg.BgOffsetY, "%", delegate(double v) { cfg.BgOffsetY = (int)v; }));

            ComboBox cbLayout = Ui.HudCombo("bgLayout", 250, new string[] { "fill（铺满，可裁边）", "fit（完整放下）", "stretch（拉伸变形）", "center（原尺寸居中）", "tile（平铺）" }, LayoutIndex(cfg.BgLayout));
            cbLayout.SelectionChanged += delegate { cfg.BgLayout = LayoutValue(cbLayout.SelectedIndex); owner.ApplyBackground(); };
            col.Children.Add(ComboRow("放置方式：", cbLayout));
            ComboBox cbScope = Ui.HudCombo("bgScope", 250, new string[] { "只铺右侧内容区", "全局铺满整个窗口（控制面板也透图）" }, cfg.BgScope == "global" ? 1 : 0);
            cbScope.SelectionChanged += delegate { cfg.BgScope = cbScope.SelectedIndex == 1 ? "global" : "content"; owner.ApplyBackground(); };
            col.Children.Add(ComboRow("背景范围：", cbScope));

            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button ok = Ui.FlatButton("确定", "bgOk");
            ok.Width = 84; ok.Height = 28; ok.Margin = new Thickness(0, 0, 8, 0);
            ok.Click += delegate
            {
                cfg.Save();
                Log.Write("背景设置：图片=" + (string.IsNullOrEmpty(cfg.BgImage) ? "<无>" : cfg.BgImage)
                    + "  透明度=" + (100 - cfg.BgOpacity) + "%  面板暗化=" + cfg.PanelScrim + "%  模糊=" + cfg.BgBlur
                    + "  放置=" + cfg.BgLayout + "  位移=" + cfg.BgOffsetX + "," + cfg.BgOffsetY
                    + "  范围=" + (cfg.BgScope == "global" ? "全局" : "内容区"));
                Close();
            };
            bar.Children.Add(ok);
            Button cancel = Ui.FlatButton("取消", "bgCancel");
            cancel.Width = 84; cancel.Height = 28;
            cancel.Click += delegate
            {
                cfg.BgImage = oImg; cfg.BgLayout = oLay; cfg.BgScope = oScope;
                cfg.BgOpacity = oOp; cfg.BgBlur = oBlur; cfg.BgOffsetX = oOx; cfg.BgOffsetY = oOy;
                cfg.PanelScrim = oScrim; Ui.SetScrim(cfg.PanelScrim);
                owner.ApplyBackground();
                Close();
            };
            bar.Children.Add(cancel);
            col.Children.Add(bar);

            Content = col;
        }

        static int LayoutIndex(string v)
        {
            if (v == "fit") return 1;
            if (v == "stretch") return 2;
            if (v == "center") return 3;
            if (v == "tile") return 4;
            return 0;
        }

        static string LayoutValue(int i)
        {
            if (i == 1) return "fit";
            if (i == 2) return "stretch";
            if (i == 3) return "center";
            if (i == 4) return "tile";
            return "fill";
        }

        UIElement SliderRow(string label, string id, double min, double max, double val, string suffix, Action<double> onChange)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            TextBlock lb = Ui.Txt(label, 12.5, MainForm.HudText, false);
            lb.Width = 96; lb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lb);
            Slider sl = Ui.HudSlider(min, max, val, id);
            TextBlock num = Ui.Txt(((int)val).ToString() + suffix, 12, MainForm.HudText, false);
            num.Width = 44; num.Margin = new Thickness(8, 0, 0, 0);
            num.VerticalAlignment = VerticalAlignment.Center;
            sl.ValueChanged += delegate
            {
                onChange(sl.Value);
                num.Text = ((int)sl.Value).ToString() + suffix;
                owner.ApplyBackground();
            };
            row.Children.Add(sl);
            row.Children.Add(num);
            return row;
        }

        UIElement ComboRow(string label, UIElement combo)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            TextBlock lb = Ui.Txt(label, 12.5, MainForm.HudText, false);
            lb.Width = 96; lb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lb);
            row.Children.Add(combo);
            return row;
        }
    }
}
