// ============================================================
//  Ui.cs —— WPF 界面层（新写的那一半）
//
//  阶段 0.3 只做「壳」：无边框窗口 + 顶栏/左栏/右栏/底栏骨架 + 背景图层 +
//  调色板（直接引用 MainForm.Hud* 那几个常量，保证和旧版是同一个真源，阶段 5
//  再把它们搬过来）。数据、卡片内容、列表、终端都在后面的阶段填。
//
//  跑法：RepRecorderWPF.exe --wpf      （不带 --wpf 还是旧的那套 WinForms）
//  参考实现：D:\WPF样机\WpfProbe.cs（已实测：缩放 9.2ms/步、DPI 感知不糊、最大化不盖任务栏）
// ============================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
using Drawing = System.Drawing;

namespace RepRecorder
{
    static partial class Ui
    {
        static Settings cfg;

        // ---------------------------------------------------------------- 入口
        public static void Start(string[] args)
        {
            MakeDpiAware();
            cfg = Settings.Load();
            // 只认 --update-url=（验收脚本用，正式用户不会带）—— 必须在 StartBackgroundCheck
            // 之前调，所以放在这儿、离启动最近的地方。
            UpdateCheck.ApplyUrlOverride(args);
            RepRecorder.Log.Write("==== WPF 界面启动（阶段 0.3：骨架）====");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                Recorder rec = new Recorder(cfg);
                ShellWindow win = new ShellWindow(cfg, rec);
                // ⚠ 旧版 MainForm 的构造函数里订阅了 Log.OnLine，终端面板才能实时显示日志。
                //   这一段搬过来时漏了 —— 结果日志只进文件、面板一直空着（冒烟测试截图里看出来的）。
                RepRecorder.Log.OnLine += delegate(string line) { Ui.Log(line); };
                DebugPort.TryStart(args, win);       // 只有带 --debug-port=<端口> 时才起
                win.HookRecorder(args);              // 按钮/拖放/定时器接到录制逻辑上
                app.Run(win);
            }
            catch (Exception ex)
            {
                RepRecorder.Log.Write("WPF 界面崩溃：" + ex);
                MessageBox.Show(ex.ToString(), "RepRecorder (WPF) 出错");
            }
        }

        // DPI 感知：**运行时设**，不要写进 manifest。
        //   manifest 是整个 exe 生效的，而迁移期间同一个 exe 里还跑着旧的 WinForms 界面 ——
        //   那套界面是按"DPI 不感知"布局的，一旦变感知，在 300% 屏上会缩成原来的 1/3。
        //   所以只有走 WPF 这条路时才设，而且必须在创建任何窗口之前。
        static void MakeDpiAware()
        {
            // -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero) return; } catch { }
            try { SetProcessDPIAware(); } catch { }   // 老系统退回 system-DPI-aware
        }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);

        // ---------------------------------------------------------------- 给调试接口用的小工具
        public static Settings CfgNow { get { return cfg; } }

        // 在 UI 线程上跑一段并取回结果（调试接口跑在别的线程上，碰控件必须绕回 UI 线程）
        // ⚠ `app == null` 那一支是【在调用线程上直接跑】的。调用方要是后台线程，那就等于
        //   从非 UI 线程碰控件 —— 有时抛异常、有时静默画错，现场极难看懂。
        //   2026-10-10 写"后台找到东西之后通知界面"时差点踩中：扫描可能在 new Application()
        //   之前就跑完。这一支留着是因为启动早期确实要靠它，但现在会留一行日志，
        //   免得下次又靠猜（当时是靠"把通知改挂到已有的 UI 定时器上"绕开的）。
        public static T OnUi<T>(Func<T> f)
        {
            Application app = Application.Current;
            if (app == null)
            {
                if (System.Threading.Thread.CurrentThread.IsBackground)
                    RepRecorder.Log.Write("⚠ 界面还没建好，后台线程直接调了 OnUi（会在该线程上执行）：" +
                              (f == null || f.Method == null ? "?" : f.Method.Name));
                return f();
            }
            if (app.Dispatcher.CheckAccess()) return f();
            return app.Dispatcher.Invoke<T>(f);
        }

        // ---------------------------------------------------------------- 调色板
        // 和旧版共用 MainForm 里那几个常量（同一个真源，免得两边颜色慢慢跑偏）。
        // 阶段 5 会把它们搬到这里，旧界面一删就没人引用了。
        public static SolidColorBrush Br(Drawing.Color c)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        public static SolidColorBrush Br(Drawing.Color c, byte alpha)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        static Drawing.Color CBg { get { return MainForm.HudBg; } }
        static Drawing.Color CPanel { get { return MainForm.HudPanel; } }
        static Drawing.Color CPanel2 { get { return MainForm.HudPanel2; } }
        static Drawing.Color CLine { get { return MainForm.HudLine; } }
        static Drawing.Color CLineDim { get { return MainForm.HudLineDim; } }
        static Drawing.Color CCorner { get { return MainForm.HudCorner; } }
        static Drawing.Color CText { get { return MainForm.HudText; } }
        static Drawing.Color CDim { get { return MainForm.HudDim; } }
        static Drawing.Color CAccent { get { return MainForm.HudAccent; } }
        static Drawing.Color CAccent2 { get { return MainForm.HudAccent2; } }
        static FontFamily UiFont { get { return new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI"); } }

        // 面板底色：一个【不冻结】的共享画刷 —— 顶栏 / 底栏 / 队列表头 / 每张卡片全都用这一个实例。
        // 改它的颜色，所有面板立刻跟着变，不用重建界面（缩放的性能就是靠这个才没被拖垮：
        // 之前试过给每个 TextBlock 挂 Effect 做描边，窗口缩放直接从 17ms 掉到 35ms）。
        // 强度来自设置里的「面板暗化」：0 = 完全透明（图片零遮挡），100 = 最暗。
        static readonly SolidColorBrush ScrimBrush = new SolidColorBrush();
        public static SolidColorBrush Scrim { get { return ScrimBrush; } }

        // 窗口底色（不透明）：左栏那种"底下没有背景图"的区域就是它。
        // 它也跟「面板暗化」走 —— 只铺右侧时左栏本来是一块固定的深色底，
        // 滑块拉到底也没反应（用户 2026-10-04 报的"面板暗化对控制面板不生效"）。
        static readonly SolidColorBrush BaseBrush = new SolidColorBrush();
        public static SolidColorBrush Base { get { return BaseBrush; } }

        public static void SetScrim(int percent)
        {
            int p = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
            Drawing.Color c = CPanel;
            ScrimBrush.Color = Color.FromArgb((byte)(p * 255 / 100), c.R, c.G, c.B);

            // 底色 = 在 HudBg 上按同样的比例掺入面板色，这样左右两边"掺的量"一致
            Drawing.Color b = MainForm.HudBg;
            BaseBrush.Color = Color.FromArgb(255,
                (byte)(b.R + (c.R - b.R) * p / 100),
                (byte)(b.G + (c.G - b.G) * p / 100),
                (byte)(b.B + (c.B - b.B) * p / 100));
        }

        static FontFamily MonoFont { get { return new FontFamily("Consolas, Courier New"); } }

        // ---------------------------------------------------------------- 小零件
        public static void Id(FrameworkElement el, string automationId)
        {
            AutomationProperties.SetAutomationId(el, automationId);
        }

        public static TextBlock Txt(string s, double size, Drawing.Color fg, bool mono)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = Br(fg);
            t.FontFamily = mono ? MonoFont : UiFont;
            return t;
        }

        // 卡片：半透明底 + 一圈蓝灰线 + 图标标题 + 标题下一条分隔线（和旧版那张 HUD 一个样子）
        public static Grid Card(string title, UIElement content, string automationId)
        {
            return Card(title, null, content, automationId, 10);
        }

        public static Grid Card(string title, string icon, UIElement content, string automationId, double pad)
        {
            return Card(title, icon, content, automationId, pad, false);
        }

        // flush = true：卡片严丝合缝铺满自己那一格 —— 去掉下沿留白、去掉圆角。
        // 右侧的队列 / 终端用这个：几块 + 中间那条可拖分隔条拼起来正好是一整块完整的板，
        // 背景不会从卡片下沿和圆角处漏出来（以前就是漏在那里的）。
        public static Grid Card(string title, string icon, UIElement content, string automationId, double pad, bool flush)
        {
            Grid outer = new Grid();
            outer.Margin = flush ? new Thickness(0) : new Thickness(0, 0, 0, 12);

            Border bd = new Border();
            bd.Background = Scrim;                  // 淡淡一层（强度见设置里的「面板暗化」）
            bd.BorderBrush = Br(CLineDim);          // 极淡的一圈描边，别抢背景图
            bd.BorderThickness = new Thickness(1);
            bd.CornerRadius = flush ? new CornerRadius(0) : new CornerRadius(10);
            outer.Children.Add(bd);

            // ⚠ 必须是 Grid，不能是 StackPanel：StackPanel 在堆叠方向上给子元素**无限高度**，
            // 队列/终端里的 ListBox 于是永远不觉得自己需要滚动条，会一路撑到内容那么高。
            // 2026-10-05 实测：塞 12 行时 ListBox 高度 882 DIP，而窗口才 648 DIP ——
            // 底下几行既滚不到也点不到；列表被行铺满没有空白处，框选也没法起手。
            // 两个毛病同一个根因。Grid 的星号行会把"剩余高度"交给内容，ListBox 才受约束。
            Grid body = new Grid();
            body.Margin = new Thickness(12, 10, 12, 12);   // 新设计稿：内边距更宽松
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 6) };
            string caption = string.IsNullOrEmpty(icon) ? title : icon + " " + title;
            head.Children.Add(Txt(caption, 12.5, CText, false));
            body.Children.Add(head);

            Grid.SetRow(content, 1);
            body.Children.Add(content);
            outer.Children.Add(body);

            if (!string.IsNullOrEmpty(automationId)) Id(outer, automationId);
            return outer;
        }

        // 一行横排（旧版的 FlowLayoutPanel LeftToRight）
        public static StackPanel Row(double topMargin, params UIElement[] items)
        {
            StackPanel p = new StackPanel();
            p.Orientation = Orientation.Horizontal;
            p.Margin = new Thickness(0, topMargin, 0, 0);
            foreach (UIElement e in items) p.Children.Add(e);
            return p;
        }

        // 左栏里的小灰字（"约 3.2 GB" / "本机静音，录像里照样有声音"）
        public static TextBlock Note(string s, string automationId, double top)
        {
            TextBlock t = Txt(s, 11.5, CDim, false);
            t.Margin = new Thickness(0, top, 0, 2);
            t.TextWrapping = TextWrapping.Wrap;
            if (!string.IsNullOrEmpty(automationId)) Id(t, automationId);
            return t;
        }

        // 勾选框：自己画（系统默认那个白框压在深色主题上太跳）。旧版 HudCheck 也是自绘的。
        public static CheckBox HudCheck(string text, string automationId, bool isChecked, double top)
        {
            CheckBox c = new CheckBox();
            c.Content = text;
            c.IsChecked = isChecked;
            c.Foreground = Br(CText);
            c.FontFamily = UiFont;
            c.FontSize = 11.5;
            c.Margin = new Thickness(0, top, 0, 0);
            c.Cursor = Cursors.Hand;
            c.Template = CheckTemplate();
            Id(c, automationId);
            return c;
        }

        static ControlTemplate CheckTemplate()
        {
            ControlTemplate t = new ControlTemplate(typeof(CheckBox));
            FrameworkElementFactory g = new FrameworkElementFactory(typeof(StackPanel));
            g.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory box = new FrameworkElementFactory(typeof(Border));
            box.Name = "box";
            box.SetValue(Border.WidthProperty, 13.0);
            box.SetValue(Border.HeightProperty, 13.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            box.SetValue(Border.BackgroundProperty, Br(CPanel2));
            box.SetValue(Border.BorderBrushProperty, Br(CLine));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 6, 0));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            g.AppendChild(box);

            FrameworkElementFactory tick = new FrameworkElementFactory(typeof(TextBlock));
            tick.Name = "tick";
            tick.SetValue(TextBlock.TextProperty, "✓");
            tick.SetValue(TextBlock.FontSizeProperty, 10.0);
            tick.SetValue(TextBlock.ForegroundProperty, Br(CAccent2));
            tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tick.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(tick);

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            g.AppendChild(cp);
            t.VisualTree = g;

            Trigger on = new Trigger { Property = CheckBox.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BorderBrushProperty, Br(CAccent), "box"));
            on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "tick"));
            t.Triggers.Add(on);
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Br(CAccent), "box"));
            t.Triggers.Add(hover);
            return t;
        }

        // 下拉框：系统默认在深色主题上是一块浅灰，自己画一个（钢蓝底 + 右边小三角）
        public static ComboBox HudCombo(string automationId, double width, string[] items, int sel)
        {
            ComboBox cb = new ComboBox();
            cb.Width = width;
            cb.Height = 22;
            cb.FontFamily = UiFont;
            cb.FontSize = 12.5;
            cb.Foreground = Br(CText);
            cb.Background = Br(MainForm.HudLine);      // 旧版 cbParallel 的 Face = HudLine（钢蓝底）
            cb.VerticalContentAlignment = VerticalAlignment.Center;
            foreach (string s in items) cb.Items.Add(s);
            if (sel >= 0 && sel < items.Length) cb.SelectedIndex = sel;
            cb.Template = ComboTemplate();
            if (!string.IsNullOrEmpty(automationId)) Id(cb, automationId);
            return cb;
        }

        static ControlTemplate ComboTemplate()
        {
            ControlTemplate t = new ControlTemplate(typeof(ComboBox));
            FrameworkElementFactory g = new FrameworkElementFactory(typeof(Grid));

            FrameworkElementFactory tg = new FrameworkElementFactory(typeof(ToggleButton));
            tg.Name = "Toggle";
            tg.SetValue(ToggleButton.FocusableProperty, false);
            tg.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            tg.SetValue(ToggleButton.BackgroundProperty, new TemplateBindingExtension(ComboBox.BackgroundProperty));
            tg.SetValue(ToggleButton.BorderThicknessProperty, new Thickness(1));
            tg.SetValue(ToggleButton.BorderBrushProperty, Br(CLine));
            tg.SetValue(ToggleButton.TemplateProperty, ComboToggleTemplate());
            tg.SetValue(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
            g.AppendChild(tg);

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 20, 0));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(UIElement.IsHitTestVisibleProperty, false);
            g.AppendChild(cp);

            FrameworkElementFactory arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "▾");
            arrow.SetValue(TextBlock.FontSizeProperty, 10.0);
            arrow.SetValue(TextBlock.ForegroundProperty, Br(CText));
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            g.AppendChild(arrow);

            FrameworkElementFactory pop = new FrameworkElementFactory(typeof(Popup));
            pop.Name = "PART_Popup";
            pop.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            pop.SetValue(Popup.AllowsTransparencyProperty, true);
            pop.SetValue(Popup.IsOpenProperty, new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));
            FrameworkElementFactory pb = new FrameworkElementFactory(typeof(Border));
            pb.SetValue(Border.BackgroundProperty, Br(CPanel));
            pb.SetValue(Border.BorderBrushProperty, Br(CLine));
            pb.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            pb.SetValue(Border.PaddingProperty, new Thickness(0, 2, 0, 2));
            // 弹出列表至少和上面的框一样宽 —— 不然选项行只有文字那么宽，能点中的范围太小
            // （用户 2026-10-05 报的"选数字鼠标能点到的范围太小"）。
            // 用 TemplatedParent 而不是 FindAncestor：弹出内容不在 ComboBox 的可视树里。
            pb.SetValue(FrameworkElement.MinWidthProperty,
                        new Binding("ActualWidth") { RelativeSource = RelativeSource.TemplatedParent });
            FrameworkElementFactory ip = new FrameworkElementFactory(typeof(ItemsPresenter));
            pb.AppendChild(ip);
            pop.AppendChild(pb);
            g.AppendChild(pop);

            t.VisualTree = g;
            return t;
        }

        static ControlTemplate ComboToggleTemplate()
        {
            ControlTemplate t = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(ToggleButton.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(ToggleButton.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(ToggleButton.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            t.VisualTree = bd;
            return t;
        }

        // 带图标的按钮（目前只有"开始录制 / 停止"这一对）。
        //  ① 内容块整体**居中**，不顶在最左边（2026-10-05 用户第二次提的）。
        //  ② 图标列固定宽 + 文字列固定宽，两块加起来是同一个宽度，
        //     所以两个按钮的 ▶ 和 ■ 落在同一个 x 上（第一次提的"图标不是对齐的"）。
        //     两列都用 Auto 的话，文字长的那个整体就宽，居中了以后图标还是会错开。
        const double IconColW = 20;   // 图标列：▶ / ■ 都居中在这一列里
        const double TextColW = 52;   // 文字列：够放下最长的"开始录制"（4 个汉字 @12px = 48）
        public static Button FlatButton(string icon, string text, string automationId)
        {
            Button b = FlatButton("", automationId);
            // 光给内层 Grid 设对齐没用：外层 ContentPresenter 是模板里的，
            // 要对齐得设按钮自己的 HorizontalContentAlignment（模板已把它绑到 ContentPresenter）。
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            Grid g = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = IconColW + TextColW,
            };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColW) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TextColW) });
            TextBlock ic = new TextBlock { Text = icon, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(ic, 0);
            g.Children.Add(ic);
            // 文字在它那 52 宽的列里也居中：这样"停止"（2 个字）不会比"开始录制"（4 个字）
            // 更靠左，两个按钮看上去才是一对。
            TextBlock tx = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            Grid.SetColumn(tx, 1);
            g.Children.Add(tx);
            b.Content = g;
            return b;
        }

        public static Button FlatButton(string text, string automationId)
        {
            Button b = new Button();
            b.Content = text;
            b.Foreground = Br(CText);
            b.Background = Br(CPanel2, 0xD8);
            b.BorderBrush = Br(CLine);
            b.BorderThickness = new Thickness(1);
            b.FontFamily = UiFont;
            b.FontSize = 12;
            b.Cursor = Cursors.Hand;
            b.Template = FlatTemplate();
            if (!string.IsNullOrEmpty(automationId)) Id(b, automationId);
            return b;
        }

        static ControlTemplate FlatTemplate()
        {
            ControlTemplate t = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "b";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            // 绑到按钮自己的 HorizontalContentAlignment，别写死 Center ——
            // 写死的话，带图标的按钮想左对齐都做不到（2026-10-05 图标对不齐就是这个）。
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                        new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Br(CAccent, 0x66), "b"));
            t.Triggers.Add(hover);
            Trigger press = new Trigger { Property = Button.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(Border.BackgroundProperty, Br(CAccent, 0x99), "b"));
            t.Triggers.Add(press);
            return t;
        }

        // ================================================================ Ui 门面（阶段 5）
        //  录制逻辑（Recorder.cs）唯一能碰界面的入口。全部方法都是"UI 线程上跑一段"，
        //  从子线程调也安全（内部走 Dispatcher）。
        static ShellWindow W { get { return Application.Current == null ? null : Application.Current.MainWindow as ShellWindow; } }

        public static void Post(Action a)
        {
            Application app = Application.Current;
            if (app == null) { a(); return; }
            if (app.Dispatcher.CheckAccess()) a();
            else app.Dispatcher.BeginInvoke(a);
        }

        public static void Status(string s)
        {
            Post(delegate { if (W != null && W.StatusText != null) W.StatusText.Text = s; });
        }


        public static void Env(string s, int level)
        {
            Post(delegate
            {
                if (W == null || W.StatusEnv == null) return;
                W.StatusEnv.Text = s;
                W.StatusEnv.Foreground = Br(level == 1 ? CAccent2 : level == 2 ? (Drawing.Color)MainForm.HudOrange
                                          : level == 3 ? (Drawing.Color)MainForm.HudRed : CDim);
            });
        }

        public static void Log(string s)
        {
            Post(delegate { if (W != null) W.AppendLog(s); });
        }

        // 收尾线程用它再关一次窗（那时候 ShellWindow.OnClosing 会放行）
        public static void CloseMain()
        {
            Post(delegate { if (W != null) W.Close(); });
        }

        public static void Hint(string s)
        {
            Post(delegate
            {
                if (W == null || W.HintText == null) return;
                W.HintText.Text = s;
                W.HintText.Visibility = string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        public static void ParHint(string s)
        {
            Post(delegate { if (W != null && W.ParHint != null) W.ParHint.Text = s == null ? "" : s; });
        }

        public static int ParallelCount
        {
            get { return W == null || W.ComboParallel == null ? 0 : W.ComboParallel.SelectedIndex + 1; }
        }

        public static void SetParallel(int n)
        {
            Post(delegate { if (W != null && W.ComboParallel != null) W.ComboParallel.SelectedIndex = n - 1; });
        }

        public static int RowCount { get { return W == null ? 0 : W.Rows.Count; } }

        public static int SelectedJobCount { get { return W == null ? 0 : W.QueueList.SelectedItems.Count; } }

        public static void SetBusy(bool recording)
        {
            Post(delegate
            {
                if (W == null) return;
                W.SetRecordingLook(recording);
            });
        }

        // 把一整批 Job 塞进列表（旧版是 BeginUpdate/Add/EndUpdate）
        public static void AddRows(List<Job> jobs)
        {
            Post(delegate
            {
                if (W == null) return;
                foreach (Job j in jobs) W.AddRow(j);
            });
        }

        public static void AddRow(Job j)
        {
            Post(delegate { if (W != null) W.AddRow(j); });
        }

        public static void ClearRows()
        {
            Post(delegate { if (W != null) W.Rows.Clear(); });
        }

        public static void SetJob(Job j, string status, string detail)
        {
            Post(delegate { if (W != null) W.UpdateRow(j, status, detail); });
        }

        public static Job[] SelectedJobs()
        {
            if (W == null) return new Job[0];
            List<Job> l = new List<Job>();
            foreach (object o in W.QueueList.SelectedItems)
            {
                RowVm r = o as RowVm;
                if (r != null && r.Job != null) l.Add(r.Job);
            }
            return l.ToArray();
        }

        public static void RemoveRow(Job j) { Post(delegate { if (W != null) W.RemoveRow(j); }); }
        public static void RenumberRows() { Post(delegate { if (W != null) W.RenumberRows(); }); }
        public static void ClearSelection() { Post(delegate { if (W != null) W.ClearSelection(); }); }

        public static void HighlightBatch(List<Job> batch)
        {
            Post(delegate { if (W != null) W.HighlightBatchRows(batch); });
        }

        public static void ScrollToBatch(List<Job> batch)
        {
            Post(delegate { if (W != null) W.ScrollRowsIntoView(batch == null || batch.Count == 0 ? null : batch[0]); });
        }

        public static void SyncScroll()
        {
            Post(delegate { if (W != null) W.SyncScrollToBottom(); });
        }

        // 旧版 HudSetToggle(btn, on) 的语义：亮底+亮字=开，暗底+暗字=关，描边固定
        public static void ToggleLook(UiButton b, bool on)
        {
            if (b == null) return;
            Post(delegate
            {
                Ui.SetButtonBackColor(b.Id, on ? MainForm.HudOn : MainForm.HudOff);
                Ui.SetButtonForeColor(b.Id, on ? MainForm.HudText : MainForm.HudDim);
                Ui.SetButtonBorderColor(b.Id, MainForm.HudLine);
            });
        }

        public static void SoundText(string text, bool on)
        {
            Post(delegate { W.SetToggleText("btnSound", text, on); });
        }

        public static void GameAudioText(string text, bool on)
        {
            Post(delegate { W.SetToggleText("btnGameAudio", text, on); });
        }

        public static void BgmText(string text, bool on)
        {
            Post(delegate { W.SetToggleText("btnBgm", text, on); });
        }

        // 设置窗口（旧版是一个 PropertyGrid 对话框）：反射列出 Settings 的所有公开属性，
        // 能改的直接给个输入框。够用就好 —— 主要是"背景/保存位置/并行数"这些。
        public static void ShowSettings()
        {
            ShellWindow w = W;
            if (w == null) return;
            SettingsBox box = new SettingsBox(w.CfgPublic);
            box.Owner = w;
            box.ShowDialog();
            if (box.Ok)
            {
                try { w.CfgPublic.Save(); } catch { }
                w.ComboParallelSelectedIndexRefresh();
                // 音频那几项（录打击音效 / 录BGM / 录音模式）改了要立刻生效，
                // 不然设置里写着"开"、按钮却还显示"关"，要等下一次动作才对上。
                try { if (w.RecPublic != null) w.RecPublic.UiApplyAudioSettings(); } catch { }
                RepRecorder.Log.Write("设置已保存");
            }
        }

        // 门面：背景设置窗（顶栏「背景…」按钮）
        public static void ShowBackgroundDialog()
        {
            ShellWindow w = W;
            if (w == null) return;
            BackgroundBox box = new BackgroundBox(w);
            box.Owner = w;
            box.ShowDialog();
        }

        // 门面：弹出环境自检窗 —— 只报「缺什么、去哪儿补」，不装任何东西。
        // ⚠ 必须跑在 UI 线程上（里面会 ShowDialog）。
        public static void ShowEnvDialog(EnvReport rep)
        {
            ShellWindow w = W;
            if (w == null) return;
            EnvBox box = new EnvBox(rep);
            box.Owner = w;
            box.ShowDialog();
        }

        // 环境卡还开着的话，按新报告把内容重画一遍。
        // 用在"后台找游戏 / 找 ffmpeg 落定"的时候 —— 不然卡上会一直写着「未找到」，
        // 而设置里其实已经填好了（2026-10-09 用户报的那个矛盾）。
        // ⚠ 必须在 UI 线程上调（调用方是那 2 秒的界面定时器，本来就是）。
        public static void RefreshEnvDialogIfOpen(EnvReport rep)
        {
            EnvBox b = EnvBox.Current;
            if (b == null || rep == null) return;
            try { b.Render(rep); }
            catch (Exception ex) { RepRecorder.Log.Write("环境卡刷新失败（不影响使用）：" + ex.Message); }
        }

        // 门面：弹出"发现新版本"（模态，和 EnvBox 一样）
        public static void ShowUpdateDialog(UpdateInfo info)
        {
            ShellWindow w = W;
            if (w == null || info == null) return;
            UpdateBox box = new UpdateBox(info);
            box.Owner = w;
            box.ShowDialog();
        }

        // 门面：弹一个只说一句话的小窗（模态）。手动「检查更新」的"已是最新 / 检查失败"用它。
        // ⚠ 必须跑在 UI 线程上（里面会 ShowDialog）。
        public static void ShowInfo(string title, string msg)
        {
            ShellWindow w = W;
            if (w == null) return;
            InfoBox box = new InfoBox(title, msg);
            box.Owner = w;
            box.ShowDialog();
        }

        // 「检查更新」按钮的忙 / 闲：检查中变灰、文字改「检查中…」，免得连点把服务器敲爆。
        // 走 Post（不阻塞调用方）—— 后台线程收尾时也会调它。
        public static void SetCheckUpdateBusy(bool busy)
        {
            Post(delegate
            {
                if (W == null || W.CheckUpdateButton == null) return;
                W.CheckUpdateButton.IsEnabled = !busy;
                W.CheckUpdateButton.Content = busy ? "检查中…" : "检查更新";
            });
        }

        // 门面：弹出"视频放哪"的询问框（返回 Choice/Checked/Picked）
        public static AskResult ShowAskDialog(bool upfront, string info, string lastFolder, string checkText)
        {
            ShellWindow w = W;
            if (w == null) return null;
            AskDialog d = new AskDialog(upfront, info, lastFolder, checkText);
            d.Owner = w;
            d.ShowDialog();
            AskResult r = new AskResult();
            r.Choice = d.Choice;
            r.Checked = d.Checked;
            r.Picked = d.Picked;
            return r;
        }

        public static void SetButtonWidth(string id, double w)
        {
            Post(delegate
            {
                FrameworkElement e = Elm(id);
                if (e != null) e.Width = w;
            });
        }

        // 滑杆：系统默认那条浅灰在深色主题上很跳，自己画一条
        // 滑杆模板：Track 的三个部件是只读 CLR 属性（代码里 SetValue 设不了），所以走 XAML
        const string SliderXaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
  <Grid>
    <Border Height='4' CornerRadius='2' Background='#38425A' VerticalAlignment='Center'/>
    <Track x:Name='PART_Track'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='Slider.DecreaseLarge' Opacity='0' Focusable='False'/>
      </Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='Slider.IncreaseLarge' Opacity='0' Focusable='False'/>
      </Track.IncreaseRepeatButton>
      <Track.Thumb>
        <Thumb Width='12' Height='18'>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'>
              <Border Background='#4F74D8' CornerRadius='3' BorderThickness='1' BorderBrush='#E6EBF4'/>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </Grid>
</ControlTemplate>";

        // ---------------------------------------------------------------- 滚动条样式
        // 主窗口和所有对话框共用这一套（对话框是独立窗口，不会自动继承主窗口的 Resources ——
        // 设置窗原来就因此露出了系统原生那条浅灰的，在深色底上很扎眼，2026-10-05 用户报的）。
        static Style scrollBarBase;

        const string HudScrollBarXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
            </Track.DecreaseRepeatButton>
            <!-- 滑块**不画圆角**（CornerRadius=0）。
                 终端行数一多，滑块的自然长度只有 8 DIP 左右；圆角在这种矮矩形上渲染出来
                 是「上面圆、下面被切平」的半椭圆（2026-10-05 用户报的就是这个）。
                 试过 Thumb 的 MinHeight、Border 的 MinHeight、ClipToBounds=False，
                 在真程序里都没能让它变长或者变圆 —— 所以直接用实心小方块，
                 形状至少是完整的、不会看着像坏了。
                 ⚠ 本段是 C# 逐字字符串，注释里不能出现双引号，只能用「」。 -->
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border x:Name='tb' Background='#5A6E8C' CornerRadius='0' Margin='2,0' Opacity='0.85'/>
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

        public static Style HudScrollBar(double width)
        {
            if (scrollBarBase == null) scrollBarBase = (Style)XamlReader.Parse(HudScrollBarXaml);
            Style st = new Style(typeof(ScrollBar), scrollBarBase);
            st.Setters.Add(new Setter(FrameworkElement.WidthProperty, width));
            // ⚠ 光加 Setter 没用：ScrollBar 自带样式的宽度是靠**触发器**（Orientation=Vertical）
            //   设成 SystemParameters.VerticalScrollBarWidth（17）的，优先级压过应用层的 Setter，
            //   于是滚动条一直是 17 DIP 宽（2026-10-05 用"把滑块涂成红的"量出来的：39px = 13 DIP
            //   加上左右各 2 的 Margin 正好 17）。应用层也放一个同属性的触发器才压得住。
            Trigger tg = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Vertical };
            tg.Setters.Add(new Setter(FrameworkElement.WidthProperty, width));
            st.Triggers.Add(tg);
            return st;
        }

        public static ScrollBar FindVerticalScrollBar(DependencyObject root)
        {
            if (root == null) return null;
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(root); } catch { return null; }
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(root, i);
                ScrollBar sb = c as ScrollBar;
                if (sb != null && sb.Orientation == Orientation.Vertical) return sb;
                ScrollBar r = FindVerticalScrollBar(c);
                if (r != null) return r;
            }
            return null;
        }

        // ⚠ ScrollBar 的宽度**光靠样式压不住**：它自带样式的宽度是 Orientation 触发器设的
        //   （SystemParameters.VerticalScrollBarWidth = 17），实测无论我们把 Width 写成
        //   应用层 Setter 还是应用层触发器，量出来都还是 17 DIP（39px 的滑块减去左右各 2 的
        //   Margin 正好 17 —— 2026-10-05 把滑块涂成红色量出来的）。
        //   只有**局部值**压得住。所以等它进了可视树之后直接改 Width，
        //   并挂在 ScrollChanged 上兜底（模板重建过也会补回来）。
        public static void PinScrollBarWidth(ScrollViewer sv, double w)
        {
            if (sv == null) return;
            // ⚠ 判断里必须先处理 NaN：Width 默认就是 NaN，而 Math.Abs(NaN - w) > 0.1 是 **false**
            //   —— 2026-10-05 第一版就栽在这，等于一次都没设过。
            Action pin = delegate
            {
                ScrollBar b = FindVerticalScrollBar(sv);
                if (b == null) return;
                if (double.IsNaN(b.Width) || Math.Abs(b.Width - w) > 0.1) b.Width = w;
            };
            sv.ScrollChanged += delegate { pin(); };
            sv.Loaded += delegate { pin(); };
        }

        public static Slider HudSlider(double min, double max, double val, string id)
        {
            Slider sl = new Slider();
            sl.Minimum = min; sl.Maximum = max; sl.Value = val;
            sl.Width = 250; sl.Height = 20;
            sl.IsSnapToTickEnabled = false;
            sl.Template = (ControlTemplate)XamlReader.Parse(SliderXaml);
            if (!string.IsNullOrEmpty(id)) Id(sl, id);
            return sl;
        }

    }

    // 询问框的返回值
    public class AskResult
    {
        public int Choice = -1;
        public bool Checked = false;
        public string Picked = null;
    }
}
