# 验证-无边框窗口.ps1
# 验证无边框窗口改造，逐项实测：
#   1. 窗口态几何 / 顶栏位置 / 三个窗口按钮
#   2. 顶部有没有残留的系统标题栏深色带（PrintWindow + 真实屏幕两种采法）
#   3. 窗口态下的 WM_NCHITTEST 命中结果（缩放热区 + 顶栏 HTCAPTION）
#   4. 拖顶栏能不能移动窗口
#   5. 拖右边缘能不能缩放窗口
#   6. 最大化是否严格贴合显示器工作区（不盖任务栏）
#   7. 最大化状态下的 WM_NCHITTEST（边缘要报 HTCLIENT，顶栏仍要能拖）
# ⚠ 会移动鼠标并抢焦点，跑完还原光标。
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class BW {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int d, IntPtr e);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    // ⚠ 保留了 WS_CAPTION 之后，最大化时【窗口】矩形会比工作区大一圈（那圈在屏幕外，看不见）。
    //   用户真正看到的是【客户区】—— 断言要量客户区，量窗口矩形会误判。
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINTAPI p);
    public static int CliL(IntPtr h){ RECT r; GetClientRect(h, out r); POINTAPI p; p.X = 0; p.Y = 0; ClientToScreen(h, ref p); return p.X; }
    public static int CliT(IntPtr h){ RECT r; GetClientRect(h, out r); POINTAPI p; p.X = 0; p.Y = 0; ClientToScreen(h, ref p); return p.Y; }
    public static int CliW(IntPtr h){ RECT r; GetClientRect(h, out r); return r.R - r.L; }
    public static int CliH(IntPtr h){ RECT r; GetClientRect(h, out r); return r.B - r.T; }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINTAPI p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct POINTAPI { public int X, Y; }
    public static IntPtr At(int x, int y) {
        POINTAPI p; p.X = x; p.Y = y;
        IntPtr c = WindowFromPoint(p);
        return (c == IntPtr.Zero) ? IntPtr.Zero : GetAncestor(c, 2);   // GA_ROOT
    }
    public static IntPtr ChildAt(int x, int y) {
        POINTAPI p; p.X = x; p.Y = y;
        return WindowFromPoint(p);                                     // the actual child window
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

    public static List<IntPtr> All = new List<IntPtr>();
    public static void Enum(IntPtr root) {
        All.Clear();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) { All.Add(h); return true; }, IntPtr.Zero);
    }
    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static string Txt(IntPtr h) { StringBuilder sb = new StringBuilder(300); GetWindowText(h, sb, 300); return sb.ToString(); }
    public static int L(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.L; }
    public static int T(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.T; }
    public static int R(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R; }
    public static int B(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B; }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
    public static bool Focus(IntPtr h){ return SetForegroundWindow(h); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [BW]::W($h); $ht = [BW]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [BW]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $path = Join-Path $PSScriptRoot ("_看片\" + $name)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps) {
    [BW]::SetCursorPos($x1, $y1) | Out-Null; Start-Sleep -Milliseconds 300
    [BW]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 250
    for ($i = 1; $i -le $steps; $i++) {
        [BW]::SetCursorPos($x1 + [int](($x2 - $x1) * $i / $steps), $y1 + [int](($y2 - $y1) * $i / $steps)) | Out-Null
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 250
    [BW]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800
}

function Click([int]$x, [int]$y) {
    [BW]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 250
    [BW]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 120
    [BW]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 600
}

# ⚠ 这套测试会点顶栏按钮、也会在内容区点一下（用来激活窗口），顺手就会改掉设置里的
#   PanelWidth / PanelCollapsed（点顶栏按钮）和 ParallelCount（点一下选中列表行）。
#   开跑前记下来，跑完原样写回 —— 上一次就是忘了这一步，把用户的 376/4 改成了 286/1。
$cfgPath = Join-Path $PSScriptRoot 'RepRecorder.settings.json'
function CfgGet([string]$key) {
    $raw = [System.IO.File]::ReadAllText($cfgPath)
    return [regex]::Match($raw, '"' + $key + '"\s*:\s*([^,}\r\n]+)').Groups[1].Value.Trim()
}
function CfgSet([string]$key, [string]$val) {
    $raw = [System.IO.File]::ReadAllText($cfgPath)
    $raw = [regex]::Replace($raw, '"' + $key + '"\s*:\s*[^,}\r\n]+', '"' + $key + '":' + $val)
    [System.IO.File]::WriteAllText($cfgPath, $raw, (New-Object System.Text.UTF8Encoding($false)))
}
$snap = @{}
foreach ($k in @('PanelWidth', 'PanelCollapsed', 'ParallelCount')) { $snap[$k] = CfgGet $k }
"settings snapshot: " + (($snap.GetEnumerator() | Sort-Object Name | ForEach-Object { $_.Key + '=' + $_.Value }) -join '  ') | Write-Output

$saved = [System.Windows.Forms.Cursor]::Position
$work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
"work area = $($work.X),$($work.Y) $($work.Width)x$($work.Height)"

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 700
$proc = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 300
    $proc.Refresh()
    if ($proc.HasExited) { "app exited, code=$($proc.ExitCode)"; exit 1 }
    if ($proc.MainWindowHandle -ne 0) { $h = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 4
[BW]::Focus($h) | Out-Null
Start-Sleep -Milliseconds 600
# 先点一下内容区把窗口真正激活 —— 否则第一次合成拖动会被系统当成"激活点击"吃掉
Click ([BW]::L($h) + 600) ([BW]::T($h) + 300)
$topBar = [IntPtr]::Zero
[BW]::Enum($h)
foreach ($c in [BW]::All) { if ([BW]::H($c) -eq 30 -and [BW]::W($c) -gt 500) { $topBar = $c; break } }

# 命中测试：把屏幕坐标打包成 lParam 问窗口打算怎么处理
function Hit([int]$sx, [int]$sy) {
    $lp = [IntPtr]((($sy -band 0xFFFF) -shl 16) -bor ($sx -band 0xFFFF))
    return [int]([BW]::SendMessage($h, 0x0084, [IntPtr]::Zero, $lp))
}
$HT = @{ 1='HTCLIENT'; 2='HTCAPTION'; 10='HTLEFT'; 11='HTRIGHT'; 12='HTTOP'; 13='HTTOPLEFT';
         14='HTTOPRIGHT'; 15='HTBOTTOM'; 16='HTBOTTOMLEFT'; 17='HTBOTTOMRIGHT' }
function HitLine([string]$what, [int]$sx, [int]$sy, [int]$expect) {
    $r = Hit $sx $sy
    $got = $HT[$r]; if (-not $got) { $got = $r }
    $mark = if ($r -eq $expect) { 'OK ' } else { 'BAD' }
    "  [$mark] {0,-14} ({1},{2}) -> {3,-12} (expect {4})" -f $what, $sx, $sy, $got, $HT[$expect] | Write-Output
    return ($r -eq $expect)
}

"=== 1. windowed geometry ==="
"  window   L={0} T={1} R={2} B={3}  {4}x{5}" -f [BW]::L($h),[BW]::T($h),[BW]::R($h),[BW]::B($h),[BW]::W($h),[BW]::H($h) | Write-Output
"  top bar  L={0} T={1} {2}x{3}   (T must equal the window T — no frame offset)" -f [BW]::L($topBar),[BW]::T($topBar),[BW]::W($topBar),[BW]::H($topBar) | Write-Output
"  --- which child window is actually under each chrome point? ---"
[BW]::Enum($h)
function WhoIs([string]$what, [int]$x, [int]$y) {
    $c = [BW]::ChildAt($x, $y)
    $cls = if ($c -eq $h) { '<the form itself>' } elseif ($c -eq [IntPtr]::Zero) { '<none>' } else { [BW]::Cls($c) }
    $isForm = ($c -eq $h)
    "  {0,-16} ({1,5},{2,3}) -> {3}  {4}" -f $what, $x, $y, $cls, $(if ($isForm) { '[OK - form handles WM_NCHITTEST]' } else { '[child swallows it]' }) | Write-Output
}
WhoIs 'top bar' ([BW]::L($topBar) + 500) ([BW]::T($topBar) + 15)
WhoIs 'top-left corner' ([BW]::L($h) + 1) ([BW]::T($h) + 1)
WhoIs 'top edge' ([BW]::L($h) + 600) ([BW]::T($h) + 2)
WhoIs 'left edge' ([BW]::L($h) + 1) ([BW]::T($h) + 300)
WhoIs 'right edge' ([BW]::R($h) - 2) ([BW]::T($h) + 300)
WhoIs 'bottom edge' ([BW]::L($h) + 600) ([BW]::B($h) - 2)
WhoIs 'bottom-right corner' ([BW]::R($h) - 2) ([BW]::B($h) - 2)

$caps = @()
foreach ($c in [BW]::All) { if ([BW]::GetParent($c) -eq $topBar -and [BW]::W($c) -eq 46) { $caps += $c } }
"  caption buttons: $($caps.Count) (expect 3), right-aligned at " + (($caps | Sort-Object { [BW]::L($_) } | ForEach-Object { [BW]::L($_) }) -join ', ')
"  window right edge = $([BW]::R($h))  (last button + 46 must equal this)"

"=== 2. top rows — must all be the top-bar colour #4A4C54, no near-black #202020 ==="
$f1 = Shot $h "borderless-1-windowed.png"
$bmp = [System.Drawing.Bitmap]::FromFile($f1)
$bad = 0
foreach ($y in @(0, 2, 6, 14, 25, 29)) {
    $c1 = $bmp.GetPixel(6, $y); $c2 = $bmp.GetPixel([int]($bmp.Width/2), $y); $c3 = $bmp.GetPixel($bmp.Width-6, $y)
    $t = "   y={0,3}  left=#{1:X2}{2:X2}{3:X2}  mid=#{4:X2}{5:X2}{6:X2}  right=#{7:X2}{8:X2}{9:X2}" -f $y,$c1.R,$c1.G,$c1.B,$c2.R,$c2.G,$c2.B,$c3.R,$c3.G,$c3.B
    "  $t"
    if ($c1.R -lt 60) { $bad++ }
}
$bmp.Dispose()
"  >>> dark-band pixels found: $bad  [$(if ($bad -eq 0) {'PASS'} else {'FAIL'})]"

"=== 3. WM_NCHITTEST, WINDOWED ==="
$pass = 0; $total = 0
$total++; if (HitLine 'top bar' ([BW]::L($topBar) + 600) ([BW]::T($topBar) + 15) 2) { $pass++ }
$total++; if (HitLine 'over close btn' ([BW]::R($h) - 20) ([BW]::T($topBar) + 15) 1) { $pass++ }
$total++; if (HitLine 'content' ([BW]::L($h) + 600) ([BW]::T($h) + 300) 1) { $pass++ }
$total++; if (HitLine 'right edge' ([BW]::R($h) - 2) ([BW]::T($h) + 300) 11) { $pass++ }
$total++; if (HitLine 'left edge' ([BW]::L($h) + 1) ([BW]::T($h) + 300) 10) { $pass++ }
$total++; if (HitLine 'bottom edge' ([BW]::L($h) + 600) ([BW]::B($h) - 2) 15) { $pass++ }
$total++; if (HitLine 'top-left corner' ([BW]::L($h) + 1) ([BW]::T($h) + 1) 13) { $pass++ }
$total++; if (HitLine 'bottom-right corner' ([BW]::R($h) - 2) ([BW]::B($h) - 2) 17) { $pass++ }
"  >>> $pass/$total hit tests correct"

"=== 4. drag the top bar — window must move by the same delta ==="
$fg = [BW]::GetForegroundWindow()
"  foreground window = $fg ; app window = $h ; same = $($fg -eq $h)"
"  window under the drag point ($([BW]::L($topBar) + 500),$([BW]::T($topBar) + 15)) = $([BW]::At(([BW]::L($topBar) + 500),([BW]::T($topBar) + 15)))  (must equal $h)"
$bx = [BW]::L($h); $by = [BW]::T($h)
Drag ([BW]::L($topBar) + 500) ([BW]::T($topBar) + 15) ([BW]::L($topBar) + 500 + 120) ([BW]::T($topBar) + 15 + 90) 14
$dx = [BW]::L($h) - $bx; $dy = [BW]::T($h) - $by
"  window moved: ({0},{1}) -> ({2},{3})   delta=({4},{5})  [expect ~(120,90)]" -f $bx,$by,[BW]::L($h),[BW]::T($h),$dx,$dy | Write-Output
"  >>> $(if ([Math]::Abs($dx - 120) -le 12 -and [Math]::Abs($dy - 90) -le 12) {'PASS'} else {'FAIL'})"

"=== 5. drag the right edge — window must get narrower ==="
# 上一步把窗口拖到了 x=170，右边缘已经出了 1280 宽的屏幕 —— 先把它挪回屏幕里，
# 否则待会儿那个拖拽点根本点不到（第一次跑就是这么假失败的）。
[BW]::SetWindowPos($h, [IntPtr]::Zero, 60, 40, [BW]::W($h), [BW]::H($h), 0x0004) | Out-Null
Start-Sleep -Milliseconds 800
"  repositioned to {0},{1}  (right edge now {2})" -f [BW]::L($h), [BW]::T($h), [BW]::R($h) | Write-Output
$w0 = [BW]::W($h); $h0 = [BW]::H($h)
Drag ([BW]::R($h) - 3) ([BW]::T($h) + [int]([BW]::H($h) / 2)) ([BW]::R($h) - 3 - 150) ([BW]::T($h) + [int]([BW]::H($h) / 2)) 12
"  size ${w0}x${h0} -> $([BW]::W($h))x$([BW]::H($h))   [expect width -150]"
"  >>> $(if ([Math]::Abs(([BW]::W($h)) - $w0 + 150) -le 12) {'PASS'} else {'FAIL'})"

"=== 6. maximize — must be exactly the work area, not the full screen ==="
[BW]::ShowWindow($h, 3) | Out-Null
Start-Sleep -Seconds 2
"  window rect  L={0} T={1} {2}x{3}   (比工作区大一圈是正常的，那圈在屏幕外)" -f [BW]::L($h),[BW]::T($h),[BW]::W($h),[BW]::H($h) | Write-Output
"  client rect  L={0} T={1} {2}x{3}" -f [BW]::CliL($h),[BW]::CliT($h),[BW]::CliW($h),[BW]::CliH($h) | Write-Output
"  work area    L={0} T={1} {2}x{3}" -f $work.X,$work.Y,$work.Width,$work.Height | Write-Output
$maxOk = ([BW]::CliL($h) -eq $work.X) -and ([BW]::CliT($h) -eq $work.Y) -and
         ([BW]::CliW($h) -eq $work.Width) -and ([BW]::CliH($h) -eq $work.Height)
"  >>> 客户区必须精确等于工作区: $(if ($maxOk) {'PASS'} else {'FAIL'})"
Shot $h "borderless-2-maximized.png" | Out-Null

"=== 7. WM_NCHITTEST, MAXIMIZED (edges must not resize; top bar must still drag) ==="
$pass = 0; $total = 0
$total++; if (HitLine 'top bar' ([BW]::L($topBar) + 600) ([BW]::T($topBar) + 15) 2) { $pass++ }
$total++; if (HitLine 'right edge (no resize)' ([BW]::R($h) - 2) ([BW]::T($h) + 300) 1) { $pass++ }
$total++; if (HitLine 'bottom edge (no resize)' ([BW]::L($h) + 600) ([BW]::B($h) - 2) 1) { $pass++ }
"  >>> $pass/$total correct"

function DoubleClick([int]$x, [int]$y) {
    [BW]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 250
    [BW]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 40
    [BW]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60
    [BW]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 40
    [BW]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 800
}

function TopBarButtons() {
    [BW]::Enum($h)
    $r = @()
    foreach ($c in [BW]::All) {
        if ([BW]::GetParent($c) -eq $topBar -and [BW]::Cls($c).Contains('BUTTON')) { $r += $c }
    }
    return @($r | Sort-Object { [BW]::L($_) })
}

"=== 8. the top-bar buttons must still be clickable (HTCAPTION must not eat them) ==="
[BW]::ShowWindow($h, 9) | Out-Null
Start-Sleep -Seconds 2
# 先把窗口激活：合成输入的第一步经常只被用来激活窗口，不激活的话这次点击就白点了
[BW]::Focus($h) | Out-Null
Start-Sleep -Milliseconds 400
Click ([BW]::L($h) + 700) ([BW]::T($h) + 500)
"  foreground = $([BW]::GetForegroundWindow()) ; app = $h ; same = $([BW]::GetForegroundWindow() -eq $h)"
$tbs = TopBarButtons
"  buttons on the top bar: $($tbs.Count)  ->  " + (($tbs | ForEach-Object { "'" + [BW]::Txt($_) + "'" }) -join ', ')
# 用「点击前后文本有没有变」来判定，不拿中文字面量去比 ——
# 脚本里的中文常量过一道 PowerShell 读取/编码就可能对不上（实测就是这么假失败过）。
$b0 = $tbs[0]
$t0 = [BW]::Txt($b0)
Click ([BW]::L($b0) + [int]([BW]::W($b0) / 2)) ([BW]::T($b0) + [int]([BW]::H($b0) / 2))
$tbs = TopBarButtons
$t1 = [BW]::Txt($tbs[0])
"  click #1: '$t0' -> '$t1'   " + $(if ($t1 -ne $t0) { '[OK - button reacted]' } else { '[FAIL - button did nothing]' })
Click ([BW]::L($tbs[0]) + [int]([BW]::W($tbs[0]) / 2)) ([BW]::T($tbs[0]) + [int]([BW]::H($tbs[0]) / 2))
$tbs = TopBarButtons
$t2 = [BW]::Txt($tbs[0])
"  click #2: '$t1' -> '$t2'   " + $(if ($t2 -eq $t0) { '[OK - toggled back]' } else { '[FAIL]' })

"=== 9. the self-drawn minimize / maximize / close buttons ==="
# ⚠ 注意：这三个按钮继承的是 Control，不是 Button —— 类名里没有 BUTTON，
#   所以不能跟上面那两个（收起面板 / 背景）一起找。按宽度 46 认。
$cb = @()
[BW]::Enum($h)
foreach ($c in [BW]::All) { if ([BW]::GetParent($c) -eq $topBar -and [BW]::W($c) -eq 46) { $cb += $c } }
$cb = @($cb | Sort-Object { [BW]::L($_) })
"  found $($cb.Count) caption buttons (min / max / close)"
$minB = $cb[0]; $maxB = $cb[1]; $closeB = $cb[2]
$z0 = [BW]::IsZoomed($h)
Click ([BW]::L($maxB) + 23) ([BW]::T($maxB) + 15)
$z1 = [BW]::IsZoomed($h)
"  clicked 'maximize' -> IsZoomed $z0 -> $z1  " + $(if ($z1 -ne $z0) { '[OK]' } else { '[FAIL]' })
Shot $h "borderless-4-maximized-by-button.png" | Out-Null
Click ([BW]::L($maxB) + 23) ([BW]::T($maxB) + 15)
$z2 = [BW]::IsZoomed($h)
"  clicked 'maximize' again -> IsZoomed $z1 -> $z2  " + $(if ($z2 -ne $z1) { '[OK - toggles]' } else { '[FAIL]' })

"=== 10. double-clicking the top bar toggles maximize ==="
$z0 = [BW]::IsZoomed($h)
DoubleClick ([BW]::L($topBar) + 700) ([BW]::T($topBar) + 15)
$z1 = [BW]::IsZoomed($h)
"  double-click #1 -> IsZoomed $z0 -> $z1  " + $(if ($z1 -ne $z0) { '[OK]' } else { '[FAIL]' })
$shotMax = Shot $h "borderless-5-maximized.png"
DoubleClick ([BW]::L($topBar) + 700) ([BW]::T($topBar) + 15)
$z2 = [BW]::IsZoomed($h)
"  double-click #2 -> IsZoomed $z1 -> $z2  " + $(if ($z2 -ne $z1) { '[OK - toggles]' } else { '[FAIL]' })

"=== 12. the actual child window under each chrome point must answer HTTRANSPARENT ==="
# 直接验钩子：问【光标底下那个子窗口】要命中结果，装饰区它必须回 -1 (HTTRANSPARENT)，
# 命中才会继续往下一层落到窗体上。回 1 (HTCLIENT) 就意味着点击会被吃掉。
[BW]::ShowWindow($h, 9) | Out-Null
Start-Sleep -Seconds 2
function ChildHit([int]$sx, [int]$sy) {
    $child = [BW]::ChildAt($sx, $sy)
    $lp = [IntPtr]((($sy -band 0xFFFF) -shl 16) -bor ($sx -band 0xFFFF))
    $r = [int]([BW]::SendMessage($child, 0x0084, [IntPtr]::Zero, $lp))
    return $r
}
function CheckChild([string]$what, [int]$sx, [int]$sy, [int]$expect, [string]$note) {
    $r = ChildHit $sx $sy
    $got = if ($r -eq -1) { 'HTTRANSPARENT' } elseif ($r -eq 1) { 'HTCLIENT' } else { "$r" }
    $exp = if ($expect -eq -1) { 'HTTRANSPARENT' } elseif ($expect -eq 1) { 'HTCLIENT' } else { "$expect" }
    $mk = if ($r -eq $expect) { 'OK ' } else { 'BAD' }
    "  [$mk] {0,-22} -> {1,-14} (expect {2})  {3}" -f $what, $got, $exp, $note | Write-Output
    return ($r -eq $expect)
}

$pass = 0; $t = 0
"  -- panel EXPANDED --"
$t++; if (CheckChild 'top bar (drag zone)' ([BW]::L($topBar) + 500) ([BW]::T($topBar) + 15) -1 'must pass through to the form') { $pass++ }
$t++; if (CheckChild 'left edge (resize)' ([BW]::L($h) + 1) ([BW]::T($h) + 300) -1 'must pass through to the form') { $pass++ }
$t++; if (CheckChild 'right edge (resize)' ([BW]::R($h) - 2) ([BW]::T($h) + 300) -1 'must pass through to the form') { $pass++ }
$t++; if (CheckChild 'bottom edge (resize)' ([BW]::L($h) + 600) ([BW]::B($h) - 2) -1 'must pass through to the form') { $pass++ }
$t++; if (CheckChild 'close button' ([BW]::R($h) - 20) ([BW]::T($topBar) + 15) 1 'the button must win') { $pass++ }
"  >>> $pass/$t correct (expanded)"

# 收起面板 → 左边多出一条活动栏，压在左侧热区上，钩子得跟着重装
$tbs = TopBarButtons
$st = [BW]::Txt($tbs[0])
Click ([BW]::L($tbs[0]) + [int]([BW]::W($tbs[0]) / 2)) ([BW]::T($tbs[0]) + [int]([BW]::H($tbs[0]) / 2))
Start-Sleep -Milliseconds 800
$collapsed = ([BW]::Txt((TopBarButtons)[0]) -ne $st)
"  -- panel COLLAPSED (activity bar now covers the left edge) = $collapsed --"
$pass2 = 0; $t2 = 0
$t2++; if (CheckChild 'left edge (resize)' ([BW]::L($h) + 1) ([BW]::T($h) + 300) -1 'activity bar must pass through too') { $pass2++ }
$t2++; if (CheckChild 'top bar (drag zone)' ([BW]::L($topBar) + 500) ([BW]::T($topBar) + 15) -1 'must still pass through') { $pass2++ }
"  >>> $pass2/$t2 correct (collapsed)"

"=== 13. Aero Snap: dragging the top bar to the screen edge should snap ==="
# WS_THICKFRAME + HTCAPTION 交给系统那套移动循环，理论上贴边分屏应该还在。
# 不验一下的话就只是"理论上"。
[BW]::ShowWindow($h, 9) | Out-Null
Start-Sleep -Seconds 2
[BW]::SetWindowPos($h, [IntPtr]::Zero, 300, 300, 900, 600, 0x0004) | Out-Null
Start-Sleep -Milliseconds 900
$before = "{0},{1} {2}x{3}" -f [BW]::L($h), [BW]::T($h), [BW]::W($h), [BW]::H($h)
"  before: $before  (zoomed=$([BW]::IsZoomed($h)))"
# 拖到屏幕最上边 —— 标准的"贴顶最大化"手势
Drag ([BW]::L($topBar) + 450) ([BW]::T($topBar) + 15) 640 1 20
"  after dragging to the top edge: L={0} T={1} {2}x{3}  zoomed={4}" -f [BW]::L($h),[BW]::T($h),[BW]::W($h),[BW]::H($h),[BW]::IsZoomed($h) | Write-Output
"  >>> $(if ([BW]::IsZoomed($h)) {'PASS - snap to top maximized the window'} else {'no snap (not fatal, just means snapping is off)'})"

"=== 11. the close button must actually close the app ==="
$cb = @()
[BW]::Enum($h)
foreach ($c in [BW]::All) { if ([BW]::GetParent($c) -eq $topBar -and [BW]::W($c) -eq 46) { $cb += $c } }
$cb = @($cb | Sort-Object { [BW]::L($_) })
Click ([BW]::L($cb[2]) + 23) ([BW]::T($cb[2]) + 15)
Start-Sleep -Seconds 2
$proc.Refresh()
"  clicked 'close' -> app exited = $($proc.HasExited)  " + $(if ($proc.HasExited) { '[OK]' } else { '[FAIL]' })

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
foreach ($k in @('PanelWidth', 'PanelCollapsed', 'ParallelCount')) { CfgSet $k $snap[$k] }
"settings restored: " + (($snap.GetEnumerator() | Sort-Object Name | ForEach-Object { $_.Key + '=' + $_.Value }) -join '  ') | Write-Output
[System.Windows.Forms.Cursor]::Position = $saved
"cursor restored; done"
