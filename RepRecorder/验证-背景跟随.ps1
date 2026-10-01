# 验证-背景跟随.ps1
# Measures whether the drawn background image tracks the main splitter.
#   - enumerates the SplitContainer's two SplitterPanels to get the exact splitter x
#   - pixel-scans a horizontal line to find where the image actually starts being drawn
#   - repeats after dragging the splitter
# ASCII only on purpose: PowerShell 5.1 reads non-BOM files as ANSI.
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class BG {
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
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
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
    $w = [BG]::W($h); $ht = [BG]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [BG]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot ("_看片\" + $name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return (Join-Path $PSScriptRoot ("_看片\" + $name))
}

# report the flat runs along one scanline: start-end and the colour of the run
function ScanRow([string]$file, [int]$y) {
    $bmp = [System.Drawing.Bitmap]::FromFile($file)
    if ($y -ge $bmp.Height) { $y = [int]($bmp.Height / 2) }
    $res = @()
    $start = 0
    $prev = $bmp.GetPixel(0, $y)
    for ($x = 1; $x -lt $bmp.Width; $x++) {
        $c = $bmp.GetPixel($x, $y)
        $d = [Math]::Abs($c.R - $prev.R) + [Math]::Abs($c.G - $prev.G) + [Math]::Abs($c.B - $prev.B)
        if ($d -gt 12) {
            if (($x - $start) -ge 12) { $res += ("{0,5}-{1,5}  #{2:X2}{3:X2}{4:X2}" -f $start, $x, $prev.R, $prev.G, $prev.B) }
            $start = $x
        }
        $prev = $c
    }
    $res += ("{0,5}-{1,5}  #{2:X2}{3:X2}{4:X2}" -f $start, $bmp.Width, $prev.R, $prev.G, $prev.B)
    $bmp.Dispose()
    return $res
}

# Locate SplitContainer.Panel1 / Panel2 among the form's child windows.
#   clientLeft = the leftmost child x (mainSplit + Panel1 both sit there)
#   Panel1     = widest child at clientLeft whose right edge stops short of the window
#   Panel2     = widest child starting at or right of Panel1's right edge
function Split([IntPtr]$h) {
    [BG]::Enum($h)
    $winL = [BG]::L($h); $winT = [BG]::T($h)
    $all = @()
    $clientLeft = 999999; $maxR = 0
    foreach ($c in [BG]::All) {
        if ([BG]::W($c) -lt 100 -or [BG]::H($c) -lt 300) { continue }
        $all += $c
        if ([BG]::L($c) -lt $clientLeft) { $clientLeft = [BG]::L($c) }
        if ([BG]::R($c) -gt $maxR) { $maxR = [BG]::R($c) }
    }
    $p1 = [IntPtr]::Zero
    foreach ($c in $all) {
        if ([BG]::L($c) -ne $clientLeft) { continue }
        if ([BG]::R($c) -ge $maxR - 40) { continue }          # that's the SplitContainer itself
        if ([BG]::W($c) -lt 150) { continue }
        if ($p1 -eq [IntPtr]::Zero -or [BG]::R($c) -lt [BG]::R($p1)) { $p1 = $c }
    }
    $p2 = [IntPtr]::Zero
    foreach ($c in $all) {
        if ($p1 -ne [IntPtr]::Zero -and [BG]::L($c) -lt [BG]::R($p1)) { continue }
        if ($p2 -eq [IntPtr]::Zero -or [BG]::W($c) -gt [BG]::W($p2)) { $p2 = $c }
    }
    return @{ P1 = $p1; P2 = $p2; WinL = $winL; WinT = $winT }
}

function Report([string]$tag, [string]$file, $geo, [int]$y) {
    ""
    "=== $tag ==="
    $p1 = $geo.P1; $p2 = $geo.P2; $winL = $geo.WinL
    if ($p1 -ne [IntPtr]::Zero) { "  Panel1 : screen L={0} R={1}  -> bitmap x {2}..{3}" -f [BG]::L($p1), [BG]::R($p1), ([BG]::L($p1) - $winL), ([BG]::R($p1) - $winL) | Write-Output }
    $p2L = 0
    if ($p2 -ne [IntPtr]::Zero) {
        $p2L = [BG]::L($p2) - $winL
        "  Panel2 : screen L={0} R={1}  -> bitmap x {2}..{3}" -f [BG]::L($p2), [BG]::R($p2), $p2L, ([BG]::R($p2) - $winL) | Write-Output
    }
    "  scanline y=$y  (bitmap x from 0 = window left border)"
    $runs = @(ScanRow $file $y)
    foreach ($r in $runs) { "    $r" }

    # PASS/FAIL: the last flat run that ends before Panel2 must reach right up to it.
    # A stale background cache shows up as the image being painted further and further left
    # of Panel2 as the control panel grows -> a big gap here.
    if ($p2L -gt 0) {
        $lastEnd = -1
        foreach ($r in $runs) {
            $m = [regex]::Match($r, '^\s*\d+-\s*(\d+)\s')
            if ($m.Success) { $e = [int]$m.Groups[1].Value; if ($e -le $p2L -and $e -gt $lastEnd) { $lastEnd = $e } }
        }
        $gap = $p2L - $lastEnd
        $verdict = if ($gap -le 40) { "PASS" } else { "FAIL" }
        "  >>> content-area left edge = $p2L ; last flat run ends at $lastEnd ; gap = $gap px  [$verdict]"
    }
}

# Dragging the splitter rewrites PanelWidth in the settings file. Remember it and put it back
# at the end -- never leave the user's own config changed.
$cfgPath = Join-Path $PSScriptRoot 'RepRecorder.settings.json'
$panelBefore = $null
try {
    $m = [regex]::Match((Get-Content $cfgPath -Raw -Encoding UTF8), '"PanelWidth"\s*:\s*(\d+)')
    if ($m.Success) { $panelBefore = [int]$m.Groups[1].Value }
} catch { }
"PanelWidth before test = $panelBefore"

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 600
$proc = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 300
    $proc.Refresh()
    if ($proc.HasExited) { "app exited, code=$($proc.ExitCode)"; exit 1 }
    if ($proc.MainWindowHandle -ne 0) { $h = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 4
[BG]::Focus($h) | Out-Null
Start-Sleep -Milliseconds 600
# ⚠ 面板要是收起的，分隔条根本拖不动（OnPanelSplitterMoved 会提前返回），
#   这个测试就白跑了。开跑前先按一次 Ctrl+B 保证是展开的。
$pc = $false
try { $pc = ([regex]::Match((Get-Content $cfgPath -Raw -Encoding UTF8), '"PanelCollapsed"\s*:\s*(\w+)').Groups[1].Value -eq 'true') } catch { }
if ($pc) {
    "panel starts collapsed -> pressing Ctrl+B to expand it"
    [BG]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
    [BG]::keybd_event(0x42, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [BG]::keybd_event(0x42, 0, 2, [IntPtr]::Zero)
    [BG]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Seconds 2
}
"window screen rect: L={0} T={1}  {2}x{3}" -f [BG]::L($h), [BG]::T($h), [BG]::W($h), [BG]::H($h) | Write-Output

$y = [int]([BG]::H($h) * 0.55)
$geo = Split $h
$f1 = Shot $h "bgfollow-1-initial.png"
Report "A. fresh start (no drag)" $f1 $geo $y

# ---- drag the splitter to the RIGHT by 160 px ----
if ($geo.P1 -ne [IntPtr]::Zero) {
    $sx = [BG]::R($geo.P1) + 2
    $sy = [BG]::T($h) + [int]([BG]::H($h) / 2)
    "dragging splitter at ($sx,$sy) right by 160"
    [BG]::SetCursorPos($sx, $sy) | Out-Null
    Start-Sleep -Milliseconds 400
    [BG]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 250
    for ($i = 1; $i -le 16; $i++) {
        [BG]::SetCursorPos($sx + $i * 10, $sy) | Out-Null
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 300
    [BG]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Seconds 2
    $geo2 = Split $h
    $f2 = Shot $h "bgfollow-2-after-drag-right.png"
    Report "B. after dragging splitter +160px to the right" $f2 $geo2 $y
}

# ---- resize the window and check the image still ends exactly at the splitter ----
if ($geo2.P1 -ne [IntPtr]::Zero) {
    $wx = [BG]::L($h); $wy = [BG]::T($h); $ww = [BG]::W($h); $wh = [BG]::H($h)
    "resizing window from ${ww}x${wh} to 900x640"
    # the app starts maximized -> restore first, otherwise there is no resize border to grab
    [BG]::ShowWindow($h, 9) | Out-Null          # SW_RESTORE
    Start-Sleep -Milliseconds 600
    [BG]::SetWindowPos($h, [IntPtr]::Zero, 60, 60, 900, 640, 0x0004) | Out-Null   # SWP_NOZORDER
    Start-Sleep -Seconds 3
    $geo3 = Split $h
    $f3 = Shot $h "bgfollow-3-after-resize.png"
    $y3 = [int]([BG]::H($h) * 0.55)
    Report "C. after resizing the window" $f3 $geo3 $y3
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

# put PanelWidth back the way it was
if ($panelBefore -ne $null) {
    $raw = Get-Content $cfgPath -Raw -Encoding UTF8
    $now = [regex]::Match($raw, '"PanelWidth"\s*:\s*(\d+)').Groups[1].Value
    $fixed = [regex]::Replace($raw, '"PanelWidth"\s*:\s*\d+', ('"PanelWidth":' + $panelBefore))
    [System.IO.File]::WriteAllText($cfgPath, $fixed, (New-Object System.Text.UTF8Encoding($false)))
    "PanelWidth restored: $now -> $panelBefore"
}
"done"
