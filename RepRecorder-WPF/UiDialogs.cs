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
    // ================================================================ 环境自检窗
    //
    // 2026-10-08：新用户拿到包，双击之后"缺什么、怎么补"必须一次说清楚。
    // 以前这件事只写进日志（用户得自己去翻终端），环境卡又只报两项 ——
    // 缺 ffmpeg 的时候卡片还写着「已就绪」，并行录出来却是每一格「切分失败」。
    //
    // 这个窗【只报事实 + 给出口】：缺 OBS 就给 winget 命令让他自己装（不替他装），
    // 缺 ffmpeg 才由程序代下（用户点一下才下，100 MB 不静默拉）。
    class EnvBox : Window
    {
        EnvReport rep;

        // 当前开着的那张卡（同一时刻只会有一张）。
        // 这张卡本来是按"弹出那一刻的事实"一次性画好的；而自动找游戏 / 找 ffmpeg 是**后台**
        // 跑的，落定的时候卡可能还开着（典型：用户点「检查环境」，那会强制重新查找）。
        // 那时卡上还写着「未找到」，用户进设置却看到路径已经填好了 —— 2026-10-09 用户报的
        // 就是这个矛盾。所以留一个出口，让搜索结果落定时把卡重画一遍。
        internal static EnvBox Current;

        TextBlock introText;
        StackPanel itemsHost;
        StackPanel actionRow;
        TextBlock statusLine;

        public EnvBox(EnvReport report)
        {
            Title = "环境检查";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            // 620 而不是 560：ffmpeg 那种完整路径（…\WinGet\Packages\Gyan.FFmpeg_…\bin\ffmpeg.exe）
            // 很长，560 的时候右边会被切掉一截（2026-10-08 截图看出来的）。
            Width = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Br(MainForm.HudBg);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Ui.Id(this, "envDialog");
            Ui.MakeDraggable(this);

            StackPanel col = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

            // ⚠ 这一行必须 Wrap：StackPanel 的宽度取所有子元素里最宽的那个，
            //   一句不换行的长提示会把整块内容撑得比窗口还宽，ScrollViewer 就把右边裁掉
            //   （2026-10-08 截图看出来的 —— 连 OBS 路径都被切了一半）。
            introText = Ui.Txt("", 12.5, MainForm.HudText, false);
            introText.TextWrapping = TextWrapping.Wrap;
            introText.MaxWidth = 570;
            col.Children.Add(introText);

            itemsHost = new StackPanel();
            col.Children.Add(itemsHost);

            statusLine = Ui.Txt("", 11.5, MainForm.HudDim, false);
            statusLine.TextWrapping = TextWrapping.Wrap;
            statusLine.Margin = new Thickness(0, 8, 0, 0);
            Ui.Id(statusLine, "envStatus");
            col.Children.Add(statusLine);

            actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            col.Children.Add(actionRow);

            Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            Render(report);
            Current = this;
            Closed += delegate { if (Current == this) Current = null; };
        }

        // 按一份报告把整张卡重画一遍。构造时调一次；之后"后台查找落定了"可以再调。
        // 行和按钮都要重画 —— 按钮是按状态出现的（缺 OBS 才有「复制安装命令」，
        // 缺游戏/ffmpeg 才有「去设置里指一下」），只换行不换按钮就会对不上。
        internal void Render(EnvReport r)
        {
            rep = r;
            if (rep == null) return;

            introText.Text = rep.AllOk
                ? "环境没问题，可以直接用了。"
                : "下面几项还差点东西 —— 补完就能开录（单个录制只要「游戏目录」和 OBS 就行）。";

            itemsHost.Children.Clear();
            foreach (EnvItem it in rep.Items) itemsHost.Children.Add(ItemRow(it));

            actionRow.Children.Clear();

            EnvItem obs = rep.Get("obs");
            if (obs != null && !obs.Ok)
            {
                Button copy = Ui.FlatButton("复制安装命令", "envCopyWinget");
                copy.Height = 28; copy.Margin = new Thickness(8, 0, 0, 0);
                copy.Click += delegate
                {
                    try
                    {
                        Clipboard.SetText("winget install --id OBSProject.OBSStudio -e");
                        statusLine.Text = "命令已复制。粘到 PowerShell 里跑一下，装完先启动一次 OBS 再关掉。";
                    }
                    catch { statusLine.Text = "复制失败，请手动敲：winget install --id OBSProject.OBSStudio -e"; }
                };
                actionRow.Children.Add(copy);
            }

            // 游戏 或 ffmpeg 缺了 —— 两个都得去设置里指。只做一个按钮：
            // 上面每一行自己写着该怎么修，两个一模一样的按钮只是碍事。
            EnvItem game = rep.Get("game");
            EnvItem ffmRow = rep.Get("ffmpeg");
            if ((game != null && !game.Ok) || (ffmRow != null && !ffmRow.Ok))
            {
                Button goSet = Ui.FlatButton("去设置里指一下", "envOpenSettings");
                goSet.Height = 28; goSet.Margin = new Thickness(8, 0, 0, 0);
                goSet.Click += delegate { Close(); Ui.ShowSettings(); };
                actionRow.Children.Add(goSet);
            }

            Button closeBtn = Ui.FlatButton("关闭", "envClose");
            closeBtn.Height = 28; closeBtn.Margin = new Thickness(8, 0, 0, 0);
            closeBtn.Click += delegate { Close(); };
            actionRow.Children.Add(closeBtn);

            // 重画之后就当作"什么都没说过"：状态行里那句提示（"命令已复制"）是给上一版内容看的
            statusLine.Text = "";
        }

        // 一行 = ✓/✗ + 名字 + 当前值；不 OK 的话下面再跟一句"怎么修"
        UIElement ItemRow(EnvItem it)
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock mark = Ui.Txt(it.Ok ? "✓" : "✗", 12.5, it.Ok ? MainForm.HudAccent2 : MainForm.HudRed, true);
            mark.Width = 18;
            // 给验收脚本读的：text:<这个 id> 直接返回 "✓" / "✗"。
            // 卡上的内容原本是"弹出那一刻的事实"；2026-10-10 起后台查找落定后会重画
            // （见 Render），所以这个标记现在也可能中途从 ✗ 变成 ✓ —— 正是要能测这件事。
            Ui.Id(mark, "env_" + it.Key + "Mark");
            head.Children.Add(mark);
            TextBlock name = Ui.Txt(it.Label, 12.5, MainForm.HudText, false);
            name.Width = 118;
            name.TextWrapping = TextWrapping.NoWrap;
            head.Children.Add(name);
            TextBlock val = Ui.Txt(it.Value, 12.5, it.Ok ? MainForm.HudDim : MainForm.HudText, false);
            val.TextWrapping = TextWrapping.Wrap;
            val.MaxWidth = 420;
            head.Children.Add(val);
            sp.Children.Add(head);
            if (!it.Ok && !string.IsNullOrEmpty(it.Fix))
            {
                TextBlock fix = Ui.Txt("　→ " + it.Fix, 11.5, MainForm.HudOrange, false);
                fix.TextWrapping = TextWrapping.Wrap;
                fix.Margin = new Thickness(18, 2, 0, 0);
                fix.MaxWidth = 550;
                sp.Children.Add(fix);
            }
            Ui.Id(sp, "env_" + it.Key);
            return sp;
        }
    }

    // ================================================================ 发现新版本（2026-10-10）
    // 只在启动时查到有新版才弹，而且同一个版本弹过一次就不再弹（「以后再说」记在设置里）。
    // 样式照 EnvBox：无边框 HUD + SizeToContent。
    class UpdateBox : Window
    {
        readonly UpdateInfo info;
        TextBlock statusLine;
        Button primary;
        bool busy, applied;

        public UpdateBox(UpdateInfo info)
        {
            this.info = info;
            Title = "发现新版本";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Br(MainForm.HudBg);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Ui.Id(this, "updateDialog");
            Ui.MakeDraggable(this);

            StackPanel col = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            TextBlock head = Ui.Txt("发现新版本 v" + info.Version +
                                    "（你现在是 v" + AppInfo.Version + "）", 13, MainForm.HudText, true);
            Ui.Id(head, "updateVersion");
            col.Children.Add(head);

            // 更新说明可能好几行，用只读 TextBox —— 顺手能复制
            TextBox notes = new TextBox();
            notes.Text = info.Notes;
            notes.IsReadOnly = true;
            notes.TextWrapping = TextWrapping.Wrap;
            notes.MaxHeight = 220;
            notes.Margin = new Thickness(0, 10, 0, 0);
            notes.Background = Ui.Br(MainForm.HudPanel2);
            notes.Foreground = Ui.Br(MainForm.HudDim);
            notes.BorderBrush = Ui.Br(MainForm.HudLine);
            Ui.Id(notes, "updateNotes");
            col.Children.Add(notes);

            statusLine = Ui.Txt("", 11.5, MainForm.HudDim, false);
            statusLine.TextWrapping = TextWrapping.Wrap;
            statusLine.Margin = new Thickness(0, 10, 0, 0);
            Ui.Id(statusLine, "updateStatus");
            col.Children.Add(statusLine);

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            primary = Ui.FlatButton("下载并更新", "updateGo");
            primary.Height = 28;
            primary.Click += delegate { OnPrimary(); };
            row.Children.Add(primary);

            Button later = Ui.FlatButton("以后再说", "updateLater");
            later.Height = 28; later.Margin = new Thickness(8, 0, 0, 0);
            later.Click += delegate { OnLater(); };
            row.Children.Add(later);

            Button page = Ui.FlatButton("打开下载页", "updatePage");
            page.Height = 28; page.Margin = new Thickness(8, 0, 0, 0);
            page.Click += delegate { OpenPage(); };
            row.Children.Add(page);

            col.Children.Add(row);
            Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // 「下载并更新」/「马上重启」——同一个按钮，按状态换意思。
        // 下载在后台线程上跑（450KB 的包，但网络不可控，不能让模态窗假装卡死）；
        // 回来时走这个窗自己的 Dispatcher —— 它是 Window，一定有 Dispatcher，
        // 不牵扯 Ui.OnUi 那个"Application 还没建好时就地执行"的坑。
        void OnPrimary()
        {
            if (busy) return;
            if (applied) { Restart(); return; }
            busy = true;
            primary.IsEnabled = false;
            statusLine.Text = "正在下载…";
            System.Threading.Thread t = new System.Threading.Thread(delegate ()
            {
                string err;
                bool ok;
                try
                {
                    ok = UpdateCheck.DownloadAndApply(info, UpdateCheck.RunningExePath,
                                                     UpdateCheck.InstallDir, out err);
                }
                catch (Exception ex) { ok = false; err = ex.Message; }
                try
                {
                    Dispatcher.Invoke(delegate
                    {
                        busy = false;
                        primary.IsEnabled = true;
                        if (ok)
                        {
                            applied = true;
                            primary.Content = "马上重启";     // Content 是字符串（2 参数版 FlatButton）
                            statusLine.Text = "已更新到 v" + info.Version +
                                              "。点「马上重启」立刻用新版，或者下次自己打开也行。";
                        }
                        else
                        {
                            statusLine.Text = "更新失败：" + err + "。可以点「打开下载页」自己下下来替换。";
                        }
                    });
                }
                catch { }   // 窗已经被关掉就别管了
            });
            t.IsBackground = true;
            t.Name = "下载更新";
            t.Start();
        }

        void Restart()
        {
            try
            {
                string exe = UpdateCheck.RunningExePath;
                if (!string.IsNullOrEmpty(exe)) System.Diagnostics.Process.Start(exe);
            }
            catch (Exception ex) { statusLine.Text = "重启失败，请自己关掉再打开：" + ex.Message; return; }
            try { System.Windows.Application.Current.Shutdown(); } catch { }
        }

        // 「以后再说」= 记住这个版本，以后不再为它弹。
        // ⚠ 写设置只走 Ui.CfgNow（那就是 Ui.Start 里 Load 出来的那一份），
        //   不自己 Load 一份新的 —— 否则会覆盖掉界面上的其它改动。
        void OnLater()
        {
            Settings cur = Ui.CfgNow;
            if (cur != null)
            {
                cur.SkippedUpdateVersion = info.Version;
                try { cur.Save(); } catch { }
            }
            Close();
        }

        void OpenPage()
        {
            string u = info.FullPackage.Length > 0
                       ? info.FullPackage
                       : (info.Urls.Count > 0 ? info.Urls[0] : "");
            if (u.Length == 0) { statusLine.Text = "清单里没有下载地址"; return; }
            // ⚠ 只放行 http/https。这个字符串来自网上的清单 —— 清单一被改，file:// 或
            //   UNC 路径就能让「打开下载页」变成"运行任意本地文件"。这是本功能唯一的
            //   信任边界，宁可挡错也不能放开。
            if (!(u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                  u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                statusLine.Text = "清单里的下载地址不是网页地址，已忽略：" + u;
                return;
            }
            try { System.Diagnostics.Process.Start(u); }
            catch (Exception ex) { statusLine.Text = "打不开：" + ex.Message; }
        }
    }

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
            public TextBox Box;            // 路径类才有（「浏览」按钮要改它）
            // 「重新自动检测」要把检测到的值写回输入框。Box 只认 TextBox，而密码那个是
            // PasswordBox（它没有 .Text）—— 所以另给一个写入口，各种控件各自实现。
            // ⚠ 2026-10-08：以前只有 PathRow 填了 Box，NumberRow（端口）和 PasswordRow（密码）
            //   都没有 —— 于是点「重新自动检测」时日志说"已重新自动检测…端口 / 密码"，
            //   输入框里却还是旧值；再点「确定」就把旧值当成用户输入写回去，
            //   **刚检测到的端口和密码被静默丢掉**，OBS 照样连不上。
            public Action<string> SetBox;
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
            f.SetBox = delegate(string v) { tb.Text = v; };
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
            f.SetBox = delegate(string v) { pb.Password = v; };   // PasswordBox 没有 .Text
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
            f.SetBox = delegate(string v) { tb.Text = v; };
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
                    if (f.SetBox != null) f.SetBox(Convert.ToString(f.Pi.GetValue(cfg, null)));
                RepRecorder.Log.Write("设置窗：已重新自动检测游戏目录 / OBS / ffmpeg / 端口 / 密码");
            };
            head.Children.Add(auto);
            root.Children.Add(head);

            StackPanel col = new StackPanel { Margin = new Thickness(18, 0, 12, 8) };

            Section(col, "OBS、游戏与 ffmpeg");
            PathRow(col, "OBS 程序", "ObsExe", false, "");
            PathRow(col, "游戏目录", "GameDir", true, "（里面要有 th123.exe）");
            // 2026-10-10 加的。以前 ffmpeg 没有任何设置项 —— 用户自己下了一份放在别处就
            // 永远找不到，而且是三个依赖里唯一不能手动指定的（OBS 和游戏都能在这儿填）。
            PathRow(col, "ffmpeg 程序", "FfmpegExe", false, "（留空自动查找）");
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
            // 上限 3（2026-10-09 用户拍板）。切分链路里 crop / lanczos 缩放 / 淡出跑在 CPU 上，
            // 而并发数本来就被"这一批有几格"夹住 —— 4/5/6 只有一次真录 4~6 个的人才用得上，
            // 收益小、还会抢 CPU 和顶穿显卡的并发编码路数（顶穿就掉软件编码，慢好几倍）。
            // ⚠ 老设置文件里存着 4/5/6 的情况，靠 Core.Load 里那条无条件归一化兜住 ——
            //   不能只改这里：下拉找不到该值会静默回落到第一项，再被「确定」写回去，
            //   用户就会莫名其妙从 6 变成 1。
            ChoiceRow(col, "同时跑几个 ffmpeg", "SplitParallel",
                      new string[] { "1", "2", "3" },
                      new string[] { "1", "2", "3" });
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
        readonly bool oFlipH, oFlipV;
        readonly int oZoom;
        TextBox txtPath;
        // 位置那两行的滑块和行本身都要留引用：量程跟着对齐方式变，某些模式下整行要藏掉。
        Slider panX, panY;
        StackPanel rowX, rowY;
        bool syncingPan;                 // 见 SliderRow 里的说明

        public BackgroundBox(ShellWindow w)
        {
            owner = w; cfg = w.CfgPublic;
            oImg = cfg.BgImage; oLay = cfg.BgLayout; oScope = cfg.BgScope;
            oOp = cfg.BgOpacity; oBlur = cfg.BgBlur; oOx = cfg.BgOffsetX; oOy = cfg.BgOffsetY;
            oScrim = cfg.PanelScrim;
            oFlipH = cfg.BgFlipH; oFlipV = cfg.BgFlipV; oZoom = cfg.BgZoom;

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
            txtPath.TextChanged += delegate { cfg.BgImage = txtPath.Text; owner.ApplyBackground(); RefreshPanRows(); };
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
                        RefreshPanRows();
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
                RefreshPanRows();
            };
            r0.Children.Add(bClear);
            col.Children.Add(r0);

            // ------------------------------------------------------------------
            //  下面这一段的顺序和说法都是照着 Wallpaper Engine 的属性面板摆的：
            //  对齐方式 → 位置 → 翻转 → 缩放，四层互相独立，任意组合。
            // ------------------------------------------------------------------

            // ① 对齐方式
            ComboBox cbLayout = Ui.HudCombo("bgLayout", 250, new string[] {
                "覆盖（铺满，裁掉多余）", "适应（整张放下，会留边）", "拉伸（拉满，会变形）",
                "居中（原始大小）", "平铺" }, LayoutIndex(cfg.BgLayout));
            cbLayout.SelectionChanged += delegate
            {
                cfg.BgLayout = LayoutValue(cbLayout.SelectedIndex);
                owner.ApplyBackground();   // 先让它按新对齐方式把手上的位移夹一遍
                RefreshPanRows();          // 再照新的量程调滑块
            };
            col.Children.Add(ComboRow("对齐方式：", cbLayout));

            // ② 位置。量程不固定 —— 覆盖/适应/居中只给「有东西可挪」的那根轴，
            //    滑到头图边正好贴住框边、永远滑不出底色；拉伸时两行都会消失。
            //    2026-10-07 原本还有个「自由」（两轴不限、可拖出空白），用户说它和「覆盖」
            //    只差在"没余量的那根轴能不能动"，合并掉了。所以现在**没有任何模式能拖出空白**。
            rowX = SliderRow("水平移动 X", "bgOffX", -100, 100, cfg.BgOffsetX, "%",
                             delegate(double v) { cfg.BgOffsetX = (int)v; }, out panX);
            Ui.Id(rowX, "bgOffXRow");    // 藏的是整行，验收脚本要能单独查这一行
            col.Children.Add(rowX);
            rowY = SliderRow("垂直移动 Y", "bgOffY", -100, 100, cfg.BgOffsetY, "%",
                             delegate(double v) { cfg.BgOffsetY = (int)v; }, out panY);
            Ui.Id(rowY, "bgOffYRow");
            col.Children.Add(rowY);

            // ③ 缩放。独立一层，改完量程会跟着变（放大 = 能挪的余量变多）。
            Slider zoomSl;
            StackPanel rowZoom = SliderRow("缩放", "bgZoom", 10, 400, cfg.BgZoom, "%",
                delegate(double v) { cfg.BgZoom = (int)v; }, out zoomSl);
            // 缩放会改变「能挪多远」→ 拖完得把上面那两行的量程重新刷一遍。
            // 单独挂在这里而不是塞进 SliderRow：别的行不需要这个动作。
            // 上一层 handler 已经调过 ApplyBackground()（PanXNow 是新的），所以这里直接刷。
            zoomSl.ValueChanged += delegate { if (!syncingPan) RefreshPanRows(); };
            col.Children.Add(rowZoom);

            // ---- 下面这些是画面观感，跟上面四层无关 ----
            // 滑块显示的是「透明度」（0 = 图片最清晰、100 = 图片完全看不见），
            // 存盘的 BgOpacity 仍然是不透明度——只在滑块这一层翻转，老设置和调试接口都不受影响。
            col.Children.Add(SliderRow("图片透明度", "bgOpacity", 0, 100, 100 - cfg.BgOpacity, "%", delegate(double v) { cfg.BgOpacity = (int)(100 - v); }));
            // 面板底色强度：0 = 面板全透明、图片零遮挡；越大文字越好读。
            // 走共享画刷，改完所有面板立刻跟着变，不用重建界面。
            col.Children.Add(SliderRow("面板暗化", "panelScrim", 0, 100, cfg.PanelScrim, "%", delegate(double v) { cfg.PanelScrim = (int)v; Ui.SetScrim(cfg.PanelScrim); }));
            col.Children.Add(SliderRow("模糊", "bgBlur", 0, 60, cfg.BgBlur, "", delegate(double v) { cfg.BgBlur = (int)v; }));

            ComboBox cbScope = Ui.HudCombo("bgScope", 250, new string[] { "只铺右侧内容区", "全局铺满整个窗口（控制面板也透图）" }, cfg.BgScope == "global" ? 1 : 0);
            cbScope.SelectionChanged += delegate { cfg.BgScope = cbScope.SelectedIndex == 1 ? "global" : "content"; owner.ApplyBackground(); RefreshPanRows(); };
            col.Children.Add(ComboRow("背景范围：", cbScope));

            // ---- 翻转（镜像）放最下面（用户 2026-10-07 要求）。
            //      和位移正交：镜像改的是「图本身」，位移改的是「图在框里的落点」。
            StackPanel rowFlip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            TextBlock lbFlip = Ui.Txt("翻转：", 12.5, MainForm.HudText, false);
            lbFlip.Width = 96; lbFlip.VerticalAlignment = VerticalAlignment.Center;
            rowFlip.Children.Add(lbFlip);
            CheckBox ckH = Ui.HudCheck("左右镜像", "bgFlipH", cfg.BgFlipH, 0);
            ckH.Margin = new Thickness(0, 0, 14, 0);
            ckH.Click += delegate { cfg.BgFlipH = ckH.IsChecked == true; owner.ApplyBackground(); };
            rowFlip.Children.Add(ckH);
            CheckBox ckV = Ui.HudCheck("上下镜像", "bgFlipV", cfg.BgFlipV, 0);
            ckV.Click += delegate { cfg.BgFlipV = ckV.IsChecked == true; owner.ApplyBackground(); };
            rowFlip.Children.Add(ckV);
            col.Children.Add(rowFlip);

            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button ok = Ui.FlatButton("确定", "bgOk");
            ok.Width = 84; ok.Height = 28; ok.Margin = new Thickness(0, 0, 8, 0);
            ok.Click += delegate
            {
                cfg.Save();
                Log.Write("背景设置：图片=" + (string.IsNullOrEmpty(cfg.BgImage) ? "<无>" : cfg.BgImage)
                    + "  透明度=" + (100 - cfg.BgOpacity) + "%  面板暗化=" + cfg.PanelScrim + "%  模糊=" + cfg.BgBlur
                    + "  对齐=" + cfg.BgLayout + "  位移=" + cfg.BgOffsetX + "," + cfg.BgOffsetY
                    + "  翻转=" + (cfg.BgFlipH ? "左右" : "") + (cfg.BgFlipV ? "上下" : "")
                    + (cfg.BgFlipH || cfg.BgFlipV ? "" : "无") + "  缩放=" + cfg.BgZoom + "%"
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
                cfg.BgFlipH = oFlipH; cfg.BgFlipV = oFlipV; cfg.BgZoom = oZoom;
                cfg.PanelScrim = oScrim; Ui.SetScrim(cfg.PanelScrim);
                owner.ApplyBackground();
                Close();
            };
            bar.Children.Add(cancel);
            col.Children.Add(bar);

            Content = col;

            // 位置那两行的量程要等主窗口布局出来才算得准（要用到背景图层的实际尺寸），
            // 所以放到 Loaded 里初始化一次。
            Loaded += delegate { owner.ApplyBackground(); RefreshPanRows(); };

            // ⚠ 窗口大小一变，量程就跟着变，滑块的上限必须同步 —— 否则开着这个对话框
            //   去改窗口大小（比如点最大化）就会对不上：
            //     · 滑块还能拖到 18、渲染器只认 12 → 拖了没反应
            //     · 滑块只给到 12、渲染器其实能给 18 → **拖不到被裁掉的那部分**
            //   2026-10-07 用户报的"全屏后移动 Y 看不到被裁掉的部分"就是这个。
            //   ShellWindow 的 SizeChanged 里已经先调过 ApplyBackground()（PanYNow 是新的），
            //   这里挂的处理器排在它后面跑，所以直接用就行。
            owner.SizeChanged += OnOwnerResized;
            // 必须摘掉：owner 比对话框活得久，不摘的话对话框被一直引用着，
            // 而且每关一次再开一次就多挂一个。
            Closed += delegate { owner.SizeChanged -= OnOwnerResized; };
        }

        void OnOwnerResized(object sender, SizeChangedEventArgs e)
        {
            RefreshPanRows();
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

        // 按当前对齐方式算出来的量程，把「位置」那两行调成该有的样子：
        //   lim < 0 → 不限（只剩「平铺」了），滑块给 ±100
        //   lim = 0 → 没东西可挪（拉伸、或缩小到比例刚好吻合）→ **锁住滑块**（见下）
        //   lim > 0 → 滑块夹到 ±lim，滑到头正好贴边
        // 图层尺寸一变，「水平/垂直移动」的量程就跟着变（能拖多远取决于图比框大多少）。
        // ⚠ 凡是会改图层尺寸的操作都要调它 —— 2026-10-08 补上了漏掉的四个：
        //   换图片（输入框 / 浏览 / 清除）和换「背景范围」。不调的话滑块量程还是旧的，
        //   要么拖了没反应，要么拖不到新露出来的那部分画面，只能关掉重开或缩放窗口。
        void RefreshPanRows()
        {
            syncingPan = true;
            try
            {
                ApplyPanRow(rowX, panX, owner.PanXNow, cfg.BgOffsetX);
                ApplyPanRow(rowY, panY, owner.PanYNow, cfg.BgOffsetY);
            }
            finally { syncingPan = false; }
        }

        // 只调**界面上**的滑块，不写回 cfg。
        // 量程是随窗口大小变的，写回就等于拿"当前这个窗口"去抹掉用户存的位置
        // （详见 UiWindow.EffectivePan 上的说明：这就是"伸缩时背景闪几下"的根因）。
        // 用户真的拖动滑块时，SliderRow 的 ValueChanged 才会写。
        //
        // ⚠ 量程为 0 时**把滑块锁住，不隐藏整行**（用户 2026-10-07 要求）。
        //   以前是 Visibility.Collapsed，但本对话框是 SizeToContent=Height ——
        //   一行消失 = 对话框高度重算 = 下面所有控件上移，**正在拖的滑块会从指针底下跑掉**，
        //   于是拖动「缩放」时数值乱跳（用户报的"移动遥杆时乱跳"就是这个）。
        //   常显 + 置灰，布局就永远是稳定的。
        static void ApplyPanRow(StackPanel row, Slider sl, double lim, int cfgVal)
        {
            if (row == null || sl == null) return;
            if (lim == 0)
            {
                // 锁住。滑块的显示值给 0 —— 渲染那边 EffectivePan 也是夹到 0，显示的就得是真实值。
                // （不写回 cfg：syncingPan 为真，ValueChanged 只会刷新数字文本。）
                sl.IsEnabled = false;
                sl.Minimum = -1;
                sl.Maximum = 1;
                sl.Value = 0;
                row.Opacity = 0.45;          // 视觉上"这是禁用的"
                return;
            }
            sl.IsEnabled = true;
            row.Opacity = 1.0;
            double m = lim < 0 ? 100 : lim;
            sl.Minimum = -m;
            sl.Maximum = m;
            // 重新可用时要把显示值拉回用户存的位置 —— 锁定期间它被按在 0，
            // 而 cfg 里的值一直没变（渲染用的就是它），不拉回来两者就对不上了。
            int show = Math.Max(-(int)m, Math.Min((int)m, cfgVal));
            if (Math.Abs(sl.Value - show) > 0.001) sl.Value = show;
        }

        UIElement SliderRow(string label, string id, double min, double max, double val, string suffix, Action<double> onChange)
        {
            Slider ignored;
            return SliderRow(label, id, min, max, val, suffix, onChange, out ignored);
        }

        StackPanel SliderRow(string label, string id, double min, double max, double val, string suffix,
                             Action<double> onChange, out Slider slOut)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            TextBlock lb = Ui.Txt(label, 12.5, MainForm.HudText, false);
            lb.Width = 96; lb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lb);
            Slider sl = Ui.HudSlider(min, max, val, id);
            slOut = sl;
            TextBlock num = Ui.Txt(((int)val).ToString() + suffix, 12, MainForm.HudText, false);
            num.Width = 44; num.Margin = new Thickness(8, 0, 0, 0);
            num.VerticalAlignment = VerticalAlignment.Center;
            sl.ValueChanged += delegate
            {
                // syncingPan：这是 RefreshPanRows() 把「夹过的值」刷回界面，不是用户拖的 ——
                // 再写回配置会跟 ApplyBackground() 的夹取打架，来回弹。
                if (syncingPan) { num.Text = ((int)sl.Value).ToString() + suffix; return; }
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
