param([string]$Exe = "")

# ============================================================
# 验证两件事（都用外部可测的硬指标，不靠肉眼）：
#   1) 滚轮：往队列 ListView 的窗口上【投递】WM_MOUSEWHEEL（真实情况下光标停在列表上，
#      消息就是发给 ListView 的），读 LVM_GETTOPINDEX 看顶端行号有没有变。
#      再测一次"光标压在自绘滚动条上"（消息发给主窗口）。
#   2) 对齐：截图里扫像素，找三条框的上/下边框线落在第几行（边框色 #57616F）。
#
# 用法：powershell -File 验证-滚轮与对齐.ps1 [-Exe "别的路径\RepRecorder.exe"]
# ============================================================

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class V4 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", EntryPoint = "FindWindowEx", CharSet = CharSet.Auto)]
    static extern IntPtr FindWindowExPtr(IntPtr parent, IntPtr after, IntPtr cls, IntPtr title);
    // ⚠ 必须走 IntPtr 这个重载：PowerShell 传 $null 给 string 参数会变成【空字符串】，
    //   而 FindWindowEx 里空串 ≠ NULL（空串是"匹配类名为空"）—— 结果一个子窗口都找不到。
    public static IntPtr NextChild(IntPtr parent, IntPtr after) {
        return FindWindowExPtr(parent, after, IntPtr.Zero, IntPtr.Zero);
    }
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
    public static int L(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.L; }
    public static int T(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.T; }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
    // 投递一条 WM_MOUSEWHEEL（delta: 正=上滚, 负=下滚）
    public static int PostWheel(IntPtr h, int delta, int sx, int sy) {
        int wp = (int)((uint)(delta & 0xFFFF) << 16);
        int lp = (sy << 16) | (sx & 0xFFFF);
        return PostMessage(h, 0x020A, (IntPtr)wp, (IntPtr)lp) ? 1 : 0;
    }
}
"@

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Exe -eq "") { $Exe = Join-Path $dir 'RepRecorder.exe' }
# ⚠ 用临时目录里的一份干净拷贝：真实 rep 目录的名字里有 [ ] 和空格，
#   Start-Process 传参时会被吃掉，程序会当成"没有可加入的 rep"（踩过一次）。
$reps = Join-Path $env:TEMP 'reps'
# 临时目录里没有就拿游戏目录里的 rep 现拷一份
if (-not (Test-Path -LiteralPath $reps) -or
    (Get-ChildItem -LiteralPath $reps -Filter *.rep -ErrorAction SilentlyContinue).Count -lt 20) {
    New-Item -ItemType Directory -Force -Path $reps | Out-Null
    $gameReps = Join-Path $dir '..\【完整包】非想天则整合包2026-06-15\th123\replay'
    $n = 0
    Get-ChildItem -LiteralPath $gameReps -Recurse -Filter *.rep -ErrorAction SilentlyContinue |
        Select-Object -First 30 | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $reps ('rep{0:d3}.rep' -f $n)) -Force
            $n++
        }
    "已从游戏目录拷 $n 个 rep 到 $reps"
}
$out  = Join-Path $dir '_看片'

function Shot([IntPtr]$h, [string]$name) {
    $w = [V4]::W($h); $ht = [V4]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [V4]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $path = Join-Path $out $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

# 在一列上找出"边框线"落在哪些行（边框色 #57616F，容差 12）
function FindLines([string]$png, [int]$x, [int]$y0, [int]$y1) {
    $bmp = New-Object System.Drawing.Bitmap $png
    $hits = @()
    for ($y = $y0; $y -le $y1; $y++) {
        $c = $bmp.GetPixel($x, $y)
        if ([Math]::Abs($c.R - 87) -le 12 -and [Math]::Abs($c.G - 97) -le 12 -and [Math]::Abs($c.B - 111) -le 12) {
            $hits += $y
        }
    }
    $bmp.Dispose()
    return $hits
}

function TopIndex([IntPtr]$lv) {
    return [V4]::SendMessage($lv, 0x1027, [IntPtr]::Zero, [IntPtr]::Zero).ToInt32()   # LVM_GETTOPINDEX
}

"=== 被测 exe: $Exe ==="
if (-not (Test-Path -LiteralPath $Exe)) { "找不到 $Exe"; exit 1 }
# ⚠ 必须用 -LiteralPath：目录名里有方括号，Test-Path 会把它当通配符字符组，直接判成"不存在"
if (-not (Test-Path -LiteralPath $reps)) { "找不到测试 rep 目录：$reps"; exit 1 }
"rep 目录: $reps"

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 600

$proc = Start-Process -FilePath $Exe -ArgumentList @($reps) -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 300
    $proc.Refresh()
    if ($proc.HasExited) { "程序退出了 ExitCode=$($proc.ExitCode)"; exit 1 }
    if ($proc.MainWindowHandle -ne 0) { $h = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 3

[V4]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003) | Out-Null
[V4]::SetForegroundWindow($h) | Out-Null
[V4]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x0003) | Out-Null
Start-Sleep -Milliseconds 800

# ListView 不是主窗口的直接子控件（中间还隔着 mainSplit / split / 两个带边框的面板），
# 而且 WinForms 建的窗口类名是 "WindowsForms10.SysListView32.app.0.xxxx" 这种，
# 不是裸的 "SysListView32" —— 所以按【类名包含】递归找。
function ClassOf([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 256
    [V4]::GetClassName($h, $sb, 256) | Out-Null
    return $sb.ToString()
}

function FindByClass([IntPtr]$parent, [string]$needle) {
    $kid = [V4]::NextChild($parent, [IntPtr]::Zero)
    while ($kid.ToInt64() -ne 0) {
        if ((ClassOf $kid).Contains($needle)) { return $kid }
        $r = FindByClass $kid $needle
        if ($r.ToInt64() -ne 0) { return $r }
        $kid = [V4]::NextChild($parent, $kid)
    }
    return [IntPtr]::Zero
}

# 找不到就把整棵窗口树打出来（排查用）
function DumpTree([IntPtr]$parent, [int]$depth) {
    $kid = [V4]::NextChild($parent, [IntPtr]::Zero)
    while ($kid.ToInt64() -ne 0) {
        "  " * $depth + (ClassOf $kid) + "  " + $kid.ToString("X")
        if ($depth -lt 6) { DumpTree $kid ($depth + 1) }
        $kid = [V4]::NextChild($parent, $kid)
    }
}

$lv = FindByClass $h "SysListView32"
"主窗口 = $h   ListView = $lv"
if ($lv.ToInt64() -eq 0) {
    "没找到 ListView，整棵窗口树："
    DumpTree $h 0
    Stop-Process -Id $proc.Id -Force
    exit 1
}

$lw = [V4]::W($lv); $lh = [V4]::H($lv)
$lx = [V4]::L($lv); $lt = [V4]::T($lv)
"ListView 屏幕矩形: ($lx,$lt) ${lw}x${lh}"

$wx = [V4]::L($h)
$png = Shot $h "滚轮-对齐-1-初始.png"
"截图: $png"

# ---------- 对齐：三条框的上/下边框落在第几行 ----------
""
"---- 对齐（边框色 #57616F 出现的行）----"
# 左栏用窗口相对 x=300：避开标题文字那块"擦掉框线"的底色（不然看不到顶边）
$top = FindLines $png ($wx + 300) 24 90
"左栏控制面板 上边框行: " + ($top -join ',')
$top2 = FindLines $png ($wx + 700) 24 90
"队列框       上边框行: " + ($top2 -join ',')
$bh = [V4]::H($h)
"窗口尺寸: " + [V4]::W($h) + "x" + $bh
$bot = FindLines $png ($wx + 300) ($bh - 60) ($bh - 20)
"左栏控制面板 下边框行: " + ($bot -join ',')
$bot2 = FindLines $png ($wx + 700) ($bh - 60) ($bh - 20)
"终端框       下边框行: " + ($bot2 -join ',')
$bot3 = FindLines $png ($wx + 700) 300 ($bh - 20) | Select-Object -Last 4
"（队列/终端分界与终端底，供参考）: " + ($bot3 -join ',')

# ---------- 滚轮 ----------
""
"---- 滚轮 ----"
$before = TopIndex $lv
"初始 top index = $before"

# 1) 光标停在队列列表中间（真实情况：消息发给 ListView 自己）
$cx = $lx + [int]($lw / 2); $cy = $lt + [int]($lh / 2)
[V4]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 400
for ($i = 0; $i -lt 3; $i++) { [V4]::PostWheel($lv, -120, $cx, $cy) | Out-Null; Start-Sleep -Milliseconds 150 }
Start-Sleep -Milliseconds 600
$after1 = TopIndex $lv
"光标在列表上，向下滚 3 格: top index $before -> $after1   " + $(if ($after1 -ne $before) { "OK 滚动了" } else { "X 没动" })

for ($i = 0; $i -lt 2; $i++) { [V4]::PostWheel($lv, 120, $cx, $cy) | Out-Null; Start-Sleep -Milliseconds 150 }
Start-Sleep -Milliseconds 600
$after2 = TopIndex $lv
"光标在列表上，向上滚 2 格: top index $after1 -> $after2   " + $(if ($after2 -lt $after1) { "OK 回滚了" } else { "X 没动" })

# 2) 光标停在自绘滚动条上（消息发给主窗口）
$sx = $lx + $lw + 5; $sy = $lt + [int]($lh / 2)
[V4]::SetCursorPos($sx, $sy) | Out-Null
Start-Sleep -Milliseconds 400
for ($i = 0; $i -lt 3; $i++) { [V4]::PostWheel($h, -120, $sx, $sy) | Out-Null; Start-Sleep -Milliseconds 150 }
Start-Sleep -Milliseconds 600
$after3 = TopIndex $lv
"光标在滚动条上，向下滚 3 格: top index $after2 -> $after3   " + $(if ($after3 -ne $after2) { "OK 滚动了" } else { "X 没动" })

Shot $h "滚轮-对齐-2-滚过之后.png" | Out-Null
"截图: _看片\滚轮-对齐-2-滚过之后.png"

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
"残留进程: " + ((Get-Process RepRecorder -ErrorAction SilentlyContinue | Measure-Object).Count)
