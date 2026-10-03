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

    // ================================================================ 设置窗（反射版）
    //  旧版是个 PropertyGrid 对话框；WPF 没有现成控件，反射列 Settings 的公开属性自己搭。
    class SettingsBox : Window
    {
        public bool Ok = false;
        readonly List<TextBox> boxes = new List<TextBox>();
        readonly List<System.Reflection.PropertyInfo> props = new List<System.Reflection.PropertyInfo>();

        public SettingsBox(Settings cfg)
        {
            Title = "设置";
            WindowStyle = WindowStyle.None;
            Width = 520; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Br(MainForm.HudBg);
            Ui.Id(this, "settingsDialog");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

            StackPanel col = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
            foreach (System.Reflection.PropertyInfo pi in typeof(Settings).GetProperties())
            {
                if (!pi.CanRead || !pi.CanWrite) continue;
                if (pi.PropertyType != typeof(string) && pi.PropertyType != typeof(int)
                    && pi.PropertyType != typeof(bool) && pi.PropertyType != typeof(double)) continue;
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                TextBlock lb = Ui.Txt(pi.Name, 12, MainForm.HudDim, false);
                lb.Width = 150;
                row.Children.Add(lb);
                TextBox tb = new TextBox();
                tb.Width = 320; tb.Height = 22;
                tb.FontFamily = new FontFamily("Consolas");
                tb.FontSize = 12;
                tb.Background = Ui.Br(MainForm.HudPanel2);
                tb.Foreground = Ui.Br(MainForm.HudText);
                tb.BorderBrush = Ui.Br(MainForm.HudLine);
                object v = pi.GetValue(cfg, null);
                tb.Text = v == null ? "" : v.ToString();
                Ui.Id(tb, "set_" + pi.Name);
                row.Children.Add(tb);
                props.Add(pi);
                boxes.Add(tb);
                col.Children.Add(row);
            }
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.Content = col;
            root.Children.Add(sv);

            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 14, 0) };
            Button ok = Ui.FlatButton("确定", "setOk");
            ok.Width = 84; ok.Height = 28; ok.Margin = new Thickness(0, 0, 8, 0);
            ok.Click += delegate
            {
                for (int i = 0; i < props.Count; i++)
                {
                    System.Reflection.PropertyInfo pi = props[i];
                    string t = boxes[i].Text;
                    try
                    {
                        if (pi.PropertyType == typeof(string)) pi.SetValue(cfg, t, null);
                        else if (pi.PropertyType == typeof(int)) { int n; if (int.TryParse(t, out n)) pi.SetValue(cfg, n, null); }
                        else if (pi.PropertyType == typeof(double)) { double d; if (double.TryParse(t, out d)) pi.SetValue(cfg, d, null); }
                        else if (pi.PropertyType == typeof(bool)) pi.SetValue(cfg, t == "true" || t == "True" || t == "1", null);
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
            Grid.SetRow(bar, 1);
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
        readonly int oOp, oBlur, oOx, oOy;
        TextBox txtPath;

        public BackgroundBox(ShellWindow w)
        {
            owner = w; cfg = w.CfgPublic;
            oImg = cfg.BgImage; oLay = cfg.BgLayout; oScope = cfg.BgScope;
            oOp = cfg.BgOpacity; oBlur = cfg.BgBlur; oOx = cfg.BgOffsetX; oOy = cfg.BgOffsetY;

            Title = "背景设置";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 470;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Br(MainForm.HudBg);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Ui.Id(this, "bgDialog");

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

            col.Children.Add(SliderRow("图片不透明度", "bgOpacity", 0, 100, cfg.BgOpacity, "%", delegate(double v) { cfg.BgOpacity = (int)v; }));
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
                    + "  不透明度=" + cfg.BgOpacity + "%  模糊=" + cfg.BgBlur
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
