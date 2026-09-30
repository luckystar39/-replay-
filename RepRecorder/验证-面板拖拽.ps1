# 验证-面板拖拽.ps1
# 验证「拖动分隔条把控制面板拉窄 → 自动收起」这条（VS Code 那种手感）。
# ⚠ 会用合成鼠标事件真的动一下光标，跑完自动把光标移回原处。
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class M {
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

    public static IntPtr FindButton(IntPtr root, string contains) {
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
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [M]::W($h); $ht = [M]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [M]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot ("_看片\" + $name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "  已存 $name"
}

$saved = [System.Windows.Forms.Cursor]::Position
try {
    Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
    Start-Sleep -Milliseconds 500

    $proc = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 300
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) { $h = $proc.MainWindowHandle; break }
    }
    if ($h -eq [IntPtr]::Zero) { "没等到主窗口"; exit 1 }
    Start-Sleep -Seconds 2

    $L = [M]::L($h); $T = [M]::T($h)
    "窗口客户区起点: ($L,$T)   尺寸 $([M]::W($h))x$([M]::H($h))"

    # 非最大化时：左边框 8px、标题栏 31px（从窗口/客户区尺寸差反推）
    $cx = $L + 8
    $cy = $T + 31
    $splitX = $cx + 324          # SplitterDistance≈322，分隔条 4px 宽 → 瞄准 324
    $midY   = $cy + 300

    "把光标移到分隔条 ($splitX,$midY)，按住往左拖到 x=$($cx+60)（间距 < 110 应触发收起）"
    [M]::SetCursorPos($splitX, $midY) | Out-Null
    Start-Sleep -Milliseconds 400
    [M]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)      # LEFTDOWN
    Start-Sleep -Milliseconds 250
    for ($x = $splitX; $x -ge ($cx + 60); $x -= 20) {
        [M]::SetCursorPos($x, $midY) | Out-Null
        Start-Sleep -Milliseconds 30
    }
    [M]::SetCursorPos(($cx + 60), $midY) | Out-Null
    Start-Sleep -Milliseconds 250
    [M]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)      # LEFTUP
    Start-Sleep -Milliseconds 900

    $btn = [M]::FindButton($h, "面板")
    $txt = [M]::Text($btn)
    "拖完按钮写着：'$txt'"
    if ($txt -like "*展开面板*") { "  → 自动收起 ✅ 生效" } else { "  → ✗ 没有自动收起" }
    Shot $h "面板-4-拖拽自动收起.png"

    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}
finally {
    [M]::SetCursorPos($saved.X, $saved.Y) | Out-Null
    "光标已移回原位 ($($saved.X),$($saved.Y))"
}
