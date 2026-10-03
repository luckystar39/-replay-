# 验证-滚动条.ps1
# 验证自绘滚动条 + 滚轮：把光标放到左栏上滚几下，内容应该往上跑（能滚出下面的「录音模式」）。
# ⚠ 会短暂抢焦点并移动光标，跑完还原。
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class S2 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int d, IntPtr e);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
    public static bool Focus(IntPtr h) { return SetForegroundWindow(h); }
    public static int L(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.L; }
    public static int T(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.T; }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [S2]::W($h); $ht = [S2]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [S2]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot ("_看片\" + $name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 500
$proc = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 300
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne 0) { $h = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 2
[S2]::Focus($h) | Out-Null
Start-Sleep -Milliseconds 400

$L = [S2]::L($h); $T = [S2]::T($h)
"窗口 ($L,$T)"
Shot $h "滚动条-1-初始.png"

# 光标放到左栏中部
$cx = $L + 8 + 140
$cy = $T + 31 + 300
[S2]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 400
"光标放到左栏 ($cx,$cy)，滚轮向下 5 格"
for ($i = 0; $i -lt 5; $i++) {
    [S2]::mouse_event(0x0800, 0, 0, -120, [IntPtr]::Zero)   # MOUSEEVENTF_WHEEL，负 = 向下
    Start-Sleep -Milliseconds 180
}
Start-Sleep -Milliseconds 600
Shot $h "滚动条-2-左栏滚过之后.png"

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
