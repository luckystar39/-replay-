// ============================================================
//  UiWindow.cs —— 窗口壳：顶栏 / 左栏五张卡片 / 队列 / 终端 / 底栏 / 背景图层
//  （2026-10-03 一次误删之后重建；行为规格见 验证WPF-00..06，重建后必须全绿）
// ============================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace RepRecorder
{
    class ShellWindow : Window
    {
        readonly Settings cfg;
        readonly Recorder rec;

        // 左栏 / 顶栏 / 底栏
        ColumnDefinition colBar, colLeft, colSplit;
        StackPanel leftCol;
        Border activityBar;
        GridSplitter splitterV;
        Button btnStart, btnStop, btnClear, btnRemove, btnMark, btnRefresh, btnPrepare, btnCheck, btnSet, btnOpenLog;
        Button btnSound, btnGameAudio, btnBgm, btnPanel, btnBg;
        CheckBox chkAuto, chkPrepare, chkCloseObs, chkRound;
        ComboBox cbParallel, cbMic;
        TextBlock lblMemHint, lblParHint, hintText, statusText, statusEnv;
        bool collapsed = false;

        // 队列
        ListBox queueList;
        Canvas marqueeLayer;
        Rectangle marqueeRect;
        Point marqueeFrom;
        bool marqueeArmed, marqueeOn, marqueeAdd;
        readonly ObservableCollection<RowVm> rows = new ObservableCollection<RowVm>();
        public ObservableCollection<RowVm> Rows { get { return rows; } }
        public ListBox QueueList { get { return queueList; } }

        // 终端
        ListBox logList;
        TextBlock logHeaderArrow, logHeaderTail;
        bool logCollapsed = false;
        string lastLogLine = "";
        readonly List<string> logRaw = new List<string>();
        readonly ObservableCollection<string> logLines = new ObservableCollection<string>();
        readonly Dictionary<char, int> logCharW = new Dictionary<char, int>();
        public ObservableCollection<string> LogLines { get { return logLines; } }
        public bool LogCollapsed { get { return logCollapsed; } }

        // 背景图层
        Rectangle bgLayer;
        BlurEffect bgBlurFx;
        string bgLoadedPath = null;

        System.Windows.Threading.DispatcherTimer soundTimer;

        public double PanelWidthNow { get { return colLeft == null ? 0 : colLeft.Width.Value; } }
        public bool CollapsedNow { get { return collapsed; } }
        public TextBlock StatusText { get { return statusText; } }
        public TextBlock StatusEnv { get { return statusEnv; } }
        public TextBlock HintText { get { return hintText; } }
        public TextBlock ParHint { get { return lblParHint; } }
        public ComboBox ComboParallel { get { return cbParallel; } }
        public Settings CfgPublic { get { return cfg; } }

        public ShellWindow(Settings settings, Recorder recorder)
        {
            cfg = settings;
            rec = recorder;
            Title = "Rep → MP4 录制器（OBS）";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Width = 1180; Height = 648;                 // DIP：和旧版的逻辑像素一样大
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Br(MainForm.HudBg);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            Ui.Id(this, "win");

            // 全局滚动条样式（列表和终端都吃这条）
            Resources.Add(typeof(ScrollBar), (Style)XamlReader.Parse(HudScrollBarXaml));

            // 无边框窗口的拖动/缩放/双击最大化/贴边分屏，WPF 自带
            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 30;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            Content = BuildRoot();
            if (cfg != null && cfg.PanelCollapsed) SetPanelCollapsed(true);
            SizeChanged += delegate { ApplyBackground(); };
            SourceInitialized += delegate
            {
                HwndSource src = (HwndSource)PresentationSource.FromVisual(this);
                if (src != null) src.AddHook(Hook);
            };
        }

        // ---------------------------------------------------------------- 骨架
        UIElement BuildRoot()
        {
            Grid root = new Grid();
            root.Children.Add(BuildBackgroundLayer());

            Grid body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            UIElement top = BuildTopBar();
            UIElement mid = BuildMiddle();
            UIElement st = BuildStatus();
            Grid.SetRow(mid, 1);
            Grid.SetRow(st, 2);
            body.Children.Add(top);
            body.Children.Add(mid);
            body.Children.Add(st);
            root.Children.Add(body);
            return root;
        }

        UIElement BuildTopBar()
        {
            Grid bar = new Grid();
            bar.Background = Ui.Br(MainForm.HudPanel, 0xE6);
            Ui.Id(bar, "topBar");
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(Ui.Txt("REP → MP4  录制器   (OBS)", 13, MainForm.HudText, true));
            btnPanel = Ui.FlatButton("◀  收起面板", "btnPanel");
            btnPanel.Width = 112; btnPanel.Height = 24; btnPanel.Margin = new Thickness(8, 0, 0, 0);
            btnPanel.Click += delegate { SetPanelCollapsed(!collapsed); };
            WindowChrome.SetIsHitTestVisibleInChrome(btnPanel, true);
            left.Children.Add(btnPanel);
            btnBg = Ui.FlatButton("背景…", "btnBg");
            btnBg.Width = 64; btnBg.Height = 24; btnBg.Margin = new Thickness(8, 0, 0, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(btnBg, true);
            left.Children.Add(btnBg);
            bar.Children.Add(left);

            StackPanel caps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            caps.Children.Add(CapButton("─", "capMin", delegate { WindowState = WindowState.Minimized; }));
            caps.Children.Add(CapButton("▢", "capMax", delegate
            {
                WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
            }));
            caps.Children.Add(CapButton("✕", "capClose", delegate { Close(); }));
            Grid.SetColumn(caps, 1);
            bar.Children.Add(caps);
            return bar;
        }

        Button CapButton(string glyph, string id, Action onClick)
        {
            Button b = new Button();
            b.Content = glyph;
            b.Width = 46; b.Height = 30;
            b.Foreground = Ui.Br(MainForm.HudText);
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
            b.FontSize = 13;
            b.Cursor = Cursors.Hand;
            b.Template = (ControlTemplate)XamlReader.Parse(CapButtonXaml);
            b.Click += delegate { onClick(); };
            Ui.Id(b, id);
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        const string CapButtonXaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Border x:Name='b' Background='{TemplateBinding Background}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='b' Property='Background' Value='#4F74D866'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        UIElement BuildMiddle()
        {
            Grid g = new Grid();
            colBar = new ColumnDefinition { Width = new GridLength(0) };
            colLeft = new ColumnDefinition { Width = new GridLength(PanelWidth()) };
            colSplit = new ColumnDefinition { Width = new GridLength(6) };
            g.ColumnDefinitions.Add(colBar);
            g.ColumnDefinitions.Add(colLeft);
            g.ColumnDefinitions.Add(colSplit);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            g.Children.Add(BuildActivityBar());
            g.Children.Add(BuildLeftPanel());

            splitterV = new GridSplitter();
            splitterV.Width = 6;
            splitterV.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitterV.VerticalAlignment = VerticalAlignment.Stretch;
            splitterV.Style = Ui.SplitterStyle();
            Ui.Id(splitterV, "splitterV");
            splitterV.DragCompleted += delegate { OnPanelSplitterDone(); };
            Grid.SetColumn(splitterV, 2);
            g.Children.Add(splitterV);

            UIElement right = BuildRight();
            Grid.SetColumn(right, 3);
            g.Children.Add(right);

            PreviewKeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    SetPanelCollapsed(!collapsed);
                    ke.Handled = true;
                }
            };
            return g;
        }

        UIElement BuildActivityBar()
        {
            activityBar = new Border();
            activityBar.Background = Ui.Br(MainForm.HudPanel, 0xE6);
            activityBar.BorderBrush = Ui.Br(MainForm.HudLine);
            activityBar.BorderThickness = new Thickness(0, 0, 1, 0);
            activityBar.Visibility = Visibility.Collapsed;
            Ui.Id(activityBar, "activityBar");
            Button expand = Ui.FlatButton("▶", "btnExpand");
            expand.Width = 28; expand.Height = 26;
            expand.HorizontalAlignment = HorizontalAlignment.Center;
            expand.VerticalAlignment = VerticalAlignment.Top;
            expand.Margin = new Thickness(0, 8, 0, 0);
            expand.Click += delegate { SetPanelCollapsed(false); };
            activityBar.Child = expand;
            Grid.SetColumn(activityBar, 0);
            return activityBar;
        }

        double PanelWidth()
        {
            double w = (cfg == null) ? 0 : cfg.PanelWidth;
            if (w < 120) w = 322;
            return w;
        }

        string MemHintText()
        {
            int n = (cfg == null) ? 1 : cfg.ParallelCount;
            return "约 " + (n * 550 / 1024.0).ToString("0.0") + " GB";
        }

        int MicIndex()
        {
            if (cfg == null) return 0;
            if (cfg.MicMode == "micOnly") return 1;
            if (cfg.MicMode == "both") return 2;
            return 0;
        }

        void OnPanelSplitterDone()
        {
            if (colLeft == null) return;
            double w = colLeft.Width.Value;
            if (w < 110) { SetPanelCollapsed(true); return; }
            if (cfg != null) { cfg.PanelWidth = (int)Math.Round(w); cfg.Save(); }
        }

        void SetPanelCollapsed(bool want)
        {
            collapsed = want;
            if (want)
            {
                if (colLeft.Width.Value >= 110 && cfg != null) cfg.PanelWidth = (int)Math.Round(colLeft.Width.Value);
                colLeft.Width = new GridLength(0);
                colSplit.Width = new GridLength(0);
                colBar.Width = new GridLength(36);
                activityBar.Visibility = Visibility.Visible;
                if (btnPanel != null) btnPanel.Content = "▶  展开面板";
            }
            else
            {
                colLeft.Width = new GridLength(PanelWidth());
                colSplit.Width = new GridLength(6);
                colBar.Width = new GridLength(0);
                activityBar.Visibility = Visibility.Collapsed;
                if (btnPanel != null) btnPanel.Content = "◀  收起面板";
            }
            if (cfg != null) { cfg.PanelCollapsed = want; cfg.Save(); }
        }

        // ---------------------------------------------------------------- 左栏五张卡片
        UIElement BuildLeftPanel()
        {
            leftCol = new StackPanel { Margin = new Thickness(6, 6, 0, 6) };

            // ① 录制
            btnStart = Ui.FlatButton("▶  开始录制", "btnStart");
            btnStart.Background = Ui.Br(MainForm.HudOn);
            btnStart.Height = 26; btnStart.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnStop = Ui.FlatButton("■  停止", "btnStop");
            btnStop.Background = Ui.Br(MainForm.HudOff);
            btnStop.Height = 26; btnStop.HorizontalAlignment = HorizontalAlignment.Stretch; btnStop.Margin = new Thickness(0, 6, 0, 0);
            leftCol.Children.Add(Ui.Card("录 制", "◉", new StackPanel { Children = { btnStart, btnStop } }, "cardRecord", 8));

            // ② 并行
            StackPanel par = new StackPanel();
            TextBlock lblPar = Ui.Txt("同时录制数量：", 12.5, MainForm.HudText, false);
            lblPar.FontWeight = FontWeights.Bold;
            lblPar.VerticalAlignment = VerticalAlignment.Center;
            string[] nums = { "1", "2", "3", "4", "5", "6" };
            int pn = cfg == null ? 1 : cfg.ParallelCount;
            if (pn < 1) pn = 1; if (pn > 6) pn = 6;
            cbParallel = Ui.HudCombo("cbParallel", 96, nums, pn - 1);
            cbParallel.SelectionChanged += delegate(object s, SelectionChangedEventArgs se)
            {
                if (cfg == null || cbParallel.SelectedIndex < 0) return;
                cfg.ParallelCount = cbParallel.SelectedIndex + 1;
                try { cfg.Save(); } catch { }
                Log.Write("同时录制数量改为 " + cfg.ParallelCount + " 个（内存约 "
                          + (cfg.ParallelCount * 550 / 1024.0).ToString("0.0") + " GB）");
                if (lblMemHint != null) lblMemHint.Text = MemHintText();
            };
            // 旧版 cbParallel.MouseWheel 里把 Handled 置 true —— 用户要求「滚轮滚过数量下拉不要改它」。
            // 移植时漏了，靠 验证WPF-10 抓出来。
            cbParallel.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs me) { me.Handled = true; };
            StackPanel parRow = Ui.Row(0, lblPar, cbParallel);
            parRow.Children.Add(new Border { Width = 8 });
            par.Children.Add(parRow);
            lblMemHint = Ui.Note(MemHintText(), "lblMemHint", 2);
            par.Children.Add(lblMemHint);
            chkRound = Ui.HudCheck("轮录（录完一批自动接着录下一批）", "chkRound", cfg == null || cfg.RoundRecord, 8);
            par.Children.Add(chkRound);
            leftCol.Children.Add(Ui.Card("并 行", "≡", par, "cardParallel", 8));

            // ③ 队列
            btnClear = Ui.FlatButton("清空队列", "btnClear");
            btnRemove = Ui.FlatButton("移除选中", "btnRemove");
            btnMark = Ui.FlatButton("标记本批", "btnMark");
            btnRefresh = Ui.FlatButton("刷新", "btnRefresh");
            leftCol.Children.Add(Ui.Card("队 列", "▍", EvenRow(btnClear, btnRemove, btnMark, btnRefresh), "cardQueue", 8));

            // ④ 声音
            StackPanel snd = new StackPanel();
            btnSound = Ui.FlatButton("🔊 声音：开 ✓", "btnSound");
            btnSound.HorizontalAlignment = HorizontalAlignment.Left;
            snd.Children.Add(btnSound);
            snd.Children.Add(Ui.Note("本机静音，录像里照样有声音", null, 2));
            btnGameAudio = Ui.FlatButton("🔊 录打击音效：开 ✓", "btnGameAudio");
            btnGameAudio.Foreground = Ui.Br(MainForm.HudAccent2);
            btnGameAudio.HorizontalAlignment = HorizontalAlignment.Left; btnGameAudio.Margin = new Thickness(0, 4, 0, 0);
            snd.Children.Add(btnGameAudio);
            btnBgm = Ui.FlatButton(cfg != null && cfg.RecordBgm ? "录BGM：开 ✓" : "录BGM：关 ✕", "btnBgm");
            btnBgm.HorizontalAlignment = HorizontalAlignment.Left; btnBgm.Margin = new Thickness(0, 4, 0, 0);
            snd.Children.Add(btnBgm);
            TextBlock lblMic = Ui.Txt("录音模式：", 12.5, MainForm.HudText, false);
            lblMic.VerticalAlignment = VerticalAlignment.Center;
            cbMic = Ui.HudCombo("cbMic", 150, new string[] { "只录游戏声音", "只录麦克风", "游戏 + 麦克风" }, MicIndex());
            snd.Children.Add(Ui.Row(6, lblMic, cbMic));
            leftCol.Children.Add(Ui.Card("声 音", "🔊", snd, "cardSound", 8));

            // ⑤ 环境
            StackPanel env = new StackPanel();
            btnPrepare = Ui.FlatButton("准备 OBS", "btnPrepare");
            btnCheck = Ui.FlatButton("检查环境", "btnCheck");
            btnSet = Ui.FlatButton("设置…", "btnSet");
            btnOpenLog = Ui.FlatButton("日志", "btnOpenLog");
            env.Children.Add(EvenRow(btnPrepare, btnCheck, btnSet, btnOpenLog));
            chkAuto = Ui.HudCheck("拖入后自动开始", "chkAuto", cfg != null && cfg.AutoStartOnDrop, 8);
            chkPrepare = Ui.HudCheck("自动准备采集源并绑定游戏窗口", "chkPrepare", cfg == null || cfg.AutoPrepareObs, 4);
            chkCloseObs = Ui.HudCheck("录完关闭 OBS", "chkCloseObs", cfg != null && cfg.CloseObsAfter, 4);
            env.Children.Add(chkAuto);
            env.Children.Add(chkPrepare);
            env.Children.Add(chkCloseObs);
            leftCol.Children.Add(Ui.Card("环 境", "▍", env, "cardEnv", 8));

            hintText = Ui.Txt("", 12, MainForm.HudAccent, false);
            hintText.Margin = new Thickness(2, 0, 0, 0);
            Ui.Id(hintText, "hint");
            hintText.Visibility = Visibility.Collapsed;
            leftCol.Children.Add(hintText);
            lblParHint = Ui.Note("", "lblParHint", 10);
            leftCol.Children.Add(lblParHint);

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Content = leftCol;
            Ui.Id(sv, "panelLeft");
            Grid.SetColumn(sv, 1);
            return sv;
        }

        Grid EvenRow(params UIElement[] items)
        {
            Grid g = new Grid();
            for (int i = 0; i < items.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                FrameworkElement fe = items[i] as FrameworkElement;
                if (fe != null)
                {
                    fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                    fe.Margin = new Thickness(i == 0 ? 0 : 3, 0, i == items.Length - 1 ? 0 : 3, 0);
                    Grid.SetColumn(fe, i);
                }
                g.Children.Add(items[i]);
            }
            return g;
        }

        // ---------------------------------------------------------------- 右栏
        UIElement BuildRight()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.62, GridUnitType.Star) });
            g.Margin = new Thickness(0, 6, 6, 6);

            g.Children.Add(Ui.Card("队列 / QUEUE", "▍", BuildQueueBody(), "cardQueue", 6));

            GridSplitter hs = new GridSplitter();
            hs.Height = 6;
            hs.HorizontalAlignment = HorizontalAlignment.Stretch;
            hs.VerticalAlignment = VerticalAlignment.Stretch;
            hs.Style = Ui.SplitterStyle();
            Ui.Id(hs, "splitterH");
            Grid.SetRow(hs, 1);
            g.Children.Add(hs);

            UIElement logCard = Ui.Card("终端 / STDOUT", "▍", BuildLogPanel(), "cardLog", 6);
            Grid.SetRow(logCard, 2);
            g.Children.Add(logCard);
            return g;
        }

        // ---------------------------------------------------------------- 队列
        UIElement BuildQueueBody()
        {
            Grid wrap = new Grid();
            wrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            wrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid head = new Grid { Background = Ui.Br(MainForm.HudHead) };
            AddListCols(head);
            string[] names = { "#", "文件", "状态", "用时", "大小", "说明" };
            for (int i = 0; i < names.Length; i++)
            {
                TextBlock tb = Ui.Txt(names[i], 12.5, MainForm.HudText, true);
                tb.FontWeight = FontWeights.Bold;
                tb.Margin = new Thickness(i == 0 ? 8 : 4, 0, 0, 0);
                tb.VerticalAlignment = VerticalAlignment.Center;
                Ui.Id(tb, "colHead" + i);
                Grid.SetColumn(tb, i);
                head.Children.Add(tb);
            }
            Rectangle hline = new Rectangle { Height = 1, Fill = Ui.Br(MainForm.HudLine), VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumnSpan(hline, 6);
            head.Children.Add(hline);
            wrap.Children.Add(head);

            Grid body = new Grid();
            queueList = new ListBox();
            queueList.Background = Brushes.Transparent;
            queueList.BorderThickness = new Thickness(0);
            queueList.Foreground = Ui.Br(MainForm.HudText);
            queueList.SelectionMode = SelectionMode.Extended;
            queueList.ItemsSource = rows;
            queueList.ItemTemplate = (DataTemplate)XamlReader.Parse(ListRowXaml);
            queueList.ItemContainerStyle = (Style)XamlReader.Parse(ListRowStyleXaml);
            Ui.Id(queueList, "list");
            queueList.MouseDoubleClick += OnQueueDoubleClick;
            queueList.PreviewMouseLeftButtonDown += OnListMouseDown;
            queueList.PreviewMouseMove += OnListMouseMove;
            queueList.PreviewMouseLeftButtonUp += OnListMouseUp;
            body.Children.Add(queueList);

            marqueeLayer = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            marqueeRect = new Rectangle();
            marqueeRect.Stroke = Ui.Br(MainForm.HudAccent);
            marqueeRect.StrokeThickness = 1;
            marqueeRect.Fill = Ui.Br(MainForm.HudAccent, 0x3C);
            marqueeLayer.Children.Add(marqueeRect);
            body.Children.Add(marqueeLayer);

            Grid.SetRow(body, 1);
            wrap.Children.Add(body);
            return wrap;
        }

        void AddListCols(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        }

        const string ListRowXaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <Grid Height='22' Background='Transparent'>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='44'/><ColumnDefinition Width='*'/><ColumnDefinition Width='84'/>
      <ColumnDefinition Width='78'/><ColumnDefinition Width='82'/><ColumnDefinition Width='1.15*'/>
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column='0' Text='{Binding Index}' Margin='6,0,4,0' VerticalAlignment='Center' Foreground='#E6EBF4' FontFamily='Consolas' FontSize='12.5'/>
    <TextBlock Grid.Column='1' Text='{Binding File}' Margin='4,0,4,0' VerticalAlignment='Center' Foreground='#E6EBF4' FontFamily='Consolas' FontSize='12.5' TextTrimming='CharacterEllipsis'/>
    <StackPanel Grid.Column='2' Orientation='Horizontal' VerticalAlignment='Center'>
      <Ellipse Width='8' Height='8' Fill='{Binding StatusColor}' Margin='4,0,6,0'/>
      <TextBlock Text='{Binding Status}' Foreground='{Binding StatusColor}' FontFamily='Consolas' FontSize='12.5' VerticalAlignment='Center'/>
    </StackPanel>
    <TextBlock Grid.Column='3' Text='{Binding Dur}' Margin='4,0,4,0' VerticalAlignment='Center' Foreground='#93A0B4' FontFamily='Consolas' FontSize='12.5'/>
    <TextBlock Grid.Column='4' Text='{Binding Size}' Margin='4,0,4,0' VerticalAlignment='Center' Foreground='#93A0B4' FontFamily='Consolas' FontSize='12.5'/>
    <TextBlock Grid.Column='5' Text='{Binding Detail}' Margin='4,0,4,0' VerticalAlignment='Center' Foreground='#93A0B4' FontFamily='Consolas' FontSize='12.5' TextTrimming='CharacterEllipsis'/>
  </Grid>
</DataTemplate>";

        const string ListRowStyleXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'>
  <Setter Property='Padding' Value='0'/>
  <Setter Property='Margin' Value='0'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Foreground' Value='#E6EBF4'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
        <Grid>
          <Border x:Name='bg' Background='{TemplateBinding Background}'/>
          <Rectangle x:Name='bar' Width='2' HorizontalAlignment='Left' Fill='#37C8A0' Visibility='Collapsed'/>
          <ContentPresenter/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='bar' Property='Visibility' Value='Collapsed'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
  <Style.Triggers>
    <DataTrigger Binding='{Binding Batch}' Value='True'>
      <Setter Property='Background' Value='#2E4A3E'/>
    </DataTrigger>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter Property='Background' Value='#5A6E8C33'/>
    </Trigger>
    <Trigger Property='IsSelected' Value='True'>
      <Setter Property='Background' Value='#3A4A6B'/>
    </Trigger>
  </Style.Triggers>
</Style>";

        const string HudScrollBarXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border x:Name='tb' Background='#475979' CornerRadius='4' Margin='2,0' Opacity='0.55'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='tb' Property='Opacity' Value='1'/>
                        <Setter TargetName='tb' Property='Background' Value='#4F74D8'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

        // 框选：从行上按下拖出矩形，按住 Ctrl 是追加；空白处点击不清空选择
        void OnListMouseDown(object sender, MouseButtonEventArgs e)
        {
            marqueeFrom = e.GetPosition(queueList);
            marqueeArmed = true;
            marqueeOn = false;
            marqueeAdd = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (HitItem(marqueeFrom) < 0) e.Handled = true;
        }

        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (!marqueeArmed) return;
            Point p = e.GetPosition(queueList);
            if (!marqueeOn && Math.Abs(p.X - marqueeFrom.X) < 4 && Math.Abs(p.Y - marqueeFrom.Y) < 4) return;
            marqueeOn = true;
            marqueeLayer.Visibility = Visibility.Visible;
            Rect r = new Rect(marqueeFrom, p);
            Canvas.SetLeft(marqueeRect, r.Left);
            Canvas.SetTop(marqueeRect, r.Top);
            marqueeRect.Width = r.Width;
            marqueeRect.Height = r.Height;
            ApplyMarquee(r);
        }

        void OnListMouseUp(object sender, MouseButtonEventArgs e)
        {
            marqueeArmed = false;
            if (marqueeOn)
            {
                marqueeOn = false;
                marqueeLayer.Visibility = Visibility.Collapsed;
                ApplyMarquee(new Rect(marqueeFrom, e.GetPosition(queueList)));
            }
        }

        int HitItem(Point p)
        {
            for (int i = 0; i < queueList.Items.Count; i++)
            {
                ListBoxItem it = queueList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (it == null) continue;
                try
                {
                    Point lp = it.TransformToAncestor(queueList).Transform(new Point(0, 0));
                    if (new Rect(lp.X, lp.Y, it.ActualWidth, it.ActualHeight).Contains(p)) return i;
                }
                catch { }
            }
            return -1;
        }

        void ApplyMarquee(Rect r)
        {
            List<int> hit = new List<int>();
            for (int i = 0; i < queueList.Items.Count; i++)
            {
                ListBoxItem it = queueList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (it == null) continue;
                try
                {
                    Point lp = it.TransformToAncestor(queueList).Transform(new Point(0, 0));
                    if (new Rect(lp.X, lp.Y, it.ActualWidth, it.ActualHeight).IntersectsWith(r)) hit.Add(i);
                }
                catch { }
            }
            if (!marqueeAdd) queueList.SelectedItems.Clear();
            foreach (int i in hit)
            {
                ListBoxItem it = queueList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (it != null) it.IsSelected = true;
            }
        }

        void OnQueueDoubleClick(object sender, MouseButtonEventArgs e)
        {
            RowVm row = queueList.SelectedItem as RowVm;
            if (row == null) return;
            try
            {
                string p = (!string.IsNullOrEmpty(row.OutputPath) && File.Exists(row.OutputPath)) ? row.OutputPath : row.RepPath;
                if (!string.IsNullOrEmpty(p)) Process.Start("explorer.exe", "/select,\"" + p + "\"");
            }
            catch { }
        }

        // ---------------------------------------------------------------- 终端
        UIElement BuildLogPanel()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Button hdr = Ui.FlatButton("", "logHeader");
            hdr.Height = 22;
            hdr.Padding = new Thickness(0);
            hdr.HorizontalAlignment = HorizontalAlignment.Stretch;
            hdr.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            hdr.Background = Brushes.Transparent;
            hdr.BorderThickness = new Thickness(0);
            Grid hdrIn = new Grid();
            TextBlock arrow = Ui.Txt("▼ 收起", 11.5, MainForm.HudDim, false);
            arrow.Margin = new Thickness(2, 0, 0, 0);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            Ui.Id(arrow, "logHeaderArrow");
            logHeaderArrow = arrow;
            hdrIn.Children.Add(arrow);
            TextBlock tail = Ui.Txt("", 11.5, MainForm.HudDim, false);
            tail.HorizontalAlignment = HorizontalAlignment.Right;
            tail.Margin = new Thickness(0, 0, 10, 0);
            tail.VerticalAlignment = VerticalAlignment.Center;
            tail.Visibility = Visibility.Collapsed;
            Ui.Id(tail, "logHeaderTail");
            logHeaderTail = tail;
            hdrIn.Children.Add(tail);
            Rectangle hl = new Rectangle { Height = 1, Fill = Ui.Br(MainForm.HudLine), VerticalAlignment = VerticalAlignment.Bottom };
            hdrIn.Children.Add(hl);
            hdr.Content = hdrIn;
            hdr.Click += delegate { ToggleLogCollapse(); };
            g.Children.Add(hdr);

            logList = new ListBox();
            logList.Background = Brushes.Transparent;
            logList.BorderThickness = new Thickness(0);
            logList.Foreground = Ui.Br(MainForm.HudLog);
            logList.FontFamily = new FontFamily("Consolas, Courier New");
            logList.FontSize = 12.5;
            logList.ItemsSource = logLines;
            logList.ItemTemplate = (DataTemplate)XamlReader.Parse(LogLineXaml);
            logList.ItemContainerStyle = (Style)XamlReader.Parse(LogLineStyleXaml);
            logList.Focusable = false;
            Ui.Id(logList, "logBox");
            ContextMenu menu = new ContextMenu();
            MenuItem copy = new MenuItem();
            copy.Header = "复制全部日志";
            copy.Click += delegate
            {
                try { Clipboard.SetText(string.Join(Environment.NewLine, logRaw.ToArray())); } catch { }
            };
            menu.Items.Add(copy);
            logList.ContextMenu = menu;
            Grid.SetRow(logList, 1);
            g.Children.Add(logList);
            return g;
        }

        const string LogLineXaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <TextBlock Text='{Binding}' FontFamily='Consolas' FontSize='12.5' Foreground='#A9C2E8' TextWrapping='NoWrap'/>
</DataTemplate>";

        const string LogLineStyleXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'>
  <Setter Property='Padding' Value='0,0,0,0'/>
  <Setter Property='Margin' Value='0'/>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='IsHitTestVisible' Value='False'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
        <ContentPresenter/>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

        public void AppendLog(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int visible = VisibleLogLines();
            bool atBottom = !logCollapsed && logList != null &&
                            (logList.Items.Count == 0 || logList.Items.Count - visible <= logLines.Count + 2);
            string[] parts = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int n = parts.Length;
            if (n > 0 && parts[n - 1].Length == 0) n--;
            for (int i = 0; i < n; i++)
            {
                logRaw.Add(parts[i]);
                foreach (string w in WrapLogLine(parts[i])) logLines.Add(w);
            }
            if (logRaw.Count > 2000)
            {
                logRaw.RemoveRange(0, logRaw.Count - 2000);
                RebuildLogLines();
            }
            lastLogLine = s.Replace("\r", "").Replace("\n", " ");
            if (logHeaderTail != null) logHeaderTail.Text = lastLogLine;
            if (atBottom) ScrollLogToEnd();
        }

        void RebuildLogLines()
        {
            logLines.Clear();
            foreach (string s in logRaw)
                foreach (string w in WrapLogLine(s)) logLines.Add(w);
            ScrollLogToEnd();
        }

        List<string> WrapLogLine(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) { outp.Add(""); return outp; }
            int maxW = LogTextWidth();
            int cur = 0;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                int w = (s[i] == '\t') ? 4 * LogCharW(' ') : LogCharW(s[i]);
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

        int LogTextWidth()
        {
            double w = (logList == null ? 400 : logList.ActualWidth);
            if (w <= 40) w = 400;
            return Math.Max(32, (int)(w - 16));
        }

        int LogCharW(char c)
        {
            int w;
            if (logCharW.TryGetValue(c, out w)) return w;
            TextBlock tb = new TextBlock { Text = c.ToString(), FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 12.5 };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            w = Math.Max(1, (int)Math.Ceiling(tb.DesiredSize.Width));
            logCharW[c] = w;
            return w;
        }

        int VisibleLogLines()
        {
            double h = (logList == null ? 200 : logList.ActualHeight);
            return Math.Max(1, (int)(h / 16.5));
        }

        void ScrollLogToEnd()
        {
            if (logLines.Count == 0) return;
            try { logList.ScrollIntoView(logLines[logLines.Count - 1]); } catch { }
        }

        void ToggleLogCollapse()
        {
            logCollapsed = !logCollapsed;
            if (logList != null) logList.Visibility = logCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (logHeaderArrow != null) logHeaderArrow.Text = logCollapsed ? "▶ 展开" : "▼ 收起";
            if (logHeaderTail != null) logHeaderTail.Visibility = logCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------------------------------------------------------- 底栏
        UIElement BuildStatus()
        {
            Grid g = new Grid();
            g.Background = Ui.Br(MainForm.HudPanel, 0xE6);
            TextBlock l = Ui.Txt("就绪", 12, MainForm.HudDim, false);
            l.Margin = new Thickness(8, 0, 0, 0);
            l.VerticalAlignment = VerticalAlignment.Center;
            Ui.Id(l, "statusText");
            statusText = l;
            g.Children.Add(l);
            TextBlock r = Ui.Txt("", 12, MainForm.HudDim, false);
            r.HorizontalAlignment = HorizontalAlignment.Right;
            r.Margin = new Thickness(0, 0, 10, 0);
            r.VerticalAlignment = VerticalAlignment.Center;
            Ui.Id(r, "statusEnv");
            statusEnv = r;
            g.Children.Add(r);
            return g;
        }

        // ---------------------------------------------------------------- 行操作（门面用）
        public void AddRow(Job j)
        {
            if (j == null) return;
            RowVm r = new RowVm();
            r.Job = j;
            r.Index = (rows.Count + 1).ToString();
            r.RepPath = j.Rep;
            r.OutputPath = j.Output;
            FillRow(r, j);
            rows.Add(r);
        }

        RowVm FindRow(Job j)
        {
            foreach (RowVm r in rows) if (r.Job == j) return r;
            return null;
        }

        void FillRow(RowVm r, Job j)
        {
            r.File = System.IO.Path.GetFileName(j.Rep);
            r.Status = j.Status;
            r.Detail = j.Detail == null ? "" : j.Detail;
            r.StatusColor = Ui.StatusColorOf(j.Status);
            bool hasDur = j.Started != DateTime.MinValue && j.Ended != DateTime.MinValue && j.Ended >= j.Started;
            r.Dur = j.VideoSeconds > 0.5 ? Recorder.FmtDur(j.VideoSeconds)
                  : (hasDur ? Recorder.FmtDur((j.Ended - j.Started).TotalSeconds)
                            : (j.Started != DateTime.MinValue ? "…" : ""));
            r.Size = j.Size > 0 ? (j.Size / 1048576.0).ToString("0.0") + " MB" : "";
            r.OutputPath = j.Output;
        }

        public void UpdateRow(Job j, string status, string detail)
        {
            RowVm r = FindRow(j);
            if (r == null) return;
            j.Status = status;
            j.Detail = detail;
            FillRow(r, j);
            try { queueList.Items.Refresh(); } catch { }
        }

        public void RemoveRow(Job j)
        {
            RowVm r = FindRow(j);
            if (r != null) rows.Remove(r);
            RenumberRows();
        }

        public void RenumberRows()
        {
            for (int i = 0; i < rows.Count; i++) rows[i].Index = (i + 1).ToString();
            try { queueList.Items.Refresh(); } catch { }
        }

        public void ClearSelection()
        {
            try { queueList.SelectedItems.Clear(); } catch { }
        }

        public void HighlightBatchRows(List<Job> batch)
        {
            foreach (RowVm r in rows) r.Batch = false;
            if (batch != null && batch.Count > 0)
                foreach (RowVm r in rows)
                    if (batch.Contains(r.Job)) r.Batch = true;
            try { queueList.Items.Refresh(); } catch { }
        }

        public void ScrollRowsIntoView(Job j)
        {
            RowVm r = FindRow(j);
            if (r != null) { try { queueList.ScrollIntoView(r); } catch { } }
        }

        public void SyncScrollToBottom()
        {
            if (rows.Count == 0) return;
            try { queueList.ScrollIntoView(rows[rows.Count - 1]); } catch { }
        }

        public void SetRecordingLook(bool recording)
        {
            if (btnStart != null) btnStart.IsEnabled = !recording;
            if (btnStop != null) btnStop.IsEnabled = recording;
        }

        // 调试接口用：「在不在录」= 「停止」按钮可不可点（SetRecordingLook 是唯一改它的地方）
        public bool StopEnabledNow { get { return btnStop != null && btnStop.IsEnabled; } }
        public bool StartEnabledNow { get { return btnStart != null && btnStart.IsEnabled; } }

        public void SetToggleText(string id, string text, bool on)
        {
            Ui.SetButtonText(id, text);
            Ui.Post(delegate
            {
                Ui.SetButtonBackColor(id, on ? MainForm.HudOn : MainForm.HudOff);
                Ui.SetButtonForeColor(id, on ? MainForm.HudText : MainForm.HudDim);
                Ui.SetButtonBorderColor(id, MainForm.HudLine);
            });
        }

        public void ComboParallelSelectedIndexRefresh()
        {
            if (cfg == null) return;
            int n = Math.Max(1, Math.Min(6, cfg.ParallelCount));
            if (cbParallel != null && cbParallel.SelectedIndex != n - 1) cbParallel.SelectedIndex = n - 1;
            if (lblMemHint != null) lblMemHint.Text = MemHintText();
        }

        // ---------------------------------------------------------------- 背景图层
        UIElement BuildBackgroundLayer()
        {
            bgLayer = new Rectangle();
            bgLayer.Fill = Ui.Br(MainForm.HudBg);
            Ui.Id(bgLayer, "bgLayer");
            ApplyBackground();
            return bgLayer;
        }

        public void ApplyBackground()
        {
            try
            {
                if (bgLayer == null || cfg == null) return;

                if (!string.IsNullOrEmpty(cfg.BgImage) && cfg.BgImage != bgLoadedPath && File.Exists(cfg.BgImage))
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(cfg.BgImage, UriKind.Absolute);
                    bi.EndInit();
                    ImageBrush br = new ImageBrush(bi);
                    br.Stretch = StretchOf(cfg.BgLayout);
                    if (cfg.BgLayout == "tile")
                    {
                        br.TileMode = TileMode.Tile;
                        br.ViewportUnits = BrushMappingMode.Absolute;
                        br.Viewport = new Rect(0, 0, bi.PixelWidth, bi.PixelHeight);
                    }
                    if (cfg.BgLayout == "center")
                    {
                        br.AlignmentX = AlignmentX.Center;
                        br.AlignmentY = AlignmentY.Center;
                    }
                    bgLayer.Fill = br;
                    bgLoadedPath = cfg.BgImage;
                }
                else if (string.IsNullOrEmpty(cfg.BgImage))
                {
                    bgLayer.Fill = Ui.Br(MainForm.HudBg);
                    bgLoadedPath = null;
                }

                int a = Math.Max(0, Math.Min(100, cfg.BgOpacity));
                bgLayer.Opacity = a / 100.0;
                bgLayer.Visibility = a > 0 ? Visibility.Visible : Visibility.Collapsed;

                double w = ActualWidth > 0 ? ActualWidth : 1180;
                double h = ActualHeight > 0 ? ActualHeight : 648;
                if (cfg.BgOffsetX != 0 || cfg.BgOffsetY != 0)
                {
                    TransformGroup tg = new TransformGroup();
                    tg.Children.Add(new TranslateTransform(w * cfg.BgOffsetX / 100.0, h * cfg.BgOffsetY / 100.0));
                    bgLayer.RenderTransform = tg;
                }
                else bgLayer.RenderTransform = Transform.Identity;

                if (cfg.BgBlur > 0)
                {
                    if (bgBlurFx == null) bgBlurFx = new BlurEffect();
                    bgBlurFx.Radius = cfg.BgBlur;
                    bgLayer.Effect = bgBlurFx;
                }
                else bgLayer.Effect = null;

                if (cfg.BgScope == "global") bgLayer.Margin = new Thickness(0);
                else
                {
                    double left = (colBar == null ? 0 : colBar.Width.Value)
                                + (colLeft == null ? 0 : colLeft.Width.Value)
                                + (colSplit == null ? 0 : colSplit.Width.Value);
                    bgLayer.Margin = new Thickness(left, 30, 0, 22);
                }
            }
            catch (Exception ex) { Log.Write("背景层出错：" + ex.Message); }
        }

        static Stretch StretchOf(string layout)
        {
            if (layout == "fit") return Stretch.Uniform;
            if (layout == "stretch") return Stretch.Fill;
            if (layout == "center" || layout == "tile") return Stretch.None;
            return Stretch.UniformToFill;
        }

        public string BgState()
        {
            ImageBrush b = bgLayer == null ? null : bgLayer.Fill as ImageBrush;
            string px = "";
            try
            {
                BitmapImage bi = b == null ? null : b.ImageSource as BitmapImage;
                if (bi != null) px = bi.PixelWidth + "x" + bi.PixelHeight;
            }
            catch { }
            return "{\"image\":" + (cfg == null || string.IsNullOrEmpty(cfg.BgImage) ? "false" : "true")
                 + ",\"stretch\":\"" + StretchOf(cfg == null ? "" : cfg.BgLayout).ToString() + "\""
                 + ",\"opacity\":" + (bgLayer == null ? 0 : (int)Math.Round(bgLayer.Opacity * 100))
                 + ",\"blur\":" + (cfg == null ? 0 : cfg.BgBlur)
                 + ",\"ox\":" + (cfg == null ? 0 : cfg.BgOffsetX)
                 + ",\"oy\":" + (cfg == null ? 0 : cfg.BgOffsetY)
                 + ",\"scope\":\"" + (cfg == null ? "" : cfg.BgScope) + "\""
                 + ",\"pixels\":\"" + px + "\"}";
        }

        // ---------------------------------------------------------------- 接录制逻辑
        public void HookRecorder(string[] args)
        {
            if (btnStart != null) btnStart.Click += delegate { rec.UiStart(); };
            if (btnStop != null) btnStop.Click += delegate { rec.UiStop(); };
            if (btnClear != null) btnClear.Click += delegate { rec.UiClear(); };
            if (btnRemove != null) btnRemove.Click += delegate { rec.UiRemove(); };
            if (btnMark != null) btnMark.Click += delegate { rec.UiMarkBatch(); };
            if (btnRefresh != null) btnRefresh.Click += delegate { rec.UiRefreshQueue(); };
            if (btnCheck != null) btnCheck.Click += delegate { rec.UiCheck(); };
            if (btnPrepare != null) btnPrepare.Click += delegate { rec.UiPrepareObs(); };
            if (btnSet != null) btnSet.Click += delegate { rec.UiOpenSettings(); };
            if (btnOpenLog != null) btnOpenLog.Click += delegate { rec.UiOpenLog(); };
            if (btnSound != null) btnSound.Click += delegate { rec.UiToggleSound(); };
            if (btnGameAudio != null) btnGameAudio.Click += delegate { rec.UiToggleGameAudio(); };
            if (btnBgm != null) btnBgm.Click += delegate { rec.UiToggleBgm(); };
            if (btnBg != null) btnBg.Click += delegate { Ui.ShowBackgroundDialog(); };

            // 旧版 MainForm 里 list.ItemSelectionChanged 那根线（选中几个 → 「同时录制数量」跟着变）
            if (queueList != null) queueList.SelectionChanged += delegate { rec.UiOnSelectionChanged(); };

            if (chkAuto != null) chkAuto.Click += delegate { if (cfg != null) { cfg.AutoStartOnDrop = chkAuto.IsChecked == true; cfg.Save(); } };
            if (chkPrepare != null) chkPrepare.Click += delegate { if (cfg != null) { cfg.AutoPrepareObs = chkPrepare.IsChecked == true; cfg.Save(); } };
            if (chkCloseObs != null) chkCloseObs.Click += delegate { if (cfg != null) { cfg.CloseObsAfter = chkCloseObs.IsChecked == true; cfg.Save(); } };
            if (chkRound != null) chkRound.Click += delegate { if (cfg != null) { cfg.RoundRecord = chkRound.IsChecked == true; cfg.Save(); } };
            if (cbMic != null) cbMic.SelectionChanged += delegate
            {
                if (cfg == null) return;
                cfg.MicMode = cbMic.SelectedIndex == 1 ? "micOnly" : (cbMic.SelectedIndex == 2 ? "both" : "gameOnly");
                cfg.Save();
            };

            AllowDrop = true;
            Drop += delegate(object s2, DragEventArgs de)
            {
                try
                {
                    string[] paths = (string[])de.Data.GetData(DataFormats.FileDrop);
                    Ui.Hint("");
                    rec.UiAddPaths(paths);
                }
                catch { }
            };
            DragEnter += delegate(object s2, DragEventArgs de)
            {
                try
                {
                    if (de.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        de.Effects = DragDropEffects.Copy;
                        Ui.Hint("松手即加入队列  ↓");
                    }
                }
                catch { }
            };
            DragLeave += delegate { Ui.Hint(""); };

            soundTimer = new System.Windows.Threading.DispatcherTimer();
            soundTimer.Interval = TimeSpan.FromMilliseconds(2000);
            soundTimer.Tick += delegate { try { rec.UiRefreshSoundButtons(); } catch { } };
            soundTimer.Start();

            rec.UiOnShown(args);
        }

        // ---------------------------------------------------------------- 最大化别盖任务栏
        [StructLayout(LayoutKind.Sequential)] struct MPOINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MRECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public MPOINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public MRECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0024)
            {
                try
                {
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(l, typeof(MINMAXINFO));
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi))
                    {
                        mmi.ptMaxPosition.X = mi.rcWork.L - mi.rcMonitor.L;
                        mmi.ptMaxPosition.Y = mi.rcWork.T - mi.rcMonitor.T;
                        mmi.ptMaxSize.X = mi.rcWork.R - mi.rcWork.L;
                        mmi.ptMaxSize.Y = mi.rcWork.B - mi.rcWork.T;
                        Marshal.StructureToPtr(mmi, l, false);
                        handled = true;
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }
    }
}
