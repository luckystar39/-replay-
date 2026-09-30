# 验证-面板宽度记忆.ps1
# 验证「拖动分隔条改宽度 → 重启后仍是这个宽度」。
# ⚠ 会用合成鼠标事件真的动一下光标，跑完自动移回原处。
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W2 {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

    public static string Text(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static IntPtr Button(IntPtr root, string contains) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            if (found != IntPtr.Zero) return false;
            if (Cls(h).StartsWith("WindowsForms10.BUTTON") && Text(h).Contains(contains)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static int L(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.L; }
    public static int T(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.T; }
    public static int Wd(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int Ht(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [W2]::Wd($h); $ht = [W2]::Ht($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [W2]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot ("_看片\" + $name)), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
function Width { (Get-Content (Join-Path $PSScriptRoot 'RepRecorder.settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json).PanelWidth }
function Launch {
    Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
    Start-Sleep -Milliseconds 500
    $p = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 300; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break } }
    Start-Sleep -Seconds 2
    return @{ Proc = $p; Hwnd = $h }
}

$saved = [System.Windows.Forms.Cursor]::Position
try {
    "=== 第 1 步：启动，把分隔条从 322 拖到约 520 ==="
    $a = Launch
    $cur = Width
    "  当前 PanelWidth=$cur"
    $L = [W2]::L($a.Hwnd); $T = [W2]::T($a.Hwnd)
    $from = $L + 8 + $cur + 2
    $to   = $L + 8 + 520
    $y    = $T + 31 + 300
    "  窗口位置 L=$L T=$T   拖拽路径 x: $from → $to   y=$y"

    # 先点一下顶栏让窗口拿到焦点（不然第一次按下可能只被用来"激活窗口"，拖动被吞掉）
    [W2]::SetCursorPos(($L + 600), ($T + 45)) | Out-Null
    Start-Sleep -Milliseconds 200
    [W2]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    [W2]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500

    [W2]::SetCursorPos($from, $y) | Out-Null
    Start-Sleep -Milliseconds 400
    [W2]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 350
    for ($x = $from; $x -le $to; $x += 10) { [W2]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 25 }
    [W2]::SetCursorPos($to, $y) | Out-Null
    Start-Sleep -Milliseconds 300
    [W2]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 900
    "  拖完 PanelWidth=$(Width)   ← 应该接近 520"
    Shot $a.Hwnd "宽度-1-拖宽后.png"
    Stop-Process -Id $a.Proc.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    "=== 第 2 步：重启，宽度应该还是那个值 ==="
    $b = Launch
    "  重启后 PanelWidth=$(Width)"
    "  顶栏按钮：'$([W2]::Text([W2]::Button($b.Hwnd, '面板')))'"
    Shot $b.Hwnd "宽度-2-重启后.png"
    Stop-Process -Id $b.Proc.Id -Force -ErrorAction SilentlyContinue
}
finally {
    [W2]::SetCursorPos($saved.X, $saved.Y) | Out-Null
    "光标已移回原位 ($($saved.X),$($saved.Y))"
}
