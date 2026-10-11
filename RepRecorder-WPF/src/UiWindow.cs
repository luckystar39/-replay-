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
using System.Windows.Data;
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

        // ---------------------------------------------------------------- 顶栏高度
        // 2026-10-07 用户要求"照着 VS Code 的排布来"：原来是 30，显矮；改成 36，
        // 最右边三个窗口按钮也改成整块高（鼠标划过整块变色），底下再加一条 1px 分隔线。
        // CaptionH 是【可拖动区】的高度，要和 WindowChrome.CaptionHeight 一致；
        // TopBarH 才是顶栏占的总高（多出来的 1px 是分隔线）—— 背景铺"内容区"时，
        // 内容是从分隔线【下面】开始的，所以算 Margin 用 TopBarH。
        // 别把这两个数写死在别处：改一处、别处没跟上，背景就会差一行。
        const double CaptionH = 36;
        const double TopBarH = CaptionH + 1;

        // 左栏 / 顶栏 / 底栏
        ColumnDefinition colBar, colLeft, colSplit;
        StackPanel leftCol;
        Border activityBar;
        GridSplitter splitterV;
        Button btnStart, btnStop, btnClear, btnRemove, btnMark, btnRefresh, btnPrepare, btnCheck, btnCheckUpdate, btnSet, btnOpenLog;
        Button btnSound, btnGameAudio, btnBgm, btnPanel, btnBg;
        bool micLockShown;                   // 那个下拉现在是"被锁住替用户显示 0"的状态（见 ApplyMicLock）
        CheckBox chkPrepare, chkCloseObs, chkRound;
        ComboBox cbParallel, cbMic;
        TextBlock lblMemHint, lblParHint, hintText, statusText, statusEnv;
        bool collapsed = false;
        Grid queueHead;                      // 队列表头，列表出滚动条时要往左让一条
        ScrollViewer queueScroll, logScroll; // 队列/终端各自的滚动容器（借用它的视口宽度）
        double headPadNow = -1;

        // 队列
        ListBox queueList;
        Canvas marqueeLayer;
        Rectangle marqueeRect;
        Point marqueeFrom;
        bool marqueeArmed, marqueeOn, marqueeAdd, marqueeBlank;
        readonly List<int> marqueeHit = new List<int>();   // 上一帧框到的行（用来判断"这一帧没变"）

        bool SameHits(List<int> hit)
        {
            if (hit.Count != marqueeHit.Count) return false;
            for (int i = 0; i < hit.Count; i++) if (hit[i] != marqueeHit[i]) return false;
            return true;
        }
        readonly ObservableCollection<RowVm> rows = new ObservableCollection<RowVm>();
        public ObservableCollection<RowVm> Rows { get { return rows; } }
        public ListBox QueueList { get { return queueList; } }

        // 终端
        ListBox logList;
        TextBlock logHeaderArrow, logHeaderTail;
        bool logCollapsed = false;
        GridLength savedQueueH, savedLogH;   // 收起前两行的高度，展开时还原
        // 终端收起：整个卡片藏起来，只在右侧底部留一条窄条；这几个是收起时要改的行高/元素
        RowDefinition rowQueue, rowSplitH, rowLogBody, rowLogBar;
        UIElement logCardEl;
        Button logBar;
        string lastLogLine = "";
        const int LogMaxRaw = 2000;                  // 终端只留最近这么多"原始"行（旧版规矩）
        readonly List<string> logRaw = new List<string>();
        readonly ObservableCollection<string> logLines = new ObservableCollection<string>();
        // 每条原始行折成了几行 —— 超限丢队头时要成对地丢，不能整份重排（见 AppendLog）
        readonly Queue<int> logWrapCount = new Queue<int>();
        readonly Dictionary<char, int> logCharW = new Dictionary<char, int>();
        public ObservableCollection<string> LogLines { get { return logLines; } }
        public bool LogCollapsed { get { return logCollapsed; } }

        // 背景图层
        Rectangle bgLayer;
        BlurEffect bgBlurFx;
        // 记录画刷是按什么参数建出来的。⚠ 只记路径是不够的：放置方式（fill/fit/stretch/
        // center/tile）也是画刷的属性，只改下拉框不换图片的话，以前这里判定"没变"就不重建画刷，
        // 于是放置方式怎么选都不生效（2026-10-04 用户报的"放置位置功能都用不了"就是这个）。
        string bgLoadedKey = null;
        Grid bgClip;                     // 背景图层的裁剪容器：位移不许把图推出内容区
        Rect bgViewport = Rect.Empty;    // 画刷视口 = 背景图当前占的矩形（位移就是挪它）
        // 布局还没出来时的补算次数（封顶，见 ApplyBackground 里的说明）。
        // 以前是个 bool 闸门，但它在回调里被清掉 → 尺寸一直为 0 时就成了死循环。
        int bgRepanTries;
        bool bgReapplyQueued;            // 合并排队：一次布局里尺寸变多次也只补算一次

        // 布局完成后补算一次背景。bgLayer.SizeChanged 挂在 BuildBackgroundLayer 里，
        // 覆盖"改了列宽 / 改了背景范围 / 收起左栏"等所有会让图层尺寸变动的路径。
        void QueueBackgroundReapply()
        {
            if (bgReapplyQueued) return;
            bgReapplyQueued = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                bgReapplyQueued = false;
                ApplyBackground();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 当前摆放方式下，位移滑块的上限（百分比，按图层尺寸算）。
        //   -1 = 不限（只有 tile 了，滑块给 ±100）
        //    0 = 没东西可挪（拉伸、或比例刚好吻合）→ 设置窗里那一行直接不显示
        // 这两个值是 ApplyBackground() 算出来的，设置窗读它们来决定滑块量程和显不显示。
        public double PanXNow { get; private set; }
        public double PanYNow { get; private set; }
        // 同一量程的**未取整**版本。滑块的最小步长是 1%，而真实量程往往是 22.88% 这种数，
        // 取整后滑块拉到头也差着不到 1% —— 在 300% 缩放下就是几到几十个物理像素的空隙，
        // 正好毁掉这个功能存在的意义（"滑到头图边正好贴住框边"）。
        // 所以滑块拉到头时改用这个精确值来渲染，中间位置仍然按整数走。
        double panXExact, panYExact;

        System.Windows.Threading.DispatcherTimer soundTimer;

        public double PanelWidthNow { get { return colLeft == null ? 0 : colLeft.Width.Value; } }
        public bool CollapsedNow { get { return collapsed; } }
        public TextBlock StatusText { get { return statusText; } }
        public TextBlock StatusEnv { get { return statusEnv; } }
        public TextBlock HintText { get { return hintText; } }
        public Button CheckUpdateButton { get { return btnCheckUpdate; } }
        public TextBlock ParHint { get { return lblParHint; } }
        public ComboBox ComboParallel { get { return cbParallel; } }
        public Settings CfgPublic { get { return cfg; } }
        public Recorder RecPublic { get { return rec; } }

        public ShellWindow(Settings settings, Recorder recorder)
        {
            cfg = settings;
            rec = recorder;
            // 标题里带版本号：任务栏悬停、Alt+Tab 都能看到，界面里那行也带（见下面 229 行附近）
            Title = "Rep → MP4 录制器（OBS） v" + AppInfo.Version;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Width = 1180; Height = 648;                 // DIP：和旧版的逻辑像素一样大
            // 旧版 WinForms 里有一句 MinimumSize = new Size(760, 520)（Core.cs 的 MainForm），
            // 目的是「窗口缩小也不会漏掉按钮」。搬到 WPF 时漏了这一句 —— 能一路拖到只剩几百像素，
            // 左栏五张卡片被挤成一条缝。旧版那套是 DPI 不感知的，760×520 就是逻辑像素，
            // WPF 这边同样是 DIP，所以数值直接照搬。
            MinWidth = 760; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Base;                       // 跟着「面板暗化」走的窗口底色
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            Ui.Id(this, "win");

            // 全局滚动条样式（列表和终端都吃这条）
            Resources.Add(typeof(ScrollBar), Ui.HudScrollBar(10));

            // 面板底色强度（设置里的「面板暗化」）——必须在 BuildRoot 之前设好，
            // 因为卡片构造时就要用这个共享画刷
            Ui.SetScrim(cfg == null ? 25 : cfg.PanelScrim);   // 必须无条件调用：窗口底色画刷靠它初始化

            // 无边框窗口的拖动/缩放/双击最大化/贴边分屏，WPF 自带
            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = CaptionH;
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
                if (src != null)
                {
                    src.AddHook(Hook);
                    AddCaptionStyle(src.Handle);        // 补回 WS_CAPTION，否则没有最小化/还原动画
                    ApplyDwmWindowLooks(src.Handle);    // 四角圆角 + 染掉 DWM 那 1px 外描边
                }
            };
        }

        // ---------------------------------------------------------------- 骨架
        UIElement BuildRoot()
        {
            Grid root = new Grid();
            root.Children.Add(BuildBackgroundLayer());

            Grid body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TopBarH) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            // 2026-10-05 按用户要求删掉底部状态栏（左右角那两行小字），所以这里只有两行
            UIElement top = BuildTopBar();
            UIElement mid = BuildMiddle();
            Grid.SetRow(mid, 1);
            body.Children.Add(top);
            body.Children.Add(mid);
            root.Children.Add(body);
            return root;
        }

        UIElement BuildTopBar()
        {
            Grid bar = new Grid();
            bar.Background = Ui.Scrim;                  // 淡化后的面板底色，别再盖黑板
            Ui.Id(bar, "topBar");
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            // 横向 StackPanel 会把子元素拉满整行高度，而 TextBlock 默认贴着自己盒子的顶部画字，
            // 所以标题看着"顶在上面"。单独把它垂直居中。
            TextBlock title = Ui.Txt("REP → MP4  录制器   (OBS)   v" + AppInfo.Version, 13, MainForm.HudText, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(title);
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

            // 窗口按钮要让开右上角那 6px 的缩放边带 —— 贴死角落的话，
            // 右上角就拉不动了（另外三个角没被盖住所以正常）。
            // 2026-10-07：只留右边那 6px。原来上下也留 6（按钮 24 高、沉在下面），
            // 那是"小方块"的样子；现在按 VS Code 做成**整块高**，按钮 36 高、顶到顶栏上下边。
            // 顶边那 6px 缩放带虽然被按钮盖住一截，但右上角那块（右 6 × 上 6）还空着，拉得动。
            StackPanel caps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
            caps.Children.Add(CapButton("─", "capMin", delegate { WindowState = WindowState.Minimized; }));
            caps.Children.Add(CapButton("▢", "capMax", delegate
            {
                WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
            }));
            caps.Children.Add(CapButton("✕", "capClose", delegate { Close(); }));
            Grid.SetColumn(caps, 1);
            bar.Children.Add(caps);

            // VS Code 的顶栏底下压着一条 1px 的线，把"标题栏"和"内容"分开。
            // 用 Border 的边框画：边框画在边界【内侧】，所以这个 Border 总高 = TopBarH(37)
            // 时，里面正好剩 36 给 bar、最底下 1px 是线。
            Border wrap = new Border();
            wrap.BorderBrush = Ui.Br(MainForm.HudLine);
            wrap.BorderThickness = new Thickness(0, 0, 0, 1);
            wrap.Child = bar;
            return wrap;
        }

        Button CapButton(string glyph, string id, Action onClick)
        {
            Button b = new Button();
            b.Content = glyph;
            b.Width = 46; b.Height = CaptionH;
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
      <Setter TargetName='b' Property='Background' Value='#664F74D8'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
        // ⚠ 上面那个色原来是写反的：'#4F74D866' —— 想表达"强调蓝 #4F74D8、透明度 66"，
        //   但 WPF 的 8 位写法是 **#AARRGGBB**（透明度在前）。写成 #4F74D866 就成了
        //   A=4F / R=74 / G=D8 / B=66，一个半透明【绿】。原来是 24px 的小方块没人注意，
        //   2026-10-07 改成整块高之后一 hover 一大片绿，一眼就看出来了。
        //   同一个坑本文件 795 行踩过一次（那次是把 #33FFFFFF 理解成"白 33%"）。

        // 分隔条最多能把左栏拖到哪儿：窗口宽 - 分隔条 - 右边最小宽。
        // 不加这道栏杆的话能一路拖到底，右边（队列/终端）被挤成 0 宽就"找不到了"
        // （2026-10-05 用户报的）。380 DIP 够队列那 6 列表头 + 终端正常显示。
        const double MinRightPanel = 380;
        Grid midGrid;

        void OnPanelSplitterDelta(object sender, DragDeltaEventArgs e)
        {
            if (colLeft == null || midGrid == null) return;
            double avail = midGrid.ActualWidth - (colSplit == null ? 0 : colSplit.Width.Value) - MinRightPanel;
            if (avail < 0) avail = 0;
            double w = colLeft.Width.Value + e.HorizontalChange;
            if (w > avail) w = avail;
            if (w < 0) w = 0;
            colLeft.Width = new GridLength(w);
            e.Handled = true;                 // 宽度由这里定，别让 GridSplitter 再自己算一遍
            // ⚠ 这里**不要**调 ApplyBackground()。
            //   「只铺内容区」时它会改 bgClip 的 Margin → 背景整层重新布局、ImageBrush 按新尺寸
            //   重新采样，每一帧都做一次 → 整个窗口掉帧、看起来就是"背景一直在闪"
            //   （2026-10-05 用户报的，就是这句话引进来的：原来的代码只在 DragCompleted 里刷一次）。
            //   拖的过程中不跟，松手那一下由 OnPanelSplitterDone 一次刷到位。
        }

        UIElement BuildMiddle()
        {
            Grid g = new Grid();
            midGrid = g;
            colBar = new ColumnDefinition { Width = new GridLength(0) };
            colLeft = new ColumnDefinition { Width = new GridLength(ClampedPanelWidth()) };
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
            Ui.StyleSplitter(splitterV);
            Ui.Id(splitterV, "splitterV");
            splitterV.DragDelta += OnPanelSplitterDelta;
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
            activityBar.Background = Ui.Scrim;               // 同上
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

        // 左栏【实际】能用多宽：存下来的宽度之外，还得看窗口现在有多宽 —— 必须给右边留出
        // MinRightPanel，否则队列/终端被排成 0 宽，而分隔条跑到窗口外面、再也拉不回来。
        // 2026-10-08 查实：最大化时（客户区 2560）把分隔条拖到最右，存下 2000 多 → 关掉重开
        // 窗口回到 1180，这 2000 多的固定列宽照搬上去，右栏就是 0 宽、分隔条在窗口外
        // （MinRightPanel 那道闸只在【拖动中】管用，启动时这条路它管不到）。
        // 只在应用时夹，不改存下来的值 —— 窗口再拉大时用户自己拖回去。
        double ClampedPanelWidth()
        {
            double w = PanelWidth();
            if (midGrid == null) return w;          // 还没建出来（只在 BuildMiddle 之前调用）
            double win = ActualWidth > 0 ? ActualWidth : Width;
            double avail = win - (colSplit == null ? 6 : colSplit.Width.Value) - MinRightPanel
                                - (colBar == null ? 0 : colBar.Width.Value);
            if (avail < 120) avail = 120;           // 窗口极窄时别把左栏压成 0/负数
            return Math.Min(w, avail);
        }

        string MemHintText()
        {
            int n = (cfg == null) ? 1 : cfg.ParallelCount;
            return "约 " + (n * 550 / 1024.0).ToString("0.0") + " GB";
        }

        // 左栏专用的滚动条样式：平时透明，鼠标划过所在 ScrollViewer 才浮现。
        // ScrollBar 自己的 IsMouseOver 只管它那一竖条，鼠标在面板里别处时仍是 false，
        // 所以绑的是 ScrollViewer 的悬停状态。
        // 用代码拼而不是 XamlReader.Parse：BasedOn 的基样式在 Window.Resources 里，
        // 孤立解析那段 XAML 时 StaticResource 是找不到的。
        Style PanelBarStyle()
        {
            Style basedOn = Resources[typeof(ScrollBar)] as Style;
            Style st = new Style(typeof(ScrollBar), basedOn);
            st.Setters.Add(new Setter(UIElement.OpacityProperty, 0.0));
            Binding b = new Binding("IsMouseOver");
            b.RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScrollViewer), 1);
            DataTrigger dt = new DataTrigger();
            dt.Binding = b;                 // DataTrigger.Binding 是 BindingBase，RelativeSource 得先设好再赋
            dt.Value = true;
            dt.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
            st.Triggers.Add(dt);
            return st;
        }

        int MicIndex()
        {
            if (cfg == null) return 0;
            // 「只录麦克风」2026-10-05 按用户要求从下拉里去掉，现在只有两项。
            // 老设置文件里存着 "micOnly" 的会落到 0（只录游戏声音），不会出错。
            // ⚠ 别写 "both" —— 那是 2026-10-08 之前这里写错的词，录制路径认的是
            //   "gameAndMic"（Load 里会把老的 "both" 归一化过来）。
            return cfg.MicMode == "gameAndMic" ? 1 : 0;
        }

        // 「游戏 + 麦克风」只在真的【一个一个录】时才有意义：并行时几个游戏同时跑，
        // 麦克风会被同时采进好几条音轨，纯粹是噪音。所以「同时录制数量 ≥ 2」时把这个下拉锁掉。
        // 只锁【显示】—— cfg.MicMode 一个字都不改，数量回到 1 时用户原来选的那个自动回来
        // （录制路径那边由 Settings.EffectiveMicMode 兜底，两边同一套规矩）。
        void ApplyMicLock()
        {
            if (cbMic == null || cfg == null) return;
            bool locked = cfg.ParallelCount >= 2;
            if (locked)
            {
                micLockShown = true;                       // 先立旗子，再动 SelectedIndex —— 改它会触发 SelectionChanged
                if (cbMic.SelectedIndex != 0) cbMic.SelectedIndex = 0;
                cbMic.IsEnabled = false;
                cbMic.Opacity = 0.45;
            }
            else
            {
                cbMic.IsEnabled = true;
                cbMic.Opacity = 1.0;
                micLockShown = false;                      // 先落旗子，再把用户存的那个选回来
                int want = MicIndex();
                if (cbMic.SelectedIndex != want) cbMic.SelectedIndex = want;
            }
        }

        void OnPanelSplitterDone()
        {
            if (colLeft == null) return;
            double w = colLeft.Width.Value;
            if (w < 110) { SetPanelCollapsed(true); return; }
            if (cfg != null) { cfg.PanelWidth = (int)Math.Round(w); cfg.Save(); }
            // 拖分隔条只改列宽、不改窗口大小，SizeChanged 不会触发 ——
            // 补一次让背景跟上；拿新尺寸算视口那次同样由 bgLayer.SizeChanged 兜底。
            ApplyBackground();
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
                // ⚠ colBar 必须先归零，再算左栏宽度：ClampedPanelWidth() 会把活动条那一列
                //   算进去，而展开之后它就不占地方了 —— 顺序反了的话，每收放一次左栏就
                //   凭空窄掉 36（收起来时存的是 794，展开算成 758，如此反复）。
                colBar.Width = new GridLength(0);
                colLeft.Width = new GridLength(ClampedPanelWidth());
                colSplit.Width = new GridLength(6);
                activityBar.Visibility = Visibility.Collapsed;
                if (btnPanel != null) btnPanel.Content = "◀  收起面板";
            }
            if (cfg != null) { cfg.PanelCollapsed = want; cfg.Save(); }
            // 列宽变了 → 让背景跟着重算。这里立刻调一次是为了让 bgClip.Margin 尽快生效
            // （只铺内容区时 Margin 由列宽算出来）；真正拿新尺寸算视口的那一次，
            // 由 bgLayer.SizeChanged → QueueBackgroundReapply() 兜底（见 BuildBackgroundLayer）。
            ApplyBackground();
        }

        // ---------------------------------------------------------------- 左栏五张卡片
        UIElement BuildLeftPanel()
        {
            leftCol = new StackPanel { Margin = new Thickness(6, 6, 0, 6) };

            // ① 录制
            btnStart = Ui.FlatButton("▶", "开始录制", "btnStart");
            btnStart.Background = Ui.Br(MainForm.HudOn);
            btnStart.Height = 26; btnStart.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnStop = Ui.FlatButton("■", "停止", "btnStop");
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
                ApplyMicLock();     // 数量一过 2，麦克风那个下拉就得跟着锁/解锁
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
            leftCol.Children.Add(Ui.Card("录制设置", "≡", par, "cardParallel", 8));

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
            cbMic = Ui.HudCombo("cbMic", 150, new string[] { "只录游戏声音", "游戏 + 麦克风" }, MicIndex());
            snd.Children.Add(Ui.Row(6, lblMic, cbMic));
            leftCol.Children.Add(Ui.Card("声 音", "🔊", snd, "cardSound", 8));

            // ⑤ 环境
            StackPanel env = new StackPanel();
            // ⚠ 是「准备OBS」不是「准备 OBS」：五颗并排后每颗只剩 ~49 DIP，而微软雅黑 12px
            //   下「准备 OBS」要 51.8 DIP（那个空格自己占 4.4），实测末字的 S 会被切掉
            //   （2026-10-11 截图确认）。去掉空格是 48.2，和「检查环境」一样宽，正好放下。
            btnPrepare = Ui.FlatButton("准备OBS", "btnPrepare");
            btnCheck = Ui.FlatButton("检查环境", "btnCheck");
            btnSet = Ui.FlatButton("设置…", "btnSet");
            btnOpenLog = Ui.FlatButton("日志", "btnOpenLog");
            // 「检查更新」和那四颗并排一行（2026-10-11 用户要求）：它以前单独占一整行
            // （2026-10-10 加的，当时的理由是五颗并排会截字），左栏总高因此多一行 ——
            // 底下那行环境状态（"OBS：已就绪  游戏：已就绪"）在默认窗口高度下要拖滚动条才看得见。
            // 并进去省掉那一行。截字的问题按上面那条注释的办法解决：去掉一个空格 + 把
            // 间距从默认的 3 收到 2（多让出 8 DIP 给五颗分）。
            btnCheckUpdate = Ui.FlatButton("检查更新", "btnCheckUpdate");
            env.Children.Add(EvenRow(2, btnPrepare, btnCheck, btnSet, btnOpenLog, btnCheckUpdate));
            // 「拖入后自动开始」2026-10-05 用户要求整条删掉：拖进来只入队，
            // 一定要用户自己点「开始录制」。设置项 AutoStartOnDrop 保留（老设置文件里可能有），
            // 但程序里再也不读它了。
            chkPrepare = Ui.HudCheck("自动准备采集源并绑定游戏窗口", "chkPrepare", cfg == null || cfg.AutoPrepareObs, 8);
            chkCloseObs = Ui.HudCheck("录完关闭 OBS", "chkCloseObs", cfg != null && cfg.CloseObsAfter, 4);
            env.Children.Add(chkPrepare);
            env.Children.Add(chkCloseObs);
            // ⚠ 环境状态那一行（"OBS：已就绪  游戏：已就绪"）以前挂在【底部状态栏】上，而底部
            //   状态栏 2026-10-05 按用户要求整条删掉了 —— 于是 statusEnv 这个字段再也没被赋值，
            //   Ui.Env() 一直在往一个 null 里写（编译器一直在报 CS0649，被编译脚本的过滤挡掉了）。
            //   结果就是：**这台机器缺什么，界面上一个字都不显示**，只能去翻日志。
            //   2026-10-08 把它挂进「环 境」这张卡里 —— 这是它本来就该在的地方。
            statusEnv = Ui.Txt("", 11.5, MainForm.HudDim, false);
            statusEnv.TextWrapping = TextWrapping.Wrap;
            statusEnv.Margin = new Thickness(0, 6, 0, 0);
            Ui.Id(statusEnv, "envLine");
            env.Children.Add(statusEnv);
            leftCol.Children.Add(Ui.Card("环 境", "▍", env, "cardEnv", 8));

            hintText = Ui.Txt("", 12, MainForm.HudAccent, false);
            hintText.Margin = new Thickness(2, 0, 0, 0);
            Ui.Id(hintText, "hint");
            hintText.Visibility = Visibility.Collapsed;
            leftCol.Children.Add(hintText);
            // 左栏最下面那行「（= 你选中的 N 个）」2026-10-05 用户要求删掉。
            // 字段留着、但不挂进可视树：Recorder 的 SyncParallelCountToSelection 还在给它
            // 赋值（赋到一个没上树的 TextBlock 上是安全的，界面不显示而已，
            // 各处调用都有 null 判断，不需要动录制逻辑）。
            lblParHint = Ui.Note("", null, 0);

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            // 左栏滚动条：不用时不显示，鼠标划过整个面板才露出来。
            // 只挂左栏 —— 队列/终端要保持一直可见（全局隐形的版本用户以为滚动条坏了）。
            sv.Resources.Add(typeof(ScrollBar), PanelBarStyle());
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Ui.PinScrollBarWidth(sv, 10);
            sv.Content = leftCol;
            Ui.Id(sv, "panelLeft");
            Grid.SetColumn(sv, 1);
            return sv;
        }

        Grid EvenRow(params UIElement[] items) { return EvenRow(3, items); }

        // gap = 两颗之间留的空（第一颗左边、最后一颗右边不留）。
        // 需要它是因为「环 境」那张卡塞了五颗：五等分之后每颗只剩 ~49 DIP，
        // 而默认的 3 DIP 间距光缝就吃掉 24 DIP，「检查环境」/「检查更新」（各 48 DIP）就会被截字。
        Grid EvenRow(double gap, params UIElement[] items)
        {
            Grid g = new Grid();
            for (int i = 0; i < items.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                FrameworkElement fe = items[i] as FrameworkElement;
                if (fe != null)
                {
                    fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                    fe.Margin = new Thickness(i == 0 ? 0 : gap, 0, i == items.Length - 1 ? 0 : gap, 0);
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
            rowQueue = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
            rowSplitH = new RowDefinition { Height = new GridLength(6) };
            rowLogBody = new RowDefinition { Height = new GridLength(0.62, GridUnitType.Star) };
            rowLogBar = new RowDefinition { Height = new GridLength(0) };      // 收起后那条窄条占的行
            g.RowDefinitions.Add(rowQueue);
            g.RowDefinitions.Add(rowSplitH);
            g.RowDefinitions.Add(rowLogBody);
            g.RowDefinitions.Add(rowLogBar);
            g.Margin = new Thickness(0, 6, 6, 6);   // 照新设计稿：两块分开的圆角卡片，中间留缝

            g.Children.Add(Ui.Card("队列 / QUEUE", "▍", BuildQueueBody(), "cardQueue", 6));

            GridSplitter hs = new GridSplitter();
            hs.Height = 6;
            hs.HorizontalAlignment = HorizontalAlignment.Stretch;
            hs.VerticalAlignment = VerticalAlignment.Stretch;
            Ui.StyleSplitter(hs);
            Ui.Id(hs, "splitterH");
            Grid.SetRow(hs, 1);
            g.Children.Add(hs);

            logCardEl = Ui.Card("终端 / STDOUT", "▍", BuildLogPanel(), "cardLog", 6);
            Grid.SetRow(logCardEl, 2);
            g.Children.Add(logCardEl);

            // 收起终端之后，只在底边留的这一条：点它展开回来
            logBar = Ui.FlatButton("▲  终端", "logExpand");
            logBar.Height = 20;
            logBar.FontSize = 11.5;
            logBar.Background = Ui.Scrim;
            logBar.BorderThickness = new Thickness(0);
            logBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            logBar.Visibility = Visibility.Collapsed;
            logBar.Click += delegate { ToggleLogCollapse(); };
            Grid.SetRow(logBar, 3);
            g.Children.Add(logBar);
            return g;
        }

        // ---------------------------------------------------------------- 队列
        UIElement BuildQueueBody()
        {
            Grid wrap = new Grid();
            wrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            wrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid head = new Grid { Background = Ui.Scrim };   // 表头也走同一层淡底色
            queueHead = head;
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
            queueList.LostMouseCapture += OnListLostCapture;
            // 队列永远不出横向滚动条（旧版 FitListColumns 的规矩）：列宽本来就是星号，
            // 但显式关掉更保险 —— 万一哪行内容撑出去，出来的横向条会把表头彻底顶歪。
            ScrollViewer.SetHorizontalScrollBarVisibility(queueList, ScrollBarVisibility.Disabled);
            queueList.Loaded += delegate { Ui.PinScrollBarWidth(FindChild<ScrollViewer>(queueList), 10); };
            body.Children.Add(queueList);

            // 空队列**不显示任何提示文字**（2026-10-05 用户再次要求删掉）。
            // 这条其实是旧版就定过的规矩：Core.cs 里那句
            // 「2026-10-03：用户不要队列里这行字了（压在背景图上很碍眼）」还在，
            // 移植时按设计稿又加回来了。

            marqueeLayer = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            marqueeRect = new Rectangle();
            marqueeRect.Stroke = Ui.Br(MainForm.HudAccent);
            marqueeRect.StrokeThickness = 1;
            marqueeRect.Fill = Ui.Br(MainForm.HudAccent, 0x3C);
            marqueeLayer.Children.Add(marqueeRect);
            body.Children.Add(marqueeLayer);

            Grid.SetRow(body, 1);
            wrap.Children.Add(body);
            // 表头不在列表的 ScrollViewer 里，所以列表一冒出竖向滚动条，行内容的可用宽度就
            // 少了那一条 —— 星号列（文件 / 说明）跟着变窄，第 2 列往右的固定列全部向左移。
            // 2026-10-05 实测（300% DPI、60 行）：表头「状态」比行的状态点右了约 5 DIP。
            // 让表头右边也留出同样宽的一条，两边重新对上。
            queueList.Loaded += delegate
            {
                if (queueScroll != null) return;
                queueScroll = FindChild<ScrollViewer>(queueList);
                if (queueScroll == null) return;
                queueScroll.ScrollChanged += delegate { SyncHeaderPad(); };
                SyncHeaderPad();
            };
            return wrap;
        }

        void SyncHeaderPad()
        {
            if (queueHead == null || queueScroll == null) return;
            double pad = 0;
            if (queueScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                pad = QueueBarWidth();
            if (Math.Abs(pad - headPadNow) < 0.5) return;
            headPadNow = pad;
            queueHead.Margin = new Thickness(0, 0, pad, 0);
        }

        // 滚动条实际占多宽：直接问模板里那根竖条，别硬编码 ——
        // HudScrollBarXaml 里那个 Width 哪天改了，这里也不会对不上。
        double QueueBarWidth()
        {
            ScrollBar sb = FindVerticalBar(queueScroll);
            if (sb != null && sb.ActualWidth > 0) return sb.ActualWidth;
            return 10;
        }

        static ScrollBar FindVerticalBar(DependencyObject root)
        {
            if (root == null) return null;
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(root); } catch { return null; }
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(root, i);
                ScrollBar sb = c as ScrollBar;
                if (sb != null && sb.Orientation == Orientation.Vertical) return sb;
                ScrollBar r = FindVerticalBar(c);
                if (r != null) return r;
            }
            return null;
        }

        static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(root); } catch { return null; }
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(root, i);
                T t = c as T;
                if (t != null) return t;
                T r = FindChild<T>(c);
                if (r != null) return r;
            }
            return null;
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

        // 队列行的样子。配色抄 Win11 资源管理器：**同一个形状，只用半透明度分层次**，
        // 三种状态一眼能分出"鼠标在这 / 我选中了 / 这是本批"。
        //   · 悬停   白 8%          —— 很淡，只是「鼠标在这」
        //   · 选中   白 20% + 蓝条   —— 和资源管理器选一行一样
        //   · 本批   绿 25% + 绿条   —— 下一批要录的就是这几个
// ⚠ WPF 的 8 位十六进制是 #AARRGGBB（透明度在最前面），不是 #RRGGBBAA 也不是 CSS 的 #RRGGBBAA。
//   写成 #FFFFFF33 会被当成「不透明的黄」—— 2026-10-05 就这么黄了一大片，截图才看出来。
        // ⚠ 触发器顺序要紧（后面的盖前面的）：以前是「本批→悬停→选中」，鼠标划过绿色行时
        //   绿色被悬停色盖掉，看起来像标记丢了（2026-10-05 用户报的）。现在「本批」放最后。
        // ⚠ 这些 Setter 全放在 ControlTemplate.Triggers 里、用 TargetName 指模板里的部件 ——
        //   放到 Style.Triggers 里 TargetName 是解析不到的（不在同一个名字域）。
        const string ListRowStyleXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'>
  <Setter Property='Padding' Value='0'/>
  <Setter Property='Margin' Value='0'/>
  <Setter Property='Foreground' Value='#E6EBF4'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
        <Grid>
          <Border x:Name='bg' Background='Transparent'/>
          <Rectangle x:Name='bar' Width='3' HorizontalAlignment='Left' Fill='#4F74D8' Visibility='Collapsed'/>
          <ContentPresenter/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='bg' Property='Background' Value='#14FFFFFF'/>
          </Trigger>
          <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='bg' Property='Background' Value='#33FFFFFF'/>
            <Setter TargetName='bar' Property='Fill' Value='#4F74D8'/>
            <Setter TargetName='bar' Property='Visibility' Value='Visible'/>
          </Trigger>
          <DataTrigger Binding='{Binding Batch}' Value='True'>
            <Setter TargetName='bg' Property='Background' Value='#4037C8A0'/>
            <Setter TargetName='bar' Property='Fill' Value='#37C8A0'/>
            <Setter TargetName='bar' Property='Visibility' Value='Visible'/>
          </DataTrigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";


        // 框选：从空白处按下拖出矩形，按住 Ctrl 是追加；空白处点击不清空选择
        void OnListMouseDown(object sender, MouseButtonEventArgs e)
        {
            marqueeFrom = e.GetPosition(queueList);
            marqueeOn = false;
            marqueeAdd = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            marqueeBlank = HitItem(marqueeFrom) < 0;     // 按在"没有行"的空白处
            // 从哪儿按下都能起手（以前只允许空白处起手，用户 2026-10-05 说太别扭）。
            // 这里**不设 Handled**：单击选中、双击定位、Ctrl 点选都还要照常工作。
            // 等拖动超过阈值、真进入框选了，再到 Move 里接管。
            marqueeArmed = true;
        }

        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (!marqueeArmed) return;
            // 防御：左键已经松开却还收到 Move（漏了 MouseUp）就当场收尾
            if (e.LeftButton != MouseButtonState.Pressed) { EndMarquee(e.GetPosition(queueList)); return; }
            Point p = e.GetPosition(queueList);
            if (!marqueeOn && Math.Abs(p.X - marqueeFrom.X) < 4 && Math.Abs(p.Y - marqueeFrom.Y) < 4) return;
            if (!marqueeOn)
            {
                marqueeOn = true;
                // 抢走 ListBoxItem 手里的鼠标捕获 —— 它那套"按住拖动扩选"就此停下；
                // 光靠 e.Handled 不够，两者同时做，选框才不会被原生选择覆盖掉。
                try { Mouse.Capture(queueList); } catch { }
            }
            e.Handled = true;
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
            if (!marqueeArmed) return;
            bool wasDrag = marqueeOn;
            bool onBlank = marqueeBlank;
            if (marqueeOn) e.Handled = true;    // 压掉原生收尾，别让它在框选之后又改一次选择
            EndMarquee(e.GetPosition(queueList));
            // 在列表下方的空白处点一下 = 把当前选中的几行钉成「本批」（蓝色取消、变绿）。
            // 这是旧版里用户明确要求的交互（Core.cs 的 KeepSelectionOnEmptyClick：
            // 「选中几行（蓝色）之后，点列表下方的空白处 → 蓝色取消、这几行变成绿色标记」），
            // 交接文档把「空白处点一下标本批」列为别再改动的产品规则。
            // 移植时整条丢掉了 —— 功能还在（左栏那个「标记本批」按钮），但这个手势没了。
            // 这里是 PreviewMouseLeftButtonUp，跑在 ListBox 自己的收尾之前，所以读到的
            // 还是完整的选中集合（旧版得等 120ms 正是因为 ListView 会在点击过程中一条条取消）。
            if (!wasDrag && onBlank && queueList.SelectedItems.Count > 0)
            {
                rec.UiMarkBatch();
                e.Handled = true;
            }
        }

        // 捕获被抢走（比如别处抢焦点）时也要收尾，否则选框会留在屏幕上
        void OnListLostCapture(object sender, MouseEventArgs e)
        {
            if (!marqueeArmed) return;
            // 只有捕获真的离开了整个列表才收尾。ListBox 按下时会把捕获从 ListBoxItem
            // 挪到 ListBox 自己身上，这种"内部换手"也会冒泡到这里 —— 照收不误的话，
            // 刚起手的框选会被当场掐断。捕获还在列表上就说明是换手，接着拖。
            if (Mouse.Captured == queueList) return;
            EndMarquee(marqueeFrom);
        }

        void EndMarquee(Point end)
        {
            bool wasOn = marqueeOn;
            marqueeArmed = false;
            marqueeOn = false;
            marqueeHit.Clear();     // 下一轮框选从"没命中"重新算，免得拿上一轮的集合去比
            marqueeLayer.Visibility = Visibility.Collapsed;
            try { if (Mouse.Captured == queueList) Mouse.Capture(null); } catch { }
            if (wasOn) ApplyMarquee(new Rect(marqueeFrom, end));
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
            // 命中集合和上一帧一样就什么都别做。
            // ⚠ 这是"蓝色框不停闪烁"的根源（2026-10-05 用户报的，旧版也有类似的）：
            //   以前鼠标每移动一点点都 Clear + 重加一遍选中，于是 SelectionChanged 连锁触发
            //   → SyncParallelCountToSelection → HighlightBatchRows → queueList.Items.Refresh()，
            //   整个列表的容器每移一像素就重建一次，蓝色框和整列都在闪。
            //   顺带还省掉了每次移动都写一遍设置文件（数量跟随会 cfg.Save()）。
            if (SameHits(hit)) return;
            marqueeHit.Clear();
            marqueeHit.AddRange(hit);
            if (!marqueeAdd) queueList.SelectedItems.Clear();
            // ⚠ 必须往 SelectedItems 里 Add，不能写 ListBoxItem.IsSelected = true。
            // 2026-10-05 实测：命中 4 行、IsSelected 设了 4 次，SelectedItems.Count 始终是 1 ——
            // 那种写法在这里是"替换"不是"累加"，框选永远只能选中最后一行。
            // 调试接口的 selectrange（验证WPF-10 一直在跑）用的就是 Add，那才是对的写法。
            foreach (int i in hit)
            {
                if (!queueList.SelectedItems.Contains(queueList.Items[i]))
                    queueList.SelectedItems.Add(queueList.Items[i]);
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
            // 靠左，和这张卡片的标题「▍ 终端 / STDOUT」同一个 x（2026-10-05 用户要求）。
            // 卡片内容区的左内边距是 12，卡片标题在 12+2 的地方，所以这里给 2。
            arrow.Margin = new Thickness(2, 0, 0, 0);
            arrow.HorizontalAlignment = HorizontalAlignment.Left;
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
            // 横向滚动条一律不要：折行是我们按字符宽度自己算的，算准了就不该出现；
            // 万一哪行差几像素顶出去，出来一条横向条反而把纵向的布局也搅乱。
            ScrollViewer.SetHorizontalScrollBarVisibility(logList, ScrollBarVisibility.Disabled);
            logList.SizeChanged += OnLogResized;    // 变宽/变窄后按新宽度重排折行
            logList.Loaded += delegate
            {
                ScrollViewer lsv = FindChild<ScrollViewer>(logList);
                Ui.PinScrollBarWidth(lsv, 10);
                if (lsv != null) lsv.ScrollChanged += OnLogScrolled;   // 维护「是否跟随最新」
            };
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
            bool atBottom = AtLogBottom();
            string[] parts = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int n = parts.Length;
            if (n > 0 && parts[n - 1].Length == 0) n--;
            for (int i = 0; i < n; i++)
            {
                logRaw.Add(parts[i]);
                List<string> ws = WrapLogLine(parts[i]);
                foreach (string w in ws) logLines.Add(w);
                logWrapCount.Enqueue(ws.Count);
            }
            // 超出 2000 行就从队头丢。⚠ 以前这里是「RemoveRange + 整份重排」——
            // 每来一行就重排一次全部（Clear + 几千次 Add，每次都发 CollectionChanged），
            // 是 O(n²)：往终端灌 3000 行时界面会直接卡住不应答（2026-10-05 实测调试接口超时）。
            // 现在按"这一行当初折成了几行"成对地丢，不重排、不发 Reset。
            int guard = 4 * LogMaxRaw + 16;   // 防呆：计数一旦对不上也不会死循环
            while (logRaw.Count > LogMaxRaw && guard-- > 0)
            {
                logRaw.RemoveAt(0);
                int c = logWrapCount.Count > 0 ? logWrapCount.Dequeue() : 0;
                for (int k = 0; k < c && logLines.Count > 0; k++) logLines.RemoveAt(0);
            }
            lastLogLine = s.Replace("\r", "").Replace("\n", " ");
            if (logHeaderTail != null) logHeaderTail.Text = lastLogLine;
            if (atBottom) ScrollLogToEnd();
        }

        // "来新日志之前，用户是不是已经在最底下了" —— 是才跟着往下滚。
        // ⚠ 以前这里写的是 logList.Items.Count vs logLines.Count 相比 —— 那俩是同一个集合，
        //   条件恒为真，等于"永远强制滚到底"：用户往上翻着看，来一行就被拽回去。
        // ⚠ 后来改成直接读 ScrollViewer 的偏移量 —— 又踩了另一个坑：
        //   ScrollIntoView 要等下一次布局才真的滚过去，**一次灌进来一大片日志时**
        //   （比如 addlog:120、或者 ffmpeg 一次吐几十行），第一行之后偏移量还是 0，
        //   于是每一行都判定"用户不在底部"、全都不滚 —— 终端就停在旧内容上
        //   （2026-10-05 实测：一次加 120 行之后 scroll% 停在 1）。
        //   现在改用"跟随标记"：默认跟随；用户自己滚走才取消；滚回底部自动恢复。
        bool logFollowTail = true;

        bool AtLogBottom()
        {
            if (logCollapsed || logList == null) return true;
            if (LogScroll() == null) return true;
            return logFollowTail;
        }

        // 由 ScrollChanged 更新：位置被滚走 → 不跟；滚回底部 → 恢复跟。
        // 我们自己调的 ScrollIntoView 也会把位置带到最底，所以它会把标记恢复成"跟"。
        void OnLogScrolled(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) < 0.5) return;      // 只是内容变高/视口变高，不算滚动
            double bottom = e.ExtentHeight - e.ViewportHeight;
            logFollowTail = bottom <= 0 || e.VerticalOffset >= bottom - 2;
        }

        ScrollViewer LogScroll()
        {
            if (logScroll == null && logList != null) logScroll = FindChild<ScrollViewer>(logList);
            return logScroll;
        }

        void RebuildLogLines(bool toEnd)
        {
            logLines.Clear();
            logWrapCount.Clear();
            foreach (string s in logRaw)
            {
                List<string> ws = WrapLogLine(s);
                foreach (string w in ws) logLines.Add(w);
                logWrapCount.Enqueue(ws.Count);
            }
            if (toEnd) ScrollLogToEnd();
        }

        // 终端是按"字符个数"手工折行的，折多宽取决于当时的宽度。窗口一变宽，
        // 之前按窄宽度折好的那些行还留在列表里 —— 右边白白空一大块。
        // 旧版 WinForms 在 OnSizeChanged 里做了重排，移植时漏了。
        // 只在宽度真的变了才重排（拖边框会连着触发很多次）。
        void OnLogResized(object sender, SizeChangedEventArgs e)
        {
            int w = LogTextWidth();
            if (w == wrapWidth) return;
            wrapWidth = w;
            if (logRaw.Count == 0) return;
            bool atBottom = AtLogBottom();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (logRaw.Count == 0) return;
                RebuildLogLines(atBottom);
            }));
        }

        int wrapWidth = -1;

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
            // 有 ScrollViewer 就用它的视口宽度 —— 那已经扣掉了竖向滚动条，
            // 再减 2 免得最宽那行正好顶出横向条。没有就退回旧那套估算。
            ScrollViewer sv = LogScroll();
            double w;
            if (sv != null && sv.ViewportWidth > 40) w = sv.ViewportWidth - 2;
            else
            {
                w = (logList == null ? 400 : logList.ActualWidth);
                if (w <= 40) w = 400;
                w -= 16;
            }
            return Math.Max(32, (int)w);
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

        void ScrollLogToEnd()
        {
            if (logLines.Count == 0) return;
            logFollowTail = true;      // 我们主动滚到底了 → 继续跟随
            // ⚠ 必须等布局跑完再滚：刚 Add 完就调 ScrollIntoView，它按**还没更新的**范围算，
            //   只能滚下去一点点（实测一次加 120 行只走到 2%）。排到布局之后再滚才到位。
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (logLines.Count == 0) return;
                try { logList.ScrollIntoView(logLines[logLines.Count - 1]); } catch { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        void ToggleLogCollapse()
        {
            logCollapsed = !logCollapsed;
            if (logHeaderArrow != null) logHeaderArrow.Text = logCollapsed ? "▶ 展开" : "▼ 收起";
            if (logHeaderTail != null) logHeaderTail.Visibility = logCollapsed ? Visibility.Visible : Visibility.Collapsed;

            // 收起 = 整块终端卡片让位（不只是把日志列表藏起来，那样只会剩一个空壳），
            // 腾出的高度全给队列；底边留一条窄条可以点开。
            if (logCardEl != null) logCardEl.Visibility = logCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (rowSplitH != null) rowSplitH.Height = logCollapsed ? new GridLength(0) : new GridLength(6);
            if (rowLogBar != null) rowLogBar.Height = logCollapsed ? new GridLength(20) : new GridLength(0);
            if (logBar != null) logBar.Visibility = logCollapsed ? Visibility.Visible : Visibility.Collapsed;

            if (logCollapsed)
            {
                // 记下收起前的高度 —— 用户可能拖过分隔条，那时行高是像素值而不是星号
                if (rowQueue != null) savedQueueH = rowQueue.Height;
                if (rowLogBody != null) { savedLogH = rowLogBody.Height; rowLogBody.Height = new GridLength(0); }
            }
            else if (rowLogBody != null)
            {
                // 还原。但要是那次拖拽把某一行拖到几乎没高度，就退回默认比例 ——
                // 不然展开后终端一点高度都分不到，看起来就是"终端区域不见了"。
                rowQueue.Height = (savedQueueH.IsStar || savedQueueH.Value >= 60) ? savedQueueH : new GridLength(1, GridUnitType.Star);
                rowLogBody.Height = (savedLogH.IsStar || savedLogH.Value >= 60) ? savedLogH : new GridLength(0.62, GridUnitType.Star);
            }
        }

        // ---------------------------------------------------------------- 底栏
        // 2026-10-05 按用户要求删掉（左右角那两行小字）。
        // statusText / statusEnv 两个字段留着但不再挂进可视树：调试接口的 status 命令还在读它们
        // （读到 null 就报空串，Ui.SetStatusEnv 那边也有 null 判断，不会崩）。
        // 代价：OBS / 游戏的"已就绪"不再显示在界面上了。

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
            // 先算出新的绿色集合，**只有真的变了**才 Refresh。
            // Items.Refresh() 是整表重建容器 —— 框选拖动时每帧都调就是那阵闪烁。
            List<RowVm> want = new List<RowVm>();
            if (batch != null && batch.Count > 0)
                foreach (RowVm r in rows) if (batch.Contains(r.Job)) want.Add(r);
            bool changed = false;
            foreach (RowVm r in rows)
            {
                bool b = want.Contains(r);
                if (r.Batch != b) { r.Batch = b; changed = true; }
            }
            if (!changed) return;
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
            // 容器负责"铺在哪儿"（Margin）和"不许溢出"（ClipToBounds）；
            // 里面的矩形负责画，位移用它的 RenderTransform。
            // 以前直接把矩形当图层，位移是加在矩形上的，裁剪区管不住它，图会被推到控制面板上。
            bgClip = new Grid();
            bgClip.ClipToBounds = true;
            Ui.Id(bgClip, "bgClip");

            bgLayer = new Rectangle();
            bgLayer.Fill = Ui.Br(MainForm.HudBg);
            Ui.Id(bgLayer, "bgLayer");
            // ⚠ 图层尺寸一变就必须重算背景 —— 这是【通用】兜底，别再逐个调用点去补。
            //   背景的尺寸/位移是靠画刷视口做的，视口尺寸 = bgLayer.ActualWidth。
            //   而改列宽、改「背景范围」、收起左栏这些操作，都是先改属性、**布局还没跑**，
            //   于是调用点里紧接着的那次 ApplyBackground() 拿到的是旧尺寸，视口就停在旧值上。
            //   实测两个症状（用户 2026-10-07 各报一次）：
            //     · 左栏收起再展开 → 图被放大 1.19 倍、顶部被切
            //     · 范围从「内容区」切「全局」→ 图只画在中间一条竖带里，两边是空白
            //   挂在这里就都自动跟上了：布局一完成，图层尺寸变化会自己通知过来。
            //   不会死循环：ApplyBackground 只改 bgClip.Margin，而 Margin 只由 cfg.BgScope
            //   和列宽决定 —— 第二次算出来一模一样，不会再触发尺寸变化。
            bgLayer.SizeChanged += delegate { QueueBackgroundReapply(); };
            bgClip.Children.Add(bgLayer);

            ApplyBackground();
            return bgClip;
        }

        public void ApplyBackground()
        {
            try
            {
                if (bgLayer == null || cfg == null) return;

                // 画刷 = 图片 + 放置方式，两个都得进 key，否则换了放置方式不会重建画刷
                string bgKey = cfg.BgImage + "|" + cfg.BgLayout;
                if (!string.IsNullOrEmpty(cfg.BgImage) && bgKey != bgLoadedKey && File.Exists(cfg.BgImage))
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(cfg.BgImage, UriKind.Absolute);
                    bi.EndInit();
                    ImageBrush br = new ImageBrush(bi);
                    // 所有对齐方式统一成「绝对单位的视口 + Stretch.Fill」：
                    //   视口 = 这张图在当前窗口里该占的矩形（可能比图层大，也可能小），
                    //   位移就是把视口整体挪一挪（见下面那段长注释）。
                    // Stretch.Fill 不会把图拉变形：除了「拉伸」模式，视口的宽高比就是图片的宽高比。
                    br.Stretch = Stretch.Fill;
                    br.ViewportUnits = BrushMappingMode.Absolute;
                    if (cfg.BgLayout == "tile") br.TileMode = TileMode.Tile;
                    bgLayer.Fill = br;
                    bgLoadedKey = bgKey;
                }
                else if (string.IsNullOrEmpty(cfg.BgImage))
                {
                    bgLayer.Fill = Ui.Br(MainForm.HudBg);
                    bgLoadedKey = null;
                }

                int a = Math.Max(0, Math.Min(100, cfg.BgOpacity));
                bgLayer.Opacity = a / 100.0;
                bgLayer.Visibility = a > 0 ? Visibility.Visible : Visibility.Collapsed;

                // 先算好裁剪容器的 Margin（它决定图层的实际大小），**排在视口计算之前**。
                // 它是下面"这一帧布局过没过期"的信号。
                Thickness wantMargin = new Thickness(0);
                if (cfg.BgScope != "global")
                {
                    double left = (colBar == null ? 0 : colBar.Width.Value)
                                + (colLeft == null ? 0 : colLeft.Width.Value)
                                + (colSplit == null ? 0 : colSplit.Width.Value);
                    wantMargin = new Thickness(left, TopBarH, 0, 0);   // 底部原来要给状态栏留 22，底栏删了就不用留
                }
                bool marginChanged = bgClip != null && !SameMargin(bgClip.Margin, wantMargin);
                if (marginChanged) bgClip.Margin = wantMargin;

                // 位移做在**画刷的视口**上，不是 Transform，也不是图层的 RenderTransform。
                //
                // ⚠ 这里以前是把位移做成画刷的 RelativeTransform 的 ——**那是错的**（2026-10-07 查实）：
                //   ImageBrush 的 UniformToFill 先把图片裁到视口里，RelativeTransform 再把
                //   **已经裁好的结果**平移，空出来的那块不会补上。实测：全屏下 Y 拉到 +12%，
                //   顶部出现一条 129 DIP 的窗口底色 —— 用户要的"看到被裁掉的那部分"根本没发生，
                //   只是整张图往下挪、上面空了一条。之前"看到被裁的边露出来"是看的缩略图看岔了。
                //   挪视口就没有这个问题：图和视口一起走，永远没有"空出来的地方"，
                //   超出容器的部分由 bgClip（ClipToBounds）裁掉 —— 这正是"滑到头正好贴边"。
                ImageBrush ib = bgLayer.Fill as ImageBrush;
                if (ib != null)
                {
                    // 量程要等布局出来才算得准，所以尺寸还没有时补算一次。
                    //
                    // ⚠ 这里以前是「入队 → 回调里把闸门清掉 → 再调 ApplyBackground」，
                    //   只要 ActualWidth 一直是 0 就会【无限自我重排】，把 UI 线程灌满：
                    //   消息队列还在回控制消息（所以 Responding 仍是 True、探针测不出来），
                    //   但输入被饿死 —— 鼠标变成漏斗、整个软件点不动。
                    //   2026-10-07 用户报的"重进软件后鼠标一直是漏斗"就是这个，
                    //   触发条件：BgOpacity == 0 → 下面把图层设成 Collapsed → 宽度**永远**是 0。
                    //   两道闸：折叠态直接不补算（补也没用），另外补算次数封顶，
                    //   免得别的未知原因（最小化之类）再造一个同样的死循环。
                    if (bgLayer.Visibility == Visibility.Visible && bgLayer.ActualWidth < 1 && bgRepanTries < 5)
                    {
                        bgRepanTries++;
                        Dispatcher.BeginInvoke(new Action(delegate { ApplyBackground(); }));
                    }
                    else if (bgLayer.ActualWidth >= 1) bgRepanTries = 0;

                    // 图层尺寸 = 窗口客户区 − bgClip 的 Margin。
                    // 实测核对过：window 1721x1033、Margin(left=304, top=30) → layer 1417x1003，分毫不差。
                    //
                    // ⚠ 不能直接用 bgLayer.ActualWidth：只要 Margin / 列宽刚改过，**布局就还没跑**，
                    //   ActualWidth 还是旧尺寸 —— 拿它算出来的视口是错的，会先按旧尺寸画一帧、
                    //   布局完再跳过去，看起来就是"闪一下"（用户 2026-10-07 报的
                    //   "展开面板时背景一闪再跑到右边"）。
                    //   用窗口尺寸反推就不必等布局。
                    //   这里**每次都推算、只在两者不一致时采信推算值**，不依赖任何"这次改没改过"
                    //   的标志位 —— 先前那版用 marginChanged 判断，实测仍会漏（视口算出个
                    //   新旧混合的 1144x858：宽按收起的、高按展开的）。
                    //   万一树结构以后变了，ActualWidth 会和推算值一致、于是走 ActualWidth；
                    //   而且 SizeChanged 那次补算还会再纠正一遍。
                    double expectW = Math.Max(1, ActualWidth - wantMargin.Left - wantMargin.Right);
                    double expectH = Math.Max(1, ActualHeight - wantMargin.Top - wantMargin.Bottom);
                    double lw = Math.Abs(bgLayer.ActualWidth - expectW) > 0.5 ? expectW : bgLayer.ActualWidth;
                    double lh = Math.Abs(bgLayer.ActualHeight - expectH) > 0.5 ? expectH : bgLayer.ActualHeight;
                    // 量程和渲染尺寸都拿这同一个 lw/lh 算 —— 上面已经确定它就是这一帧要用的尺寸，
                    // 绝不能让它们内部再去读一次 ActualWidth（那就是两种尺寸混用）。
                    ComputePanRange(ib, lw, lh);
                    double rw, rh;
                    if (!RenderedExtent(ib, lw, lh, out rw, out rh)) { rw = lw; rh = lh; }
                    // 位移量：PanFraction 返回的是「占图层尺寸的比例」，乘回去得到 DIP
                    double dx = PanFraction(panXExact, PanXNow, EffectivePan(PanXNow, cfg.BgOffsetX)) * lw;
                    double dy = PanFraction(panYExact, PanYNow, EffectivePan(PanYNow, cfg.BgOffsetY)) * lh;
                    // 视口 = 居中位置 + 位移。比图层大就往外扩，小就缩在中间（适应/居中的留边）。
                    bgViewport = new Rect((lw - rw) / 2.0 + dx, (lh - rh) / 2.0 + dy, rw, rh);
                    ib.Viewport = bgViewport;

                    // 翻转仍是变换（它不产生空白，图还是铺满整个视口），绕视口中心镜像。
                    // 缩放已经进了 rw/rh，所以这里只放 ±1。
                    ib.Transform = Transform.Identity;
                    if (cfg.BgFlipH || cfg.BgFlipV)
                        ib.RelativeTransform = new ScaleTransform(cfg.BgFlipH ? -1 : 1, cfg.BgFlipV ? -1 : 1, 0.5, 0.5);
                    else ib.RelativeTransform = Transform.Identity;
                }
                else bgViewport = Rect.Empty;
                bgLayer.RenderTransform = Transform.Identity;   // 图层自己不动

                if (cfg.BgBlur > 0)
                {
                    if (bgBlurFx == null) bgBlurFx = new BlurEffect();
                    bgBlurFx.Radius = cfg.BgBlur;
                    bgLayer.Effect = bgBlurFx;
                }
                else bgLayer.Effect = null;

                // Margin 已经在上面算好并设过了（要排在视口之前，见那里的说明）
            }
            catch (Exception ex) { Log.Write("背景层出错：" + ex.Message); }
        }

        static Stretch StretchOf(string layout)
        {
            if (layout == "fit") return Stretch.Uniform;
            if (layout == "stretch") return Stretch.Fill;
            if (layout == "center" || layout == "tile") return Stretch.None;
            // fill（覆盖）走这里：等比放大到盖住。
            // 2026-10-07 之前还有个 free 模式共用这条分支（区别只在位移限不限），已合并掉。
            return Stretch.UniformToFill;
        }

        double ZoomScale()
        {
            int z = cfg == null ? 100 : cfg.BgZoom;
            if (z < 10) z = 10;
            if (z > 400) z = 400;
            return z / 100.0;
        }

        // 背景图按当前摆放方式渲染出来，在图层里占多大（DIP）。
        // 位移的上限完全由它推出来：能挪的距离 = |渲染尺寸 - 图层尺寸| / 2。
        //
        // ⚠ lw/lh 必须由调用方传进来，**不要在这里读 bgLayer.ActualWidth**。
        //   布局没跟上时 ActualWidth 是旧值，而调用方可能已经在用推算的新尺寸 ——
        //   两套尺寸混在一起会算出个"宽按旧、高按新"的视口（实测 1144x858），
        //   画出来就是错倍数的那一帧，也就是用户看到的"闪一下"。
        bool RenderedExtent(ImageBrush b, double lw, double lh, out double rw, out double rh)
        {
            rw = rh = 0;
            if (cfg == null || bgLayer == null) return false;
            if (lw < 1 || lh < 1) return false;             // 布局还没出来，算不了
            double z = ZoomScale();
            string lay = cfg.BgLayout;
            if (lay == "stretch") { rw = lw * z; rh = lh * z; return true; }
            // 平铺：视口就是"一块砖"的尺寸（图片原始尺寸 × 缩放），TileMode 自己去重复
            if (lay == "tile")
            {
                BitmapImage tb = b == null ? null : b.ImageSource as BitmapImage;
                if (tb == null || tb.PixelWidth < 1 || tb.PixelHeight < 1) { rw = lw; rh = lh; return true; }
                rw = tb.PixelWidth * z; rh = tb.PixelHeight * z; return true;
            }
            BitmapImage bi = b == null ? null : b.ImageSource as BitmapImage;
            if (bi == null || bi.PixelWidth < 1 || bi.PixelHeight < 1)
            {
                // 没有图（或读不出尺寸）→ 整块就是底色，当作刚好铺满
                rw = lw * z; rh = lh * z; return true;
            }
            double iw = bi.PixelWidth, ih = bi.PixelHeight;
            if (lay == "center") { rw = iw * z; rh = ih * z; return true; }
            double s = (lay == "fit") ? Math.Min(lw / iw, lh / ih) : Math.Max(lw / iw, lh / ih);
            rw = iw * s * z; rh = ih * s * z;
            return true;
        }

        // 算「位移滑块的上限」，照搬 Wallpaper Engine 的规矩：
        //   覆盖/适应/居中 → 只给有东西可挪的那根轴，量程正好是余量的一半
        //                    （滑到头图边正好贴住框边，**永远滑不出底色**）
        //   拉伸          → 没东西可挪，两根轴都是 0
        //   平铺          → 不设限。平铺是无限循环的，压根没有"边"可贴，怎么挪都不会露底
        // 2026-10-07：「自由」模式已按用户要求合并进「覆盖」并删除，
        // 所以现在**没有任何摆放方式能拖出空白**（唯一的例外是平铺，它没有边）。
        void ComputePanRange(ImageBrush b, double lw, double lh)
        {
            string lay = cfg == null ? "" : cfg.BgLayout;
            if (lay == "tile") { PanXNow = PanYNow = -1; panXExact = panYExact = 0; return; }
            double rw, rh;
            if (!RenderedExtent(b, lw, lh, out rw, out rh)) { PanXNow = PanYNow = -1; panXExact = panYExact = 0; return; }
            // 向下取整：上限是 17.6 却存了 18 的话，会露出一条不到 1 像素的底色
            panXExact = Math.Abs(rw - lw) / 2.0 / lw * 100.0;
            panYExact = Math.Abs(rh - lh) / 2.0 / lh * 100.0;
            PanXNow = Math.Floor(panXExact);
            PanYNow = Math.Floor(panYExact);
        }

        static bool SameMargin(Thickness a, Thickness b)
        {
            return Math.Abs(a.Left - b.Left) < 0.01 && Math.Abs(a.Top - b.Top) < 0.01 &&
                   Math.Abs(a.Right - b.Right) < 0.01 && Math.Abs(a.Bottom - b.Bottom) < 0.01;
        }

        // 把整数百分比的位移换算成画刷要用的比例。
        // 滑块拉到**头**的时候改用未取整的精确量程，让图边正好落在框边上；
        // 中间位置仍按整数走（步长 1% 本来就够细了）。
        static double PanFraction(double exact, double rounded, int val)
        {
            if (exact > 0 && rounded > 0 && Math.Abs(Math.Abs(val) - rounded) < 0.001)
                return Math.Sign(val) * exact / 100.0;
            return val / 100.0;
        }

        // 画的时候真正用的位移：把存下来的值夹进当前量程，但**绝不写回 cfg**。
        //
        // ⚠ 这里以前是 ClampPan()，直接改 cfg.BgOffsetX/Y —— 那是个真 bug（2026-10-07 查实）：
        //   量程（PanXNow/PanYNow）是随窗口大小走的，所以每拖动一下窗口，
        //   就会把用户的位移夹掉一点，**而且窗口缩回去也不恢复**（实测：oy 18→17 之后就一直是 17）。
        //   用户看到的就是"伸缩软件的时候背景图会闪几下，之后就不闪了"——
        //   一直拖一直夹，夹到 0 就没得可夹、也就不闪了。
        //   存下来的值是用户的意图（"我要看图的这个位置"），换个窗口大小不该把它抹掉。
        //   夹在渲染这一层仍然保证"滑到头图边正好贴边、永不露底"这个承诺。
        static int EffectivePan(double limit, int val)
        {
            if (limit < 0) return val;              // 不限（只剩平铺）
            int m = (int)limit;
            return Math.Max(-m, Math.Min(m, val));
        }

        // 画刷上的位移量（图层尺寸的比例）。bgstate 用它证明"位移真的加在画刷上"。
        // 2026-10-07 起 RelativeTransform 变成了 TransformGroup（先缩放/镜像、再平移），
        // 所以要钻进组里找，不能再直接 as TranslateTransform —— 那样翻转一勾就全读成 0。
        // 2026-10-07 起位移做在**视口**上（不再是 TranslateTransform），所以这里换算成
        // "相对居中的偏移 ÷ 图层尺寸" —— 数值含义和以前完全一样，验收脚本不用改口径。
        string BgBrushOffset(bool x)
        {
            if (bgLayer == null || bgViewport.IsEmpty) return "0";
            double lw = bgLayer.ActualWidth, lh = bgLayer.ActualHeight;
            if (lw < 1 || lh < 1) return "0";
            double size = x ? lw : lh;
            double centered = ((x ? lw : lh) - (x ? bgViewport.Width : bgViewport.Height)) / 2.0;
            double off = (x ? bgViewport.X : bgViewport.Y) - centered;
            return (off / size).ToString("0.###");
        }

        static string BgBrushScale(ImageBrush b)
        {
            ScaleTransform s = FindBrushTransform(b, typeof(ScaleTransform)) as ScaleTransform;
            if (s == null) return "1,1";
            return s.ScaleX.ToString("0.###") + "," + s.ScaleY.ToString("0.###");
        }

        static Transform FindBrushTransform(ImageBrush b, Type kind)
        {
            if (b == null) return null;
            Transform direct = b.Transform;
            if (kind.IsInstanceOfType(direct)) return direct;
            TransformGroup g = b.RelativeTransform as TransformGroup;
            if (g != null)
            {
                foreach (Transform c in g.Children) if (kind.IsInstanceOfType(c)) return c;
                return null;
            }
            if (kind.IsInstanceOfType(b.RelativeTransform)) return b.RelativeTransform;
            return null;
        }

        // 背景图渲染出来在图层里占多大（DIP），只给 bgstate 看。
        // 这里要的是"**此刻**该是多大"，给 bgstate 当参照物用（viewport 和它对不上就是过期），
        // 所以用图层当前的实际尺寸算 —— 布局还没跟上时它会滞后，那正是我们要看出来的东西。
        string RenderedNow()
        {
            try
            {
                ImageBrush b = bgLayer == null ? null : bgLayer.Fill as ImageBrush;
                double rw, rh;
                if (!RenderedExtent(b, bgLayer.ActualWidth, bgLayer.ActualHeight, out rw, out rh)) return "\"?\"";
                return "\"" + (int)Math.Round(rw) + "x" + (int)Math.Round(rh) + "\"";
            }
            catch { return "\"?\""; }
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
                 // brushStretch 是画刷上**真正生效**的那个值。上面那个是从配置算出来的，
                 // 两者不一致就说明"改了放置方式但画刷没重建"——2026-10-04 那个 bug 就是因为
                 // 验收脚本只查了前者，所以一直是绿的。验收脚本现在两条都比。
                 + ",\"brushStretch\":\"" + (b == null ? "" : b.Stretch.ToString()) + "\""
                 // 位移到底加在哪儿：加在画刷上（bx/by）才对 —— 那样「铺满」裁掉的边能露回来；
                 // 加在图层上（bgLayer.RenderTransform）就只是把整块图推走。验收脚本两条都比。
                 + ",\"bx\":" + BgBrushOffset(true) + ",\"by\":" + BgBrushOffset(false)
                 // 视口 = 背景图当前占的矩形。位移就是挪它 —— 位移做在视口上没有"空出来的地方"，
                 // 所以「滑到头正好贴边、露出被裁掉的那部分」才真的成立。
                 + ",\"viewport\":\"" + (bgViewport.IsEmpty ? "" :
                       ((int)Math.Round(bgViewport.X) + "," + (int)Math.Round(bgViewport.Y) + "," +
                        (int)Math.Round(bgViewport.Width) + "x" + (int)Math.Round(bgViewport.Height))) + "\""
                 // 画刷上**真正生效**的缩放/镜像。跟 cfg 比：cfg 说翻了但 brushScale 还是 1,1，
                 // 就说明变换没落到画刷上（翻转一勾就失效的 bug 会在这里现形）。
                 + ",\"brushScale\":\"" + BgBrushScale(b) + "\""
                 + ",\"opacity\":" + (bgLayer == null ? 0 : (int)Math.Round(bgLayer.Opacity * 100))
                 + ",\"blur\":" + (cfg == null ? 0 : cfg.BgBlur)
                 + ",\"ox\":" + (cfg == null ? 0 : cfg.BgOffsetX)
                 + ",\"oy\":" + (cfg == null ? 0 : cfg.BgOffsetY)
                 // 原始的对齐方式字符串。光看 stretch 分不清 cover 和已删除的 free
                 // （两者都是 UniformToFill），所以"老设置里的 free 有没有被迁移掉"
                 // 只能靠这个字段验证。
                 + ",\"layout\":\"" + (cfg == null ? "" : cfg.BgLayout) + "\""
                 + ",\"flipH\":" + (cfg != null && cfg.BgFlipH ? "true" : "false")
                 + ",\"flipV\":" + (cfg != null && cfg.BgFlipV ? "true" : "false")
                 + ",\"zoom\":" + (cfg == null ? 100 : cfg.BgZoom)
                 // 当前位移滑块的上限（百分比）。-1 = 不限（只有平铺），0 = 没东西可挪。
                 + ",\"panx\":" + PanXNow.ToString("0.#") + ",\"pany\":" + PanYNow.ToString("0.#")
                 // 量程是拿图层尺寸和"渲染尺寸"算出来的，这两个数报出来才能核对量程对不对。
                 + ",\"layer\":\"" + (bgLayer == null ? "?" : (int)Math.Round(bgLayer.ActualWidth) + "x" + (int)Math.Round(bgLayer.ActualHeight)) + "\""
                 + ",\"rendered\":" + RenderedNow()
                 + ",\"scope\":\"" + (cfg == null ? "" : cfg.BgScope) + "\""
                 + ",\"scrim\":" + (cfg == null ? 0 : cfg.PanelScrim)
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
            if (btnCheckUpdate != null) btnCheckUpdate.Click += delegate { rec.UiCheckUpdate(); };
            if (btnPrepare != null) btnPrepare.Click += delegate { rec.UiPrepareObs(); };
            if (btnSet != null) btnSet.Click += delegate { rec.UiOpenSettings(); };
            if (btnOpenLog != null) btnOpenLog.Click += delegate { rec.UiOpenLog(); };
            if (btnSound != null) btnSound.Click += delegate { rec.UiToggleSound(); };
            if (btnGameAudio != null) btnGameAudio.Click += delegate { rec.UiToggleGameAudio(); };
            if (btnBgm != null) btnBgm.Click += delegate { rec.UiToggleBgm(); };
            if (btnBg != null) btnBg.Click += delegate { Ui.ShowBackgroundDialog(); };

            // 旧版 MainForm 里 list.ItemSelectionChanged 那根线（选中几个 → 「同时录制数量」跟着变）
            if (queueList != null) queueList.SelectionChanged += delegate { rec.UiOnSelectionChanged(); };

            if (chkPrepare != null) chkPrepare.Click += delegate { if (cfg != null) { cfg.AutoPrepareObs = chkPrepare.IsChecked == true; cfg.Save(); } };
            if (chkCloseObs != null) chkCloseObs.Click += delegate { if (cfg != null) { cfg.CloseObsAfter = chkCloseObs.IsChecked == true; cfg.Save(); } };
            if (chkRound != null) chkRound.Click += delegate { if (cfg != null) { cfg.RoundRecord = chkRound.IsChecked == true; cfg.Save(); } };
            if (cbMic != null) cbMic.SelectionChanged += delegate
            {
                if (cfg == null) return;
                // 锁着的时候这一次改动是 ApplyMicLock 自己塞进去的，不是用户选的 ——
                // 让它写回设置就把用户存着的「游戏+麦克风」冲掉了，数量改回 1 也回不来。
                if (micLockShown) return;
                // ⚠ 这里必须写 "gameAndMic"：录制路径（ApplyMicMode）比的就是这个词。
                //   2026-10-08 之前这里写的是 "both"，于是「游戏+麦克风」选了也白选 ——
                //   麦克风照样被静音（实测：set:cbMic=1 后文件里是 "both"，wantMuted=True）。
                cfg.MicMode = cbMic.SelectedIndex == 1 ? "gameAndMic" : "gameOnly";
                cfg.Save();
            };
            ApplyMicLock();     // 建完就按当前「同时录制数量」定一次锁

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
            soundTimer.Tick += delegate { try { rec.UiRefreshSoundButtons(); } catch { } try { rec.UiTick(); } catch { } };
            soundTimer.Start();

            // 旧版这段挂在 MainForm 的 Shown 事件上。移植时改成了"构造完直接调" —— 那时候
            // 可视树还没建起来，Ui 门面里"按 AutomationId 找控件再赋值"的那些（按钮宽度、
            // 前景/背景色）会**全部静默落空**；等两秒后声音状态定时器第一次跑才真正生效，
            // 于是看起来就是"开软件后控制面板里的声音按钮闪一下"（2026-10-05 用户报的）。
            // 挂到 Loaded 上，在第一次绘制之前就把状态设好，就不会有这一下。
            if (!IsLoaded) Loaded += delegate { rec.UiOnShown(args); };
            else rec.UiOnShown(args);
        }

        // 关窗口要先问录制逻辑：还在录的话不能直接退，否则 OBS 会一直在后台录下去。
        // 旧版是 MainForm 的 FormClosing += OnFormClosing（Core.cs），移植时整块漏了。
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (rec != null && !rec.UiOnClosing())
                {
                    e.Cancel = true;    // 拦住，等收尾线程把 worker 收干净再关
                    return;
                }
            }
            catch { }
            base.OnClosing(e);
        }

        // ---------------------------------------------------------------- 最大化别盖任务栏
        [StructLayout(LayoutKind.Sequential)] struct MPOINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)] struct MRECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public MPOINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public MRECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

        const int GWL_STYLE = -16;
        const int WS_CAPTION = 0x00C00000;
        [DllImport("user32.dll", SetLastError = true)] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // WindowStyle=None 的窗口会被 Windows 当成"没有标题栏"，连 WS_CAPTION 一起丢掉 ——
        // 而没有 CAPTION，DWM 就不给放最小化 / 还原 / 最大化的那套动画。
        // 把这一位补回去动画就回来了；标题栏仍然由 WindowChrome 自己画，用户看到的样子不变。
        static void AddCaptionStyle(IntPtr hwnd)
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            if ((style & WS_CAPTION) == WS_CAPTION) return;
            SetWindowLong(hwnd, GWL_STYLE, style | WS_CAPTION);
        }

        // ---------------------------------------------------------------- DWM 外观
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        // 把旧版 Core.cs 的 EnableDarkTitleBar() 搬过来 —— WPF 版一直没调用过它，
        // 所以窗口的四角一直是方的（实测 GWL_STYLE 有 WS_CAPTION/WS_THICKFRAME，
        // 但 DWMWA_WINDOW_CORNER_PREFERENCE 是 0=DEFAULT，DWM 就没给圆角）。
        //
        // 旧版还设了 attr 19/20（深色标题栏），那是给【系统标题栏】用的；
        // 这里的标题栏完全由 WindowChrome 自绘，没有作用对象，所以不搬 —— 搬了是死配置。
        static void ApplyDwmWindowLooks(IntPtr hwnd)
        {
            try
            {
                int round = 2;                                                 // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));       // DWMWA_WINDOW_CORNER_PREFERENCE
                // 圆角是 DWM 在合成层切的，不用开 AllowsTransparency（不影响硬件加速，
                // 也不影响那些靠 PrintWindow 截图采样的验收脚本）。
                // 但 DWM 会在窗口外围留 1px 描边，默认色压在深色顶栏边上会显出一道浅线；
                // 染成顶栏色就看不出来了。旧版注释里也是这个原因。
                int border = HudPanelColorRef();
                DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));      // DWMWA_BORDER_COLOR
            }
            catch { }   // Win10 上没有 attr 33/34，调用会失败 —— 不影响别的功能
        }

        // COLORREF 是 0x00BBGGRR，不是 WPF 的 #RRGGBB。
        // ColorTranslator 正好干这个转换，而且和旧版 Core.cs:2609 用的是同一个调用。
        static int HudPanelColorRef()
        {
            return System.Drawing.ColorTranslator.ToWin32(MainForm.HudPanel);
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0024)
            {
                try
                {
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(l, typeof(MINMAXINFO));
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    // ⚠ 这条消息【必须】自己把 ptMinTrackSize 也写上。
                    //   在 WPF 里，Window 的 MinWidth/MinHeight 是它自己处理 WM_GETMINMAXINFO
                    //   时换算成 ptMinTrackSize 的；这里 handled = true 把消息吞了，那个换算就
                    //   永远不会发生 —— 最小尺寸限制直接消失，窗口能拖到 700x500 还更小。
                    //   旧版 WinForms 那边为同一件事专门留过一句警告：
                    //   「拦下来不调 base 的话最小尺寸限制就没了（还能拖到比 760x520 更小）」。
                    //   移植时原样又踩了一遍（2026-10-05 实测：设了 MinWidth 仍然能缩到 700x500）。
                    double sc = 1.0;
                    try
                    {
                        HwndSource hs = HwndSource.FromHwnd(hwnd);
                        if (hs != null && hs.CompositionTarget != null)
                            sc = hs.CompositionTarget.TransformToDevice.M11;
                    }
                    catch { }
                    if (MinWidth > 0) mmi.ptMinTrackSize.X = (int)Math.Ceiling(MinWidth * sc);
                    if (MinHeight > 0) mmi.ptMinTrackSize.Y = (int)Math.Ceiling(MinHeight * sc);

                    if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi))
                    {
                        // ⚠-1：别把 ptMaxSize 设成【正好】等于工作区（2026-10-07 用户报"最大化之后
                        //     顶部贴着上面、不好看" — 根因就在这一句）。
                        //
                        // 实测规律（2560x1600 / 150%，边框 SM_CXSIZEFRAME+SM_CXPADDEDBORDER = 5+6 = 11px）：
                        //   请求 0,0 2560x1528（= 工作区）→ 实际 -11,-11 2582x1550
                        //     四条边各切掉 11px：顶栏那行字的上半截正好在屏幕外，看着就是"贴顶"。
                        //   请求 0,0 2560x1527（比工作区矮 1px）→ 实际 0,0 2560x1527（原样照搬）
                        // **只有"正好等于工作区"会触发那圈外扩，差一点点就原样照搬。**
                        // 机制没细究（推测是"要给客户区铺满工作区，就把带可缩放边框的那一圈甩到
                        // 屏幕外"——对普通窗口没错，但本窗口客户区 == 整个窗口，甩出去的就是界面）；
                        // 反正改一次量一次，量出来的就是上面这两行，照它来。
                        // 所以这里故意矮 1 物理像素，代价是屏幕最底下留一条 1px 的缝（紧贴任务栏，
                        // 看不出来）。试过把这 1px 补偿回去的两种办法，都不行：
                        //   · 把 ptMaxSize 先减去那一圈、指望 Windows 加回来 —— 它这次原样照搬，
                        //     窗口四周反而各缩进 11px、把桌面露出来（有截图）；
                        //   · 最大化之后再 SetWindowPos 钉回工作区 —— **对最大化窗口无效**，
                        //     而且它自己也会按"给的矩形是要铺满的区域"再外扩一圈，越弄越糟。
                        // x 方向不用减：实测 2560x1527（右边正好顶到屏幕）也是原样照搬，
                        // 只减 y 是为了让左右两条竖边齐平 —— 竖直的缝比水平的缝显眼。
                        mmi.ptMaxPosition.X = mi.rcWork.L - mi.rcMonitor.L;
                        mmi.ptMaxPosition.Y = mi.rcWork.T - mi.rcMonitor.T;
                        mmi.ptMaxSize.X = mi.rcWork.R - mi.rcWork.L;
                        mmi.ptMaxSize.Y = (mi.rcWork.B - mi.rcWork.T) - 1;
                    }
                    Marshal.StructureToPtr(mmi, l, false);
                    handled = true;
                }
                catch { }
            }
            return IntPtr.Zero;
        }
    }
}
