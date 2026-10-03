# 验证-快捷键.ps1
# 验证 Ctrl+B 能不能收起 / 展开左侧控制面板。
# ⚠ 会短暂把焦点抢到被测程序上，跑完还回原窗口。
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class K {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

    public static string Text(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static bool Focus(IntPtr h) { return SetForegroundWindow(h); }
    public static IntPtr FindBtn(IntPtr root, string contains) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            if (found != IntPtr.Zero) return false;
            if (Cls(h).StartsWith("WindowsForms10.BUTTON") && Text(h).Contains(contains)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [K]::W($h); $ht = [K]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [K]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
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

"起手按钮：'$([K]::Text([K]::FindBtn($h, '面板')))'"
[K]::Focus($h) | Out-Null
Start-Sleep -Milliseconds 500

"[1] 按 Ctrl+B（应该收起）"
[K]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)              # Ctrl down
[K]::keybd_event(0x42, 0, 0, [IntPtr]::Zero)              # B down
Start-Sleep -Milliseconds 80
[K]::keybd_event(0x42, 0, 2, [IntPtr]::Zero)              # B up
[K]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)              # Ctrl up
Start-Sleep -Milliseconds 900
"  现在按钮：'$([K]::Text([K]::FindBtn($h, '面板')))'   ← 应为「展开面板」"
Shot $h "快捷键-1-CtrlB收起.png"

"[2] 再按一次 Ctrl+B（应该展开）"
[K]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
[K]::keybd_event(0x42, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[K]::keybd_event(0x42, 0, 2, [IntPtr]::Zero)
[K]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900
"  现在按钮：'$([K]::Text([K]::FindBtn($h, '面板')))'   ← 应为「收起面板」"
Shot $h "快捷键-2-CtrlB展开.png"

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
