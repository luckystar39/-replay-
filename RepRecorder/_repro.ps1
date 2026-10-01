# _repro.ps1 -- temporary harness for "queue rows blank out / need the mouse over them".
# ASCII only on purpose: PowerShell 5.1 reads non-BOM files as ANSI.
#
# The app already calls AddPaths(args) from the command line (MainForm.Shown) -- the same
# code path a file drop takes -- so the drop itself does not need to be simulated.
#
# A background image is temporarily installed for the run: without one, Bg.Cache == null and
# the whole PaintEmptyArea branch never executes. BgImage/BgOpacity are restored at the end.
#
#   r0-empty   : plain launch, empty queue
#   r1-drop    : launched with the reps, cursor parked over the list the whole time
#   r2-hover   : after sweeping the mouse across every row
#   r3-click   : after clicking a row
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public class RP {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int i);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int d, IntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

    public static List<IntPtr> Kids = new List<IntPtr>();
    public static void Enum(IntPtr root) {
        Kids.Clear();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) { Kids.Add(h); return true; }, IntPtr.Zero);
    }
    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static int[] RectOf(IntPtr h) { RECT r; GetWindowRect(h, out r); return new int[] { r.L, r.T, r.R-r.L, r.B-r.T }; }

    public static double Scale() {
        IntPtr dc = GetDC(IntPtr.Zero);
        int phys = GetDeviceCaps(dc, 118);
        int virt = GetSystemMetrics(0);
        ReleaseDC(IntPtr.Zero, dc);
        return virt <= 0 ? 1.0 : (double)phys / (double)virt;
    }
    public static int PhysW() { IntPtr dc = GetDC(IntPtr.Zero); int v = GetDeviceCaps(dc, 118); ReleaseDC(IntPtr.Zero, dc); return v; }
    // 117 = DESKTOPVERTRES (physical). 10 = VERTRES, which this DPI-unaware process
    // reports as the *virtual* 720 and would clip the capture.
    public static int PhysH() { IntPtr dc = GetDC(IntPtr.Zero); int v = GetDeviceCaps(dc, 117); ReleaseDC(IntPtr.Zero, dc); return v; }

    public static byte[] Lum(string path, int x, int y, int w, int h, int step) {
        using (Bitmap src = new Bitmap(path)) {
            int cols = w / step, rows = h / step;
            byte[] o = new byte[cols * rows];
            BitmapData bd = src.LockBits(new Rectangle(x, y, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                IntPtr p0 = bd.Scan0;
                int stride = bd.Stride;
                byte[] line = new byte[stride];
                for (int j = 0; j < rows; j++) {
                    Marshal.Copy(new IntPtr(p0.ToInt64() + (long)(j * step) * stride), line, 0, stride);
                    for (int k = 0; k < cols; k++) {
                        int off = (k * step) * 4;
                        o[j * cols + k] = (byte)((line[off+2]*299 + line[off+1]*587 + line[off]*114) / 1000);
                    }
                }
            } finally { src.UnlockBits(bd); }
            return o;
        }
    }
    public static double[] Diff(byte[] a, byte[] b) {
        long sum = 0; long big = 0;
        for (int i = 0; i < a.Length; i++) {
            int d = Math.Abs(a[i] - b[i]);
            sum += d;
            if (d > 24) big++;
        }
        return new double[] { Math.Round((double)sum / a.Length, 2), Math.Round(100.0 * big / a.Length, 1) };
    }
    public static string BandDiff(byte[] a, byte[] b, int cols, int rows, int bandPx) {
        StringBuilder sb = new StringBuilder();
        int bands = rows / Math.Max(1, bandPx);
        for (int t = 0; t < bands; t++) {
            long big = 0; int n = 0;
            for (int j = t*bandPx; j < (t+1)*bandPx && j < rows; j++)
                for (int k = 0; k < cols; k++) { if (Math.Abs(a[j*cols+k]-b[j*cols+k]) > 24) big++; n++; }
            if (big > 0) sb.AppendFormat("    band {0,2} (y {1,4}-{2,4}): {3,5}%{4}", t, t*bandPx, (t+1)*bandPx, Math.Round(100.0*big/n,1), Environment.NewLine);
        }
        return sb.ToString();
    }
}
"@

$out = Join-Path $env:TEMP 'rrrepro'
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }
$exe = Join-Path $PSScriptRoot 'RepRecorder.exe'
$cfgPath = Join-Path $PSScriptRoot 'RepRecorder.settings.json'

# ---- a loud, obviously-not-UI background image ---------------------------------
$bgPath = Join-Path $out 'bg.png'
$bmp = New-Object System.Drawing.Bitmap 1920, 1080
$g = [System.Drawing.Graphics]::FromImage($bmp)
for ($i = 0; $i -lt 1080; $i += 4) {
    $c = [System.Drawing.Color]::FromArgb(255, (60 + ($i % 160)), (200 - ($i % 180)), 90)
    $pen = New-Object System.Drawing.Pen $c, 4
    $g.DrawLine($pen, 0, $i, 1920, $i)
    $pen.Dispose()
}
$g.Dispose()
$bmp.Save($bgPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"background image -> $bgPath"

# ---- settings we must not leave changed --------------------------------------
$cfgRaw = Get-Content $cfgPath -Raw -Encoding UTF8
$keep = @{}
foreach ($k in @('PanelWidth', 'ParallelCount', 'PanelCollapsed', 'BgImage', 'BgOpacity')) {
    $m = [regex]::Match($cfgRaw, '"' + $k + '"\s*:\s*([^,}]+)')
    if ($m.Success) { $keep[$k] = $m.Groups[1].Value.Trim() }
}
"settings before: " + (($keep.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join ', ')

# install the test background
$patched = $cfgRaw
$esc = $bgPath.Replace('\', '\\')
$patched = [regex]::Replace($patched, '"BgImage"\s*:\s*"[^"]*"', '"BgImage":"' + $esc + '"')
$patched = [regex]::Replace($patched, '"BgOpacity"\s*:\s*[^,}]+', '"BgOpacity":100')
if ($patched -ne $cfgRaw) {
    [System.IO.File]::WriteAllText($cfgPath, $patched, (New-Object System.Text.UTF8Encoding($false)))
    "installed test background"
}

function Grab([string]$path) {
    $pw = [RP]::PhysW(); $ph = [RP]::PhysH()
    $b = New-Object System.Drawing.Bitmap $pw, $ph
    $gg = [System.Drawing.Graphics]::FromImage($b)
    $gg.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size $pw, $ph))
    $gg.Dispose()
    $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
}

function Launch([string[]]$extra) {
    Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
    Start-Sleep -Milliseconds 800
    if ($extra -and $extra.Count -gt 0) { $p = Start-Process -FilePath $exe -ArgumentList $extra -PassThru }
    else { $p = Start-Process -FilePath $exe -PassThru }
    for ($i = 0; $i -lt 80; $i++) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        if ($p.HasExited) { Write-Host "app exited, code=$($p.ExitCode)"; return $null }
        if ($p.MainWindowHandle -ne 0) { break }
    }
    Start-Sleep -Seconds 6
    return $p
}

function FindList($h) {
    [RP]::Enum($h)
    foreach ($c in [RP]::Kids) { if ([RP]::Cls($c) -like '*SysListView32*') { return $c } }
    return [IntPtr]::Zero
}

$scale = [RP]::Scale()
"dpi scale = $scale"
$reps = @(Get-Content (Join-Path $out 'reps.txt') -Encoding UTF8 | Where-Object { $_.Trim() -ne '' })
"reps to add = $($reps.Count)"

# ---------- run A: plain launch, empty queue ----------
$p = Launch @()
if ($null -eq $p) { exit 1 }
$h = $p.MainWindowHandle
$lv = FindList $h
if ($lv -eq [IntPtr]::Zero) { "no list"; $p.Kill(); exit 1 }
$lr = [RP]::RectOf($lv)
"list rect (virtual) = {0},{1} {2}x{3}" -f $lr[0], $lr[1], $lr[2], $lr[3] | Write-Output
$lx = [int]($lr[0] * $scale); $ly = [int]($lr[1] * $scale)
$lw = [int]($lr[2] * $scale); $lh = [int]($lr[3] * $scale)
$cx = $lx + [int]($lw / 2); $cy = $ly + [int]($lh * 0.12)
"list physical = {0},{1} {2}x{3} ; cursor park = {4},{5}" -f $lx, $ly, $lw, $lh, $cx, $cy | Write-Output
[RP]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 1200
Grab (Join-Path $out 'r0-empty.png')
$p.Kill(); Start-Sleep -Milliseconds 900

# ---------- run B: reps via command line, cursor parked over the list ----------
[RP]::SetCursorPos($cx, $cy) | Out-Null
$p = Launch $reps
if ($null -eq $p) { exit 1 }
$h = $p.MainWindowHandle
$lv = FindList $h
[RP]::Enum($h)
$popup = 0
foreach ($c in [RP]::Kids) { if ([RP]::Cls($c) -like '*#32770*') { $popup++ } }
"popup dialogs = $popup"
"item count after AddPaths = " + [RP]::SendMessage($lv, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
Grab (Join-Path $out 'r1-drop.png')

# sweep the mouse across EVERY row, left to right, then down
for ($row = 0; $row -lt 17; $row++) {
    $yy = $ly + 74 + $row * 60
    for ($k = 0; $k -le 6; $k++) {
        [RP]::SetCursorPos(($lx + 40 + $k * 380), $yy) | Out-Null
        Start-Sleep -Milliseconds 45
    }
}
Start-Sleep -Milliseconds 1200
Grab (Join-Path $out 'r2-hover.png')

# click in the middle of the list
[RP]::SetCursorPos(($lx + [int]($lw/2)), ($ly + 400)) | Out-Null
Start-Sleep -Milliseconds 200
[RP]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 120
[RP]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 1200
Grab (Join-Path $out 'r3-click.png')

$step = 4
$cols = [int]($lw / $step); $rows = [int]($lh / $step)
$band = [int](20 * $scale / $step)
$f0 = Join-Path $out 'r0-empty.png'
$f1 = Join-Path $out 'r1-drop.png'
$f2 = Join-Path $out 'r2-hover.png'
$f3 = Join-Path $out 'r3-click.png'
$a = [RP]::Lum($f0, $lx, $ly, $lw, $lh, $step)
$b = [RP]::Lum($f1, $lx, $ly, $lw, $lh, $step)
$c = [RP]::Lum($f2, $lx, $ly, $lw, $lh, $step)
$d = [RP]::Lum($f3, $lx, $ly, $lw, $lh, $step)

$d01 = [RP]::Diff($a, $b)
$d12 = [RP]::Diff($b, $c)
$d13 = [RP]::Diff($b, $d)
""
"=== queue list region, $lw x $lh physical ==="
"empty      -> after AddPaths    mean={0}  changed={1}%" -f $d01[0], $d01[1] | Write-Output
"AddPaths   -> after mouse sweep mean={0}  changed={1}%" -f $d12[0], $d12[1] | Write-Output
"AddPaths   -> after click       mean={0}  changed={1}%" -f $d13[0], $d13[1] | Write-Output
""
"per-band (after AddPaths -> after mouse sweep):"
[RP]::BandDiff($b, $c, $cols, $rows, $band) | Write-Output
"per-band (after AddPaths -> after click):"
[RP]::BandDiff($b, $d, $cols, $rows, $band) | Write-Output
""
if ($d01[1] -lt 3) { "VERDICT: AddPaths drew NOTHING" }
elseif (($d12[1] -gt 3) -or ($d13[1] -gt 3)) { "VERDICT: rows drawn, but mouse/click CHANGED them  <<< bug reproduced" }
else { "VERDICT: no bug this run" }

$p.Kill(); Start-Sleep -Milliseconds 700

$now = Get-Content $cfgPath -Raw -Encoding UTF8
$fixed = $now
foreach ($k in $keep.Keys) {
    $fixed = [regex]::Replace($fixed, '"' + $k + '"\s*:\s*[^,}]+', ('"' + $k + '":' + $keep[$k]))
}
if ($fixed -ne $now) {
    [System.IO.File]::WriteAllText($cfgPath, $fixed, (New-Object System.Text.UTF8Encoding($false)))
    "settings restored: " + (($keep.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join ', ')
} else { "settings unchanged" }
"done"
